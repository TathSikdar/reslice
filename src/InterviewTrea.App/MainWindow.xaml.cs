using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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

    private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PresetSelector.SelectedItem is WindowPreset preset)
        {
            viewModel.Window = preset.Window;
        }
    }
}
