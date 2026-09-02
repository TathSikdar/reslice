using System;
using System.Threading.Tasks;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;
using static System.FormattableString;

namespace InterviewTrea.Rendering3D;

/// <summary>
/// Renders a volume by ray casting (FR-601, FR-602). Produces BGRA32 and knows nothing
/// about bitmaps, exactly as the 2D renderer produces Gray8 and knows nothing about them.
/// </summary>
public static class VolumeRaycaster
{
    /// <summary>Four bytes per pixel: blue, green, red, alpha, in that order.</summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// Renders <paramref name="volume"/> into <paramref name="destination"/>, which must
    /// hold <c>width * height * 4</c> bytes.
    /// </summary>
    /// <remarks>
    /// A <c>byte[]</c> rather than a <c>Span&lt;byte&gt;</c> because the scanlines run in
    /// parallel and a ref struct cannot be captured by the loop body. The 2D renderer takes
    /// a span because it is single-threaded; the difference is not an inconsistency.
    /// </remarks>
    public static void Render(
        Volume volume,
        Camera3D camera,
        TransferFunction transferFunction,
        RaycastSettings settings,
        int width,
        int height,
        byte[] destination)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(transferFunction);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        int required = width * height * BytesPerPixel;
        if (destination.Length != required)
        {
            throw new ArgumentException(
                Invariant($"Destination holds {destination.Length} bytes; a {width}x{height} BGRA image needs {required}."),
                nameof(destination));
        }

        // Corrected once per render rather than once per sample: FR-603's correction is a
        // Pow, and 4096 of them beats one per sample by about five orders of magnitude.
        float[] opacities = transferFunction.OpacitiesForStep(settings.StepMm);
        byte[] colours = transferFunction.Colours.ToArray();

        Vector3D forward = camera.Forward;

        // Every ray is parallel under an orthographic projection, so the direction converts
        // to voxel space once for the whole image. Only the origin changes per pixel.
        Vector3D stepInVoxels = volume.PatientToVoxel.TransformDirection(forward).Scale(settings.StepMm);

        // Rows, not pixels: a row is enough work to cover the cost of scheduling it, and
        // each row writes a disjoint run of the buffer, so nothing is shared.
        Parallel.For(0, height, row =>
        {
            int offset = row * width * BytesPerPixel;

            for (int column = 0; column < width; column++)
            {
                Rgb pixel = Cast(
                    volume, camera.RayOrigin(column, row, width, height), forward, stepInVoxels,
                    colours, opacities, settings);

                destination[offset + 0] = pixel.B;
                destination[offset + 1] = pixel.G;
                destination[offset + 2] = pixel.R;
                destination[offset + 3] = 255;
                offset += BytesPerPixel;
            }
        });
    }

    private static Rgb Cast(
        Volume volume,
        Point3D origin,
        Vector3D direction,
        Vector3D stepInVoxels,
        byte[] colours,
        float[] opacities,
        RaycastSettings settings)
    {
        if (!RayBox.TryIntersect(volume, origin, direction, out double enter, out double exit))
        {
            return Rgb.Black;
        }

        double step = settings.StepMm;

        // Midpoint sampling: each sample stands for the segment of ray around it, so it is
        // taken in the middle of that segment. Starting flush against the entry face would
        // give the first voxel a full sample's worth of weight for half a step of ray, and
        // a cube would render half a step wider than it is.
        int samples = (int)Math.Floor((exit - enter) / step);
        if (samples <= 0)
        {
            return Rgb.Black;
        }

        Point3D voxel = volume.PatientToVoxel.Transform(origin + direction.Scale(enter + (step / 2)));

        RayAccumulator ray = default;

        for (int i = 0; i < samples; i++)
        {
            int index = TransferFunction.IndexOf(volume.SampleTrilinear(voxel.X, voxel.Y, voxel.Z));
            float alpha = opacities[index];

            if (alpha > 0)
            {
                index *= 3;
                ray.Add(colours[index], colours[index + 1], colours[index + 2], alpha);

                if (ray.Opacity >= settings.EarlyTerminationOpacity)
                {
                    break;
                }
            }

            voxel += stepInVoxels;
        }

        return ray.OverBlack();
    }
}
