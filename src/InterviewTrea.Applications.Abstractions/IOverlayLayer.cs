using System.Collections.Generic;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;

namespace InterviewTrea.Applications.Abstractions;

/// <summary>
/// FR-505. Something an application draws on top of a viewport.
/// </summary>
/// <remarks>
/// <para>
/// The layer is asked what it wants drawn <em>on a given plane</em>, rather than handed a
/// canvas. That is what keeps the contract free of WPF and what makes an overlay work in
/// all four panes at once without the application knowing there are four: it answers the
/// question separately for each plane it is asked about, and a lesion that does not touch
/// a plane returns nothing for it.
/// </para>
/// <para>
/// Every coordinate is patient millimetres, for the same reason measurements are (FR-402):
/// the shell owns the mapping from millimetres to pixels, including zoom, pan and
/// obliquity, and an overlay expressed in pixels would have to be recomputed by its author
/// every time any of those changed.
/// </para>
/// </remarks>
public interface IOverlayLayer
{
    /// <summary>Stable identifier, unique within the session.</summary>
    string Id { get; }

    /// <summary>Whether the shell should draw it at all.</summary>
    bool IsVisible { get; }

    /// <summary>
    /// What to draw where this layer meets <paramref name="plane"/>. Empty is normal and
    /// costs nothing.
    /// </summary>
    IReadOnlyList<OverlayShape> ShapesOn(ReslicePlane plane);
}

/// <summary>The two things an overlay can draw.</summary>
public enum OverlayShapeKind
{
    /// <summary>An open or closed run of line segments through <see cref="OverlayShape.Points"/>.</summary>
    Polyline,

    /// <summary><see cref="OverlayShape.Text"/>, anchored at the first point.</summary>
    Text,
}

/// <summary>
/// One primitive in an overlay, in patient millimetres.
/// </summary>
/// <remarks>
/// Two kinds rather than a general drawing API. An outline and a label are what a clinical
/// overlay actually consists of - a scoring tool draws the boundary of a lesion and writes
/// its score beside it - and every kind added here is a kind the shell must be able to
/// render for every application forever.
/// </remarks>
public sealed record OverlayShape
{
    public required OverlayShapeKind Kind { get; init; }

    /// <summary>
    /// The geometry, in patient space. A polyline uses all of them; text uses the first as
    /// its anchor.
    /// </summary>
    public required IReadOnlyList<Point3D> Points { get; init; }

    /// <summary>The label, for <see cref="OverlayShapeKind.Text"/>.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Colour as packed ARGB, e.g. <c>0xFFE0A030</c> for the interface accent.
    /// </summary>
    /// <remarks>
    /// A number rather than a colour type, because every colour type in .NET that is not a
    /// primitive lives in a presentation assembly, and this project may not reference one.
    /// </remarks>
    public uint ColorArgb { get; init; } = 0xFFE0A030;

    /// <summary>
    /// Stroke width in screen pixels, held constant as the user zooms - a line meant to be
    /// pointed at should not become a band.
    /// </summary>
    public double ThicknessPixels { get; init; } = 1.5;

    /// <summary>Whether a polyline closes back to its first point.</summary>
    public bool IsClosed { get; init; }
}
