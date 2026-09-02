using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using InterviewTrea.Dicom;

namespace InterviewTrea.App.Views;

/// <summary>
/// FR-102. Asks which series to open when a folder holds more than one.
/// </summary>
/// <remarks>
/// The scan is header-only and already complete by the time this opens, so the counts
/// shown are real rather than estimated, and choosing costs nothing that has to be undone.
/// </remarks>
public sealed partial class SeriesPromptWindow : Window
{
    private readonly IReadOnlyList<SeriesDescriptor> series;

    public SeriesPromptWindow(IReadOnlyList<SeriesDescriptor> series)
    {
        ArgumentNullException.ThrowIfNull(series);

        this.series = series;

        InitializeComponent();

        SeriesList.ItemsSource = series.Select(Row).ToList();

        // The largest is what the viewer used to take without asking, so it is the
        // selection the dialog opens on: pressing Open immediately is the old behaviour.
        SeriesList.SelectedIndex = 0;
    }

    /// <summary>The series the user chose, or null if they cancelled.</summary>
    public SeriesDescriptor? Chosen { get; private set; }

    private static object Row(SeriesDescriptor descriptor) => new
    {
        Description = string.IsNullOrWhiteSpace(descriptor.Metadata.SeriesDescription)
            ? "(no series description)"
            : descriptor.Metadata.SeriesDescription,

        Detail = string.Create(
            CultureInfo.InvariantCulture,
            $"{descriptor.Metadata.Modality}  {descriptor.SliceCount} slices"),
    };

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (SeriesList.SelectedIndex < 0)
        {
            return;
        }

        Chosen = series[SeriesList.SelectedIndex];
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
