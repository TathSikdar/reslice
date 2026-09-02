using System;
using System.Threading;
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
        byte[] destination,
        CancellationToken cancellationToken = default)
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

        // The headlight sits at the eye, so the direction back to it is the reverse of the
        // view direction and is the same for every pixel under a parallel projection.
        Vector3D towardViewer = forward.Negate();

        // Every ray is parallel under an orthographic projection, so the direction converts
        // to voxel space once for the whole image. Only the origin changes per pixel.
        Vector3D stepInVoxels = volume.PatientToVoxel.TransformDirection(forward).Scale(settings.StepMm);

        // Rows, not pixels: a row is enough work to cover the cost of scheduling it, and
        // each row writes a disjoint run of the buffer, so nothing is shared.
        // A full-quality frame is hundreds of milliseconds and the camera can move during
        // it. Cancelling lets the caller start the next one straight away rather than wait
        // for a frame it has already decided not to show.
        ParallelOptions options = new() { CancellationToken = cancellationToken };

        Parallel.For(0, height, options, row =>
        {
            int offset = row * width * BytesPerPixel;

            for (int column = 0; column < width; column++)
            {
                Rgb pixel = Cast(
                    volume, camera.RayOrigin(column, row, width, height), forward, stepInVoxels,
                    colours, opacities, settings, towardViewer);

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
        RaycastSettings settings,
        Vector3D towardViewer)
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
                double shade = settings.IsShaded && alpha >= settings.MinimumOpacityToShade
                    ? GradientShader.Shade(
                        GradientShader.Gradient(volume, voxel.X, voxel.Y, voxel.Z),
                        towardViewer,
                        settings.Shading)
                    : 1.0;

                ray.Add(
                    Lit(colours[index], shade),
                    Lit(colours[index + 1], shade),
                    Lit(colours[index + 2], shade),
                    alpha);

                if (ray.Opacity >= settings.EarlyTerminationOpacity)
                {
                    break;
                }
            }

            voxel += stepInVoxels;
        }

        return ray.OverBlack();
    }

    // Clamped rather than left to wrap: a specular highlight on a bright preset drives the
    // product well past 255, and a wrapped byte turns the brightest part of a surface black.
    private static byte Lit(byte channel, double shade) =>
        (byte)Math.Clamp(channel * shade, 0, 255);
}
