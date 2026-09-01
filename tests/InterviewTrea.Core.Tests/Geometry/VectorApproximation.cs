using FluentAssertions;
using InterviewTrea.Core.Geometry;

namespace InterviewTrea.Core.Tests.Geometry;

internal static class VectorApproximation
{
    /// <summary>
    /// Default tolerance for geometry assertions. Tight on purpose: these operations
    /// are a handful of multiplies and adds, so anything looser would hide a real
    /// error in the arithmetic rather than absorb accumulated rounding.
    /// </summary>
    public const double Tolerance = 1e-12;

    public static void ShouldBeApproximately(this Vector3D actual, Vector3D expected, double tolerance = Tolerance)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance, "X component");
        actual.Y.Should().BeApproximately(expected.Y, tolerance, "Y component");
        actual.Z.Should().BeApproximately(expected.Z, tolerance, "Z component");
    }

    public static void ShouldBeApproximately(this Point3D actual, Point3D expected, double tolerance = Tolerance)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance, "X component");
        actual.Y.Should().BeApproximately(expected.Y, tolerance, "Y component");
        actual.Z.Should().BeApproximately(expected.Z, tolerance, "Z component");
    }
}
