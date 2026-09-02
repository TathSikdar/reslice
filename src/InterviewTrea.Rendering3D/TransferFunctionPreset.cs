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

    /// <summary>Iodinated vessels in red, with bone kept pale behind them for orientation.</summary>
    public static TransferFunction Angio { get; } = new(new[]
    {
        new TransferFunctionPoint(-1024, Rgb.Black, 0),
        new TransferFunctionPoint(120, new Rgb(140, 30, 30), 0),
        new TransferFunctionPoint(220, new Rgb(200, 60, 50), 0.35),
        new TransferFunctionPoint(450, new Rgb(240, 130, 110), 0.75),
        new TransferFunctionPoint(900, new Rgb(230, 225, 215), 0.45),
        new TransferFunctionPoint(3071, new Rgb(245, 245, 240), 0.60),
    });

    /// <summary>
    /// Parenchyma and airways. The opacities are an order of magnitude below the others:
    /// lung is mostly air, so anything more turns the whole chest into a solid block.
    /// </summary>
    public static TransferFunction Lung { get; } = new(new[]
    {
        new TransferFunctionPoint(-1024, Rgb.Black, 0),

        // Scanned air reads about -1000, and the ray crosses hundreds of millimetres of it
        // outside the patient. At even 0.003 per millimetre that accumulates to a fog
        // around the whole chest, so the ramp starts above air rather than at it.
        new TransferFunctionPoint(-980, Rgb.Black, 0),
        new TransferFunctionPoint(-940, new Rgb(60, 80, 110), 0.008),
        new TransferFunctionPoint(-700, new Rgb(120, 150, 190), 0.04),
        new TransferFunctionPoint(-450, new Rgb(200, 200, 205), 0.10),
        new TransferFunctionPoint(200, new Rgb(240, 235, 225), 0.05),
        new TransferFunctionPoint(3071, new Rgb(255, 255, 255), 0.05),
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
