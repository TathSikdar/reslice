using System;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Rendering3D;

/// <summary>
/// A single clip plane that trims the volume in from the patient's back (FR-613).
/// </summary>
/// <remarks>
/// <para>
/// The problem it exists for is the scanner table. A CT couch reads between about 0 and
/// 100 HU, which is where soft tissue reads, so no transfer function can classify it away:
/// classification is by density and the table has the patient's density. Removing it by
/// finding the body and keeping only that is segmentation, which Phase 2 §1.4 rules out by
/// name. What is left is geometry - the table is behind the patient and nothing else is,
/// so a plane parallel to the coronal plane separates them.
/// </para>
/// <para>
/// The axis is not a guess. DICOM patient coordinates are LPS, so +y <em>is</em> posterior
/// by definition of the coordinate system, whatever orientation the slices were acquired
/// in. That is why the clip is expressed in patient millimetres rather than in voxel
/// indices: an obliquely acquired study has no voxel axis that points at the patient's
/// back, but it still has a +y.
/// </para>
/// <para>
/// The depth is measured inwards from the volume's own posterior face rather than as an
/// absolute y, so the same number means the same thing on the next study. It is one plane
/// and not six: a full clip box would be a general tool, and this is a specific one that
/// removes the table on a supine scan. A prone scan puts the table in front of the patient
/// and this will not touch it - stated here rather than half-solved.
/// </para>
/// </remarks>
public static class VolumeClip
{
    /// <summary>The largest patient y the volume reaches: the back of the acquired box.</summary>
    public static double PosteriorExtent(Volume volume) => Extent(volume).High;

    /// <summary>How far the volume spans from its anterior face to its posterior one, in mm.</summary>
    public static double AnteroposteriorSpan(Volume volume)
    {
        (double low, double high) = Extent(volume);
        return high - low;
    }

    /// <summary>
    /// Narrows a ray interval to the part of the ray anterior to the plane at
    /// <paramref name="posteriorLimit"/>.
    /// </summary>
    /// <returns>False when the whole ray is behind the plane and nothing survives.</returns>
    /// <remarks>
    /// The same half-space arithmetic as one of <see cref="RayBox"/>'s slabs, with only the
    /// far face. Which end of the interval moves depends on which way the ray is pointing:
    /// a ray travelling posteriorly leaves early, one travelling anteriorly enters late.
    /// Getting that backwards would clip the near half of the patient and leave the table,
    /// which is a mistake the picture makes obvious - so it is worth saying that the sign
    /// of <paramref name="direction"/>.Y is the whole of it.
    /// </remarks>
    public static bool TryNarrow(
        Point3D origin,
        Vector3D direction,
        double posteriorLimit,
        ref double tEnter,
        ref double tExit)
    {
        if (direction.Y == 0)
        {
            // Parallel to the plane: the ray is wholly on one side of it and never crosses.
            return origin.Y <= posteriorLimit;
        }

        double t = (posteriorLimit - origin.Y) / direction.Y;

        if (direction.Y > 0)
        {
            tExit = Math.Min(tExit, t);
        }
        else
        {
            tEnter = Math.Max(tEnter, t);
        }

        return tEnter <= tExit;
    }

    // The eight corners, because an obliquely acquired volume's posterior face is not any
    // one of its voxel axes and the extreme y can be at any corner.
    private static (double Low, double High) Extent(Volume volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;

        for (int corner = 0; corner < 8; corner++)
        {
            Point3D patient = volume.VoxelToPatient.Transform(new Point3D(
                (corner & 1) == 0 ? 0 : volume.DimX - 1,
                (corner & 2) == 0 ? 0 : volume.DimY - 1,
                (corner & 4) == 0 ? 0 : volume.DimZ - 1));

            low = Math.Min(low, patient.Y);
            high = Math.Max(high, patient.Y);
        }

        return (low, high);
    }
}
