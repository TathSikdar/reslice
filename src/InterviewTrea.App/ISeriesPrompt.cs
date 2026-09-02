using System.Collections.Generic;
using InterviewTrea.Dicom;

namespace InterviewTrea.App;

/// <summary>
/// FR-102. How the view model asks which series to open without knowing what a window is.
/// </summary>
/// <remarks>
/// An interface rather than the view model opening a dialog itself. The load path is the
/// one piece of application logic worth testing without a UI, and a direct
/// <c>new SeriesPromptWindow(...).ShowDialog()</c> in the middle of it would make that
/// impossible and would put a WPF type in a class whose job is orchestration.
/// </remarks>
public interface ISeriesPrompt
{
    /// <summary>
    /// Returns the chosen series, or null if the user cancelled. Called on the UI thread,
    /// and only when there is more than one candidate.
    /// </summary>
    SeriesDescriptor? Choose(IReadOnlyList<SeriesDescriptor> series);
}
