using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Tests.Geometry;
using InterviewTrea.Core.Volumes;
using InterviewTrea.TestData;
using Xunit;

namespace InterviewTrea.Core.Tests.Reslicing;

/// <summary>
/// The sampling grid every viewport draws. Expected values are derived from the phantom
/// dimensions by hand, because a grid that is wrong by a pixel or reversed on one axis
/// still produces an image that looks entirely plausible.
/// </summary>
public sealed class ReslicePlaneTests
{
    // 64 x 64 x 32 at 0.7 x 0.7 x 3.0 mm, centred on the patient origin. The extents
    // between outermost voxel centres are therefore 63 * 0.7 = 44.1 mm in x and y, and
    // 31 * 3.0 = 93.0 mm in z. Every number below comes from those three.
    private const double PixelSize = 0.7;
    private const double InPlaneExtentMm = 63 * 0.7;
    private const double ThroughPlaneExtentMm = 31 * 3.0;

    private static Volume Chest() => Phantoms.Uniform(
        Phantoms.SoftTissue, dimX: 64, dimY: 64, dimZ: 32, spacing: Phantoms.ChestSpacing);

    private static ReslicePlane Plane(PlaneOrientation orientation, Point3D? crosshair = null) =>
        ReslicePlane.Through(Chest(), orientation, crosshair ?? Point3D.Origin, PixelSize);

    [Fact]
    public void AxialRunsPatientLeftAcrossAndPosteriorDown()
    {
        ReslicePlane plane = Plane(PlaneOrientation.Axial);

        plane.RowStep.ShouldBeApproximately(new Vector3D(PixelSize, 0, 0));
        plane.ColumnStep.ShouldBeApproximately(new Vector3D(0, PixelSize, 0));

        // +Z is towards the head, so scrolling forward through axial slices walks up
        // the patient.
        plane.Normal.ShouldBeApproximately(Vector3D.UnitZ);
    }

    [Fact]
    public void CoronalRunsPatientLeftAcrossAndInferiorDown()
    {
        ReslicePlane plane = Plane(PlaneOrientation.Coronal);

        plane.RowStep.ShouldBeApproximately(new Vector3D(PixelSize, 0, 0));
        plane.ColumnStep.ShouldBeApproximately(new Vector3D(0, 0, -PixelSize));
        plane.Normal.ShouldBeApproximately(Vector3D.UnitY);
    }

    /// <summary>
    /// Sagittal is viewed from the patient's left, so anterior is on the viewer's left
    /// and the row axis runs towards posterior. The normal falls out as -X, pointing to
    /// the patient's right - a negative patient axis, and deliberately not flipped to
    /// make it look tidier. Flipping it would mirror the image.
    /// </summary>
    [Fact]
    public void SagittalIsViewedFromThePatientsLeft()
    {
        ReslicePlane plane = Plane(PlaneOrientation.Sagittal);

        plane.RowStep.ShouldBeApproximately(new Vector3D(0, PixelSize, 0));
        plane.ColumnStep.ShouldBeApproximately(new Vector3D(0, 0, -PixelSize));
        plane.Normal.ShouldBeApproximately(new Vector3D(-1, 0, 0));
    }

    [Theory]
    [InlineData(PlaneOrientation.Axial)]
    [InlineData(PlaneOrientation.Coronal)]
    [InlineData(PlaneOrientation.Sagittal)]
    public void ThePlaneContainsTheCrosshair(PlaneOrientation orientation)
    {
        // Off centre on all three axes, so a plane that quietly ignored the crosshair
        // and sat at the volume centre would fail for every orientation.
        Point3D crosshair = new(-11.3, 6.4, 21.0);

        Plane(orientation, crosshair).SignedDistanceTo(crosshair).Should().BeApproximately(0, 1e-9);
    }

    /// <summary>
    /// FR-208, and the reason for the isotropic grid. The volume has 32 slices 3 mm
    /// apart. Rendering coronal at native resolution would give 32 rows; the correct
    /// answer is the physical extent divided by the pixel size, so
    /// ceil(93.0 / 0.7) + 1 = 134 rows. The width, whose axis is already at 0.7 mm,
    /// comes out at the native 64 - which is the check that the +1 endpoint rule is not
    /// quietly adding a column.
    /// </summary>
    [Fact]
    public void CoronalResolvesTheSliceAxisInMillimetresRatherThanInSlices()
    {
        ReslicePlane plane = Plane(PlaneOrientation.Coronal);

        plane.Width.Should().Be(64);
        plane.Height.Should().Be(134);

        // The grid covers the whole volume and overshoots by less than one pixel.
        double covered = (plane.Height - 1) * PixelSize;
        covered.Should().BeGreaterThanOrEqualTo(ThroughPlaneExtentMm);
        covered.Should().BeLessThan(ThroughPlaneExtentMm + PixelSize);
    }

    [Fact]
    public void AxialKeepsTheNativeInPlaneResolution()
    {
        ReslicePlane plane = Plane(PlaneOrientation.Axial);

        plane.Width.Should().Be(64);
        plane.Height.Should().Be(64);
        ((plane.Width - 1) * PixelSize).Should().BeApproximately(InPlaneExtentMm, 1e-9);
    }

    /// <summary>
    /// The anchoring rule. Moving the crosshair sideways within a plane must not move
    /// the plane, or the image would slide under the cursor instead of the crosshair
    /// moving over the image.
    /// </summary>
    [Fact]
    public void MovingTheCrosshairWithinThePlaneLeavesTheGridWhereItWas()
    {
        ReslicePlane centred = Plane(PlaneOrientation.Axial, new Point3D(0, 0, 12));
        ReslicePlane shifted = Plane(PlaneOrientation.Axial, new Point3D(-18, 7, 12));

        shifted.Origin.ShouldBeApproximately(centred.Origin);
        shifted.Width.Should().Be(centred.Width);
        shifted.Height.Should().Be(centred.Height);
    }

    [Fact]
    public void MovingTheCrosshairAlongTheNormalShiftsTheGridByThatDistance()
    {
        ReslicePlane at0 = Plane(PlaneOrientation.Axial, new Point3D(0, 0, 0));
        ReslicePlane at12 = Plane(PlaneOrientation.Axial, new Point3D(0, 0, 12));

        (at12.Origin - at0.Origin).ShouldBeApproximately(new Vector3D(0, 0, 12));
        at12.Width.Should().Be(at0.Width);
        at12.Height.Should().Be(at0.Height);
    }

    [Fact]
    public void PixelAndPatientCoordinatesRoundTrip()
    {
        ReslicePlane plane = Plane(PlaneOrientation.Sagittal);

        (double column, double row) = plane.ToPixel(plane.ToPatient(37.25, 91.75));

        column.Should().BeApproximately(37.25, 1e-9);
        row.Should().BeApproximately(91.75, 1e-9);
    }

    /// <summary>
    /// A crosshair set in another viewport is almost never exactly in this plane, so the
    /// projection has to drop the normal component rather than refuse the point.
    /// </summary>
    [Fact]
    public void APointOffThePlaneProjectsOntoIt()
    {
        ReslicePlane plane = Plane(PlaneOrientation.Axial);
        Point3D inPlane = plane.ToPatient(20, 44);

        (double column, double row) = plane.ToPixel(inPlane + plane.Normal.Scale(25.0));

        column.Should().BeApproximately(20, 1e-9);
        row.Should().BeApproximately(44, 1e-9);
    }

    /// <summary>
    /// An obliquely acquired series is a tilted box in patient space, so its footprint on
    /// a standard axial plane is wider than the box itself. A square of side L rotated by
    /// theta has an axis-aligned bounding box of side L * (cos theta + sin theta); at
    /// 30 degrees that is 44.1 * 1.3660254 = 60.2417 mm, which is ceil(/0.7) + 1 = 88
    /// pixels. Sizing the grid from the volume's dimensions instead of its corners would
    /// give 64 and crop the corners off the image.
    /// </summary>
    [Fact]
    public void AnObliquelyAcquiredVolumeGetsAGridWideEnoughForItsFootprint()
    {
        const double degrees = 30.0;
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        // Rotate the acquisition about the patient's Z axis. The slices stay axial, but
        // their rows and columns no longer line up with the patient axes.
        Matrix4x4Affine rotated = Matrix4x4Affine.FromImagePlane(
            rowCosine: new Vector3D(cos, sin, 0),
            columnCosine: new Vector3D(-sin, cos, 0),
            adjacentRowSpacing: 0.7,
            adjacentColumnSpacing: 0.7,
            sliceStep: new Vector3D(0, 0, 3.0),
            origin: new Point3D(-22.05, -22.05, -46.5));

        Volume volume = new(
            new short[64 * 64 * 32], 64, 64, 32, rotated, Chest().Metadata);

        ReslicePlane plane = ReslicePlane.Through(
            volume, PlaneOrientation.Axial, Point3D.Origin, PixelSize);

        int expected = (int)Math.Ceiling(InPlaneExtentMm * (cos + sin) / PixelSize) + 1;
        expected.Should().Be(88);

        plane.Width.Should().Be(88);
        plane.Height.Should().Be(88);
    }

    [Fact]
    public void ANonPositivePixelSizeIsRejected()
    {
        Action build = () => ReslicePlane.Through(Chest(), PlaneOrientation.Axial, Point3D.Origin, 0);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }
}
