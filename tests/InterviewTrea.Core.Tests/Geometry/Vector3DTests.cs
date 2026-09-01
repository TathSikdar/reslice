using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using Xunit;

namespace InterviewTrea.Core.Tests.Geometry;

public class Vector3DTests
{
    // (2,3,6) is a Pythagorean quadruple: 4 + 9 + 36 = 49, so the length is exactly 7.
    // Using exact integer arithmetic keeps the expected values analytic rather than
    // "whatever the machine produced".
    private static readonly Vector3D TwoThreeSix = new(2, 3, 6);

    [Fact]
    public void Length_OfPythagoreanQuadruple_IsExact()
    {
        TwoThreeSix.Length.Should().Be(7.0);
        TwoThreeSix.LengthSquared.Should().Be(49.0);
    }

    [Fact]
    public void Normalized_DividesEachComponentByLength()
    {
        TwoThreeSix.Normalized().ShouldBeApproximately(new Vector3D(2.0 / 7, 3.0 / 7, 6.0 / 7));
    }

    [Fact]
    public void Normalized_ProducesUnitLength()
    {
        TwoThreeSix.Normalized().Length.Should().BeApproximately(1.0, VectorApproximation.Tolerance);
    }

    [Fact]
    public void Normalized_OfZeroVector_Throws()
    {
        // Degenerate ImageOrientationPatient must fail here rather than emit NaN and
        // surface three layers downstream as an unexplainable render.
        Action act = () => Vector3D.Zero.Normalized();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Dot_OfPerpendicularUnitVectors_IsZero()
    {
        Vector3D.UnitX.Dot(Vector3D.UnitY).Should().Be(0.0);
    }

    [Fact]
    public void Dot_OfUnitVectorWithItself_IsOne()
    {
        Vector3D.UnitZ.Dot(Vector3D.UnitZ).Should().Be(1.0);
    }

    /// <summary>
    /// FR-103 foundation. A standard axial CT has ImageOrientationPatient
    /// [1,0,0, 0,1,0]: the row cosine points to the patient's left, the column cosine
    /// to their posterior. The slice normal must therefore come out superior, which is
    /// the direction slice positions increase in a normal feet-to-head acquisition.
    /// </summary>
    [Fact]
    public void Cross_OfStandardAxialDirectionCosines_PointsSuperior()
    {
        Vector3D rowCosine = new(1, 0, 0);
        Vector3D columnCosine = new(0, 1, 0);

        rowCosine.Cross(columnCosine).Should().Be(Vector3D.UnitZ);
    }

    /// <summary>
    /// The reason the argument order in <see cref="Vector3D.Cross"/> is load-bearing:
    /// swapping it flips the slice normal, which reverses the sort order in FR-103 and
    /// loads the patient head-to-foot. Nothing about the resulting image looks wrong.
    /// </summary>
    [Fact]
    public void Cross_IsAntiCommutative()
    {
        Vector3D rowCosine = new(1, 0, 0);
        Vector3D columnCosine = new(0, 1, 0);

        columnCosine.Cross(rowCosine).Should().Be(rowCosine.Cross(columnCosine).Negate());
        columnCosine.Cross(rowCosine).Should().Be(new Vector3D(0, 0, -1));
    }

    [Fact]
    public void Cross_OfParallelVectors_IsZero()
    {
        TwoThreeSix.Cross(TwoThreeSix.Scale(3)).ShouldBeApproximately(Vector3D.Zero);
    }

    [Fact]
    public void Cross_IsPerpendicularToBothInputs()
    {
        Vector3D a = new(1, 2, 3);
        Vector3D b = new(-4, 5, 6);

        Vector3D n = a.Cross(b);

        n.Dot(a).Should().BeApproximately(0.0, VectorApproximation.Tolerance);
        n.Dot(b).Should().BeApproximately(0.0, VectorApproximation.Tolerance);
    }

    [Fact]
    public void Operators_MatchTheirNamedEquivalents()
    {
        Vector3D a = new(1, 2, 3);
        Vector3D b = new(10, 20, 30);

        (a + b).Should().Be(new Vector3D(11, 22, 33));
        (b - a).Should().Be(new Vector3D(9, 18, 27));
        (-a).Should().Be(new Vector3D(-1, -2, -3));
        (a * 2).Should().Be(new Vector3D(2, 4, 6));
        (2 * a).Should().Be(a * 2);
    }
}
