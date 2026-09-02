using System;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;

namespace InterviewTrea.Core.Measurements;

/// <summary>The three shapes FR-401, FR-403 and FR-404 ask for.</summary>
public enum MeasurementKind
{
    Distance,
    Ellipse,
    Rectangle,
}

/// <summary>
/// The plane a measurement was drawn on, reduced to what a measurement actually needs
/// from it (FR-402, FR-406).
/// </summary>
/// <remarks>
/// Not a <see cref="ReslicePlane"/>, deliberately. That type carries a grid size and an
/// output pixel pitch, which are properties of how a pane happened to be drawing at the
/// time - resize the window or load a finer series and they change, while the measurement
/// has not moved a millimetre. What survives is the frame: somewhere the plane passes
/// through and the two directions lying in it.
///
/// The axes must be perpendicular unit vectors, which they are because every one of them
/// comes from <see cref="ReslicePlane.DisplayAxes"/> or from rotating it.
/// </remarks>
public readonly record struct MeasurementFrame(Point3D Anchor, Vector3D Row, Vector3D Column)
{
    /// <summary>Unit normal, right-handed from the two axes.</summary>
    public Vector3D Normal => Row.Cross(Column);
}

/// <summary>
/// One measurement, held entirely in patient millimetres (FR-401 to FR-404).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Start"/> and <see cref="End"/> are where the drag began and ended, in
/// patient space. Storing them there rather than in output pixels is the whole of FR-402:
/// anisotropic spacing and plane obliquity are already inside the coordinates by the time
/// they arrive, so a length is a subtraction and there is no place left for a correction
/// factor to be forgotten. A measurement recorded in pixels would need the pane's pitch,
/// the plane's axes and the voxel spacing to be reapplied on read, and would silently
/// change value if any of the three did.
/// </para>
/// <para>
/// A record rather than three subclasses. The shapes differ only in two switch arms - how
/// area is computed and what counts as inside - and a hierarchy would buy nothing while
/// making a heterogeneous list awkward to export (FR-408).
/// </para>
/// </remarks>
public sealed record Measurement(
    MeasurementKind Kind,
    MeasurementFrame Frame,
    Point3D Start,
    Point3D End)
{
    /// <summary>
    /// FR-410. Which measurement this is, as shown beside it and exported in the CSV.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assigned once by whoever adds the measurement to a list and never reused, rather
    /// than being the position in that list. Deleting the second of three would renumber
    /// the third into its place, and a CSV row exported before the deletion would then name
    /// a different measurement than the same number does on screen - the one thing an
    /// identifier exists to prevent.
    /// </para>
    /// <para>
    /// An init-only property rather than a positional parameter, so the identity of a
    /// measurement is not part of its value: two measurements of the same thing are equal
    /// as geometry, and `with` carries the number through an edit unchanged.
    /// </para>
    /// <para>
    /// DICOM has no standard for this. What it standardises is storage and exchange -
    /// Structured Reporting (TID 1500/300) and Presentation States, which carry an optional
    /// free-text label and say nothing about what a viewer draws. A stable sequential
    /// number is convention, not conformance.
    /// </para>
    /// </remarks>
    public int Id { get; init; }

    /// <summary>Straight-line distance in millimetres (FR-401).</summary>
    public double LengthMillimetres => (End - Start).Length;

    /// <summary>
    /// The drag's extent along the frame's row axis, signed, in millimetres. Signed
    /// because a drag right-to-left is as valid as left-to-right and the sign is what
    /// <see cref="Contains"/> needs to know which side of <see cref="Start"/> the shape
    /// lies on.
    /// </summary>
    public double SpanAcross => (End - Start).Dot(Frame.Row);

    /// <summary>The drag's extent down the frame's column axis, signed, in millimetres.</summary>
    public double SpanDown => (End - Start).Dot(Frame.Column);

    /// <summary>
    /// Enclosed area in square millimetres, or zero for a distance (FR-403, FR-404).
    /// </summary>
    /// <remarks>
    /// The ellipse is the one inscribed in the dragged box, so its semi-axes are half the
    /// spans and its area is pi/4 of the rectangle's - not pi times the spans, which is
    /// the factor-of-four error that looks plausible on screen because the outline is
    /// drawn from the same two numbers.
    /// </remarks>
    public double AreaSquareMillimetres => Kind switch
    {
        MeasurementKind.Rectangle => Math.Abs(SpanAcross * SpanDown),
        MeasurementKind.Ellipse => Math.PI / 4 * Math.Abs(SpanAcross * SpanDown),
        _ => 0,
    };

    /// <summary>
    /// Whether a patient-space point falls inside the region (FR-403, FR-404). Points off
    /// the plane are judged by their projection onto it, which is what the statistics pass
    /// wants: it walks the plane, so every point it offers is on it already.
    /// </summary>
    public bool Contains(Point3D patient)
    {
        if (Kind == MeasurementKind.Distance)
        {
            return false;
        }

        Vector3D offset = patient - Start;
        double across = offset.Dot(Frame.Row);
        double down = offset.Dot(Frame.Column);

        if (Kind == MeasurementKind.Rectangle)
        {
            return Between(across, SpanAcross) && Between(down, SpanDown);
        }

        // Normalised to the ellipse's own axes, where the boundary is the unit circle.
        // A zero span would divide by zero, and a drag that never left its starting pixel
        // is a real thing a user can do, so it encloses nothing rather than everything.
        double semiAcross = SpanAcross / 2;
        double semiDown = SpanDown / 2;

        if (semiAcross == 0 || semiDown == 0)
        {
            return false;
        }

        double u = (across - semiAcross) / semiAcross;
        double v = (down - semiDown) / semiDown;

        return (u * u) + (v * v) <= 1;
    }

    /// <summary>
    /// FR-406. Whether this measurement belongs on <paramref name="plane"/>: near enough
    /// to it, and parallel to the plane it was drawn on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The spec says "more than half a slice thickness from the measurement's plane",
    /// which has no meaning once FR-307 exists - an oblique plane has no slices and no
    /// thickness. Read as distance, with the tolerance handed in by the caller, which is
    /// the only place that knows what the volume's spacing is.
    /// </para>
    /// <para>
    /// The parallelism test is not in the spec and is not optional. Every measurement sits
    /// on the crosshair, and the crosshair is on all three planes at once, so distance
    /// alone would show an axial measurement in the sagittal pane - edge-on, collapsed to
    /// a line, and wrong. Requiring the normals to agree is what confines a measurement to
    /// the view it was made in. Sign is ignored because a plane and its reverse are the
    /// same plane.
    /// </para>
    /// </remarks>
    public bool IsVisibleOn(ReslicePlane plane, double toleranceMillimetres)
    {
        ArgumentNullException.ThrowIfNull(plane);

        return Math.Abs(plane.Normal.Dot(Frame.Normal)) >= ParallelThreshold
            && Math.Abs(plane.SignedDistanceTo(Frame.Anchor)) <= toleranceMillimetres;
    }

    /// <summary>
    /// How closely two normals have to agree to count as the same plane, as the absolute
    /// cosine of the angle between them.
    /// </summary>
    /// <remarks>
    /// A calibration knob. 0.9999 is about 0.8 degrees, which is far tighter than any
    /// rotation a hand makes and far looser than the last bits of a normal that has been
    /// through a few hundred cross products. It is not 1.0 because exact equality of two
    /// independently computed unit vectors is a coin toss in floating point, and a
    /// measurement that vanished from the pane it was drawn in would look like data loss.
    /// </remarks>
    private const double ParallelThreshold = 0.9999;

    // Inclusive between 0 and a signed span, whichever way round the drag went.
    private static bool Between(double value, double span) =>
        span >= 0 ? value >= 0 && value <= span : value <= 0 && value >= span;
}
