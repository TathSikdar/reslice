using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Dicom;
using InterviewTrea.Rendering.Reslicing;
using InterviewTrea.Rendering.Windowing;

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

    public MainViewModel(SeriesLoader loader, GeometryValidator validator, VolumeBuilder builder)
    {
        this.loader = loader;
        this.validator = validator;
        this.builder = builder;

        Viewports =
        [
            new ViewportViewModel(PlaneOrientation.Axial, isSlab: false),
            new ViewportViewModel(PlaneOrientation.Coronal, isSlab: false),
            new ViewportViewModel(PlaneOrientation.Sagittal, isSlab: false),
            new ViewportViewModel(PlaneOrientation.Axial, isSlab: true),
        ];
    }

    /// <summary>The 2x2 layout, in reading order (FR-201).</summary>
    public IReadOnlyList<ViewportViewModel> Viewports { get; }

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

    /// <summary>
    /// One table for all four panes. Rebuilt in place on every window change, so a
    /// window/level drag allocates nothing however fast the mouse moves.
    /// </summary>
    public WindowLevelLut Lut { get; } = new(WindowLevel.SoftTissue);

    partial void OnWindowChanged(WindowLevel value)
    {
        Lut.Rebuild(value);

        if (SelectedPreset is WindowPreset preset && preset.Window != value)
        {
            SelectedPreset = null;
        }

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

    /// <summary>Slab thickness in millimetres, held to the FR-207 range of 1 to 100.</summary>
    [ObservableProperty]
    private double slabThicknessMillimetres = 20.0;

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
            viewport.Update(loaded, Crosshair, PixelSizeMillimetres, SlabMode, SlabThicknessMillimetres);
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
        Vector3D spacing = loaded.Spacing;
        double step =
            (Math.Abs(normal.X) * spacing.X) +
            (Math.Abs(normal.Y) * spacing.Y) +
            (Math.Abs(normal.Z) * spacing.Z);

        SetCrosshair(Crosshair + normal.Scale(step * notches));
    }

    /// <summary>
    /// FR-207: 1 to 100 mm. Geometric rather than linear, so the hundredfold range is
    /// crossed in about twenty-five notches instead of a hundred, and the step stays
    /// proportionate at both ends - one millimetre at a time is far too coarse near 1 mm
    /// and far too fine near 100 mm.
    /// </summary>
    public void AdjustSlabThickness(int notches) =>
        SlabThicknessMillimetres = Math.Clamp(
            SlabThicknessMillimetres * Math.Pow(1.2, notches), 1.0, 100.0);

    /// <summary>FR-203. Double-clicking the maximized pane restores the grid.</summary>
    public void ToggleMaximized(ViewportViewModel viewport) =>
        Maximized = ReferenceEquals(Maximized, viewport) ? null : viewport;

    private void RefreshViewports()
    {
        if (Volume is Volume loaded)
        {
            foreach (ViewportViewModel viewport in Viewports)
            {
                viewport.Update(loaded, Crosshair, PixelSizeMillimetres, SlabMode, SlabThicknessMillimetres);
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
            VolumeBuildResult result = await Task.Run(() =>
            {
                DirectoryScan scan = loader.Scan(directory, scanning);

                if (scan.Series.Count == 0)
                {
                    throw new SeriesRejectedException(
                        SeriesRejectionReason.TooFewSlices,
                        "No DICOM series was found in that folder.");
                }

                // FR-102 asks for a prompt when several series are present. The picker is
                // not in the approved control set, so the largest wins and the status line
                // says how many were passed over.
                SeriesDescriptor series = scan.Series[0];
                SeriesGeometry geometry = validator.Validate(series.Slices);

                return builder.Build(series, geometry, decoding);
            }).ConfigureAwait(true);

            Volume loaded = result.Volume;
            PixelSizeMillimetres = Math.Min(
                loaded.Spacing.X, Math.Min(loaded.Spacing.Y, loaded.Spacing.Z));

            // Order matters: the panes cannot build a plane before there is a volume, and
            // SetCrosshair is what builds them. Assigning Volume first also lets the view
            // size its bitmaps once, before the first render is asked for.
            Volume = loaded;

            // FR-306: open on the scanner's own window when the series carries one. The
            // preset dropdown deliberately shows no selection at this point, because none
            // of the five presets is what is on screen.
            Window = WindowLevel.InitialFor(loaded.Metadata);

            // Centre of the volume. Opening at a corner shows the edge of the scan range,
            // which on a chest study is usually air.
            SetCrosshair(loaded.VoxelToPatient.Transform(
                (loaded.DimX - 1) / 2.0, (loaded.DimY - 1) / 2.0, (loaded.DimZ - 1) / 2.0));

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
