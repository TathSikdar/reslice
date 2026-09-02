namespace InterviewTrea.Applications.Abstractions;

/// <summary>
/// FR-501. What a clinical application is, from the viewer's side.
/// </summary>
/// <remarks>
/// <para>
/// The shape of this file is the argument the project exists to make: a calcium scoring
/// tool is not a separate program, it is an application hosted inside a viewer. Everything
/// here is expressed in Core types - volumes, planes, patient millimetres - so an
/// application never learns what a viewport, a bitmap or a window is, and the shell can
/// change all three without touching a plugin.
/// </para>
/// <para>
/// Registration is one line in the composition root:
/// <c>services.AddSingleton&lt;IClinicalApplication, HistogramApplication&gt;();</c>
/// </para>
/// </remarks>
public interface IClinicalApplication
{
    /// <summary>Stable identifier, e.g. <c>interviewtrea.histogram</c>.</summary>
    string Id { get; }

    /// <summary>What the Applications menu calls it.</summary>
    string DisplayName { get; }

    /// <summary>One line, shown beside the name.</summary>
    string Description { get; }

    /// <summary>
    /// Whether this application can run against what is currently loaded - modality,
    /// series content, whatever it needs. Asked before the menu entry is enabled, so an
    /// application that cannot run says so rather than failing after it is launched.
    /// </summary>
    bool CanRun(IApplicationContext context);

    /// <summary>Starts a session against the loaded study.</summary>
    IApplicationSession Start(IApplicationContext context);
}
