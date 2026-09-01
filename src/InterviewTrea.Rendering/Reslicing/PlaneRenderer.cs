using System;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Windowing;
using static System.FormattableString;

namespace InterviewTrea.Rendering.Reslicing;

/// <summary>
/// Resamples an arbitrary <see cref="ReslicePlane"/> out of a volume by trilinear
/// interpolation (FR-202, FR-206, FR-208).
/// </summary>
/// <remarks>
/// <para>
/// The whole method rests on one fact: an affine map sends a straight, evenly spaced line
/// to a straight, evenly spaced line. So the plane's two step vectors are converted from
/// patient millimetres into voxel indices <em>once</em>, and the inner loop advances by
/// adding that constant step. There is no matrix multiply per pixel, and no trigonometry
/// anywhere - the obliqueness is already baked into the two vectors by the time this runs.
/// </para>
/// <para>
/// Nothing here knows or cares which anatomical plane it is drawing. Axial, coronal,
/// sagittal and Iteration 4's rotated planes are the same code with different step
/// vectors, which is why FR-307 needs no new render path.
/// </para>
/// </remarks>
public static class PlaneRenderer
{
    /// <summary>
    /// Renders <paramref name="plane"/> into <paramref name="destination"/>, which must
    /// hold exactly <see cref="ReslicePlane.PixelCount"/> bytes in row-major order.
    /// </summary>
    public static void Render(
        Volume volume,
        ReslicePlane plane,
        WindowLevelLut lut,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(lut);

        if (destination.Length != plane.PixelCount)
        {
            throw new ArgumentException(
                Invariant($"Destination holds {destination.Length} bytes; the plane is {plane.Width}x{plane.Height} = {plane.PixelCount}."),
                nameof(destination));
        }

        Matrix4x4Affine toVoxel = volume.PatientToVoxel;

        // The origin is a position, so it goes through the full transform. The steps are
        // directions and must not pick up the translation - see TransformDirection.
        Point3D start = toVoxel.Transform(plane.Origin);
        Vector3D acrossRow = toVoxel.TransformDirection(plane.RowStep);
        Vector3D downColumn = toVoxel.TransformDirection(plane.ColumnStep);

        ReadOnlySpan<byte> table = lut.Table;
        int width = plane.Width;
        int destinationIndex = 0;

        for (int r = 0; r < plane.Height; r++)
        {
            // Each row's start is recomputed rather than carried over from the previous
            // row. That costs three multiplies per row against half a million per frame,
            // and it makes every row independent of every other - which is what will let
            // this loop be split across threads later without changing a single pixel.
            double x = start.X + (downColumn.X * r);
            double y = start.Y + (downColumn.Y * r);
            double z = start.Z + (downColumn.Z * r);

            for (int c = 0; c < width; c++)
            {
                double sample = volume.SampleTrilinear(x, y, z);

                // Trilinear never leaves the range of its eight inputs, all of which are
                // shorts, and the out-of-bounds value is a short too - so the sample is
                // always inside the table's domain and needs no clamp.
                //
                // The +0.5 is a round-half-up, and it is not cosmetic. The biased sum is
                // never negative, so plain truncation is floor, which would darken every
                // interpolated pixel by half a grey level on average - a systematic bias
                // over the whole image rather than symmetric rounding noise.
                destination[destinationIndex++] = table[(int)(sample + WindowLevelLut.Bias + 0.5)];

                x += acrossRow.X;
                y += acrossRow.Y;
                z += acrossRow.Z;
            }
        }
    }
}
