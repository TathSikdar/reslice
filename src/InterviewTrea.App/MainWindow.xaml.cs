using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using InterviewTrea.App.ViewModels;
using InterviewTrea.App.Views;
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
        [MeasurementTool.None, MeasurementTool.Distance, MeasurementTool.Ellipse, MeasurementTool.Rectangle];

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
