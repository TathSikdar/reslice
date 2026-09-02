using System;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Rendering3D;

/// <summary>
/// An orbit camera in patient space with an orthographic projection (FR-608).
/// </summary>
/// <remarks>
/// <para>
/// The state is a target, two angles and a view height - not a position and not an
/// accumulated matrix. Same reasoning as the Phase 1 reslice frames: a stored rotation
/// history drifts and cannot be reasoned about, whereas two angles either describe the
/// view you are looking at or they do not.
/// </para>
/// <para>
/// There is no eye distance, and spec 4.1 lists one. Under an orthographic projection
/// distance changes nothing: every ray is parallel, so moving the eye back only slides
/// each ray along itself. Rays are therefore generated on a plane through the target and
/// intersected with the volume as infinite lines, which is why they may legitimately
/// enter the volume at a negative parameter. Zoom is the view height in millimetres,
/// because under parallel projection that is the only thing that changes the scale.
/// </para>
/// </remarks>
public sealed record Camera3D
{
    // At exactly straight down the superior axis the world-up reference is parallel to
    // the view direction and the up vector is undefined. Stopping a degree short keeps
    // every angle legal rather than special-casing the pole in the render loop.
    private const double MaximumElevation = 89.0 * Math.PI / 180.0;

    private readonly double elevation;
    private readonly double viewHeightMm = 1;

    /// <summary>The point the camera orbits, and the centre of the image. Panning moves this.</summary>
    public required Point3D Target { get; init; }

    /// <summary>
    /// Rotation of the eye about the patient's superior axis, in radians. Zero puts the
    /// eye at the patient's left; -pi/2 puts it anterior, which is the frontal view.
    /// </summary>
    public required double Azimuth { get; init; }

    /// <summary>Angle of the eye above the axial plane, in radians. Clamped short of the pole.</summary>
    public required double Elevation
    {
        get => elevation;
        init => elevation = Math.Clamp(value, -MaximumElevation, MaximumElevation);
    }

    /// <summary>Millimetres of patient spanned by the full height of the image. Zoom.</summary>
    public required double ViewHeightMm
    {
        get => viewHeightMm;
        init => viewHeightMm = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "View height must be positive.");
    }

    /// <summary>Unit direction the camera looks along, from the eye toward the target.</summary>
    public Vector3D Forward
    {
        get
        {
            double c = Math.Cos(Elevation);

            // The direction from the target out to the eye; the camera looks back along it.
            Vector3D toEye = new(c * Math.Cos(Azimuth), c * Math.Sin(Azimuth), Math.Sin(Elevation));
            return toEye.Negate();
        }
    }

    /// <summary>Unit up direction of the image: patient superior, squared against the view.</summary>
    public Vector3D Up
    {
        get
        {
            Vector3D forward = Forward;

            // Gram-Schmidt: take out whatever part of superior points along the view, so
            // the image stays upright at every elevation instead of shearing toward the pole.
            Vector3D superior = Vector3D.UnitZ;
            return superior.Subtract(forward.Scale(forward.Dot(superior))).Normalized();
        }
    }

    /// <summary>
    /// Unit right direction of the image. At the frontal view this is the patient's left,
    /// which is radiological convention: the image is read as if facing the patient.
    /// </summary>
    public Vector3D Right => Forward.Cross(Up);

    /// <summary>
    /// A camera framing the whole of <paramref name="volume"/> from the front.
    /// </summary>
    /// <remarks>
    /// The view height is the volume's body diagonal rather than its tallest side. A box
    /// seen corner-on presents its diagonal, so any smaller framing would clip the corners
    /// part-way through an orbit - and a view that crops itself while being rotated looks
    /// like a renderer bug rather than a framing choice.
    /// </remarks>
    public static Camera3D Framing(Volume volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        Point3D centre = volume.VoxelToPatient.Transform(
            new Point3D((volume.DimX - 1) / 2.0, (volume.DimY - 1) / 2.0, (volume.DimZ - 1) / 2.0));

        Vector3D extent = new(
            (volume.DimX - 1) * volume.Spacing.X,
            (volume.DimY - 1) * volume.Spacing.Y,
            (volume.DimZ - 1) * volume.Spacing.Z);

        return new Camera3D
        {
            Target = centre,
            Azimuth = -Math.PI / 2,
            Elevation = 0,
            ViewHeightMm = Math.Max(extent.Length, 1),
        };
    }

    /// <summary>Millimetres of patient per output pixel, for an image of this height.</summary>
    public double PixelPitch(int imageHeight) => imageHeight > 0
        ? ViewHeightMm / imageHeight
        : throw new ArgumentOutOfRangeException(nameof(imageHeight), imageHeight, "Image height must be positive.");

    /// <summary>
    /// Where the ray through pixel (<paramref name="column"/>, <paramref name="row"/>) crosses
    /// the plane through the target. Its direction is <see cref="Forward"/> for every pixel.
    /// </summary>
    /// <remarks>
    /// The half-pixel offsets sample pixel centres. Row zero is the top of the image, so the
    /// row term is subtracted from the up direction; the column term is added to the right one.
    /// </remarks>
    public Point3D RayOrigin(int column, int row, int imageWidth, int imageHeight)
    {
        double pitch = PixelPitch(imageHeight);
        double right = (column + 0.5 - (imageWidth / 2.0)) * pitch;
        double up = ((imageHeight / 2.0) - row - 0.5) * pitch;

        return Target + Right.Scale(right) + Up.Scale(up);
    }

    /// <summary>Turns the camera by the given angles, in radians (FR-608, left-drag).</summary>
    public Camera3D Orbited(double byAzimuth, double byElevation) =>
        this with { Azimuth = Azimuth + byAzimuth, Elevation = Elevation + byElevation };

    /// <summary>Scales the view height. Factors below one zoom in (FR-608, wheel).</summary>
    public Camera3D Zoomed(double factor) => this with { ViewHeightMm = ViewHeightMm * factor };

    /// <summary>Slides the target within the image plane, in millimetres (FR-608, middle-drag).</summary>
    public Camera3D Panned(double rightMm, double upMm) =>
        this with { Target = Target + Right.Scale(rightMm) + Up.Scale(upMm) };
}
