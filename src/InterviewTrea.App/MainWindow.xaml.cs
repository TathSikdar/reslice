using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InterviewTrea.App.ViewModels;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Reslicing;
using InterviewTrea.Rendering.Windowing;

namespace InterviewTrea.App;

/// <summary>
/// View concerns only: the folder dialog, and the <see cref="WriteableBitmap"/> the
/// renderer's bytes are blitted into. Both are WPF objects that cannot exist in a view
/// model without dragging <c>System.Windows</c> into it.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    // Reused across frames. Reallocating a quarter-megabyte buffer and a bitmap on every
    // wheel notch is a collection per frame, which is exactly what a scroll cannot afford.
    private readonly WindowLevelLut lut = new(WindowLevel.SoftTissue);
    private WriteableBitmap? bitmap;
    private byte[] pixels = [];

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        InitializeComponent();
    }

    /// <summary>Loads a folder without going through the dialog (see App.OnStartup).</summary>
    public Task LoadAsync(string directory) => viewModel.LoadAsync(directory);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Volume):
                CreateBitmap();
                Render();
                break;

            case nameof(MainViewModel.SliceIndex):
                Render();
                break;

            case nameof(MainViewModel.Window):
                lut.Rebuild(viewModel.Window);
                Render();
                break;

            default:
                break;
        }
    }

    private void CreateBitmap()
    {
        if (viewModel.Volume is not Volume volume)
        {
            SliceImage.Source = null;
            bitmap = null;
            return;
        }

        // Aspect correction (FR-208) carried by the bitmap's DPI rather than by a Width
        // and Height on the Image. WPF sizes an image as pixels / dpi * 96, so declaring
        // dpi as 25.4 / spacing-in-mm makes the natural size proportional to the physical
        // extent of the slice. Anisotropic in-plane pixels then display at the right shape
        // without the layout having to know anything about millimetres.
        bitmap = new WriteableBitmap(
            volume.DimX,
            volume.DimY,
            25.4 / volume.Spacing.X,
            25.4 / volume.Spacing.Y,
            PixelFormats.Gray8,
            palette: null);

        pixels = new byte[volume.DimX * volume.DimY];
        SliceImage.Source = bitmap;

        // A new series starts at native fit. Carrying the previous study's zoom over is
        // disorienting and, on a differently sized volume, can leave the image off-screen.
        ViewTransform.Matrix = Matrix.Identity;
    }

    private void Render()
    {
        if (viewModel.Volume is not Volume volume || bitmap is null)
        {
            return;
        }

        // Property changes arrive one at a time, so between "the volume changed" and "the
        // slice index changed" the view model is momentarily inconsistent with the bitmap.
        // Three compares here are cheaper than making the view model's assignment order
        // load-bearing, and they cannot be broken by a later edit to it.
        if (bitmap.PixelWidth != volume.DimX ||
            bitmap.PixelHeight != volume.DimY ||
            (uint)viewModel.SliceIndex >= (uint)volume.DimZ)
        {
            return;
        }

        ResliceRenderer.RenderAxial(volume, viewModel.SliceIndex, lut, pixels);

        bitmap.WritePixels(
            new Int32Rect(0, 0, volume.DimX, volume.DimY),
            pixels,
            stride: volume.DimX,
            offset: 0);
    }

    private async void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFolderDialog dialog = new()
        {
            Title = "Open a DICOM series",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.LoadAsync(dialog.FolderName).ConfigureAwait(true);
        }
    }

    // Drag sensitivity, in Hounsfield units per pixel of mouse travel. These are
    // calibration knobs, not derived constants: what feels right depends on the display
    // size and the pointer settings, and the only way to set them is to drag on a real
    // chest study. Width moves faster than level because its useful range is wider - a
    // lung window is 1500 wide and a brain window is 80.
    private const double WidthPerPixel = 4.0;
    private const double CenterPerPixel = 2.0;

    private const double ZoomPerNotch = 1.15;

    private Point lastMousePosition;

    /// <summary>FR-301 scroll, or FR-303 zoom about the cursor when Ctrl is held.</summary>
    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        int notches = e.Delta / Mouse.MouseWheelDeltaForOneLine;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double factor = Math.Pow(ZoomPerNotch, notches);
            Point cursor = e.GetPosition(Viewport);

            // ScaleAt post-multiplies about a point in the already-transformed space, which
            // is the screen. That is what keeps the anatomy under the cursor from sliding
            // away as the image grows - scaling about the image's own centre would move it.
            Matrix matrix = ViewTransform.Matrix;
            matrix.ScaleAt(factor, factor, cursor.X, cursor.Y);
            ViewTransform.Matrix = matrix;
        }
        else
        {
            viewModel.ScrollSlices(notches);
        }

        e.Handled = true;
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Right or MouseButton.Middle)
        {
            lastMousePosition = e.GetPosition(Viewport);
            Viewport.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!Viewport.IsMouseCaptured)
        {
            return;
        }

        Point current = e.GetPosition(Viewport);
        Vector delta = current - lastMousePosition;
        lastMousePosition = current;

        if (e.RightButton == MouseButtonState.Pressed)
        {
            // FR-302: horizontal is width, vertical is level. Screen y grows downward, so
            // the sign is flipped - dragging up must brighten, which is what every
            // workstation does and what a radiologist's hand already expects.
            viewModel.Window = viewModel.Window.AdjustedBy(
                delta.X * WidthPerPixel,
                -delta.Y * CenterPerPixel);

            // The dropdown would otherwise keep naming a preset that is no longer on
            // screen. Clearing it is more honest than leaving a stale label.
            PresetSelector.SelectedItem = null;
        }
        else if (e.MiddleButton == MouseButtonState.Pressed)
        {
            Matrix matrix = ViewTransform.Matrix;
            matrix.Translate(delta.X, delta.Y);
            ViewTransform.Matrix = matrix;
        }
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (Viewport.IsMouseCaptured)
        {
            Viewport.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PresetSelector.SelectedItem is WindowPreset preset)
        {
            viewModel.Window = preset.Window;
        }
    }
}
