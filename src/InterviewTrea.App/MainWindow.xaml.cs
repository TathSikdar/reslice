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
    /// FR-409. Exports what the dropdown names, exactly as it stands on screen - crosshair,
    /// measurements and all - with the RQ-1 disclaimer drawn into the pixels.
    /// </summary>
    /// <remarks>
    /// A single pane is found by identity against its own DataContext rather than by index,
    /// for the same reason <see cref="ApplyLayout"/> does it: the panes are laid out by grid
    /// position and the array order is not a fact anything else should depend on. The whole
    /// grid is captured as one element, gutters included, so the file is the layout someone
    /// was looking at rather than four pictures they would have to reassemble.
    /// </remarks>
    private void OnExportPng(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not ExportTarget target)
        {
            return;
        }

        // Straight back to naming itself. The control is an action, not a setting: leaving
        // an entry selected would claim a target is pending when the export has already
        // run, and would make the next one two clicks instead of one. Clearing it re-enters
        // this handler with nothing added, which the guard above absorbs.
        ExportPngTargets.SelectedItem = null;

        if (viewModel.Volume is null)
        {
            return;
        }

        FrameworkElement? source = target.Viewport is null
            ? ViewportGrid
            : PaneFor(target.Viewport)?.Host;

        if (source is null)
        {
            return;
        }

        string name = target.Name.Replace(' ', '-').ToLowerInvariant();

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
                source,
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
