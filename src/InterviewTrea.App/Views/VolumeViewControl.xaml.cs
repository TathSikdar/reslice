using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using InterviewTrea.App.ViewModels;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering3D;

namespace InterviewTrea.App.Views;

/// <summary>
/// The 3D view (FR-601). Wraps <see cref="VolumeRaycaster"/>'s BGRA buffer in a
/// <see cref="WriteableBitmap"/> and shows it; the renderer knows nothing about either.
/// </summary>
/// <remarks>
/// There is no fit-zoom-pan matrix as there is on an MPR pane. Zoom is the camera's view
/// height in millimetres and pan moves its target, so both happen in patient space before
/// a ray is cast rather than to the pixels afterwards - which is why zooming in on the 3D
/// view resolves more detail where zooming an MPR pane only magnifies what was rendered.
/// </remarks>
public partial class VolumeViewControl : UserControl, IDisposable
{
    /// <summary>
    /// The longest side of the full-quality render buffer, in pixels.
    /// </summary>
    /// <remarks>
    /// A ray caster costs one march per output pixel, so the render is bounded here rather
    /// than by however large someone drags the window. 512 is the figure NFR-401 is stated
    /// against, and the bitmap is stretched to the pane, which is a slight softening on a
    /// larger one rather than a visible one.
    /// </remarks>
    private const int MaximumRenderSize = 512;

    /// <summary>
    /// How much coarser an interaction frame is (FR-609).
    /// </summary>
    /// <remarks>
    /// Half the linear resolution is a quarter of the rays, and four times the step is a
    /// quarter of the samples along each: about a sixteenth of the work. It looks like the
    /// final image rather than a different one only because FR-603's opacity correction
    /// follows the step - without that this would be the classic preview that resolves into
    /// something else when you let go.
    /// </remarks>
    private const int PreviewDivisor = 2;

    private const double PreviewStepFactor = 4.0;

    /// <summary>Radians of orbit per pixel of drag. About a quarter turn across a 200 px pane.</summary>
    private const double RadiansPerPixel = 0.008;

    private const double ZoomPerNotch = 1.15;

    /// <summary>Closest and widest the view may be driven, in millimetres of patient.</summary>
    /// <remarks>The 3D form of FR-412: neither end leaves anything on screen worth looking at.</remarks>
    private const double MinimumViewHeightMm = 10;

    private const double MaximumViewHeightMm = 2000;

    /// <summary>
    /// How long after the last gesture the full-quality frame starts.
    /// </summary>
    /// <remarks>
    /// Long enough that a drag with a pause in it does not keep launching renders it will
    /// throw away; short enough that letting go feels like the image sharpening rather than
    /// like it hesitating.
    /// </remarks>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(180);

    private readonly DispatcherTimer settle = new() { Interval = SettleDelay };

    // One bitmap per size. The two alternate every time a gesture is followed by a settle,
    // so keeping both is what stops a drag allocating a bitmap per frame (NFR-403).
    private WriteableBitmap? previewBitmap;
    private WriteableBitmap? fullBitmap;
    private byte[] previewPixels = [];
    private byte[] fullPixels = [];

    private CancellationTokenSource? pending;
    private Point lastMousePosition;
    private bool isPreview;

    public VolumeViewControl()
    {
        InitializeComponent();

        settle.Tick += (_, _) =>
        {
            settle.Stop();
            RenderFull();
        };

        Unloaded += (_, _) => Dispose();
    }

    /// <summary>
    /// Stops the settle timer and abandons any full-quality frame still running.
    /// </summary>
    /// <remarks>
    /// The control lives as long as the window does, so in practice this runs at shutdown.
    /// It exists because the cancellation source is a disposable the control owns, and a
    /// background render that outlives its view is a real thing to stop rather than a
    /// theoretical one.
    /// </remarks>
    public void Dispose()
    {
        settle.Stop();
        pending?.Cancel();
        pending?.Dispose();
        pending = null;

        GC.SuppressFinalize(this);
    }

    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(MainViewModel), typeof(VolumeViewControl),
        new PropertyMetadata(null, OnShellChanged));

    /// <summary>The shared state: the volume, the camera, the transfer function.</summary>
    public MainViewModel? Shell
    {
        get => (MainViewModel?)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public static readonly DependencyPropertyKey PresetLabelKey = DependencyProperty.RegisterReadOnly(
        nameof(PresetLabel), typeof(string), typeof(VolumeViewControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PresetLabelProperty = PresetLabelKey.DependencyProperty;

    /// <summary>Which classification produced the picture, named in the overlay.</summary>
    public string PresetLabel => (string)GetValue(PresetLabelProperty);

    public static readonly DependencyPropertyKey QualityLabelKey = DependencyProperty.RegisterReadOnly(
        nameof(QualityLabel), typeof(string), typeof(VolumeViewControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty QualityLabelProperty = QualityLabelKey.DependencyProperty;

    /// <summary>The size and sampling step the image on screen was rendered at.</summary>
    public string QualityLabel => (string)GetValue(QualityLabelProperty);

    private static void OnShellChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        VolumeViewControl control = (VolumeViewControl)d;

        if (e.OldValue is MainViewModel old)
        {
            old.PropertyChanged -= control.OnShellPropertyChanged;
        }

        if (e.NewValue is MainViewModel shell)
        {
            shell.PropertyChanged += control.OnShellPropertyChanged;
        }

        control.Refresh();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Volume):
            case nameof(MainViewModel.Camera):
            case nameof(MainViewModel.VolumePreset):
                Refresh();
                break;
        }
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (Shell is not MainViewModel shell)
        {
            return;
        }

        if (shell.Volume is not Volume volume || shell.Camera is not Camera3D camera)
        {
            // FR-612. The same calm empty state the MPR panes have: the pane is the
            // viewport background, with no error and no half-drawn frame.
            VolumeImage.Source = null;
            return;
        }

        // Whatever full-quality frame is in flight was started for a camera that has since
        // moved, so it is already stale.
        pending?.Cancel();

        // FR-609. The cheap frame goes up now and the good one is scheduled: a full frame
        // is hundreds of milliseconds and NFR-204 gives the UI thread fifty, so the only
        // render allowed to run here synchronously is the preview.
        RenderPreview(volume, camera, shell);

        settle.Stop();
        settle.Start();
    }

    private void RenderPreview(Volume volume, Camera3D camera, MainViewModel shell)
    {
        (int width, int height) = BufferSize();
        if (width < 1 || height < 1)
        {
            return;
        }

        int previewWidth = Math.Max(1, width / PreviewDivisor);
        int previewHeight = Math.Max(1, height / PreviewDivisor);

        // Shading is off whatever the checkbox says: it is six extra trilinear samples per
        // step, and a frame that arrives late is worse than one that arrives flat. It comes
        // back a fifth of a second later when the full frame lands.
        RaycastSettings settings = RaycastSettings.For(volume, PreviewStepFactor) with { IsShaded = false };

        Ensure(ref previewBitmap, ref previewPixels, previewWidth, previewHeight);

        VolumeRaycaster.Render(
            volume, camera, shell.VolumePreset, settings, previewWidth, previewHeight, previewPixels);

        // Drawn into a bitmap of its own size and stretched up to the pane by the Image.
        // The camera saw the same aspect ratio either way, so the coarse frame is the fine
        // one with fewer pixels rather than a differently framed picture.
        Show(previewBitmap!, previewWidth, previewHeight, previewPixels);

        isPreview = true;
        Describe(shell, previewWidth, previewHeight, settings);
    }

    private void RenderFull()
    {
        if (Shell is not MainViewModel shell ||
            shell.Volume is not Volume volume || shell.Camera is not Camera3D camera)
        {
            return;
        }

        (int width, int height) = BufferSize();
        if (width < 1 || height < 1)
        {
            return;
        }

        RaycastSettings settings = RaycastSettings.For(volume) with { IsShaded = true };

        Ensure(ref fullBitmap, ref fullPixels, width, height);

        pending?.Cancel();
        pending?.Dispose();
        pending = new CancellationTokenSource();

        CancellationToken token = pending.Token;
        byte[] buffer = fullPixels;
        WriteableBitmap target = fullBitmap!;
        TransferFunction function = shell.VolumePreset;

        // Off the UI thread, because a shaded 512-square frame is hundreds of milliseconds
        // and NFR-204 allows fifty. The continuation runs back on the UI thread, which is
        // the only place the bitmap may be touched.
        _ = Task.Run(
                () => VolumeRaycaster.Render(
                    volume, camera, function, settings, width, height, buffer, token),
                token)
            .ContinueWith(
                _ =>
                {
                    // The pane may have been resized while this was in flight, in which
                    // case the buffer it filled is no longer the one on screen.
                    if (!ReferenceEquals(buffer, fullPixels))
                    {
                        return;
                    }

                    Show(target, width, height, buffer);

                    isPreview = false;
                    Describe(shell, width, height, settings);
                },
                token,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// The render size: the pane's own shape, scaled down to fit the budget.
    /// </summary>
    /// <remarks>
    /// The aspect ratio is the pane's rather than a fixed square, because the camera derives
    /// its horizontal field from the buffer's width. A square buffer stretched across a wide
    /// pane would leave the patient wider than they are.
    /// </remarks>
    private (int Width, int Height) BufferSize()
    {
        double width = Host.ActualWidth;
        double height = Host.ActualHeight;

        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        double scale = Math.Min(1, MaximumRenderSize / Math.Max(width, height));

        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static void Ensure(ref WriteableBitmap? target, ref byte[] buffer, int width, int height)
    {
        if (target is not null && target.PixelWidth == width && target.PixelHeight == height)
        {
            return;
        }

        // Bgra32 rather than the Gray8 of the MPR panes: a volume rendering is colour,
        // because telling tissues apart by giving them different ones is the transfer
        // function's whole job. Not Pbgra32 - the compositing already happened over black
        // inside the renderer, so what arrives here is opaque and needs no alpha handling.
        target = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, palette: null);
        buffer = new byte[width * height * VolumeRaycaster.BytesPerPixel];
    }

    private void Show(WriteableBitmap target, int width, int height, byte[] source)
    {
        target.WritePixels(
            new Int32Rect(0, 0, width, height), source,
            stride: width * VolumeRaycaster.BytesPerPixel, offset: 0);

        VolumeImage.Source = target;
    }

    private void Describe(MainViewModel shell, int width, int height, RaycastSettings settings)
    {
        SetValue(PresetLabelKey, NameOf(shell.VolumePreset));
        SetValue(QualityLabelKey, FormattableString.Invariant(
            $"{width}x{height}  step {settings.StepMm:0.00} mm{(isPreview ? "  preview" : string.Empty)}"));
    }

    // FR-608. Left orbits, wheel zooms, middle drags the target across the image plane -
    // the three gestures every workstation uses, and not one of them a visible control.
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Host.Focus();
        lastMousePosition = e.GetPosition(Host);

        if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            Host.CaptureMouse();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!Host.IsMouseCaptured || Shell is not MainViewModel shell || shell.Camera is not Camera3D camera)
        {
            return;
        }

        Point current = e.GetPosition(Host);
        Vector moved = current - lastMousePosition;
        lastMousePosition = current;

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            // Dragging right turns the volume to the right, which means walking the camera
            // the other way round it - the same inversion as pushing a physical object.
            shell.Camera = camera.Orbited(
                byAzimuth: -moved.X * RadiansPerPixel,
                byElevation: moved.Y * RadiansPerPixel);
        }
        else if (e.MiddleButton == MouseButtonState.Pressed)
        {
            // In millimetres of patient per pixel of pane, so a pan tracks the cursor at
            // any zoom rather than accelerating as the view height shrinks.
            double perPixel = camera.ViewHeightMm / Math.Max(Host.ActualHeight, 1);

            shell.Camera = camera.Panned(rightMm: -moved.X * perPixel, upMm: moved.Y * perPixel);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (Host.IsMouseCaptured)
        {
            Host.ReleaseMouseCapture();
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Shell is not MainViewModel shell || shell.Camera is not Camera3D camera)
        {
            return;
        }

        int notches = e.Delta / Mouse.MouseWheelDeltaForOneLine;

        // Multiplicative, so a notch is the same proportional change wherever you already
        // are, and clamped at both ends so the view cannot be driven inside the volume or
        // out to a speck. The 3D form of FR-412, for the same reason.
        double height = Math.Clamp(
            camera.ViewHeightMm * Math.Pow(1 / ZoomPerNotch, notches),
            MinimumViewHeightMm,
            MaximumViewHeightMm);

        shell.Camera = camera with { ViewHeightMm = height };
        e.Handled = true;
    }

    private static string NameOf(TransferFunction function)
    {
        foreach ((string name, TransferFunction preset) in TransferFunctionPreset.All)
        {
            if (ReferenceEquals(preset, function))
            {
                return name;
            }
        }

        return "Custom";
    }
}
