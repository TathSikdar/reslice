using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InterviewTrea.App.ViewModels;
using InterviewTrea.Core.Geometry;
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

    private static void OnShellChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ViewportControl control = (ViewportControl)d;

        if (control.subscribedShell is MainViewModel old)
        {
            old.PropertyChanged -= control.OnShellPropertyChanged;
        }

        control.subscribedShell = e.NewValue as MainViewModel;

        if (control.subscribedShell is MainViewModel shell)
        {
            shell.PropertyChanged += control.OnShellPropertyChanged;
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
        ZoomLabel = string.Create(CultureInfo.InvariantCulture, $"zoom {user.M11:0.00}x");
    }

    private void UpdateCrosshair(ReslicePlane plane, double totalScale)
    {
        if (DataContext is not ViewportViewModel viewport || Shell is null)
        {
            return;
        }

        (double column, double row) = plane.ToPixel(viewport.Crosshair);

        // The lines live inside the scaled scene, so their stroke width has to be divided
        // back out or the crosshair thickens with the zoom until it hides the anatomy it
        // is pointing at.
        double thickness = CrosshairThickness / totalScale;

        VerticalLine.X1 = VerticalLine.X2 = column;
        VerticalLine.Y1 = 0;
        VerticalLine.Y2 = plane.Height;
        VerticalLine.StrokeThickness = thickness;

        HorizontalLine.Y1 = HorizontalLine.Y2 = row;
        HorizontalLine.X1 = 0;
        HorizontalLine.X2 = plane.Width;
        HorizontalLine.StrokeThickness = thickness;
    }

    private double CrosshairThickness =>
        TryFindResource("Size.Crosshair") is double value ? value : 1.0;

    /// <summary>Turns a mouse position into a patient-space point, or null if there is no plane.</summary>
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
        else if (e.ChangedButton == MouseButton.Left)
        {
            MoveCrosshairTo(lastMousePosition);
        }

        if (e.ChangedButton is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
        {
            Host.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!Host.IsMouseCaptured || Shell is not MainViewModel shell)
        {
            return;
        }

        Point current = e.GetPosition(Host);
        Vector delta = current - lastMousePosition;
        lastMousePosition = current;

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            // Dragging keeps setting the crosshair, so the other two panes track the
            // pointer live. That is the FR-304 demo beat.
            MoveCrosshairTo(current);
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
        if (Host.IsMouseCaptured)
        {
            Host.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void MoveCrosshairTo(Point mousePosition)
    {
        if (Shell is MainViewModel shell && ToPatient(mousePosition) is Point3D patient)
        {
            shell.SetCrosshair(patient);
        }
    }
}
