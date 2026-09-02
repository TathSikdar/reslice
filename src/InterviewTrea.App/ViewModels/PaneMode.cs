using InterviewTrea.Rendering.Reslicing;

namespace InterviewTrea.App.ViewModels;

/// <summary>
/// What the fourth pane is showing: one of the slab projections, or the 3D view (FR-610).
/// </summary>
/// <remarks>
/// The 3D view is a mode of the fourth pane rather than a layout of its own. It answers the
/// same kind of question the slab projections answer - what is along this direction, rather
/// than what is at this plane - so the control that already decides that keeps deciding it,
/// and Phase 2 adds no new layout chrome. A null <see cref="Slab"/> means the 3D view; the
/// same shape as <see cref="ExportTarget"/>, where a null viewport means the whole grid.
/// </remarks>
public sealed record PaneMode(string Name, SlabMode? Slab)
{
    public bool IsVolume => Slab is null;
}
