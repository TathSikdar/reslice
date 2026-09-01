using System;

namespace InterviewTrea.Core.Geometry;

/// <summary>
/// A displacement in patient space (LPS), in millimetres: +X toward the patient's
/// Left, +Y toward Posterior, +Z toward Superior.
/// </summary>
/// <remarks>
/// A <see cref="Vector3D"/> is a direction and a distance, never a location - that is
/// <see cref="Point3D"/>. Keeping them as separate types is what stops two patient
/// positions being added together, which is meaningless but compiles happily if both
/// are the same type.
/// </remarks>
public readonly record struct Vector3D(double X, double Y, double Z)
{
    // Below this, a vector carries no reliable direction and normalising it would
    // amplify floating-point noise into a confident-looking wrong answer. Real DICOM
    // direction cosines are unit length, so anything near zero here is malformed input.
    private const double MinimumNormalisableLength = 1e-9;

    public static Vector3D Zero => default;

    /// <summary>Unit vector toward the patient's left.</summary>
    public static Vector3D UnitX => new(1, 0, 0);

    /// <summary>Unit vector toward the patient's posterior.</summary>
    public static Vector3D UnitY => new(0, 1, 0);

    /// <summary>Unit vector toward the patient's superior (head).</summary>
    public static Vector3D UnitZ => new(0, 0, 1);

    public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

    public double Length => Math.Sqrt(LengthSquared);

    public double Dot(Vector3D other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

    /// <summary>
    /// The vector perpendicular to both, with a magnitude equal to the area of the
    /// parallelogram they span.
    /// </summary>
    /// <remarks>
    /// Order matters and is not a detail: the cross product is anti-commutative, so
    /// <c>b.Cross(a) == -a.Cross(b)</c>. The slice normal is always
    /// <c>rowCosine.Cross(columnCosine)</c> in that order (DICOM PS3.3 C.7.6.2.1.1).
    /// Reversed, the normal points the other way, slices sort in reverse, and the
    /// volume loads head-to-foot with nothing visibly wrong with the image.
    /// </remarks>
    public Vector3D Cross(Vector3D other) => new(
        (Y * other.Z) - (Z * other.Y),
        (Z * other.X) - (X * other.Z),
        (X * other.Y) - (Y * other.X));

    /// <summary>Returns this vector scaled to unit length.</summary>
    /// <exception cref="InvalidOperationException">
    /// The vector is too short to have a meaningful direction. Thrown rather than
    /// returning zero so that a degenerate ImageOrientationPatient fails at the point
    /// of the mistake, instead of propagating NaN into the reslice geometry.
    /// </exception>
    public Vector3D Normalized()
    {
        double length = Length;
        if (!(length >= MinimumNormalisableLength) || double.IsInfinity(length))
        {
            throw new InvalidOperationException(
                $"Cannot normalise a vector of length {length}; direction is undefined.");
        }

        return new Vector3D(X / length, Y / length, Z / length);
    }

    /// <summary>
    /// Rotates this vector about <paramref name="axis"/> by <paramref name="radians"/>,
    /// right-handed: positive angles turn counter-clockwise seen from the axis tip.
    /// </summary>
    /// <remarks>
    /// Rodrigues' rotation formula. The vector splits into a part along the axis, which a
    /// rotation about that axis leaves untouched, and a part perpendicular to it, which
    /// turns within the plane spanned by that part and its cross product with the axis.
    /// The three terms below are exactly that decomposition.
    ///
    /// Written out rather than built as a rotation matrix because the reslice frame stores
    /// its axes and rotates them in place, so successive drags compose in the stored state
    /// itself; there is never a need to multiply two rotations together symbolically.
    ///
    /// The axis is normalized here rather than demanded of the caller. Callers pass a
    /// plane normal, which is a cross product of two unit vectors and therefore unit only
    /// when those two are exactly perpendicular - true analytically, and drifting in the
    /// last bits after a few hundred rotations.
    /// </remarks>
    public Vector3D RotatedAbout(Vector3D axis, double radians)
    {
        Vector3D k = axis.Normalized();
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        return Scale(cos) + k.Cross(this).Scale(sin) + k.Scale(k.Dot(this) * (1 - cos));
    }

    public Vector3D Scale(double factor) => new(X * factor, Y * factor, Z * factor);

    public Vector3D Add(Vector3D other) => new(X + other.X, Y + other.Y, Z + other.Z);

    public Vector3D Subtract(Vector3D other) => new(X - other.X, Y - other.Y, Z - other.Z);

    public Vector3D Negate() => new(-X, -Y, -Z);

    public static Vector3D operator +(Vector3D a, Vector3D b) => a.Add(b);

    public static Vector3D operator -(Vector3D a, Vector3D b) => a.Subtract(b);

    public static Vector3D operator -(Vector3D v) => v.Negate();

    public static Vector3D operator *(Vector3D v, double factor) => v.Scale(factor);

    public static Vector3D operator *(double factor, Vector3D v) => v.Scale(factor);

    public override string ToString() =>
        $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
