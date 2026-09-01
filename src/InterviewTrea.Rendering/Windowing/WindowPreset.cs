using System.Collections.Generic;

namespace InterviewTrea.Rendering.Windowing;

/// <summary>A named window/level setting, for the preset list (FR-305).</summary>
public sealed record WindowPreset(string Name, WindowLevel Window)
{
    /// <summary>
    /// The five presets FR-305 requires, in the order a radiologist reaches for them on a
    /// chest study. The list lives here rather than in the view model because the values
    /// are domain knowledge, not presentation: they are the same numbers whether they are
    /// shown in a dropdown, bound to a key, or applied by a plugin.
    /// </summary>
    public static IReadOnlyList<WindowPreset> All { get; } =
    [
        new("Lung", WindowLevel.Lung),
        new("Soft Tissue", WindowLevel.SoftTissue),
        new("Bone", WindowLevel.Bone),
        new("Brain", WindowLevel.Brain),
        new("Mediastinum", WindowLevel.Mediastinum),
    ];
}
