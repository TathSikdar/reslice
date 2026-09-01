using FluentAssertions;
using InterviewTrea.Core.Geometry;
using Xunit;

namespace InterviewTrea.Core.Tests.Geometry;

public class Point3DTests
{
    [Fact]
    public void Subtracting_TwoPositions_GivesTheDisplacementBetweenThem()
    {
        Point3D sliceOne = new(10, 20, 30);
        Point3D sliceTwo = new(10, 20, 33);

        // This is exactly the operation FR-103 uses on successive
        // ImagePositionPatient values to find the slice step.
        (sliceTwo - sliceOne).Should().Be(new Vector3D(0, 0, 3));
    }

    [Fact]
    public void Translating_ByAVector_MovesTheLocation()
    {
        Point3D origin = new(-250, -250, 100);

        (origin + new Vector3D(0.7, 0, 0)).Should().Be(new Point3D(-249.3, -250, 100));
    }

    [Fact]
    public void TranslatingThenTranslatingBack_ReturnsTheOriginalLocation()
    {
        Point3D start = new(1.5, -2.25, 3.125);
        Vector3D step = new(0.7, 0.7, 3.0);

        ((start + step) - step).ShouldBeApproximately(start);
    }

    [Fact]
    public void DistanceTo_OfPythagoreanQuadruple_IsExact()
    {
        Point3D.Origin.DistanceTo(new Point3D(2, 3, 6)).Should().Be(7.0);
    }

    [Fact]
    public void DistanceTo_IsSymmetric()
    {
        Point3D a = new(12.5, -3, 88);
        Point3D b = new(-4, 17.25, 2);

        a.DistanceTo(b).Should().Be(b.DistanceTo(a));
    }
}
