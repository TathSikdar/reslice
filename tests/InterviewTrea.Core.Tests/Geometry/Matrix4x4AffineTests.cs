using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using Xunit;

namespace InterviewTrea.Core.Tests.Geometry;

public class Matrix4x4AffineTests
{
    // A plausible axial chest CT: standard orientation, anisotropic 0.7 x 0.7 x 3.0 mm
    // voxels, first slice sitting at the usual negative in-plane corner.
    private static readonly Point3D SeriesOrigin = new(-175.0, -175.0, -400.0);

    private static Matrix4x4Affine StandardAxial() => Matrix4x4Affine.FromImagePlane(
        rowCosine: new Vector3D(1, 0, 0),
        columnCosine: new Vector3D(0, 1, 0),
        adjacentRowSpacing: 0.7,
        adjacentColumnSpacing: 0.7,
        sliceStep: new Vector3D(0, 0, 3.0),
        origin: SeriesOrigin);

    [Fact]
    public void Transform_OfVoxelZero_IsTheImagePositionOfTheFirstSlice()
    {
        StandardAxial().Transform(0, 0, 0).Should().Be(SeriesOrigin);
    }

    [Fact]
    public void Transform_AppliesEachSpacingToItsOwnAxis()
    {
        Point3D patient = StandardAxial().Transform(10, 20, 30);

        patient.ShouldBeApproximately(
            new Point3D(-175.0 + 7.0, -175.0 + 14.0, -400.0 + 90.0),
            1e-9);
    }

    /// <summary>
    /// The PixelSpacing transpose trap, pinned. DICOM defines PixelSpacing as
    /// "adjacent row spacing \ adjacent column spacing", so [0] scales the COLUMN
    /// cosine and [1] scales the ROW cosine - the indices cross over. Square pixels
    /// hide the mistake completely, so this test uses deliberately non-square ones:
    /// swap the two and AxisI/AxisJ come out as 1.0 and 0.5 instead.
    /// </summary>
    [Fact]
    public void FromImagePlane_PairsEachPixelSpacingWithTheOppositeDirectionCosine()
    {
        Matrix4x4Affine transform = Matrix4x4Affine.FromImagePlane(
            rowCosine: new Vector3D(1, 0, 0),
            columnCosine: new Vector3D(0, 1, 0),
            adjacentRowSpacing: 1.0,
            adjacentColumnSpacing: 0.5,
            sliceStep: new Vector3D(0, 0, 2.0),
            origin: Point3D.Origin);

        // +1 column index moves by the adjacent-COLUMN spacing, along the ROW cosine.
        transform.AxisI.Should().Be(new Vector3D(0.5, 0, 0));

        // +1 row index moves by the adjacent-ROW spacing, along the COLUMN cosine.
        transform.AxisJ.Should().Be(new Vector3D(0, 1.0, 0));
    }

    /// <summary>
    /// FR-107b in embryo. The slice axis is whatever was measured between successive
    /// ImagePositionPatient values, never (spacing * sliceNormal). A tilted acquisition
    /// therefore produces a transform whose K axis is visibly off the plane normal,
    /// which the validator can then detect - rather than a clean, wrong, sheared volume.
    /// </summary>
    [Fact]
    public void FromImagePlane_UsesTheMeasuredSliceStepRatherThanTheAssumedNormal()
    {
        Vector3D tiltedStep = new(0, 0.5, 2.9);

        Matrix4x4Affine transform = Matrix4x4Affine.FromImagePlane(
            rowCosine: new Vector3D(1, 0, 0),
            columnCosine: new Vector3D(0, 1, 0),
            adjacentRowSpacing: 0.7,
            adjacentColumnSpacing: 0.7,
            sliceStep: tiltedStep,
            origin: Point3D.Origin);

        transform.AxisK.Should().Be(tiltedStep);
        transform.AxisK.Should().NotBe(new Vector3D(0, 0, tiltedStep.Length));
    }

    [Fact]
    public void Determinant_IsTheVolumeOfOneVoxel()
    {
        // 0.7 * 0.7 * 3.0 mm^3, positive because row/column/normal is right-handed.
        StandardAxial().Determinant.Should().BeApproximately(1.47, 1e-12);
    }

    [Fact]
    public void Inverse_RoundTripsAnAxisAlignedTransform()
    {
        Matrix4x4Affine forward = StandardAxial();

        Point3D patient = forward.Transform(13, 27, 41);
        Point3D voxel = forward.Inverse().Transform(patient);

        voxel.ShouldBeApproximately(new Point3D(13, 27, 41), 1e-9);
    }

    /// <summary>
    /// The round trip that actually has teeth. An axis-aligned transform inverts
    /// correctly even under an implementation that only handles the diagonal, so the
    /// image plane here is rotated 30 degrees about the patient's superior-inferior
    /// axis - an oblique acquisition, which FR-206 will need to resample.
    /// </summary>
    [Fact]
    public void Inverse_RoundTripsAnObliqueTransform()
    {
        double angle = Math.PI / 6;
        Vector3D rowCosine = new(Math.Cos(angle), Math.Sin(angle), 0);
        Vector3D columnCosine = new(-Math.Sin(angle), Math.Cos(angle), 0);

        Matrix4x4Affine forward = Matrix4x4Affine.FromImagePlane(
            rowCosine,
            columnCosine,
            adjacentRowSpacing: 0.68,
            adjacentColumnSpacing: 0.68,
            sliceStep: rowCosine.Cross(columnCosine).Scale(1.25),
            origin: SeriesOrigin);

        Point3D patient = forward.Transform(13, 27, 41);
        Point3D voxel = forward.Inverse().Transform(patient);

        voxel.ShouldBeApproximately(new Point3D(13, 27, 41), 1e-9);
    }

    /// <summary>
    /// The round trip that actually discriminates. A rotated image plane is still an
    /// ORTHOGONAL basis, so an inverse implemented as transpose-and-scale - valid only
    /// when the axes are mutually perpendicular - passes the oblique test above. A
    /// gantry-tilted acquisition is the case that breaks it: the slice step is not
    /// perpendicular to the image plane, so the basis is sheared and only a general
    /// inverse recovers the original voxel. That is also the geometry FR-107b rejects,
    /// which is why this transform has to invert correctly rather than merely throw.
    /// </summary>
    [Fact]
    public void Inverse_RoundTripsAShearedTransform()
    {
        Matrix4x4Affine forward = Matrix4x4Affine.FromImagePlane(
            rowCosine: new Vector3D(1, 0, 0),
            columnCosine: new Vector3D(0, 1, 0),
            adjacentRowSpacing: 0.7,
            adjacentColumnSpacing: 0.7,
            // Tilted: leans into +Y as it advances in +Z, so AxisJ . AxisK != 0.
            sliceStep: new Vector3D(0, 0.5, 2.9),
            origin: SeriesOrigin);

        forward.AxisJ.Dot(forward.AxisK).Should().NotBe(0.0, "the basis must be sheared for this test to mean anything");

        Point3D patient = forward.Transform(13, 27, 41);
        Point3D voxel = forward.Inverse().Transform(patient);

        voxel.ShouldBeApproximately(new Point3D(13, 27, 41), 1e-9);
    }

    [Fact]
    public void Inverse_OfACollapsedAxis_Throws()
    {
        // Zero slice spacing: every slice lands on the same plane, so no patient point
        // maps back to a unique k. Better to say so than to hand back infinities.
        Matrix4x4Affine singular = Matrix4x4Affine.FromImagePlane(
            rowCosine: new Vector3D(1, 0, 0),
            columnCosine: new Vector3D(0, 1, 0),
            adjacentRowSpacing: 0.7,
            adjacentColumnSpacing: 0.7,
            sliceStep: Vector3D.Zero,
            origin: Point3D.Origin);

        Action act = () => singular.Inverse();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Inverse_AppliedTwice_ReturnsTheOriginalTransform()
    {
        Matrix4x4Affine forward = StandardAxial();

        Matrix4x4Affine roundTripped = forward.Inverse().Inverse();

        roundTripped.AxisI.ShouldBeApproximately(forward.AxisI, 1e-9);
        roundTripped.AxisJ.ShouldBeApproximately(forward.AxisJ, 1e-9);
        roundTripped.AxisK.ShouldBeApproximately(forward.AxisK, 1e-9);
        roundTripped.Origin.ShouldBeApproximately(forward.Origin, 1e-9);
    }
}
