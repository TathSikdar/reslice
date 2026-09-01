using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using Xunit;

namespace InterviewTrea.Core.Tests.Geometry;

/// <summary>
/// Rodrigues rotation (FR-307 groundwork). Every expected value here is derived from the
/// definition of a rotation rather than from a previous run.
/// </summary>
public class Vector3DRotationTests
{
    private const double QuarterTurn = Math.PI / 2;

    /// <summary>
    /// Right-handedness, pinned. A quarter turn of +X about +Z lands on +Y, which is the
    /// same handedness as the cross product the rest of the geometry already uses. Get
    /// this backwards and every oblique plane rotates the wrong way while still looking
    /// like a plausible image.
    /// </summary>
    [Fact]
    public void AQuarterTurnOfXAboutZLandsOnY() =>
        Vector3D.UnitX.RotatedAbout(Vector3D.UnitZ, QuarterTurn)
            .ShouldBeApproximately(Vector3D.UnitY);

    [Fact]
    public void AQuarterTurnOfYAboutXLandsOnZ() =>
        Vector3D.UnitY.RotatedAbout(Vector3D.UnitX, QuarterTurn)
            .ShouldBeApproximately(Vector3D.UnitZ);

    [Fact]
    public void AQuarterTurnOfZAboutYLandsOnX() =>
        Vector3D.UnitZ.RotatedAbout(Vector3D.UnitY, QuarterTurn)
            .ShouldBeApproximately(Vector3D.UnitX);

    /// <summary>
    /// 30 degrees is the angle the oblique reslice tests use, and cos 30 = sqrt(3)/2 is
    /// irrational, so this expected value cannot be satisfied by arithmetic that happens
    /// to be exact at the quarter turns.
    /// </summary>
    [Fact]
    public void AThirtyDegreeTurnMatchesTheCosineAndSine()
    {
        Vector3D rotated = Vector3D.UnitX.RotatedAbout(Vector3D.UnitZ, Math.PI / 6);

        rotated.ShouldBeApproximately(new Vector3D(Math.Sqrt(3) / 2, 0.5, 0));
    }

    /// <summary>The component along the axis is what a rotation about that axis preserves.</summary>
    [Fact]
    public void TheAxisItselfIsUnmoved() =>
        Vector3D.UnitZ.RotatedAbout(Vector3D.UnitZ, 1.234)
            .ShouldBeApproximately(Vector3D.UnitZ);

    [Fact]
    public void TheComponentAlongTheAxisSurvivesWhileThePerpendicularPartTurns()
    {
        Vector3D v = new(1, 0, 5);

        Vector3D rotated = v.RotatedAbout(Vector3D.UnitZ, QuarterTurn);

        rotated.ShouldBeApproximately(new Vector3D(0, 1, 5));
    }

    [Fact]
    public void LengthIsPreserved()
    {
        Vector3D v = new(2, 3, 6);

        v.RotatedAbout(new Vector3D(1, 1, 1), 0.7).Length.Should().BeApproximately(7.0, 1e-12);
    }

    [Fact]
    public void RotatingBackByTheSameAngleReturnsTheOriginal()
    {
        Vector3D v = new(2, 3, 6);
        Vector3D axis = new(1, -2, 4);

        v.RotatedAbout(axis, 0.9).RotatedAbout(axis, -0.9).ShouldBeApproximately(v);
    }

    /// <summary>
    /// Composition is what makes successive crosshair drags work: the frame is rotated in
    /// place, so two drags of 45 degrees have to equal one of 90.
    /// </summary>
    [Fact]
    public void TwoEighthTurnsEqualOneQuarterTurn()
    {
        Vector3D once = Vector3D.UnitX.RotatedAbout(Vector3D.UnitZ, QuarterTurn);
        Vector3D twice = Vector3D.UnitX
            .RotatedAbout(Vector3D.UnitZ, QuarterTurn / 2)
            .RotatedAbout(Vector3D.UnitZ, QuarterTurn / 2);

        twice.ShouldBeApproximately(once);
    }

    /// <summary>
    /// The axis arrives as a plane normal, which is a cross product and therefore unit
    /// only when its two inputs are exactly perpendicular. Scaling it must not scale the
    /// result, or a plane would deform as it rotated instead of turning.
    /// </summary>
    [Fact]
    public void ANonUnitAxisIsNormalizedRatherThanScalingTheResult() =>
        Vector3D.UnitX.RotatedAbout(new Vector3D(0, 0, 5), QuarterTurn)
            .ShouldBeApproximately(Vector3D.UnitY);

    /// <summary>
    /// Two perpendicular vectors rotated about a common axis stay perpendicular. This is
    /// the property the whole oblique frame rests on: the panes are kept orthogonal by the
    /// rotation itself, not by re-orthogonalising them afterwards.
    /// </summary>
    [Fact]
    public void PerpendicularityIsPreservedAcrossARotation()
    {
        Vector3D axis = new(1, 2, -3);

        Vector3D a = Vector3D.UnitX.RotatedAbout(axis, 1.1);
        Vector3D b = Vector3D.UnitY.RotatedAbout(axis, 1.1);

        a.Dot(b).Should().BeApproximately(0, 1e-12);
    }

    /// <summary>A full turn is the identity, which also bounds the drift over many drags.</summary>
    [Fact]
    public void AFullTurnIsTheIdentity()
    {
        Vector3D v = new(2, 3, 6);

        v.RotatedAbout(new Vector3D(1, 1, 1), 2 * Math.PI).ShouldBeApproximately(v, 1e-14);
    }
}
