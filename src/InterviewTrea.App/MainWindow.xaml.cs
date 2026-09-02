using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InterviewTrea.App.ViewModels;
using InterviewTrea.Applications.Abstractions;
using InterviewTrea.App.Views;
using InterviewTrea.Core.Measurements;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Reslicing;

namespace InterviewTrea.App;

/// <summary>
/// The shell: the folder dialog, and the 2x2 to maximized switch (FR-201, FR-203).
/// Everything else lives in the four <see cref="ViewportControl"/>s.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private ViewportControl[] panes = [];

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        InitializeComponent();

        panes = [AxialPane, CoronalPane, SagittalPane, SlabPane];
    }

    /// <summary>
    /// The FR-207 dropdown's items. An instance property rather than a static one because
    /// a WPF binding path resolves against the source object, and a static CLR property is
    /// not on that path.
    /// </summary>
    public IReadOnlyList<SlabMode> SlabModes { get; } =
        [SlabMode.Maximum, SlabMode.Minimum, SlabMode.Average];

    /// <summary>The FR-401 to FR-404 tool dropdown's items, for the same binding reason.</summary>
    public IReadOnlyList<MeasurementTool> MeasurementTools { get; } =
    [
        MeasurementTool.None,
        MeasurementTool.Move,
        MeasurementTool.Distance,
        MeasurementTool.Ellipse,
        MeasurementTool.Rectangle,
    ];

    /// <summary>Loads a folder without going through the dialog (see App.OnStartup).</summary>
    public Task LoadAsync(string directory) => viewModel.LoadAsync(directory);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Maximized))
        {
            ApplyLayout();
        }
    }

    /// <summary>
    /// FR-203. The maximized pane is stretched over all four cells rather than moved into
    /// a different container, so it keeps its bitmap, its zoom and its event handlers -
    /// re-parenting a control would tear all three down and rebuild them.
    /// </summary>
    private void ApplyLayout()
    {
        ViewportViewModel? maximized = viewModel.Maximized;

        for (int i = 0; i < panes.Length; i++)
        {
            ViewportControl pane = panes[i];
            bool isMaximized = maximized is not null && ReferenceEquals(pane.DataContext, maximized);

            pane.Visibility = maximized is null || isMaximized ? Visibility.Visible : Visibility.Collapsed;

            Grid.SetRow(pane, isMaximized ? 0 : i / 2);
            Grid.SetColumn(pane, isMaximized ? 0 : i % 2);
            Grid.SetRowSpan(pane, isMaximized ? 2 : 1);
            Grid.SetColumnSpan(pane, isMaximized ? 2 : 1);
        }
    }

    /// <summary>
    /// FR-407. Delete removes the measurement under the pointer. Handled on the window
    /// rather than on a viewport so that no pane has to hold keyboard focus for it to work,
    /// which would mean focus following the mouse across four panes and back to the toolbar.
    /// </summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && viewModel.Hovered is not null)
        {
            viewModel.DeleteHovered();
            e.Handled = true;
        }
    }

    private void OnClearMeasurements(object sender, RoutedEventArgs e) =>
        viewModel.Measurements.Clear();

    private void OnReset(object sender, RoutedEventArgs e) => viewModel.Reset();

    /// <summary>
    /// FR-502. The clicked entry's DataContext is the application it was generated from,
    /// which is why the menu needs no command plumbing and no per-application handler.
    /// </summary>
    private void OnLaunchApplication(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: IClinicalApplication application })
        {
            viewModel.Launch(application);
        }
    }

    private void OnCloseApplication(object sender, RoutedEventArgs e) =>
        viewModel.CloseApplication();

    /// <summary>
    /// FR-408. The document is built in Core and only the file is written here, so the
    /// format is covered by unit tests and this method has nothing in it but a path.
    /// </summary>
    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        if (viewModel.Volume is not Volume volume)
        {
            return;
        }

        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "Export measurements",
            FileName = "measurements.csv",
            DefaultExt = ".csv",
            Filter = "CSV file|*.csv",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, MeasurementCsv.Write(viewModel.Measurements, volume));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The one place a message box is warranted: the export was asked for
            // explicitly, it failed, and the status line is hidden while a volume is on
            // screen - so silence here would look like success.
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// FR-409. Exports the active pane exactly as it stands - crosshair, measurements and
    /// all - with the RQ-1 disclaimer drawn into the pixels.
    /// </summary>
    /// <remarks>
    /// The pane is found by identity against its own DataContext rather than by index, for
    /// the same reason <see cref="ApplyLayout"/> does it: the panes are laid out by grid
    /// position and the array order is not a fact anything else should depend on.
    /// </remarks>
    private void OnExportPng(object sender, RoutedEventArgs e)
    {
        if (viewModel.Volume is null || PaneFor(viewModel.Active) is not ViewportControl pane)
        {
            return;
        }

        string name = viewModel.Active is ViewportViewModel active
            ? active.Title.Replace(' ', '-').ToLowerInvariant()
            : "viewport";

        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "Export viewport",
            FileName = name + ".png",
            DefaultExt = ".png",
            Filter = "PNG image|*.png",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            BitmapSource image = ViewportCapture.Render(
                pane.Host,
                new Typeface(
                    (FontFamily)FindResource("Font.Interface"),
                    FontStyles.Normal,
                    FontWeights.SemiBold,
                    FontStretches.Normal),
                (Brush)FindResource("Brush.Regulatory.Background"),
                (Brush)FindResource("Brush.Regulatory.Text"));

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using FileStream file = File.Create(dialog.FileName);
            encoder.Save(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private ViewportControl? PaneFor(ViewportViewModel? viewport)
    {
        foreach (ViewportControl pane in panes)
        {
            if (viewport is not null && ReferenceEquals(pane.DataContext, viewport))
            {
                return pane;
            }
        }

        return null;
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
}
