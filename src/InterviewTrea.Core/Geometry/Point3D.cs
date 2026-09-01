using System;

namespace InterviewTrea.Core.Geometry;

/// <summary>
/// A location in patient space (LPS), in millimetres. All measurements are reported
/// in this space, never in voxel indices.
/// </summary>
/// <remarks>
/// Deliberately not addable to another <see cref="Point3D"/>. The sum of two patient
/// positions has no physical meaning, so the operator does not exist; the difference
/// of two positions is a displacement, so that one returns <see cref="Vector3D"/>.
/// </remarks>
public readonly record struct Point3D(double X, double Y, double Z)
{
    public static Point3D Origin => default;

    /// <summary>The displacement that carries <paramref name="from"/> to this point.</summary>
    public Vector3D DisplacementFrom(Point3D from) => new(X - from.X, Y - from.Y, Z - from.Z);

    public Point3D Translate(Vector3D by) => new(X + by.X, Y + by.Y, Z + by.Z);

    /// <summary>Straight-line distance in millimetres (FR-401/FR-402).</summary>
    public double DistanceTo(Point3D other) => DisplacementFrom(other).Length;

    /// <summary>This location read as a displacement from the patient-space origin.</summary>
    public Vector3D AsVector() => new(X, Y, Z);

    public static Vector3D operator -(Point3D a, Point3D b) => a.DisplacementFrom(b);

    public static Point3D operator +(Point3D p, Vector3D v) => p.Translate(v);

    public static Point3D operator -(Point3D p, Vector3D v) => p.Translate(v.Negate());

    public override string ToString() =>
        $"({X:0.###}, {Y:0.###}, {Z:0.###}) mm";
}
