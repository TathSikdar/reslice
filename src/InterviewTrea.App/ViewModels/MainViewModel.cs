using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Measurements;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Dicom;
using InterviewTrea.Rendering.Reslicing;
using InterviewTrea.Rendering.Windowing;
using InterviewTrea.Rendering3D;

namespace InterviewTrea.App.ViewModels;

/// <summary>
/// The shared state of the four panes: one volume, one window, one crosshair.
/// </summary>
/// <remarks>
/// The crosshair is a patient-space point, not a slice index, and that is the central
/// design decision of the iteration. A slice index only means anything to the viewport
/// that owns it; a millimetre coordinate means the same thing in all four, which is what
/// makes FR-304's linking a consequence of the model rather than a feature bolted onto it.
/// It is also what FR-307 needs, because a rotated plane has no slice index at all.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly SeriesLoader loader;
    private readonly GeometryValidator validator;
    private readonly VolumeBuilder builder;
    private readonly ISeriesPrompt prompt;

    public MainViewModel(
        SeriesLoader loader,
        GeometryValidator validator,
        VolumeBuilder builder,
        ISeriesPrompt prompt)
    {
        this.loader = loader;
        this.validator = validator;
        this.builder = builder;
        this.prompt = prompt;

        // Three planar panes. The fourth cell of the 2x2 is the 3D view, which is not a
        // reslice plane and so is not one of these.
        Viewports =
        [
            new ViewportViewModel(PlaneOrientation.Axial),
            new ViewportViewModel(PlaneOrientation.Coronal),
            new ViewportViewModel(PlaneOrientation.Sagittal),
        ];

        // FR-409. Named targets rather than an implicit "whichever pane you last clicked":
        // an export that cannot be seen in advance is one the user has to run to find out
        // what it does.
        ExportTargets =
        [
            new ExportTarget("All four panes", null),
            new ExportTarget("Axial", Viewports[0]),
            new ExportTarget("Coronal", Viewports[1]),
            new ExportTarget("Sagittal", Viewports[2]),
            new ExportTarget("3D", null, IsVolume: true),
        ];


        // The window's field initializer assigns the backing field directly, so it never
        // passes through OnWindowChanged and the dropdown starts out disagreeing with the
        // window actually on screen. Naming it once here is what makes that hook the only
        // rule afterwards, rather than a second copy of the default living in the field.
        SelectedPreset = WindowPreset.All.FirstOrDefault(candidate => candidate.Window == Window);

        // The pointer can be over a measurement when the list changes under it - Clear, a
        // reset, or a new series - and a Hovered left pointing at something no longer in
        // the list aims the Delete key at a measurement nobody can see.
        Measurements.CollectionChanged += (_, _) =>
        {
            if (Hovered is Measurement stale && !Measurements.Contains(stale))
            {
                Hovered = null;
            }

            // Numbering restarts once the list is empty. Carrying on from #7 into a new
            // series would suggest six measurements had been made on it and lost.
            if (Measurements.Count == 0)
            {
                nextId = 0;
            }
        };
    }

    /// <summary>The 2x2 layout, in reading order (FR-201).</summary>
    public IReadOnlyList<ViewportViewModel> Viewports { get; }

    /// <summary>
    /// The row and column axes of each orientation's current plane, indexed by
    /// <see cref="PlaneOrientation"/>. This is the whole of the oblique state (FR-307):
    /// there is no angle, no rotation matrix and no history, only where the three frames
    /// currently point.
    /// </summary>
    /// <remarks>
    /// Keyed by orientation rather than held per pane, because two panes are axial - the
    /// MPR view and the MIP slab. Per-pane axes would let the slab drift away from the thin
    /// view it is supposed to be a thick version of, which is a bug you would only notice
    /// after rotating, and only by looking carefully.
    /// </remarks>
    private readonly (Vector3D Row, Vector3D Column)[] axes =
    [
        ReslicePlane.DisplayAxes(PlaneOrientation.Axial),
        ReslicePlane.DisplayAxes(PlaneOrientation.Coronal),
        ReslicePlane.DisplayAxes(PlaneOrientation.Sagittal),
    ];

    /// <summary>
    /// Bumped on every rotation. A pane whose own plane did not move still has to redraw
    /// its crosshair, because the lines in it are where the <em>other</em> planes cut
    /// through, and those turned. Without this the pane you are dragging in - the one pane
    /// guaranteed not to get a Plane change - would hold stale arms under the cursor.
    /// </summary>
    [ObservableProperty]
    private int axesVersion;

    /// <summary>
    /// Bumped by <see cref="Reset"/>. Zoom and pan are per-pane view state, so the only way
    /// the shell can return them to fit is to say that a reset happened and let each pane
    /// act on it.
    /// </summary>
    [ObservableProperty]
    private int resetVersion;

    public (Vector3D Row, Vector3D Column) AxesFor(PlaneOrientation orientation) =>
        axes[(int)orientation];

    /// <summary>Unit normal of an orientation's current plane, oblique or not.</summary>
    public Vector3D NormalFor(PlaneOrientation orientation)
    {
        (Vector3D row, Vector3D column) = axes[(int)orientation];
        return row.Cross(column);
    }

    /// <summary>
    /// FR-307. Turns the two orientations that are not <paramref name="viewport"/>'s about
    /// that pane's own normal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rotating the others and not this one is what makes the gesture feel like taking hold
    /// of a line. The image under your cursor stays perfectly still while the arms sweep
    /// across it and the two dependent panes re-cut the volume live. Rotating this pane
    /// instead would spin the anatomy you are pointing at, and you would lose the feature
    /// you were aiming for the moment you started to drag.
    /// </para>
    /// <para>
    /// Both other orientations turn by the same angle, so the three normals stay mutually
    /// perpendicular. A rotation preserves angles, which keeps the two that moved
    /// perpendicular to each other, and the axis is the third normal, which a rotation
    /// about it leaves exactly where it was. Nothing here re-orthogonalises the frame, and
    /// no number of drags can accumulate a shear into it.
    /// </para>
    /// </remarks>
    public void RotateAbout(ViewportViewModel viewport, double radians)
    {
        if (Volume is null || radians == 0)
        {
            return;
        }

        Vector3D axis = NormalFor(viewport.Orientation);

        for (int i = 0; i < axes.Length; i++)
        {
            if ((PlaneOrientation)i == viewport.Orientation)
            {
                continue;
            }

            (Vector3D row, Vector3D column) = axes[i];
            axes[i] = (row.RotatedAbout(axis, radians), column.RotatedAbout(axis, radians));
        }

        RefreshViewports();
        AxesVersion++;
    }

    /// <summary>
    /// FR-310. Back to the opening view: the three anatomical planes, the middle of the
    /// volume, and nothing drawn on it.
    /// </summary>
    /// <remarks>
    /// Zoom and pan go back to fit too. They live per pane in the view's own matrix rather
    /// than here, so this raises <see cref="ResetVersion"/> and each pane resets its own -
    /// the shell does not need to know how many panes there are or what a matrix is.
    /// </remarks>
    public void Reset()
    {
        if (Volume is not Volume loaded)
        {
            return;
        }

        ResetAxes();
        Measurements.Clear();
        SetCrosshair(Centre(loaded));
        ResetVersion++;

        // FR-310 reaches the 3D view too. An orbit is view state in exactly the way zoom
        // and pan are, and a reset that left the volume spun round would leave the one
        // pane still showing what the user asked to undo.
        Camera = Camera3D.Framing(loaded);

        // After the crosshair, because SetCrosshair is what re-cuts the panes with the
        // restored axes. This is what tells a pane whose own plane did not move to redraw
        // the arms of the two that did.
        AxesVersion++;
    }

    /// <summary>
    /// The middle voxel, in patient millimetres. Opening at a corner shows the edge of the
    /// scan range, which on a chest study is usually air.
    /// </summary>
    private static Point3D Centre(Volume volume) => volume.VoxelToPatient.Transform(
        (volume.DimX - 1) / 2.0, (volume.DimY - 1) / 2.0, (volume.DimZ - 1) / 2.0);

    /// <summary>Returns all three frames to the standard anatomical planes.</summary>
    private void ResetAxes()
    {
        for (int i = 0; i < axes.Length; i++)
        {
            axes[i] = ReslicePlane.DisplayAxes((PlaneOrientation)i);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVolume))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBanner))]
    private Volume? volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowLabel))]
    private WindowLevel window = WindowLevel.SoftTissue;

    /// <summary>
    /// The preset named in the dropdown, or null when the window on screen is not one of
    /// them. Two-way: picking a preset sets the window, and dragging the window off a
    /// preset clears the selection rather than leaving the dropdown naming something that
    /// is no longer displayed.
    /// </summary>
    [ObservableProperty]
    private WindowPreset? selectedPreset;

    [ObservableProperty]
    private SlabMode slabMode = SlabMode.Maximum;

    /// <summary>FR-207. How a slab is collapsed, when there is one.</summary>
    public IReadOnlyList<SlabProjection> SlabProjections { get; } =
    [
        new SlabProjection("MIP", SlabMode.Maximum),
        new SlabProjection("MinIP", SlabMode.Minimum),
        new SlabProjection("Average", SlabMode.Average),
    ];

    /// <summary>
    /// Whether the three planar panes are currently showing a projection rather than a
    /// plane (FR-207).
    /// </summary>
    /// <remarks>
    /// Zero thickness is off, and off is where the viewer starts: the panes are then
    /// exactly the single-plane reconstructions they have always been. It matters beyond
    /// the picture, because a projected pixel has no one depth. The Hounsfield readout and
    /// the measurement tools are both switched off while this is true, for the same reason
    /// the old fourth pane never offered them - a number read off a projection is a
    /// property of the thickest structure somewhere in the slab, not of the point under
    /// the cursor, and there is no honest way to label it.
    /// </remarks>
    public bool IsProjecting => SlabThicknessMillimetres > 0;

    /// <summary>
    /// The 3D camera (FR-608). Null until a volume is loaded, which is what the 3D view's
    /// FR-612 empty state turns on.
    /// </summary>
    [ObservableProperty]
    private Camera3D? camera;

    /// <summary>The transfer function the 3D view classifies with (FR-604, FR-605).</summary>
    [ObservableProperty]
    private TransferFunction volumePreset = TransferFunctionPreset.Bone;

    // A new study gets a camera framing it, and nothing else would do: the camera is in
    // patient millimetres, so the previous study's target is a coordinate in someone
    // else's frame of reference and would point the view at empty space.
    partial void OnVolumeChanged(Volume? value) =>
        Camera = value is null ? null : Camera3D.Framing(value);

    /// <summary>
    /// One table for all four panes. Rebuilt in place on every window change, so a
    /// window/level drag allocates nothing however fast the mouse moves.
    /// </summary>
    public WindowLevelLut Lut { get; } = new(WindowLevel.SoftTissue);

    partial void OnWindowChanged(WindowLevel value)
    {
        Lut.Rebuild(value);

        // The dropdown reflects the window rather than driving it. A right-drag that lands
        // exactly on a preset's numbers names it, one that leaves blanks the box, and the
        // window a series arrives with is named if it happens to be one of the five. The
        // assignment cannot recurse: OnSelectedPresetChanged sets Window back to a value it
        // already holds, so SetProperty finds no change and raises nothing.
        SelectedPreset = WindowPreset.All.FirstOrDefault(candidate => candidate.Window == value);

        RefreshViewports();
    }

    partial void OnSelectedPresetChanged(WindowPreset? value)
    {
        if (value is not null)
        {
            Window = value.Window;
        }
    }

    partial void OnSlabModeChanged(SlabMode value) => RefreshViewports();

    partial void OnSlabThicknessMillimetresChanged(double value) => RefreshViewports();

    /// <summary>Slab thickness in millimetres: off, or the FR-207 range of 1 to 100.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjecting))]
    private double slabThicknessMillimetres;

    /// <summary>
    /// Every measurement drawn on this volume, in patient millimetres (FR-401 to FR-404).
    /// </summary>
    /// <remarks>
    /// One flat list for the whole study rather than a list per pane. A measurement is a
    /// mark on the patient and not on a viewport: the plane test in
    /// <see cref="Measurement.IsVisibleOn"/> is what decides where it appears, so an axial
    /// measurement shows up in the axial pane and in a maximized copy of it without being
    /// stored twice or kept in step.
    /// </remarks>
    public ObservableCollection<Measurement> Measurements { get; } = [];

    /// <summary>
    /// FR-410. Adds a measurement under the next identifier. The only way one should enter
    /// the list, so that nothing on screen or in an export is ever unnumbered.
    /// </summary>
    public void AddMeasurement(Measurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        Measurements.Add(measurement with { Id = ++nextId });
    }

    private int nextId;

    /// <summary>Which shape the left button draws. None leaves the navigation gestures alone.</summary>
    [ObservableProperty]
    private MeasurementTool tool;

    /// <summary>
    /// FR-407. The measurement the pointer is over, which is the one the Delete key
    /// removes.
    /// </summary>
    /// <remarks>
    /// Held here rather than in the pane that detected it because the key is handled by the
    /// window: a viewport would have to hold keyboard focus to see it, and focus would then
    /// have to follow the mouse across four panes and back out to the toolbar. Only one
    /// measurement can be hovered at a time however many panes are showing it, which this
    /// gets for free and a flag per pane would not.
    /// </remarks>
    [ObservableProperty]
    private Measurement? hovered;

    /// <summary>FR-407. Removes the hovered measurement, if there is one.</summary>
    public void DeleteHovered()
    {
        if (Hovered is Measurement measurement)
        {
            Measurements.Remove(measurement);
            Hovered = null;
        }
    }

    /// <summary>
    /// FR-409. What the PNG export can capture. There is no selected one: the dropdown is
    /// the button, so picking an entry runs the export rather than storing a preference.
    /// </summary>
    public IReadOnlyList<ExportTarget> ExportTargets { get; }

    /// <summary>The pane filling the window on its own, or null for the 2x2 grid (FR-203).</summary>
    [ObservableProperty]
    private ViewportViewModel? maximized;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private double loadProgress;

    /// <summary>
    /// The rejection message when a series will not load, and the load summary when it
    /// does. Spec 1.6 rule 3: a bad series has to fail in a way that can be narrated, not
    /// as a stack trace or a hang.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusBanner))]
    private string? status;

    public bool HasVolume => Volume is not null;

    /// <summary>
    /// Whether the message belongs in the middle of the empty window rather than in a
    /// pane's corner overlay. A rejection is a paragraph and has to be readable; a load
    /// summary is one line and belongs out of the way of the image.
    /// </summary>
    public bool ShowStatusBanner => !HasVolume && !string.IsNullOrEmpty(Status);

    public string WindowLabel => $"W {Window.Width:0}  L {Window.Center:0}";

    /// <summary>Patient-space point every pane's plane passes through (FR-304).</summary>
    public Point3D Crosshair { get; private set; }

    /// <summary>
    /// Output pixel size for every pane, in millimetres: the volume's finest voxel spacing.
    /// One value shared by all four so that a distance measured on screen is the same
    /// distance in every view, and so zoom means the same thing everywhere.
    /// </summary>
    public double PixelSizeMillimetres { get; private set; } = 1.0;

    /// <summary>
    /// Moves the crosshair to a patient point and re-derives every plane. This is the
    /// whole of FR-304: nothing knows which pane the click came from.
    /// </summary>
    public void SetCrosshair(Point3D patient)
    {
        if (Volume is not Volume loaded)
        {
            return;
        }

        Crosshair = ClampToVolume(loaded, patient);

        foreach (ViewportViewModel viewport in Viewports)
        {
            viewport.Update(
                loaded, AxesFor(viewport.Orientation), Crosshair,
                PixelSizeMillimetres, SlabMode, SlabThicknessMillimetres);
        }

        OnPropertyChanged(nameof(Crosshair));

    }

    /// <summary>
    /// Scrolls one pane by whole steps along its own normal (FR-301). The step is the
    /// volume's own spacing in that direction, so a wheel notch advances to the next plane
    /// of real data rather than to the next interpolated one - on a 3 mm chest series that
    /// is 3 mm axially and 0.7 mm sagittally, which is what the data supports.
    /// </summary>
    public void ScrollAlongNormal(ViewportViewModel viewport, int notches)
    {
        if (Volume is not Volume loaded || viewport.Plane is not ReslicePlane plane)
        {
            return;
        }

        Vector3D normal = plane.Normal;

        SetCrosshair(Crosshair + normal.Scale(StepAlong(normal) * notches));
    }

    /// <summary>
    /// The volume's own spacing in a direction, in millimetres: the axis spacings weighted
    /// by the direction's components. Along an axis it is exactly that axis's spacing, and
    /// obliquely it lands between the three - which is what both callers want, the scroll
    /// step (FR-301) and the FR-406 visibility tolerance, since both are asking how far
    /// apart the planes of real data are in that direction.
    /// </summary>
    public double StepAlong(Vector3D direction)
    {
        if (Volume is not Volume loaded)
        {
            return 0;
        }

        Vector3D spacing = loaded.Spacing;

        return (Math.Abs(direction.X) * spacing.X) +
            (Math.Abs(direction.Y) * spacing.Y) +
            (Math.Abs(direction.Z) * spacing.Z);
    }

    /// <summary>
    /// FR-207: off, then 1 to 100 mm. Geometric rather than linear over the range, so the
    /// hundredfold span is crossed in about twenty-five notches instead of a hundred, and
    /// the step stays proportionate at both ends - one millimetre at a time is far too
    /// coarse near 1 mm and far too fine near 100 mm.
    /// </summary>
    /// <remarks>
    /// Zero cannot be reached by multiplying, so it is a rung of its own below 1 mm rather
    /// than the bottom of the geometric range. Scrolling down off 1 mm turns the slab off
    /// and scrolling up off zero turns it on at 1 mm, which makes the gesture reversible -
    /// the property nobody notices until it is missing.
    /// </remarks>
    public void AdjustSlabThickness(int notches)
    {
        if (SlabThicknessMillimetres < 1)
        {
            SlabThicknessMillimetres = notches > 0 ? 1.0 : 0.0;
            return;
        }

        double next = SlabThicknessMillimetres * Math.Pow(1.2, notches);
        SlabThicknessMillimetres = next < 1 ? 0.0 : Math.Min(next, 100.0);
    }

    /// <summary>FR-203. Double-clicking the maximized pane restores the grid.</summary>
    public void ToggleMaximized(ViewportViewModel viewport) =>
        Maximized = ReferenceEquals(Maximized, viewport) ? null : viewport;

    private void RefreshViewports()
    {
        if (Volume is Volume loaded)
        {
            foreach (ViewportViewModel viewport in Viewports)
            {
                viewport.Update(
                loaded, AxesFor(viewport.Orientation), Crosshair,
                PixelSizeMillimetres, SlabMode, SlabThicknessMillimetres);
            }
        }
    }

    /// <summary>
    /// Holds the crosshair on data. Clamping in voxel indices rather than in patient
    /// millimetres is what makes this correct for an obliquely acquired series, where the
    /// volume is a tilted box and its patient-space bounding box contains a good deal of
    /// nothing.
    /// </summary>
    private static Point3D ClampToVolume(Volume volume, Point3D patient)
    {
        Point3D v = volume.PatientToVoxel.Transform(patient);

        return volume.VoxelToPatient.Transform(
            Math.Clamp(v.X, 0, volume.DimX - 1),
            Math.Clamp(v.Y, 0, volume.DimY - 1),
            Math.Clamp(v.Z, 0, volume.DimZ - 1));
    }

    public async Task LoadAsync(string directory)
    {
        IsLoading = true;
        LoadProgress = 0;
        Status = "Scanning...";

        // Captured on the UI thread, so Progress<T> marshals the callbacks back to it and
        // the background work never touches a bound property directly (FR-108).
        Progress<double> scanning = new(p => LoadProgress = p * ScanShare);
        Progress<double> decoding = new(p => LoadProgress = ScanShare + (p * (1 - ScanShare)));

        try
        {
            // Two background passes with a decision between them, rather than one. The scan
            // is header-only and has to finish before there is anything to choose from; the
            // prompt has to run on the UI thread; the build is the expensive half and must
            // not start until the choice is made. Splitting them is what lets all three be
            // true at once (FR-102, FR-108).
            DirectoryScan scan = await Task.Run(() => loader.Scan(directory, scanning))
                .ConfigureAwait(true);

            if (scan.Series.Count == 0)
            {
                throw new SeriesRejectedException(
                    SeriesRejectionReason.TooFewSlices,
                    "No DICOM series was found in that folder.");
            }

            // FR-102. One series opens without asking; several put the question to the user,
            // largest first, which is the series the viewer used to take silently.
            SeriesDescriptor? chosen = scan.Series.Count == 1
                ? scan.Series[0]
                : prompt.Choose(scan.Series);

            if (chosen is not SeriesDescriptor series)
            {
                // Cancelled. Whatever was already loaded stays exactly as it was: an
                // abandoned open is not a reason to take the previous study off screen.
                Status = "Load cancelled.";
                return;
            }

            VolumeBuildResult result = await Task.Run(() =>
            {
                SeriesGeometry geometry = validator.Validate(series.Slices);

                return builder.Build(series, geometry, decoding);
            }).ConfigureAwait(true);

            Volume loaded = result.Volume;
            PixelSizeMillimetres = Math.Min(
                loaded.Spacing.X, Math.Min(loaded.Spacing.Y, loaded.Spacing.Z));

            // A new series opens orthogonal. Carrying the previous study's obliquity over
            // would present a plane chosen for someone else's anatomy as if it meant
            // something for this one, and there is no control that would undo it.
            ResetAxes();

            // Measurements are patient coordinates in the previous study's frame of
            // reference. Left in place they would draw somewhere plausible on the new
            // series and mean nothing at all.
            Measurements.Clear();

            // Order matters: the panes cannot build a plane before there is a volume, and
            // SetCrosshair is what builds them. Assigning Volume first also lets the view
            // size its bitmaps once, before the first render is asked for.
            Volume = loaded;

            // FR-306: open on the scanner's own window when the series carries one. The
            // preset dropdown deliberately shows no selection at this point, because none
            // of the five presets is what is on screen.
            Window = WindowLevel.InitialFor(loaded.Metadata);

            SetCrosshair(Centre(loaded));

            Status = $"{loaded.DimZ} slices, {loaded.DimX}x{loaded.DimY}, "
                + $"HU {result.MinimumHounsfield}..{result.MaximumHounsfield}";
        }
        catch (SeriesRejectedException rejected)
        {
            ClearVolume(rejected.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ClearVolume($"Could not read that folder: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 1;
        }
    }

    private void ClearVolume(string message)
    {
        Volume = null;
        Maximized = null;
        Status = message;
        Measurements.Clear();

        foreach (ViewportViewModel viewport in Viewports)
        {
            viewport.Clear();
        }
    }

    /// <summary>
    /// How much of the progress bar the header scan gets. Header parsing is roughly an
    /// order of magnitude cheaper than pixel decoding, so a bar that gave each phase half
    /// would sprint to the middle and then crawl - worse than no bar for judging whether
    /// the application has hung.
    /// </summary>
    private const double ScanShare = 0.15;
}
