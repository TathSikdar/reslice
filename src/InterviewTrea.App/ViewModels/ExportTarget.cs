namespace InterviewTrea.App.ViewModels;

/// <summary>
/// FR-409. What an image export will capture, named so the user can see it before
/// pressing the button.
/// </summary>
/// <remarks>
/// <para>
/// A null <see cref="Viewport"/> means the whole 2x2 grid as it stands on screen, which is
/// the entry the dropdown opens on: it is the picture someone actually wants out of an
/// MPR viewer, and it is the one choice that cannot be ambiguous. The fourth pane holds
/// no <see cref="ViewportViewModel"/> at all - it is the 3D view, which has no reslice
/// plane - so <see cref="IsVolume"/> names it rather than a null meaning two things.
/// </para>
/// <para>
/// The name is fixed rather than read from the viewport's own title, which carries the
/// slab thickness and changes as the user adjusts it. A dropdown entry that renamed itself
/// mid-session would be a moving target for no benefit - the entry has to identify a pane,
/// not describe it.
/// </para>
/// </remarks>
public sealed record ExportTarget(string Name, ViewportViewModel? Viewport, bool IsVolume = false);
