using System;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;

namespace InterviewTrea.Rendering3D;

/// <summary>An 8-bit colour. Not <c>System.Windows.Media.Color</c>: NFR-301 forbids it here.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Black => default;
}

/// <summary>
/// One control point of a transfer function: at this Hounsfield value the volume takes
/// this colour and this opacity (FR-604).
/// </summary>
/// <param name="Hounsfield">Where on the CT scale the point sits.</param>
/// <param name="Colour">The colour at that value.</param>
/// <param name="Opacity">
/// Opacity per <see cref="TransferFunction.ReferenceStepMm"/> of ray, in 0..1. Opacity is a
/// property of a length of tissue, not of a sample, which is what FR-603 turns on.
/// </param>
public readonly record struct TransferFunctionPoint(int Hounsfield, Rgb Colour, double Opacity);

/// <summary>
/// Maps Hounsfield units to colour and opacity through a lookup table over the CT scale
/// (FR-604).
/// </summary>
/// <remarks>
/// <para>
/// The same decision as the Phase 1 window/level LUT, for the same reason: classification
/// runs once per sample and a ray takes hundreds of samples, so the per-sample cost has to
/// be one array read. The table is 4096 entries covering -1024..3071 - one entry per
/// Hounsfield unit, so there is no quantisation to argue about.
/// </para>
/// <para>
/// Between control points the interpolation is linear in each channel and in opacity.
/// Outside the outermost points the value clamps rather than extrapolating: extrapolating
/// opacity runs it past 1 or below 0 within a few hundred Hounsfield units, and a preset
/// would go opaque in air the moment someone dragged its lowest point upward.
/// </para>
/// </remarks>
public sealed class TransferFunction
{
    /// <summary>Bottom of the table. Matches <c>Volume.OutsideValue</c>, so air off the end of the data classifies.</summary>
    public const int MinimumHounsfield = -1024;

    /// <summary>Top of the table.</summary>
    public const int MaximumHounsfield = 3071;

    public const int TableLength = MaximumHounsfield - MinimumHounsfield + 1;

    /// <summary>
    /// The ray length an <see cref="TransferFunctionPoint.Opacity"/> is quoted for, in
    /// millimetres. A point's opacity means "this much of the light is stopped by one
    /// millimetre of this tissue".
    /// </summary>
    public const double ReferenceStepMm = 1.0;

    private readonly byte[] colours;
    private readonly float[] opacities;

    /// <exception cref="ArgumentException">Fewer than two points, or a point out of order.</exception>
    public TransferFunction(IReadOnlyList<TransferFunctionPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 2)
        {
            throw new ArgumentException("A transfer function needs at least two control points.", nameof(points));
        }

        for (int i = 1; i < points.Count; i++)
        {
            // Strictly increasing, not merely sorted. Two points at the same Hounsfield
            // value would ask for a vertical step, and the interpolation below would
            // divide by zero to draw it.
            if (points[i].Hounsfield <= points[i - 1].Hounsfield)
            {
                throw new ArgumentException(
                    Invariant($"Control points must strictly increase in Hounsfield value; {points[i].Hounsfield} follows {points[i - 1].Hounsfield}."),
                    nameof(points));
            }
        }

        Points = points.ToArray();
        colours = new byte[TableLength * 3];
        opacities = new float[TableLength];

        Build();
    }

    /// <summary>The control points, in increasing Hounsfield order.</summary>
    public IReadOnlyList<TransferFunctionPoint> Points { get; }

    /// <summary>Three bytes per entry, R then G then B. Index with <see cref="IndexOf"/> times three.</summary>
    public ReadOnlySpan<byte> Colours => colours;

    /// <summary>Opacity per <see cref="ReferenceStepMm"/>, one entry per Hounsfield unit.</summary>
    public ReadOnlySpan<float> Opacities => opacities;

    /// <summary>Table index for a Hounsfield value, clamped to the ends of the CT scale.</summary>
    /// <remarks>
    /// Takes a <c>double</c> because it is fed by trilinear samples, which are genuinely
    /// fractional. Rounds rather than truncating so that the entry chosen is the nearest
    /// one, which keeps the half-unit bias out of a hundred composited samples.
    /// </remarks>
    public static int IndexOf(double hounsfield)
    {
        int rounded = (int)Math.Round(hounsfield, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded - MinimumHounsfield, 0, TableLength - 1);
    }

    /// <summary>
    /// The opacity table rewritten for a ray that steps <paramref name="stepMm"/> at a time
    /// (FR-603).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The classic volume-rendering bug is to skip this. Opacity is defined per unit
    /// distance, so a ray taking half-millimetre steps passes through twice as many samples
    /// of the same tissue and, uncorrected, comes out twice as opaque. The image then
    /// changes appearance when the step changes - which is exactly what progressive
    /// refinement does (FR-609), so a low-resolution preview would not merely be coarser
    /// than the image it resolves to, it would be a different picture.
    /// </para>
    /// <para>
    /// The correction is the survival probability over the step: transparency compounds, so
    /// <c>1 - A</c> raised to the number of reference lengths the step covers.
    /// </para>
    /// </remarks>
    public float[] OpacitiesForStep(double stepMm)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stepMm, 0);

        double exponent = stepMm / ReferenceStepMm;
        float[] corrected = new float[TableLength];

        for (int i = 0; i < corrected.Length; i++)
        {
            float reference = opacities[i];

            // Pow is not cheap, and 0 and 1 are the overwhelmingly common entries in a
            // preset: most of the CT scale is either invisible or solid.
            corrected[i] = reference is <= 0 or >= 1
                ? reference
                : (float)(1 - Math.Pow(1 - reference, exponent));
        }

        return corrected;
    }

    private void Build()
    {
        int segment = 0;

        for (int i = 0; i < TableLength; i++)
        {
            int hounsfield = i + MinimumHounsfield;

            // The table is walked in order, so the segment only ever moves forward: this is
            // a merge, not a binary search per entry.
            while (segment + 2 < Points.Count && Points[segment + 1].Hounsfield <= hounsfield)
            {
                segment++;
            }

            TransferFunctionPoint low = Points[segment];
            TransferFunctionPoint high = Points[segment + 1];

            double t = hounsfield <= low.Hounsfield ? 0
                : hounsfield >= high.Hounsfield ? 1
                : (double)(hounsfield - low.Hounsfield) / (high.Hounsfield - low.Hounsfield);

            colours[(i * 3) + 0] = Lerp(low.Colour.R, high.Colour.R, t);
            colours[(i * 3) + 1] = Lerp(low.Colour.G, high.Colour.G, t);
            colours[(i * 3) + 2] = Lerp(low.Colour.B, high.Colour.B, t);
            opacities[i] = (float)Math.Clamp(low.Opacity + ((high.Opacity - low.Opacity) * t), 0, 1);
        }
    }

    // Rounds rather than truncating: without it the midpoint of 0 and 255 is 127, and every
    // ramp in the table sits half a level dark.
    private static byte Lerp(byte a, byte b, double t) =>
        (byte)Math.Round(a + ((b - a) * t), MidpointRounding.AwayFromZero);
}
