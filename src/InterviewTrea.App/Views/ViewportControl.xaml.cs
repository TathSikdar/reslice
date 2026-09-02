using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using InterviewTrea.App.ViewModels;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Measurements;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Reslicing;

namespace InterviewTrea.App.Views;

/// <summary>
/// One pane of the 2x2 layout: a <see cref="WriteableBitmap"/>, a crosshair, and the
/// gestures that drive them.
/// </summary>
/// <remarks>
/// <para>
/// The scene canvas is sized in plane pixels and carries a single matrix that composes
/// fit, zoom and pan. Everything drawn inside it - the image and both crosshair lines - is
/// positioned in the renderer's own coordinates, so there is no second copy of the
/// geometry in screen space to keep in step.
/// </para>
/// <para>
/// That matrix is also the map back: inverting it turns a mouse position into a plane
/// pixel, and the plane turns that into patient millimetres. Cross-viewport linking
/// (FR-304) is then just handing that point to the shared view model, which is why no code
/// here knows anything about the other three panes.
/// </para>
/// </remarks>
public partial class ViewportControl : UserControl
{
    // Reused across frames. Reallocating a bitmap and a buffer on every wheel notch is a
    // collection per frame, which is what a scroll cannot afford.
    private WriteableBitmap? bitmap;
    private byte[] pixels = [];

    // Fit is derived from the pane size and the plane size; user is everything the mouse
    // has done since. Keeping them apart is what lets the window be resized without
    // throwing away the zoom, and lets a new series reset the zoom without measuring.
    private Matrix fit = Matrix.Identity;
    private Matrix user = Matrix.Identity;

    private Point lastMousePosition;

    // Non-null only between grabbing a crosshair arm and letting go, holding the pointer's
    // angle about the crosshair as of the last move. The drag applies differences rather
    // than a total, which is what lets the rotation compose in the stored axes instead of
    // needing an accumulated angle and a plane to measure it from.
    private double? lastArmAngle;

    // The measurement being dragged out, or null. Held here rather than in the shell's
    // list so that an abandoned drag leaves nothing behind, and so a half-drawn shape can
    // never be counted, exported or deleted while the button is still down.
    private Measurement? pending;

    /// <summary>Which part of a measurement a Move press took hold of (FR-411).</summary>
    private enum Grab
    {
        Whole,
        Start,
        End,
    }

    // Non-null only while a Move drag is under way. It holds the measurement as it was at
    // the press, not as it is now, so every move recomputes from the original and the
    // shape cannot accumulate drift over a long drag - the arithmetic is the same whether
    // the pointer arrives in one step or two hundred.
    private (int Index, Measurement Original, Grab Grab, Point3D Grabbed)? editing;

    private MainViewModel? subscribedShell;
    private ViewportViewModel? subscribedViewport;

    public ViewportControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>The shared state: volume, window, crosshair, slab settings.</summary>
    public MainViewModel? Shell
    {
        get => (MainViewModel?)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(MainViewModel), typeof(ViewportControl),
        new PropertyMetadata(null, OnShellChanged));

    /// <summary>FR-205. 1.00 is fit-to-pane, so the number means the same thing in every pane.</summary>
    public string ZoomLabel
    {
        get => (string)GetValue(ZoomLabelProperty);
        private set => SetValue(ZoomLabelPropertyKey, value);
    }

    private static readonly DependencyPropertyKey ZoomLabelPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ZoomLabel), typeof(string), typeof(ViewportControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ZoomLabelProperty = ZoomLabelPropertyKey.DependencyProperty;

    /// <summary>
    /// FR-405. The Hounsfield value of the voxel under the pointer, or empty when there is
    /// no voxel there.
    /// </summary>
    /// <remarks>
    /// Nearest-neighbour, deliberately: this is the number a user reads off a suspicious
    /// pixel and then expects to see inside the mean of an ROI drawn round it, so it has to
    /// be sampled the same way <c>RoiStatistics</c> samples. A trilinear readout would
    /// report values that exist nowhere in the data and would disagree with the statistics
    /// by a few HU on every edge.
    /// </remarks>
    public string HoverLabel
    {
        get => (string)GetValue(HoverLabelProperty);
        private set => SetValue(HoverLabelPropertyKey, value);
    }

    private static readonly DependencyPropertyKey HoverLabelPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HoverLabel), typeof(string), typeof(ViewportControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HoverLabelProperty = HoverLabelPropertyKey.DependencyProperty;

    private static void OnShellChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ViewportControl control = (ViewportControl)d;

        if (control.subscribedShell is MainViewModel old)
        {
            old.PropertyChanged -= control.OnShellPropertyChanged;
            old.Measurements.CollectionChanged -= control.OnMeasurementsChanged;
        }

        control.subscribedShell = e.NewValue as MainViewModel;

        if (control.subscribedShell is MainViewModel shell)
        {
            shell.PropertyChanged += control.OnShellPropertyChanged;
            shell.Measurements.CollectionChanged += control.OnMeasurementsChanged;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (subscribedViewport is ViewportViewModel old)
        {
            old.PropertyChanged -= OnViewportPropertyChanged;
        }

        subscribedViewport = e.NewValue as ViewportViewModel;

        if (subscribedViewport is ViewportViewModel viewport)
        {
            viewport.PropertyChanged += OnViewportPropertyChanged;
        }

        Refresh(resetZoom: true);
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The window changes the pixels but not the geometry, so there is nothing to
        // re-fit; a new volume changes both.
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Window):
                Render();
                break;

            case nameof(MainViewModel.Volume):
                Refresh(resetZoom: true);
                break;

            // A rotation leaves this pane's own plane alone when the drag started here, so
            // no Plane change arrives and the image is already correct - but the arms drawn
            // on it are the other planes and they have moved.
            case nameof(MainViewModel.AxesVersion):
                UpdateTransform();
                break;

            // FR-407. Only the outlines change, and only their weight, so the image and
            // the transform are left alone.
            case nameof(MainViewModel.Hovered):
                DrawMeasurements();
                break;

            // The slab settings leave the plane alone and change only what is projected
            // through it, so the pane that draws a slab redraws and the other three have
            // nothing to do.
            case nameof(MainViewModel.SlabMode):
            case nameof(MainViewModel.SlabThicknessMillimetres):
                if (DataContext is ViewportViewModel { IsSlab: true })
                {
                    Render();
                }

                break;

            default:
                break;
        }
    }

    private void OnViewportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Plane and Crosshair are separate notifications on purpose. Clicking inside this
        // pane moves the crosshair without moving this pane's plane at all - the plane is
        // anchored, only the offset along its normal comes from the crosshair - so
        // ReslicePlane's value equality suppresses the Plane notification and only the
        // crosshair lines need redrawing. Handling only Plane would leave the pane you
        // clicked in as the one pane whose crosshair did not move.
        switch (e.PropertyName)
        {
            case nameof(ViewportViewModel.Plane):
                Refresh(resetZoom: false);
                break;

            case nameof(ViewportViewModel.Crosshair):
                UpdateTransform();
                break;

            default:
                break;
        }
    }

    private void OnMeasurementsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        DrawMeasurements();

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTransform();

    private void Refresh(bool resetZoom)
    {
        if (resetZoom)
        {
            // A new series starts at fit. Carrying the previous study's zoom over is
            // disorienting and, on a differently sized volume, can leave it off-screen.
            user = Matrix.Identity;
        }

        EnsureBitmap();
        Render();
        UpdateTransform();
    }

    private void EnsureBitmap()
    {
        if (DataContext is not ViewportViewModel viewport || viewport.Plane is not ReslicePlane plane)
        {
            SliceImage.Source = null;
            bitmap = null;
            return;
        }

        if (bitmap is not null && bitmap.PixelWidth == plane.Width && bitmap.PixelHeight == plane.Height)
        {
            return;
        }

        // 96 dpi, deliberately. Iteration 2 abused the bitmap's DPI to correct anisotropic
        // aspect; the plane's grid is isotropic in millimetres now, so the pixels are
        // already square and any DPI other than the default would re-introduce the
        // distortion it used to remove.
        bitmap = new WriteableBitmap(plane.Width, plane.Height, 96, 96, PixelFormats.Gray8, palette: null);
        pixels = new byte[plane.PixelCount];

        SliceImage.Source = bitmap;
        SliceImage.Width = plane.Width;
        SliceImage.Height = plane.Height;
        Scene.Width = plane.Width;
        Scene.Height = plane.Height;
    }

    private void Render()
    {
        if (Shell is not MainViewModel shell ||
            shell.Volume is not Volume volume ||
            DataContext is not ViewportViewModel viewport ||
            viewport.Plane is not ReslicePlane plane ||
            bitmap is null)
        {
            return;
        }

        // Property changes arrive one at a time, so the bitmap can briefly disagree with
        // the plane. Two compares are cheaper than making the view model's assignment
        // order load-bearing, and they cannot be broken by a later edit to it.
        if (bitmap.PixelWidth != plane.Width || bitmap.PixelHeight != plane.Height)
        {
            return;
        }

        if (viewport.IsSlab)
        {
            SlabRenderer.Render(
                volume, plane, shell.SlabMode, shell.SlabThicknessMillimetres, shell.Lut, pixels);
        }
        else
        {
            PlaneRenderer.Render(volume, plane, shell.Lut, pixels);
        }

        bitmap.WritePixels(
            new Int32Rect(0, 0, plane.Width, plane.Height), pixels, stride: plane.Width, offset: 0);
    }

    private void UpdateTransform()
    {
        if (DataContext is not ViewportViewModel viewport || viewport.Plane is not ReslicePlane plane)
        {
            return;
        }

        double scale = Math.Min(Host.ActualWidth / plane.Width, Host.ActualHeight / plane.Height);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            return;
        }

        fit = Matrix.Identity;
        fit.Scale(scale, scale);
        fit.Translate(
            (Host.ActualWidth - (plane.Width * scale)) / 2,
            (Host.ActualHeight - (plane.Height * scale)) / 2);

        // Fit first, then whatever the mouse has done - so zoom and pan operate in screen
        // space, which is the space the gestures are expressed in.
        Matrix total = fit;
        total.Append(user);
        ViewTransform.Matrix = total;

        UpdateCrosshair(plane, total.M11);
        DrawMeasurements();
        ZoomLabel = string.Create(CultureInfo.InvariantCulture, $"zoom {user.M11:0.00}x");
    }

    private void UpdateCrosshair(ReslicePlane plane, double totalScale)
    {
        if (DataContext is not ViewportViewModel viewport || Shell is not MainViewModel shell)
        {
            return;
        }

        (double column, double row) = plane.ToPixel(viewport.Crosshair);

        // The lines live inside the scaled scene, so their stroke width has to be divided
        // back out or the crosshair thickens with the zoom until it hides the anatomy it
        // is pointing at.
        double thickness = CrosshairThickness / totalScale;

        // Half-length, drawn from the crosshair outwards in both directions. Since FR-307
        // the lines are not axis-aligned, so there is no edge coordinate to run to; the
        // plane's diagonal reaches past every corner from anywhere inside it, and the
        // pane's own ClipToBounds trims what hangs over.
        double reach = Math.Sqrt((plane.Width * plane.Width) + (plane.Height * plane.Height));

        Place(VerticalLine, LineDirection(plane, shell.NormalFor(viewport.VerticalLinePlane)));
        Place(HorizontalLine, LineDirection(plane, shell.NormalFor(viewport.HorizontalLinePlane)));

        void Place(Line line, Vector? direction)
        {
            if (direction is not Vector along)
            {
                line.Visibility = Visibility.Collapsed;
                return;
            }

            line.Visibility = Visibility.Visible;
            line.X1 = column - (along.X * reach);
            line.Y1 = row - (along.Y * reach);
            line.X2 = column + (along.X * reach);
            line.Y2 = row + (along.Y * reach);
            line.StrokeThickness = thickness;
        }
    }

    /// <summary>
    /// Where another plane cuts this one, as a unit direction in output pixel coordinates.
    /// </summary>
    /// <remarks>
    /// Two planes meet along the cross product of their normals: that is the one direction
    /// perpendicular to both normals, and therefore the only direction lying in both
    /// planes. Dotting it with this plane's two step vectors resolves it into pixel
    /// coordinates, and normalising afterwards is what removes the millimetres-per-pixel
    /// factor those steps carry - only the direction is wanted here.
    ///
    /// Null when the two normals are parallel. An orthonormal triad cannot produce that,
    /// so this is a guard against a frame that has gone wrong rather than a case to handle.
    /// </remarks>
    private static Vector? LineDirection(ReslicePlane plane, Vector3D otherNormal)
    {
        Vector3D along = plane.Normal.Cross(otherNormal);
        if (along.LengthSquared < 1e-12)
        {
            return null;
        }

        Vector pixel = new(along.Dot(plane.RowStep), along.Dot(plane.ColumnStep));
        pixel.Normalize();
        return pixel;
    }

    private double CrosshairThickness =>
        TryFindResource("Size.Crosshair") is double value ? value : 1.0;

    /// <summary>
    /// Redraws every measurement that belongs on this pane's plane, plus the one currently
    /// being dragged out.
    /// </summary>
    /// <remarks>
    /// Cheap enough to do wholesale on every crosshair move, zoom notch and pan: a handful
    /// of outlines with no bitmap behind them. Diffing the canvas against the list would be
    /// more code guarding a cost nobody can measure.
    /// </remarks>
    private void DrawMeasurements()
    {
        Annotations.Children.Clear();
        Readouts.Children.Clear();

        if (Shell is not MainViewModel shell ||
            shell.Volume is not Volume volume ||
            DataContext is not ViewportViewModel viewport ||
            viewport.IsSlab ||
            viewport.Plane is not ReslicePlane plane)
        {
            return;
        }

        // FR-406. Half a step to the next plane of real data: a measurement is drawn on the
        // slice it was made on and on nothing else. The spec's "half a slice thickness"
        // reads as a distance here because an oblique plane has no slices to count.
        double tolerance = shell.StepAlong(plane.Normal) / 2;
        double thickness = MeasurementThickness / Math.Max(ViewTransform.Matrix.M11, 1e-9);

        foreach (Measurement measurement in shell.Measurements)
        {
            if (measurement.IsVisibleOn(plane, tolerance))
            {
                Draw(measurement, plane, thickness, volume, ReferenceEquals(measurement, shell.Hovered));
            }
        }

        // The pending one needs no visibility test: it is being drawn on this plane, in
        // this pane, right now.
        if (pending is Measurement drawing)
        {
            Draw(drawing, plane, thickness, volume, hovered: false);
        }
    }

    /// <summary>Adds one measurement's outline to the annotation canvas, in plane pixels.</summary>
    /// <remarks>
    /// The two ends are projected back through <see cref="ReslicePlane.ToPixel"/> and the
    /// region shapes are the axis-aligned box between them. That is exact rather than
    /// approximate, because a measurement is only ever drawn on a plane parallel to the
    /// frame it was made in, and that frame's axes are the pane's own - so "across" and
    /// "down" in the measurement are the pixel x and y here.
    /// </remarks>
    private void Draw(
        Measurement measurement, ReslicePlane plane, double thickness, Volume volume, bool hovered)
    {
        (double startColumn, double startRow) = plane.ToPixel(measurement.Start);
        (double endColumn, double endRow) = plane.ToPixel(measurement.End);

        Shape shape;

        if (measurement.Kind == MeasurementKind.Distance)
        {
            shape = new Line { X1 = startColumn, Y1 = startRow, X2 = endColumn, Y2 = endRow };
        }
        else
        {
            shape = measurement.Kind == MeasurementKind.Ellipse ? new Ellipse() : new Rectangle();
            shape.Width = Math.Abs(endColumn - startColumn);
            shape.Height = Math.Abs(endRow - startRow);
            Canvas.SetLeft(shape, Math.Min(startColumn, endColumn));
            Canvas.SetTop(shape, Math.Min(startRow, endRow));
        }

        shape.Stroke = MeasurementBrush;

        // The hovered one is drawn heavier, which is the whole of the FR-407 affordance:
        // it says which measurement the Delete key would take without adding a control,
        // a selection colour or a mode to be in.
        shape.StrokeThickness = hovered ? thickness * 2 : thickness;

        // Not a colour: a hit-test surface. A 1.5-pixel outline is far too fine to point
        // at, so the interior of a region and a fat invisible line under a distance are
        // what the pointer actually catches.
        if (shape is Line)
        {
            Annotations.Children.Add(new Line
            {
                X1 = startColumn,
                Y1 = startRow,
                X2 = endColumn,
                Y2 = endRow,
                Stroke = Brushes.Transparent,
                StrokeThickness = HitPixels / Math.Max(ViewTransform.Matrix.M11, 1e-9),
                Tag = measurement,
            });
        }
        else
        {
            shape.Fill = Brushes.Transparent;
        }

        shape.Tag = measurement;
        Annotations.Children.Add(shape);

        // FR-411. Grab handles, drawn only while the Move tool is selected and only on the
        // measurement under the pointer. A handle on every measurement all the time would
        // be permanent clutter on the image for a gesture used a few times a session.
        if (hovered && Shell?.Tool == MeasurementTool.Move)
        {
            Handle(measurement, startColumn, startRow);
            Handle(measurement, endColumn, endRow);
        }
        Label(measurement, volume, Math.Max(startColumn, endColumn), Math.Min(startRow, endRow));
    }

    /// <summary>
    /// FR-411. A small square on one of the two points the creating drag passed through,
    /// which are the two the Move tool can take hold of individually.
    /// </summary>
    /// <remarks>
    /// Sized in screen pixels and divided back out of the view transform, like every other
    /// annotation here, so a handle stays the same size to point at however far the image
    /// is zoomed in. It carries the measurement in its Tag as well, so grabbing a handle
    /// hit-tests as grabbing the measurement.
    /// </remarks>
    private void Handle(Measurement measurement, double column, double row)
    {
        double side = HandlePixels / Math.Max(ViewTransform.Matrix.M11, 1e-9);

        Rectangle handle = new()
        {
            Width = side,
            Height = side,
            Fill = MeasurementBrush,
            Tag = measurement,
        };

        Canvas.SetLeft(handle, column - (side / 2));
        Canvas.SetTop(handle, row - (side / 2));
        Annotations.Children.Add(handle);
    }

    /// <summary>
    /// Puts a measurement's numbers beside it: length for a distance, area and the four
    /// FR-403 statistics for a region.
    /// </summary>
    /// <remarks>
    /// Recomputed on every redraw rather than cached on the measurement. A region's
    /// statistics are a function of the voxels under it, and nothing here is expensive
    /// enough - a few thousand samples - to be worth a cache that would have to be
    /// invalidated when a new series is loaded.
    /// </remarks>
    private void Label(Measurement measurement, Volume volume, double column, double row)
    {
        Point anchor = ViewTransform.Matrix.Transform(new Point(column, row));

        TextBlock label = new()
        {
            Text = Readout(measurement, volume),
            Style = TryFindResource("Style.Measurement") as Style,
        };

        Canvas.SetLeft(label, anchor.X + LabelOffsetPixels);
        Canvas.SetTop(label, anchor.Y);
        Readouts.Children.Add(label);
    }

    /// <summary>
    /// One decimal on the millimetres and on the mean, none on the extremes. A distance
    /// read off a 0.7 mm grid is not good to a hundredth of a millimetre, and a Hounsfield
    /// value is an integer in the data - printing more digits than the measurement supports
    /// invites the numbers to be trusted further than they should be.
    /// </summary>
    private static string Readout(Measurement measurement, Volume volume)
    {
        // FR-410. The pending measurement has no number yet - it is not in the list - so it
        // draws without one rather than under a #0 that would then change on release.
        string id = measurement.Id > 0
            ? string.Create(CultureInfo.InvariantCulture, $"#{measurement.Id} ")
            : string.Empty;

        if (measurement.Kind == MeasurementKind.Distance)
        {
            return string.Create(
                CultureInfo.InvariantCulture, $"{id}{measurement.LengthMillimetres:0.0} mm");
        }

        string area = string.Create(
            CultureInfo.InvariantCulture, $"{id}{measurement.AreaSquareMillimetres:0.0} mm²");

        RoiStatistics statistics = RoiStatistics.Compute(measurement, volume);

        // A region can be dragged out entirely off the end of the data, and a mean of
        // nothing is not zero. The area is still true, so it is still shown.
        return statistics.SampleCount == 0
            ? area
            : area + string.Create(
                CultureInfo.InvariantCulture,
                $"\n{statistics.MeanHounsfield:0.0} ± {statistics.StandardDeviationHounsfield:0.0} HU"
                + $"\n{statistics.MinimumHounsfield} .. {statistics.MaximumHounsfield} HU");
    }

    private Brush? MeasurementBrush => TryFindResource("Brush.Accent") as Brush;

    private double MeasurementThickness =>
        TryFindResource("Size.Measurement") is double value ? value : 1.0;

    // How close to an outline counts as pointing at it, and how far a label sits from the
    // shape it belongs to. Both in screen pixels, both calibration knobs: they depend on
    // pointer precision and display density, not on any geometry.
    private const double HitPixels = 8.0;
    private const double LabelOffsetPixels = 6.0;
    private const double HandlePixels = 7.0;

    /// <summary>
    /// FR-407. The measurement under the pointer, found by asking WPF what it hit rather
    /// than by re-deriving the geometry: the shapes are already on screen in the right
    /// place, and a second copy of the outline arithmetic here would be one more thing to
    /// keep in step with the first.
    /// </summary>
    private Measurement? MeasurementUnder(Point mousePosition) =>
        Annotations.InputHitTest(Host.TranslatePoint(mousePosition, Annotations)) is FrameworkElement hit
            ? hit.Tag as Measurement
            : null;

    /// <summary>Turns a mouse position into a patient-space point, or null if there is no plane.</summary>
    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        HoverLabel = string.Empty;

        if (Shell is MainViewModel shell)
        {
            shell.Hovered = null;
        }
    }

    /// <summary>FR-405. The voxel value under the pointer, formatted, or empty.</summary>
    /// <remarks>
    /// Empty on the slab pane, because there is no single voxel under the cursor there: the
    /// displayed pixel is a projection through a thickness, and quoting the value on the
    /// centre plane would put a number on screen that does not belong to the pixel it sits
    /// on. Empty off the end of the data too, rather than <see cref="Volume.OutsideValue"/>
    /// - that constant is a rendering convenience so the edge of the volume draws dark, and
    /// no scanner ever recorded it.
    /// </remarks>
    private string HounsfieldUnder(Point mousePosition)
    {
        if (DataContext is not ViewportViewModel viewport || viewport.IsSlab ||
            Shell?.Volume is not Volume volume ||
            ToPatient(mousePosition) is not Point3D patient)
        {
            return string.Empty;
        }

        Point3D voxel = volume.PatientToVoxel.Transform(patient);

        return volume.ContainsContinuous(voxel.X, voxel.Y, voxel.Z)
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{volume.SampleNearest(voxel.X, voxel.Y, voxel.Z),5} HU")
            : string.Empty;
    }

    private Point3D? ToPatient(Point mousePosition)
    {
        if (DataContext is not ViewportViewModel viewport || viewport.Plane is not ReslicePlane plane)
        {
            return null;
        }

        Matrix inverse = ViewTransform.Matrix;
        if (!inverse.HasInverse)
        {
            return null;
        }

        inverse.Invert();
        Point scene = inverse.Transform(mousePosition);
        return plane.ToPatient(scene.X, scene.Y);
    }

    // Drag sensitivity, in Hounsfield units per pixel of mouse travel, and zoom per wheel
    // notch. Calibration knobs, not derived constants: what feels right depends on the
    // display size and the pointer settings, and the only way to set them is to drag on a
    // real chest study. Width moves faster than level because its useful range is wider -
    // a lung window is 1500 wide and a brain window is 80.
    private const double WidthPerPixel = 4.0;
    private const double CenterPerPixel = 2.0;
    private const double ZoomPerNotch = 1.15;

    // How close to a crosshair arm counts as taking hold of it, and how far from the
    // crosshair you have to be for that to apply. Both in screen pixels, and both
    // calibration knobs rather than derived values: what counts as "on the line" depends on
    // pointer precision and display density, not on any geometry. The dead zone exists
    // because the two arms cross at the centre, where a grab could not say which one was
    // meant - and where the gesture almost always intended is moving the crosshair, not
    // turning it.
    private const double ArmGrabPixels = 6.0;
    private const double ArmDeadZonePixels = 24.0;

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Shell is not MainViewModel shell || DataContext is not ViewportViewModel viewport)
        {
            return;
        }

        int notches = e.Delta / Mouse.MouseWheelDeltaForOneLine;

        // Shift is tested before Control because Ctrl+Shift sets both flags, and the
        // thickness gesture is the more specific of the two.
        if (viewport.IsSlab && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            shell.AdjustSlabThickness(notches);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // ScaleAt post-multiplies about a point in the already-transformed space,
            // which is the screen. That is what keeps the anatomy under the cursor from
            // sliding away as the image grows - scaling about the image's own centre
            // would move it.
            Point cursor = e.GetPosition(Host);
            double factor = Math.Pow(ZoomPerNotch, notches);
            user.ScaleAt(factor, factor, cursor.X, cursor.Y);
            UpdateTransform();
        }
        else
        {
            shell.ScrollAlongNormal(viewport, notches);
        }

        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Host.Focus();
        lastMousePosition = e.GetPosition(Host);

        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            if (Shell is MainViewModel shell && DataContext is ViewportViewModel viewport)
            {
                shell.ToggleMaximized(viewport);
            }
        }
        else if (e.ChangedButton == MouseButton.Left &&
            !TryStartEdit(lastMousePosition) &&
            !TryStartMeasurement(lastMousePosition))
        {
            // FR-307. One button, two gestures, told apart by where the press landed: on an
            // arm it turns the other planes, anywhere else it moves the crosshair. That
            // split is why the rotation needs no mode and no control of its own.
            lastArmAngle = ArmGrabAngle(lastMousePosition);

            if (lastArmAngle is null)
            {
                MoveCrosshairTo(lastMousePosition);
            }
        }

        if (e.ChangedButton is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
        {
            Host.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Shell is not MainViewModel shell || DataContext is not ViewportViewModel viewport)
        {
            return;
        }

        if (!Host.IsMouseCaptured)
        {
            // Hover affordance, not a control: without it the arms look exactly like the
            // lines they were before FR-307 and the rotation is undiscoverable.
            Point hover = e.GetPosition(Host);
            Measurement? under = MeasurementUnder(hover);

            HoverLabel = HounsfieldUnder(hover);
            shell.Hovered = under;

            // Three cursors for three things the button would do here. The Move cursor wins
            // where both apply, because with that tool selected an arm underneath is not
            // what the press will take.
            Host.Cursor = shell.Tool == MeasurementTool.Move && under is not null ? Cursors.SizeAll
                : ArmGrabAngle(hover) is null ? null : Cursors.Hand;

            return;
        }

        Point current = e.GetPosition(Host);
        HoverLabel = HounsfieldUnder(current);
        Vector delta = current - lastMousePosition;
        lastMousePosition = current;

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (editing is { } edit)
            {
                DragEdit(edit, current);
            }
            else if (pending is Measurement drawing)
            {
                if (ToPatient(current) is Point3D patient)
                {
                    pending = drawing with { End = patient };
                    DrawMeasurements();
                }
            }
            else if (lastArmAngle is double previous && AngleAboutCrosshair(current) is double angle)
            {
                // IEEERemainder folds the difference into [-pi, pi], which matters only at
                // the one place atan2 wraps: dragging an arm through due west would
                // otherwise register as very nearly a full turn the other way.
                shell.RotateAbout(viewport, Math.IEEERemainder(angle - previous, 2 * Math.PI));
                lastArmAngle = angle;
            }
            else
            {
                // Dragging keeps setting the crosshair, so the other two panes track the
                // pointer live. That is the FR-304 demo beat.
                MoveCrosshairTo(current);
            }
        }
        else if (e.RightButton == MouseButtonState.Pressed)
        {
            // FR-302: horizontal is width, vertical is level. Screen y grows downward, so
            // the sign is flipped - dragging up must brighten, which is what every
            // workstation does and what a radiologist's hand already expects.
            shell.Window = shell.Window.AdjustedBy(delta.X * WidthPerPixel, -delta.Y * CenterPerPixel);
        }
        else if (e.MiddleButton == MouseButtonState.Pressed)
        {
            user.Translate(delta.X, delta.Y);
            UpdateTransform();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        lastArmAngle = null;
        editing = null;
        CommitMeasurement();

        if (Host.IsMouseCaptured)
        {
            Host.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    /// <summary>
    /// FR-411. Takes hold of an existing measurement if the Move tool is selected and the
    /// press landed on one, and reports whether it took the press.
    /// </summary>
    /// <remarks>
    /// Which end, if either, is decided in screen pixels rather than in millimetres,
    /// because the question being asked is whether a hand landed on a handle - a tolerance
    /// in patient space would be several handles wide zoomed out and a fraction of one
    /// zoomed in.
    /// </remarks>
    private bool TryStartEdit(Point mousePosition)
    {
        if (Shell is not MainViewModel shell ||
            shell.Tool != MeasurementTool.Move ||
            DataContext is not ViewportViewModel viewport ||
            viewport.Plane is not ReslicePlane plane ||
            MeasurementUnder(mousePosition) is not Measurement target ||
            ToPatient(mousePosition) is not Point3D grabbed)
        {
            return false;
        }

        (double startColumn, double startRow) = plane.ToPixel(target.Start);
        (double endColumn, double endRow) = plane.ToPixel(target.End);

        Point start = ViewTransform.Matrix.Transform(new Point(startColumn, startRow));
        Point end = ViewTransform.Matrix.Transform(new Point(endColumn, endRow));

        Grab grab = (mousePosition - start).Length <= HitPixels ? Grab.Start
            : (mousePosition - end).Length <= HitPixels ? Grab.End
            : Grab.Whole;

        editing = (shell.Measurements.IndexOf(target), target, grab, grabbed);

        return true;
    }

    /// <summary>
    /// FR-411. Moves or resizes the measurement being dragged, in patient millimetres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both the grabbed point and the current one come back through
    /// <see cref="ReslicePlane.ToPatient"/>, so the difference between them lies in this
    /// pane's plane by construction. A translation by it therefore cannot push a
    /// measurement off the slice it belongs to, and the frame - which is what FR-406 tests
    /// against - needs no adjusting and gets none.
    /// </para>
    /// <para>
    /// Every move is computed from the measurement as it was at the press rather than from
    /// its current position, so nothing accumulates over the drag. The record is replaced
    /// in the list rather than mutated, because a measurement is a value; the identifier
    /// rides along through <c>with</c>, so an edited measurement keeps the number it was
    /// exported under.
    /// </para>
    /// </remarks>
    private void DragEdit(
        (int Index, Measurement Original, Grab Grab, Point3D Grabbed) edit, Point mousePosition)
    {
        if (Shell is not MainViewModel shell ||
            edit.Index < 0 || edit.Index >= shell.Measurements.Count ||
            ToPatient(mousePosition) is not Point3D patient)
        {
            return;
        }

        Vector3D delta = patient - edit.Grabbed;

        Measurement moved = edit.Grab switch
        {
            Grab.Start => edit.Original with { Start = patient },
            Grab.End => edit.Original with { End = patient },
            _ => edit.Original with
            {
                Start = edit.Original.Start + delta,
                End = edit.Original.End + delta,
            },
        };

        shell.Measurements[edit.Index] = moved;

        // After the replacement, not before: swapping the element out drops the old one
        // from the list, and the view model clears a Hovered that is no longer in it.
        shell.Hovered = moved;
    }

    /// <summary>
    /// Begins a measurement if a tool is selected, and reports whether it took the press.
    /// </summary>
    /// <remarks>
    /// A tool takes the left button over from navigation completely - no arm grab, no
    /// crosshair move. One press cannot mean two things, and a drawing gesture that
    /// sometimes scrolled the slice out from under the drawing would be unusable.
    ///
    /// The frame is anchored at the pressed point rather than at the crosshair. Both lie on
    /// this plane, so both give the same FR-406 distance, but the pressed point is the one
    /// already computed here and it cannot later be moved by a click in another pane.
    /// </remarks>
    private bool TryStartMeasurement(Point mousePosition)
    {
        if (Shell is not MainViewModel shell ||
            shell.Tool is MeasurementTool.None or MeasurementTool.Move ||
            DataContext is not ViewportViewModel viewport ||
            viewport.IsSlab ||
            ToPatient(mousePosition) is not Point3D patient)
        {
            return false;
        }

        (Vector3D row, Vector3D column) = shell.AxesFor(viewport.Orientation);
        pending = new Measurement(
            shell.Tool.ToKind(), new MeasurementFrame(patient, row, column), patient, patient);

        return true;
    }

    /// <summary>
    /// Ends a drawing drag. A drag shorter than one output pixel is dropped as a mis-click:
    /// it would store a zero-length distance or an empty region, and the user would then
    /// have to find and delete something they cannot see.
    /// </summary>
    private void CommitMeasurement()
    {
        if (pending is not Measurement drawn || Shell is not MainViewModel shell)
        {
            return;
        }

        pending = null;

        if (drawn.LengthMillimetres >= shell.PixelSizeMillimetres)
        {
            shell.AddMeasurement(drawn);
        }
        else
        {
            DrawMeasurements();
        }
    }

    private void MoveCrosshairTo(Point mousePosition)
    {
        if (Shell is MainViewModel shell && ToPatient(mousePosition) is Point3D patient)
        {
            shell.SetCrosshair(patient);
        }
    }

    /// <summary>
    /// The pointer's offset from the crosshair, in screen pixels. Null when there is no
    /// plane to locate the crosshair on.
    /// </summary>
    private Vector? OffsetFromCrosshair(Point mousePosition)
    {
        if (DataContext is not ViewportViewModel viewport || viewport.Plane is not ReslicePlane plane)
        {
            return null;
        }

        (double column, double row) = plane.ToPixel(viewport.Crosshair);
        return mousePosition - ViewTransform.Matrix.Transform(new Point(column, row));
    }

    /// <summary>
    /// The pointer's angle about the crosshair, which is directly an angle of rotation in
    /// patient space about this pane's normal.
    /// </summary>
    /// <remarks>
    /// No conversion stands between the two. Turning a plane's row axis towards its column
    /// axis is a positive rotation about the normal, because the normal is defined as row
    /// cross column; on screen that is +x towards +y, which is the direction atan2
    /// increases in. The view matrix is a uniform positive scale and a translation - never
    /// a rotation, never a flip - so it carries angles through unchanged and the figure
    /// measured on screen is already the figure to rotate by.
    /// </remarks>
    private double? AngleAboutCrosshair(Point mousePosition) =>
        OffsetFromCrosshair(mousePosition) is Vector offset
            ? Math.Atan2(offset.Y, offset.X)
            : null;

    /// <summary>
    /// The starting angle for a rotation drag when the pointer is on a crosshair arm, or
    /// null when the press is an ordinary crosshair move.
    /// </summary>
    private double? ArmGrabAngle(Point mousePosition)
    {
        if (DataContext is not ViewportViewModel viewport ||
            viewport.Plane is not ReslicePlane plane ||
            Shell is not MainViewModel shell ||
            OffsetFromCrosshair(mousePosition) is not Vector offset ||
            offset.Length < ArmDeadZonePixels)
        {
            return null;
        }

        bool onArm =
            IsOnArm(offset, LineDirection(plane, shell.NormalFor(viewport.VerticalLinePlane))) ||
            IsOnArm(offset, LineDirection(plane, shell.NormalFor(viewport.HorizontalLinePlane)));

        return onArm ? Math.Atan2(offset.Y, offset.X) : null;
    }

    /// <summary>
    /// Whether an offset from the crosshair lies within the grab tolerance of the line
    /// through it in <paramref name="pixelDirection"/>.
    /// </summary>
    /// <remarks>
    /// The 2D cross product of the offset with a unit direction is the signed perpendicular
    /// distance to that line, so its magnitude is the whole test - no projection, no
    /// clamping, no end points. An infinite line is the right model here rather than a
    /// segment, because the arms are drawn to the edge of the pane in both directions.
    /// </remarks>
    private bool IsOnArm(Vector offset, Vector? pixelDirection)
    {
        if (pixelDirection is not Vector direction)
        {
            return false;
        }

        // Into screen pixels, where the tolerance is expressed. Transforming a Vector
        // applies only the matrix's linear part, so the pan is correctly ignored.
        Vector onScreen = ViewTransform.Matrix.Transform(direction);
        if (onScreen.Length < 1e-9)
        {
            return false;
        }

        onScreen.Normalize();
        return Math.Abs(Vector.CrossProduct(offset, onScreen)) <= ArmGrabPixels;
    }
}
