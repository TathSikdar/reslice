using System;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Windowing;
using static System.FormattableString;

namespace InterviewTrea.Rendering.Reslicing;

/// <summary>
/// Projects a slab of tissue centred on a plane down to a single image (FR-207).
/// </summary>
/// <remarks>
/// <para>
/// The in-plane walk is the same as <see cref="PlaneRenderer"/>. What is added is an inner
/// loop along the view normal, collapsing several samples per pixel by
/// <see cref="SlabMode"/>. The slab is centred on the plane rather than starting at it, so
/// increasing the thickness grows outwards in both directions and the anatomy under the
/// crosshair stays put.
/// </para>
/// <para>
/// Samples along the normal are spaced at the same millimetre pitch as the in-plane
/// samples. Anything coarser would alias structures the slab is meant to capture - a
/// 3 mm-pitch MIP can step straight over a 1 mm vessel - and anything finer would resolve
/// detail that is not in the data.
/// </para>
/// </remarks>
public static class SlabRenderer
{
    public static void Render(
        Volume volume,
        ReslicePlane plane,
        SlabMode mode,
        double thicknessMillimetres,
        WindowLevelLut lut,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(lut);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thicknessMillimetres);

        if (destination.Length != plane.PixelCount)
        {
            throw new ArgumentException(
                Invariant($"Destination holds {destination.Length} bytes; the plane is {plane.Width}x{plane.Height} = {plane.PixelCount}."),
                nameof(destination));
        }

        double pitch = plane.PixelSizeMillimetres;
        int intervals = (int)Math.Round(thicknessMillimetres / pitch, MidpointRounding.AwayFromZero);
        int samples = intervals + 1;

        // A slab thinner than one sample pitch degenerates to the plane itself, which is
        // the right answer rather than an error: the thickness control has to be able to
        // pass through "no slab" without the viewport going blank.
        double spacing = intervals == 0 ? 0 : thicknessMillimetres / intervals;

        // The lone sample of a degenerate slab belongs on the plane, not half a thickness
        // in front of it. Reading -thickness/2 unconditionally would leave the viewport
        // showing a plane it is not claiming to show, which is the quiet kind of wrong.
        double firstOffset = intervals == 0 ? 0 : -thicknessMillimetres / 2.0;

        Matrix4x4Affine toVoxel = volume.PatientToVoxel;
        Point3D start = toVoxel.Transform(plane.Origin);
        Vector3D acrossRow = toVoxel.TransformDirection(plane.RowStep);
        Vector3D downColumn = toVoxel.TransformDirection(plane.ColumnStep);

        // The normal is a patient-space unit vector, so one step of it in voxel space is
        // one millimetre of depth. Scaling by the sample spacing gives the increment that
        // walks the slab.
        Vector3D depthStep = toVoxel.TransformDirection(plane.Normal.Scale(spacing));
        Vector3D toFirstSample = toVoxel.TransformDirection(plane.Normal.Scale(firstOffset));

        ReadOnlySpan<byte> table = lut.Table;
        int width = plane.Width;
        int destinationIndex = 0;

        for (int r = 0; r < plane.Height; r++)
        {
            double x = start.X + (downColumn.X * r) + toFirstSample.X;
            double y = start.Y + (downColumn.Y * r) + toFirstSample.Y;
            double z = start.Z + (downColumn.Z * r) + toFirstSample.Z;

            for (int c = 0; c < width; c++)
            {
                double accumulator = mode switch
                {
                    SlabMode.Maximum => double.NegativeInfinity,
                    SlabMode.Minimum => double.PositiveInfinity,
                    _ => 0.0,
                };
                int counted = 0;

                double sx = x;
                double sy = y;
                double sz = z;

                for (int s = 0; s < samples; s++)
                {
                    // Out-of-bounds samples are skipped rather than folded in as the
                    // -1024 the sampler would return. MIP would survive that - air is
                    // never the brightest thing - but MinIP would report solid air for
                    // every pixel whose slab pokes out of the volume, and the average
                    // would be dragged down near every edge. Skipping costs one bounds
                    // test per sample and makes all three modes behave the same way at
                    // the boundary.
                    if (volume.ContainsContinuous(sx, sy, sz))
                    {
                        double sample = volume.SampleTrilinear(sx, sy, sz);
                        accumulator = mode switch
                        {
                            SlabMode.Maximum => Math.Max(accumulator, sample),
                            SlabMode.Minimum => Math.Min(accumulator, sample),
                            _ => accumulator + sample,
                        };
                        counted++;
                    }

                    sx += depthStep.X;
                    sy += depthStep.Y;
                    sz += depthStep.Z;
                }

                double value = counted == 0
                    ? Volume.OutsideValue
                    : mode == SlabMode.Average ? accumulator / counted : accumulator;

                destination[destinationIndex++] = table[(int)(value + WindowLevelLut.Bias + 0.5)];

                x += acrossRow.X;
                y += acrossRow.Y;
                z += acrossRow.Z;
            }
        }
    }
}
