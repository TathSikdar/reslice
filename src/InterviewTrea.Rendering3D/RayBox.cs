using System;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Rendering3D;

/// <summary>
/// Where a ray enters and leaves the volume (FR-602). The slab test, run in voxel space.
/// </summary>
/// <remarks>
/// <para>
/// The volume is a box with square corners in voxel space and an arbitrarily oriented one
/// in patient space, so the intersection is done in voxel coordinates where three
/// independent one-dimensional tests are all it takes. An affine map preserves the ray
/// parameter, so the t values that come out of the voxel-space test are still the
/// millimetres along the patient-space ray that went in - provided the patient direction
/// was unit length, which is the one thing this asks of its caller.
/// </para>
/// <para>
/// The domain is [0, Dim-1], voxel centre to voxel centre, matching
/// <see cref="Volume.ContainsContinuous"/>. If it did not match, a ray could be told it
/// was inside the volume at a point the sampler refuses to sample, and the silhouette
/// would carry a one-voxel skin of nothing.
/// </para>
/// </remarks>
public static class RayBox
{
    /// <summary>
    /// Intersects the infinite line through <paramref name="origin"/> along
    /// <paramref name="direction"/> with the volume.
    /// </summary>
    /// <returns>False when the line misses entirely; the t values are then meaningless.</returns>
    /// <remarks>
    /// A line, not a half-line: <paramref name="tEnter"/> is negative when the origin is
    /// already inside, and the caller must not clamp it to zero. The orthographic camera
    /// puts every ray origin on a plane through the middle of the volume, so clamping
    /// would silently render only the far half of the patient.
    /// </remarks>
    public static bool TryIntersect(
        Volume volume,
        Point3D origin,
        Vector3D direction,
        out double tEnter,
        out double tExit)
    {
        ArgumentNullException.ThrowIfNull(volume);

        Point3D o = volume.PatientToVoxel.Transform(origin);
        Vector3D d = volume.PatientToVoxel.TransformDirection(direction);

        tEnter = double.NegativeInfinity;
        tExit = double.PositiveInfinity;

        return Slab(o.X, d.X, volume.DimX - 1, ref tEnter, ref tExit)
            && Slab(o.Y, d.Y, volume.DimY - 1, ref tEnter, ref tExit)
            && Slab(o.Z, d.Z, volume.DimZ - 1, ref tEnter, ref tExit)
            && tEnter <= tExit;
    }

    /// <summary>Narrows the surviving interval by one pair of parallel faces at 0 and <paramref name="high"/>.</summary>
    private static bool Slab(double origin, double direction, double high, ref double tEnter, ref double tExit)
    {
        if (direction == 0)
        {
            // Parallel to this pair of faces: the ray is either between them for its whole
            // length or outside them for its whole length. Dividing here would give a NaN
            // that compares false against everything and would report a miss either way.
            return origin >= 0 && origin <= high;
        }

        double t0 = -origin / direction;
        double t1 = (high - origin) / direction;

        if (t0 > t1)
        {
            (t0, t1) = (t1, t0);
        }

        tEnter = Math.Max(tEnter, t0);
        tExit = Math.Min(tExit, t1);

        return tEnter <= tExit;
    }
}
