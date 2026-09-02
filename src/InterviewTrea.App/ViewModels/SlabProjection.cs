using InterviewTrea.Rendering.Reslicing;

namespace InterviewTrea.App.ViewModels;

/// <summary>
/// FR-207. One entry in the projection dropdown: a display name for a <see cref="SlabMode"/>.
/// </summary>
/// <remarks>
/// The enum could be bound directly with a converter, but then the three names the user
/// reads would live in a converter rather than beside the list they belong to. MIP and
/// MinIP are also not what <c>Maximum</c> and <c>Minimum</c> spell, and the radiological
/// abbreviations are what the dropdown has to say.
/// </remarks>
public sealed record SlabProjection(string Name, SlabMode Mode);
