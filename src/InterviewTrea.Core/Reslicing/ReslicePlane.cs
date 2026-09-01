using System;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Core.Reslicing;

/// <summary>
/// A rectangular grid of sample points in patient space: the thing a viewport actually
/// draws (FR-202, FR-206, FR-208).
/// </summary>
/// <remarks>
/// <para>
/// A plane is four values. <see cref="Origin"/> is the patient-space location of output
/// pixel (0, 0). <see cref="RowStep"/> is how far in patient millimetres one step to the
/// right along a row travels, and <see cref="ColumnStep"/> the same for one step down a
/// column. <see cref="Width"/> and <see cref="Height"/> say how many steps there are.
/// Nothing here knows whether the plane is axial or oblique, which is the point: the
/// renderer walks any of them the same way, and Iteration 4's rotated planes need no new
/// code path.
/// </para>
/// <para>
/// The grid is <em>isotropic in patient space</em>: both step vectors have the same
/// length, so an output pixel is square in millimetres regardless of how anisotropic the
/// voxels are. That is where FR-208 is satisfied. Sampling a 0.7 x 0.7 x 3.0 mm volume
/// onto a 0.7 mm grid gives a coronal image roughly 250 rows tall rather than the 60 rows
/// the volume physically has, with the space between slices genuinely interpolated rather
/// than smeared by the display. Correcting the aspect at draw time instead would stretch
/// 60 rows over the same area and produce visible banding.
/// </para>
/// <para>
/// DICOM patient coordinates are LPS: +X towards the patient's Left, +Y towards their
/// Posterior, +Z towards their Superior. Every axis below is written in those terms.
/// </para>
/// </remarks>
public sealed record ReslicePlane
{
    public ReslicePlane(Point3D origin, Vector3D rowStep, Vector3D columnStep, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Origin = origin;
        RowStep = rowStep;
        ColumnStep = columnStep;
        Width = width;
        Height = height;
    }

    /// <summary>Patient-space location of output pixel (0, 0).</summary>
    public Point3D Origin { get; }

    /// <summary>Patient-space displacement per +1 output column.</summary>
    public Vector3D RowStep { get; }

    /// <summary>Patient-space displacement per +1 output row.</summary>
    public Vector3D ColumnStep { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Unit normal, right-handed from the two step vectors.</summary>
    public Vector3D Normal => RowStep.Cross(ColumnStep).Normalized();

    /// <summary>Millimetres per output pixel. Equal on both axes by construction.</summary>
    public double PixelSizeMillimetres => RowStep.Length;

    public int PixelCount => Width * Height;

    /// <summary>Patient-space point at a continuous output pixel coordinate.</summary>
    public Point3D ToPatient(double column, double row) =>
        Origin + RowStep.Scale(column) + ColumnStep.Scale(row);

    /// <summary>
    /// Projects a patient-space point onto the plane and returns its output pixel
    /// coordinate. Any component along the normal is discarded, so a point off the plane
    /// maps to the pixel directly beneath it - which is what a crosshair needs, since the
    /// point picked in another viewport is almost never exactly in this one.
    /// </summary>
    /// <remarks>
    /// Dividing by the squared step length is only equivalent to projecting onto a unit
    /// axis and converting to pixels because the two step vectors are orthogonal and
    /// equally long. Every plane this type produces has that property, rotated ones
    /// included, because rotation preserves an orthonormal frame.
    /// </remarks>
    public (double Column, double Row) ToPixel(Point3D patient)
    {
        Vector3D offset = patient - Origin;
        double scale = 1.0 / RowStep.LengthSquared;
        return (offset.Dot(RowStep) * scale, offset.Dot(ColumnStep) * scale);
    }

    /// <summary>
    /// Signed distance in millimetres from <paramref name="patient"/> to this plane,
    /// positive on the <see cref="Normal"/> side.
    /// </summary>
    public double SignedDistanceTo(Point3D patient) => (patient - Origin).Dot(Normal);

    /// <summary>
    /// The two unit display axes of a standard plane: the direction of an output row
    /// (left to right on screen) and of an output column (top to bottom).
    /// </summary>
    /// <remarks>
    /// These encode radiological display convention, not merely mathematics.
    /// <list type="bullet">
    /// <item>Axial is viewed from the patient's feet, so the patient's Left appears on the
    /// viewer's right (+X rightwards) and Posterior is at the bottom (+Y downwards). Its
    /// normal works out as +Z, towards the head.</item>
    /// <item>Coronal is viewed from the front, Left on the right (+X), Superior at the top
    /// which is -Z downwards. Its normal works out as +Y, into the patient's back.</item>
    /// <item>Sagittal is viewed from the patient's left, so Anterior is on the viewer's
    /// left and Posterior on the right (+Y rightwards), Superior at the top (-Z
    /// downwards). Its normal works out as -X, pointing to the patient's Right, so
    /// advancing along the normal walks the body from left to right.</item>
    /// </list>
    /// </remarks>
    public static (Vector3D Row, Vector3D Column) DisplayAxes(PlaneOrientation orientation) =>
        orientation switch
        {
            PlaneOrientation.Axial => (Vector3D.UnitX, Vector3D.UnitY),
            PlaneOrientation.Coronal => (Vector3D.UnitX, -Vector3D.UnitZ),
            PlaneOrientation.Sagittal => (Vector3D.UnitY, -Vector3D.UnitZ),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, null),
        };

    /// <summary>
    /// Builds the standard plane of the given orientation that passes through
    /// <paramref name="crosshair"/>, sized to cover the whole volume.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The extent is measured by projecting the volume's eight corners onto the two
    /// display axes and taking the span. That is correct for an obliquely acquired series,
    /// where the volume is a tilted box in patient space and its footprint on a standard
    /// plane is wider than any of its own dimensions.
    /// </para>
    /// <para>
    /// Only the crosshair's position <em>along the normal</em> reaches the result, and that
    /// is not an extra step - it falls out of the projection. The origin's component along
    /// each display axis is <c>min(corner . axis)</c>, with the crosshair's own component
    /// added and immediately subtracted, so moving the crosshair within the plane leaves
    /// the grid exactly where it was. That property is what stops the coronal image
    /// sliding under the cursor when you click in the axial view, and it also fixes
    /// <see cref="Width"/> and <see cref="Height"/> for every slice of a given
    /// orientation, so a viewport allocates its bitmap once per volume.
    /// </para>
    /// </remarks>
    /// <param name="pixelSizeMillimetres">
    /// Output pixel size. Callers normally pass the volume's smallest voxel spacing: finer
    /// spends work resolving detail the data does not contain, coarser discards detail it
    /// does.
    /// </param>
    public static ReslicePlane Through(
        Volume volume,
        PlaneOrientation orientation,
        Point3D crosshair,
        double pixelSizeMillimetres) =>
        Through(volume, DisplayAxes(orientation), crosshair, pixelSizeMillimetres);

    /// <summary>
    /// The same construction on axes given explicitly rather than looked up, which is what
    /// an oblique plane needs (FR-307).
    /// </summary>
    /// <remarks>
    /// Nothing here knows or cares whether the axes came from the standard table. That is
    /// the whole of what oblique reslicing costs in this layer: the extent is measured by
    /// projecting the volume's corners onto whatever two axes arrive, so a rotated plane
    /// gets a correctly sized footprint by the same arithmetic that sizes an axis-aligned
    /// one - and a diagonal footprint is genuinely larger, which is why <see cref="Width"/>
    /// and <see cref="Height"/> change as a plane rotates and a viewport's bitmap has to
    /// follow them rather than being allocated once per volume.
    ///
    /// The axes must be perpendicular unit vectors. That is not checked: they come from
    /// the standard table or from rotating it, both of which preserve the property, and a
    /// per-frame guard on a value that cannot go wrong is a cost paid every frame.
    /// </remarks>
    public static ReslicePlane Through(
        Volume volume,
        (Vector3D Row, Vector3D Column) axes,
        Point3D crosshair,
        double pixelSizeMillimetres)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSizeMillimetres);

        (Vector3D row, Vector3D column) = axes;

        double minAlongRow = double.PositiveInfinity;
        double maxAlongRow = double.NegativeInfinity;
        double minAlongColumn = double.PositiveInfinity;
        double maxAlongColumn = double.NegativeInfinity;

        // The corners are voxel centres, [0, Dim-1], matching Volume's sampling domain.
        // Using the outer faces instead would add half a voxel of blank margin on every
        // side, which reads as a border that grows with slice thickness.
        for (int corner = 0; corner < 8; corner++)
        {
            Point3D p = volume.VoxelToPatient.Transform(
                (corner & 1) == 0 ? 0 : volume.DimX - 1,
                (corner & 2) == 0 ? 0 : volume.DimY - 1,
                (corner & 4) == 0 ? 0 : volume.DimZ - 1);

            Vector3D offset = p - crosshair;
            double alongRow = offset.Dot(row);
            double alongColumn = offset.Dot(column);

            minAlongRow = Math.Min(minAlongRow, alongRow);
            maxAlongRow = Math.Max(maxAlongRow, alongRow);
            minAlongColumn = Math.Min(minAlongColumn, alongColumn);
            maxAlongColumn = Math.Max(maxAlongColumn, alongColumn);
        }

        return new ReslicePlane(
            crosshair + row.Scale(minAlongRow) + column.Scale(minAlongColumn),
            row.Scale(pixelSizeMillimetres),
            column.Scale(pixelSizeMillimetres),
            PixelsSpanning(maxAlongRow - minAlongRow, pixelSizeMillimetres),
            PixelsSpanning(maxAlongColumn - minAlongColumn, pixelSizeMillimetres));
    }

    /// <summary>
    /// How close to a whole number of pixels an extent has to be before it is treated as
    /// exactly that many, in pixels.
    /// </summary>
    /// <remarks>
    /// A calibration knob rather than a constant of the mathematics, and it exists because
    /// the common case lands precisely on the boundary. An axis-aligned plane over a
    /// regular grid has an extent that is an exact multiple of the pixel size in real
    /// arithmetic - 63 steps of 0.7 mm - but 0.7 is not representable in binary, so the
    /// quotient comes out as 62.99999999999999 with one set of axes and 63.000000000000014
    /// with another that differs only in the last bits. Without this tolerance, rotating a
    /// plane by an angle that should change nothing adds a whole row, and the grid size
    /// depends on how the axes were arrived at rather than on where they point.
    ///
    /// 1e-9 pixels is a picometre of image at any spacing a scanner produces, so it cannot
    /// hide a real difference. It would need revisiting only if the output grid were ever
    /// made far finer than the voxel spacing.
    /// </remarks>
    private const double PixelSnapTolerance = 1e-9;

    // +1 because both endpoints are sampled: a 3 mm span at 1 mm spacing needs samples at
    // 0, 1, 2 and 3. Ceiling rather than rounding so the far edge is never cropped.
    private static int PixelsSpanning(double extentMillimetres, double pixelSizeMillimetres) =>
        (int)Math.Ceiling((extentMillimetres / pixelSizeMillimetres) - PixelSnapTolerance) + 1;
}
