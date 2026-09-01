using System;
using System.Collections.Generic;
using System.Linq;
using InterviewTrea.Core.Geometry;
using static System.FormattableString;

namespace InterviewTrea.Dicom;

/// <summary>Why a series cannot be reconstructed into a volume.</summary>
public enum SeriesRejectionReason
{
    TooFewSlices,

    /// <summary>FR-105: the slices are not all in the same patient coordinate system.</summary>
    MismatchedFrameOfReference,

    /// <summary>Slices disagree on dimensions, pixel spacing or orientation.</summary>
    InconsistentGeometry,

    /// <summary>FR-107a: ImageOrientationPatient is not two orthonormal direction cosines.</summary>
    MalformedOrientation,

    /// <summary>FR-107b: the slices do not stack along their own normal.</summary>
    GantryTilt,

    /// <summary>FR-106: slice spacing varies by more than the permitted tolerance.</summary>
    NonUniformSpacing,
}

/// <summary>
/// A series that cannot be reconstructed, carrying both a reason the code can branch on
/// and a message a human can act on.
/// </summary>
public sealed class SeriesRejectedException : Exception
{
    public SeriesRejectedException(SeriesRejectionReason reason, string message)
        : base(message) => Reason = reason;

    public SeriesRejectedException()
    {
    }

    public SeriesRejectedException(string message)
        : base(message)
    {
    }

    public SeriesRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SeriesRejectionReason Reason { get; }
}

/// <summary>A series that passed validation, in acquisition order.</summary>
/// <param name="OrderedSlices">Sorted by projection onto <paramref name="Normal"/> (FR-103).</param>
/// <param name="Normal">Unit normal of the image plane: row cosine crossed with column cosine.</param>
/// <param name="SliceStep">
/// The measured patient-space displacement from one slice to the next, taken from the
/// positions themselves rather than assumed to be <c>spacing * normal</c>.
/// </param>
public sealed record SeriesGeometry(
    IReadOnlyList<SliceHeader> OrderedSlices,
    Vector3D Normal,
    Vector3D SliceStep);

/// <summary>
/// Decides whether a set of slice headers describes a volume that can be reconstructed
/// without guessing, and puts them in order (FR-103, FR-105, FR-106, FR-107).
/// </summary>
/// <remarks>
/// <para>
/// Validation and ordering are one operation rather than two, because every uniformity
/// check is a statement about successive slices and there is no "successive" until the
/// slices are sorted. Splitting them would mean sorting twice or trusting the caller to
/// have done it.
/// </para>
/// <para>
/// <b>FR-107 as written in the spec tests the wrong thing.</b> It says to detect gantry
/// tilt by checking that ImageOrientationPatient is orthogonal. It always is: a tilted
/// gantry rotates the imaging plane relative to the table, and the two direction cosines
/// within that plane stay perpendicular to each other regardless. The check would never
/// fire. It is split here into two genuinely different faults - a malformed header
/// (FR-107a) and a tilted acquisition (FR-107b) - and only the second is what the
/// requirement was reaching for.
/// </para>
/// <para>
/// Every tolerance is a constructor parameter, not a constant. They are calibration
/// knobs: the tilt threshold in particular wants tuning against a real tilted series,
/// and the defaults here are reasoned rather than measured.
/// </para>
/// </remarks>
public sealed class GeometryValidator
{
    private readonly double orthogonalityTolerance;
    private readonly double tiltTolerance;
    private readonly double spacingVarianceTolerance;
    private readonly int minimumSlices;

    /// <param name="orthogonalityTolerance">
    /// Largest permitted |row · column|. Direction cosines are stored as decimal strings,
    /// so exact zero is not achievable and some slack is required; 1e-6 is far below any
    /// real skew and far above the rounding.
    /// </param>
    /// <param name="tiltTolerance">
    /// How far the stacking direction may fall short of the slice normal, as
    /// <c>1 - |d · n|</c>. 1e-3 corresponds to about 2.5 degrees.
    /// </param>
    /// <param name="spacingVarianceTolerance">
    /// FR-106's "1% variance", read as the spread of successive gaps divided by their
    /// median. The spec does not define the quantity; this is the reading that makes it
    /// scale-free and immune to a single outlier setting the baseline.
    /// </param>
    /// <param name="minimumSlices">
    /// Below three there are fewer than two gaps, so spacing uniformity is not a question
    /// that can be asked, and the result is not a volume worth reconstructing.
    /// </param>
    public GeometryValidator(
        double orthogonalityTolerance = 1e-6,
        double tiltTolerance = 1e-3,
        double spacingVarianceTolerance = 0.01,
        int minimumSlices = 3)
    {
        this.orthogonalityTolerance = orthogonalityTolerance;
        this.tiltTolerance = tiltTolerance;
        this.spacingVarianceTolerance = spacingVarianceTolerance;
        this.minimumSlices = minimumSlices;
    }

    /// <exception cref="SeriesRejectedException">The series cannot be reconstructed.</exception>
    public SeriesGeometry Validate(IReadOnlyList<SliceHeader> slices)
    {
        ArgumentNullException.ThrowIfNull(slices);

        if (slices.Count < minimumSlices)
        {
            throw Reject(
                SeriesRejectionReason.TooFewSlices,
                Invariant($"A volume needs at least {minimumSlices} slices; this series has {slices.Count}."));
        }

        SliceHeader first = slices[0];

        RequireSingleFrameOfReference(slices);

        // FR-107a runs before the consistency sweep and before anything that uses the
        // normal: a skewed orientation is the more specific diagnosis, and the normal is
        // the cross product of these two vectors, which a degenerate pair does not have.
        double skew = first.RowCosine.Normalized().Dot(first.ColumnCosine.Normalized());
        if (Math.Abs(skew) > orthogonalityTolerance)
        {
            throw Reject(
                SeriesRejectionReason.MalformedOrientation,
                Invariant($"ImageOrientationPatient (0020,0037) is malformed: the row and column direction cosines are not perpendicular (dot product {skew:0.######}, tolerance {orthogonalityTolerance:0.######}). This is a bad header, not a tilted gantry."));
        }

        RequireConsistentGeometry(slices, first);

        Vector3D normal = first.Normal;

        // FR-103. Sorting by the projection of ImagePositionPatient onto the normal is the
        // only ordering that reflects where the slices physically are. InstanceNumber is
        // not reliable across manufacturers and is deliberately ignored.
        IReadOnlyList<SliceHeader> ordered = slices
            .OrderBy(s => s.DistanceAlong(normal))
            .ToArray();

        double[] gaps = new double[ordered.Count - 1];
        for (int k = 1; k < ordered.Count; k++)
        {
            gaps[k - 1] = ordered[k].DistanceAlong(normal) - ordered[k - 1].DistanceAlong(normal);
        }

        RequireUniformSpacing(gaps);

        // The measured step, not spacing * normal. Building the affine from the assumption
        // rather than the measurement is precisely how a tilted series ends up rendering
        // as a sheared but plausible-looking volume.
        Vector3D step = ordered[1].Position - ordered[0].Position;

        RequireNoGantryTilt(step, normal);

        return new SeriesGeometry(ordered, normal, step);
    }

    private static SeriesRejectedException Reject(SeriesRejectionReason reason, string message) =>
        new(reason, message);

    private static void RequireSingleFrameOfReference(IReadOnlyList<SliceHeader> slices)
    {
        // FR-105. Two frames of reference means two coordinate systems, so the positions
        // are not comparable and stacking them would place slices at coordinates that mean
        // nothing. There is no safe way to guess a registration between them.
        string[] frames = slices
            .Select(s => s.FrameOfReferenceUid)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (frames.Length > 1)
        {
            throw Reject(
                SeriesRejectionReason.MismatchedFrameOfReference,
                Invariant($"Slices span {frames.Length} different FrameOfReferenceUIDs (0020,0052). They are not in the same patient coordinate system and cannot be stacked into one volume."));
        }
    }

    private void RequireConsistentGeometry(IReadOnlyList<SliceHeader> slices, SliceHeader first)
    {
        foreach (SliceHeader slice in slices)
        {
            if (slice.Rows != first.Rows || slice.Columns != first.Columns)
            {
                throw Reject(
                    SeriesRejectionReason.InconsistentGeometry,
                    Invariant($"Slices disagree on dimensions: {first.Columns}x{first.Rows} and {slice.Columns}x{slice.Rows}. A volume is a single rectangular array."));
            }

            if (Math.Abs(slice.AdjacentRowSpacing - first.AdjacentRowSpacing) > 1e-6 ||
                Math.Abs(slice.AdjacentColumnSpacing - first.AdjacentColumnSpacing) > 1e-6)
            {
                throw Reject(
                    SeriesRejectionReason.InconsistentGeometry,
                    Invariant($"Slices disagree on PixelSpacing (0028,0030): [{first.AdjacentRowSpacing}, {first.AdjacentColumnSpacing}] and [{slice.AdjacentRowSpacing}, {slice.AdjacentColumnSpacing}] mm."));
            }

            // A single affine cannot describe a stack whose slices face different ways.
            // Normalised before comparing: the cosines are decimal strings and are only
            // nominally unit length, so a raw dot product would confuse "faces a different
            // way" with "is 1.00005 long".
            if (Math.Abs(slice.RowCosine.Normalized().Dot(first.RowCosine.Normalized()) - 1) > orthogonalityTolerance ||
                Math.Abs(slice.ColumnCosine.Normalized().Dot(first.ColumnCosine.Normalized()) - 1) > orthogonalityTolerance)
            {
                throw Reject(
                    SeriesRejectionReason.InconsistentGeometry,
                    "Slices disagree on ImageOrientationPatient (0020,0037). A single volume must have one image plane orientation throughout.");
            }
        }
    }

    private void RequireUniformSpacing(double[] gaps)
    {
        // FR-106. The ratio is scale-free, so the same 1% means the same thing at 0.6 mm
        // and at 5 mm.
        //
        // Median rather than mean as the denominator. Mutation testing showed this makes
        // almost no difference to the accept/reject verdict - the spread appears in the
        // numerator too, so both denominators reject the same series in practice. Where it
        // does matter is the classification below and the number quoted in the message: a
        // single doubled gap drags the mean up far enough that the doubled gap no longer
        // looks like an outlier against it, and a missing slice gets reported as merely
        // uneven spacing.
        double[] sorted = [.. gaps.Order()];
        double median = sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2.0;

        double smallest = sorted[0];
        double largest = sorted[^1];

        if (median <= 1e-6)
        {
            throw Reject(
                SeriesRejectionReason.NonUniformSpacing,
                "Slices share the same ImagePositionPatient (0020,0032); the series has no extent along its normal. This is usually a duplicated or partially downloaded series.");
        }

        double variance = (largest - smallest) / median;
        if (variance <= spacingVarianceTolerance)
        {
            return;
        }

        // Two failure shapes dominate in public collections and they call for different
        // action, so they get different messages: a repeated slice, and a missing one.
        string detail = smallest < median * 0.5
            ? Invariant($"a near-duplicate slice ({smallest:0.###} mm gap against a median of {median:0.###} mm)")
            : largest > median * 1.8
                ? Invariant($"a gap where slices are missing ({largest:0.###} mm against a median of {median:0.###} mm)")
                : Invariant($"gaps ranging {smallest:0.###} to {largest:0.###} mm against a median of {median:0.###} mm");

        throw Reject(
            SeriesRejectionReason.NonUniformSpacing,
            Invariant($"Slice spacing varies by {variance:P1}, over the {spacingVarianceTolerance:P0} tolerance: {detail}. Resampling to a uniform grid would invent data, so the series is rejected rather than guessed at."));
    }

    private void RequireNoGantryTilt(Vector3D step, Vector3D normal)
    {
        // FR-107b. The real question is whether the slices stack along their own normal.
        // When the gantry is tilted they do not: the plane is rotated but the table still
        // advances along its own axis, so the stack leans and the volume is a sheared box
        // rather than a rectangular one.
        double alignment = Math.Abs(step.Normalized().Dot(normal));

        if (alignment < 1 - tiltTolerance)
        {
            double degrees = Math.Acos(Math.Clamp(alignment, -1, 1)) * 180.0 / Math.PI;
            throw Reject(
                SeriesRejectionReason.GantryTilt,
                Invariant($"The series was acquired with about {degrees:0.#} degrees of gantry tilt: the slices stack {degrees:0.#} degrees away from their own normal. Correcting it means resampling, which is out of scope, so the series is rejected rather than rendered as a sheared volume."));
        }
    }
}
