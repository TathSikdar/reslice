using System.Collections.Generic;

namespace InterviewTrea.Rendering3D;

/// <summary>
/// The named transfer functions (FR-605).
/// </summary>
/// <remarks>
/// <para>
/// These are data, not arithmetic: the Hounsfield thresholds come from CT tissue ranges -
/// air about -1000, lung parenchyma -900 to -500, fat -100, soft tissue 20 to 80, iodinated
/// blood 200 to 500, cancellous bone 300 to 800, cortical bone above 800 - and the colours
/// and slopes are tuned by eye against a chest study. There is no analytically correct
/// answer for what a rendering should look like, which is why FR-606 makes them a starting
/// point the user can drag rather than a fixed set.
/// </para>
/// <para>
/// Every preset opens with a zero-opacity point at the bottom of the scale. Values below
/// the lowest point clamp to it, so that point is what makes air invisible; without it a
/// preset would fog the whole volume with whatever its first colour happened to be.
/// </para>
/// </remarks>
public static class TransferFunctionPreset
{
    /// <summary>Cortical and cancellous bone against transparent soft tissue.</summary>
    public static TransferFunction Bone { get; } = new(new[]
    {
        new TransferFunctionPoint(-1024, Rgb.Black, 0),
        new TransferFunctionPoint(150, new Rgb(224, 208, 176), 0),
        new TransferFunctionPoint(400, new Rgb(240, 232, 212), 0.55),
        new TransferFunctionPoint(1200, new Rgb(255, 255, 250), 0.95),
        new TransferFunctionPoint(3071, new Rgb(255, 255, 255), 1.0),
    });

    /// <summary>
    /// Iodinated vessels in red, with bone left pale so the two are told apart by colour
    /// rather than only by shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The red is confined to 200-500 HU, which is where contrast-filled blood sits, and
    /// gives way to bone white by 700. An earlier version ran red to 900 at high opacity and
    /// painted cancellous bone with it, which on a non-contrast study produced a red
    /// skeleton and read as a vascular finding.
    /// </para>
    /// <para>
    /// It still tints the rising edge of every bone, and that is not a bug to tune away.
    /// A transfer function classifies by density: the outside of a rib passes through 300 HU
    /// on its way up, and nothing here can tell that from a vessel at 300 HU, because
    /// telling them apart is segmentation and Phase 2 section 1.4 rules it out on purpose.
    /// On a non-contrast study - which is most public data - Angio is showing bone edges,
    /// and the honest thing is to say so rather than to hide it behind a threshold.
    /// </para>
    /// </remarks>
    public static TransferFunction Angio { get; } = new(new[]
    {
        new TransferFunctionPoint(-1024, Rgb.Black, 0),
        new TransferFunctionPoint(120, new Rgb(150, 30, 30), 0),
        new TransferFunctionPoint(200, new Rgb(200, 55, 45), 0.12),
        new TransferFunctionPoint(400, new Rgb(245, 120, 100), 0.35),
        new TransferFunctionPoint(700, new Rgb(235, 225, 210), 0.55),
        new TransferFunctionPoint(3071, new Rgb(250, 250, 245), 0.75),
    });

    /// <summary>
    /// Parenchyma and airways, and deliberately nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A window of about -950 to -500 HU, transparent on both sides of it. Everything
    /// denser than -500 - the chest wall, the mediastinum, the ribs - is left invisible, so
    /// the lungs are seen through the body rather than behind it. That is the point of the
    /// preset: it is a view of the air.
    /// </para>
    /// <para>
    /// The opacities are an order of magnitude below the other presets because a lung is
    /// mostly air and a ray crosses 200 mm of it. An earlier version left soft tissue at
    /// 0.05, which is nothing per millimetre and a solid grey block over the width of a
    /// chest; the lungs were behind it and invisible.
    /// </para>
    /// </remarks>
    public static TransferFunction Lung { get; } = new(new[]
    {
        new TransferFunctionPoint(-1024, Rgb.Black, 0),

        // Scanned air reads about -1000, and the ray crosses hundreds of millimetres of it
        // outside the patient. At even 0.003 per millimetre that accumulates to a fog
        // around the whole chest, so the ramp starts above air rather than at it.
        new TransferFunctionPoint(-980, Rgb.Black, 0),
        new TransferFunctionPoint(-940, new Rgb(70, 95, 130), 0.020),
        new TransferFunctionPoint(-750, new Rgb(140, 170, 205), 0.060),
        new TransferFunctionPoint(-600, new Rgb(205, 220, 240), 0.020),
        new TransferFunctionPoint(-500, Rgb.Black, 0),
        new TransferFunctionPoint(3071, Rgb.Black, 0),
    });

    /// <summary>The outer surface: the air-to-skin step, opaque, with nothing behind it.</summary>
    public static TransferFunction Skin { get; } = new(new[]
    {
        new TransferFunctionPoint(-1024, Rgb.Black, 0),
        new TransferFunctionPoint(-320, new Rgb(150, 100, 80), 0),
        new TransferFunctionPoint(-100, new Rgb(226, 178, 148), 0.85),
        new TransferFunctionPoint(3071, new Rgb(245, 220, 205), 1.0),
    });

    /// <summary>The presets in demo order, named as they appear in the view.</summary>
    public static IReadOnlyList<KeyValuePair<string, TransferFunction>> All { get; } = new[]
    {
        new KeyValuePair<string, TransferFunction>("Bone", Bone),
        new KeyValuePair<string, TransferFunction>("Angio", Angio),
        new KeyValuePair<string, TransferFunction>("Lung", Lung),
        new KeyValuePair<string, TransferFunction>("Skin", Skin),
    };
}
