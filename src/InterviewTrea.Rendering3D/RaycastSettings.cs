using System;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Rendering3D;

/// <summary>How finely a ray is marched, and when it may give up (FR-602, FR-603).</summary>
public sealed record RaycastSettings
{
    /// <summary>Millimetres between samples along a ray.</summary>
    public required double StepMm { get; init; }

    /// <summary>Whether gradient shading is applied (FR-607).</summary>
    public bool IsShaded { get; init; }

    /// <summary>The lighting constants, when <see cref="IsShaded"/>.</summary>
    public ShadingParameters Shading { get; init; } = ShadingParameters.Default;

    /// <summary>
    /// Opacity below which a sample is not worth six extra trilinear samples to shade.
    /// </summary>
    /// <remarks>
    /// Shading costs about four times what a plain sample costs, and a sample contributing
    /// two percent of a pixel cannot repay that. The saving is real on a surface preset,
    /// where most of what a ray crosses is nearly transparent tissue in front of the one
    /// surface that matters.
    /// </remarks>
    public double MinimumOpacityToShade { get; init; } = 0.02;

    /// <summary>
    /// Accumulated opacity at which a ray stops (FR-602).
    /// </summary>
    /// <remarks>
    /// 0.99 means the remaining samples could between them change the pixel by at most one
    /// percent of full scale - under three levels out of 255, which is below what the eye
    /// resolves on a dark display. This is where most of the performance comes from: with a
    /// surface-like preset the majority of rays stop within a few millimetres of the skin
    /// instead of crossing the whole patient.
    /// </remarks>
    public double EarlyTerminationOpacity { get; init; } = 0.99;

    /// <summary>
    /// A step matched to <paramref name="volume"/>, optionally coarsened for a preview.
    /// </summary>
    /// <remarks>
    /// Half the smallest voxel dimension. The volume carries no detail finer than one
    /// voxel, so sampling at half of the shortest side is the Nyquist rate along the worst
    /// axis; stepping at a whole voxel would let a ray walk over a thin structure the same
    /// way a coarse slab MIP misses a 1 mm vessel. <paramref name="coarsenBy"/> is what
    /// FR-609's progressive refinement moves, and FR-603's correction is what keeps the
    /// coarse image looking like the fine one.
    /// </remarks>
    public static RaycastSettings For(Volume volume, double coarsenBy = 1.0)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(coarsenBy, 0);

        double finest = Math.Min(volume.Spacing.X, Math.Min(volume.Spacing.Y, volume.Spacing.Z));

        return new RaycastSettings { StepMm = finest / 2 * coarsenBy };
    }
}
