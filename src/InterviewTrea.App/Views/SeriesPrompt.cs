using System.Collections.Generic;
using System.Windows;
using InterviewTrea.Dicom;

namespace InterviewTrea.App.Views;

/// <summary>
/// FR-102. The WPF side of <see cref="ISeriesPrompt"/>: a modal dialog over the shell.
/// </summary>
/// <remarks>
/// The owner comes from <see cref="Application.Current"/> rather than being injected,
/// because taking the main window as a constructor dependency would close a loop - the
/// window is built from the view model, which would then be built from the window.
/// Centring a modal on its owner is a view concern and this is the view layer.
/// </remarks>
internal sealed class SeriesPrompt : ISeriesPrompt
{
    public SeriesDescriptor? Choose(IReadOnlyList<SeriesDescriptor> series)
    {
        SeriesPromptWindow dialog = new(series)
        {
            Owner = Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.Chosen : null;
    }
}
