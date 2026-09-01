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

    // ---- FR-307: the same construction on axes handed in rather than looked up ----

    /// <summary>
    /// The enum overload must be exactly the axes overload with the table applied, or the
    /// oblique path and the standard path would drift apart and only one of them would be
    /// under test.
    /// </summary>
    [Theory]
    [InlineData(PlaneOrientation.Axial)]
    [InlineData(PlaneOrientation.Coronal)]
    [InlineData(PlaneOrientation.Sagittal)]
    public void TheAxesOverloadReproducesTheStandardPlane(PlaneOrientation orientation)
    {
        Volume volume = Chest();
        Point3D crosshair = new(3, -4, 5);

        ReslicePlane byEnum = ReslicePlane.Through(volume, orientation, crosshair, PixelSize);
        ReslicePlane byAxes = ReslicePlane.Through(
            volume, ReslicePlane.DisplayAxes(orientation), crosshair, PixelSize);

        byAxes.Should().Be(byEnum);
    }

    /// <summary>
    /// Rotating a plane's axes about its own normal turns the image within the plane and
    /// leaves the plane itself alone. On the sagittal plane the two extents differ - 44.1
    /// mm across y against 93.0 mm down z - so a quarter turn has to swap width and
    /// height. On a square plane this test would pass while doing nothing.
    /// </summary>
    [Fact]
    public void AQuarterTurnWithinThePlaneSwapsWidthAndHeight()
    {
        Volume volume = Chest();
        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(PlaneOrientation.Sagittal);
        Vector3D normal = row.Cross(column);

        ReslicePlane upright = ReslicePlane.Through(volume, (row, column), Point3D.Origin, PixelSize);
        ReslicePlane turned = ReslicePlane.Through(
            volume,
            (row.RotatedAbout(normal, Math.PI / 2), column.RotatedAbout(normal, Math.PI / 2)),
            Point3D.Origin,
            PixelSize);

        upright.Width.Should().Be(64);
        upright.Height.Should().Be(134);

        turned.Width.Should().Be(134);
        turned.Height.Should().Be(64);

        // The plane did not move: only the choice of axes within it did.
        turned.Normal.ShouldBeApproximately(upright.Normal);
    }

    /// <summary>
    /// Rotating the axes about an in-plane axis tilts the plane, and the normal must
    /// follow by the same rotation. Verified against the normal rotated directly, so the
    /// two routes to an oblique normal have to agree.
    /// </summary>
    [Fact]
    public void TiltingTheAxesTiltsTheNormalByTheSameRotation()
    {
        Volume volume = Chest();
        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(PlaneOrientation.Axial);
        double angle = Math.PI / 6;

        ReslicePlane tilted = ReslicePlane.Through(
            volume,
            (row.RotatedAbout(row, angle), column.RotatedAbout(row, angle)),
            Point3D.Origin,
            PixelSize);

        tilted.Normal.ShouldBeApproximately(Vector3D.UnitZ.RotatedAbout(Vector3D.UnitX, angle));

        // 30 degrees off axial: (0, -sin 30, cos 30).
        tilted.Normal.ShouldBeApproximately(new Vector3D(0, -0.5, Math.Sqrt(3) / 2));
    }

    /// <summary>
    /// A tilted plane cuts a longer chord through the volume, so its grid must grow. The
    /// row axis here is the rotation axis and is unmoved, which isolates the column: its
    /// extent is the box's support along (0, cos 30, sin 30), or
    /// 44.1 cos 30 + 93.0 sin 30 = 84.6917 mm, which is 122 pixels at 0.7 mm.
    /// </summary>
    [Fact]
    public void ATiltedPlaneCoversTheLongerChordThroughTheVolume()
    {
        Volume volume = Chest();
        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(PlaneOrientation.Axial);
        double angle = Math.PI / 6;

        ReslicePlane tilted = ReslicePlane.Through(
            volume,
            (row, column.RotatedAbout(row, angle)),
            Point3D.Origin,
            PixelSize);

        double columnExtent =
            (InPlaneExtentMm * Math.Cos(angle)) + (ThroughPlaneExtentMm * Math.Sin(angle));
        columnExtent.Should().BeApproximately(84.6917, 1e-4);

        tilted.Width.Should().Be(64);
        tilted.Height.Should().Be((int)Math.Ceiling(columnExtent / PixelSize) + 1);
        tilted.Height.Should().Be(122);
    }

    // ---- FR-307: the crosshair arms, which are where the other planes cut this one ----

    /// <summary>
    /// Two planes meet along the cross product of their normals. Whatever else that
    /// direction is, it has to lie in both planes, or the line drawn for it is not on
    /// either of them.
    /// </summary>
    [Fact]
    public void TheIntersectionOfTwoPlanesLiesInBothOfThem()
    {
        Volume volume = Chest();
        ReslicePlane axial = Plane(PlaneOrientation.Axial);

        // A coronal plane turned 30 degrees about the axial normal: the state a drag in
        // the axial pane leaves behind.
        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(PlaneOrientation.Coronal);
        ReslicePlane coronal = ReslicePlane.Through(
            volume,
            (row.RotatedAbout(Vector3D.UnitZ, Math.PI / 6), column.RotatedAbout(Vector3D.UnitZ, Math.PI / 6)),
            Point3D.Origin,
            PixelSize);

        Vector3D along = axial.Normal.Cross(coronal.Normal);

        along.Dot(axial.Normal).Should().BeApproximately(0, 1e-12);
        along.Dot(coronal.Normal).Should().BeApproximately(0, 1e-12);
    }

    /// <summary>
    /// The arm has to follow the cursor exactly, not merely in the same direction. Turning
    /// the coronal plane by 30 degrees about the axial normal must turn its line in the
    /// axial pane by 30 degrees too - anything else and the line lags or leads the mouse,
    /// which makes the gesture unusable long before it makes the geometry wrong.
    /// </summary>
    /// <remarks>
    /// Derived rather than observed. The axial normal is +Z and the coronal normal starts
    /// at +Y, so the two planes meet along (0,0,1) x (0,1,0) = (-1,0,0). Turning the
    /// coronal normal 30 degrees about +Z sends +Y to (-sin30, cos30, 0), and
    /// (0,0,1) x (-0.5, sqrt(3)/2, 0) = (-sqrt(3)/2, -0.5, 0) - which is exactly (-1,0,0)
    /// turned by the same 30 degrees.
    /// </remarks>
    [Fact]
    public void RotatingAPlaneTurnsItsCrosshairArmByTheSameAngle()
    {
        Volume volume = Chest();
        ReslicePlane axial = Plane(PlaneOrientation.Axial);
        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(PlaneOrientation.Coronal);

        Vector3D before = axial.Normal.Cross(row.Cross(column));
        before.ShouldBeApproximately(new Vector3D(-1, 0, 0));

        ReslicePlane turned = ReslicePlane.Through(
            volume,
            (row.RotatedAbout(Vector3D.UnitZ, Math.PI / 6), column.RotatedAbout(Vector3D.UnitZ, Math.PI / 6)),
            Point3D.Origin,
            PixelSize);

        Vector3D after = axial.Normal.Cross(turned.Normal);

        after.ShouldBeApproximately(new Vector3D(-Math.Sqrt(3) / 2, -0.5, 0));
        after.ShouldBeApproximately(before.RotatedAbout(Vector3D.UnitZ, Math.PI / 6));
    }

    /// <summary>
    /// The rule the whole oblique model rests on: turning the two planes that are not the
    /// one being dragged, both by the same angle about its normal, leaves all three normals
    /// mutually perpendicular. If this failed, the frame would shear a little on every drag
    /// and the views would stop being orthogonal without ever looking broken.
    /// </summary>
    [Fact]
    public void TurningTheOtherTwoPlanesKeepsTheTriadOrthogonal()
    {
        Vector3D axis = Vector3D.UnitZ;
        (Vector3D Row, Vector3D Column)[] axes =
        [
            ReslicePlane.DisplayAxes(PlaneOrientation.Axial),
            ReslicePlane.DisplayAxes(PlaneOrientation.Coronal),
            ReslicePlane.DisplayAxes(PlaneOrientation.Sagittal),
        ];

        // Angles that do not divide evenly into a turn, so nothing lands back on an axis
        // by luck after the last one.
        foreach (double radians in new[] { 0.31, -0.77, 1.4, 0.05, -2.2 })
        {
            for (int i = 1; i < axes.Length; i++)
            {
                axes[i] = (
                    axes[i].Row.RotatedAbout(axis, radians),
                    axes[i].Column.RotatedAbout(axis, radians));
            }
        }

        Vector3D[] normals = [.. Array.ConvertAll(axes, a => a.Row.Cross(a.Column))];

        normals[0].Dot(normals[1]).Should().BeApproximately(0, 1e-12);
        normals[1].Dot(normals[2]).Should().BeApproximately(0, 1e-12);
        normals[2].Dot(normals[0]).Should().BeApproximately(0, 1e-12);

        foreach (Vector3D normal in normals)
        {
            normal.Length.Should().BeApproximately(1.0, 1e-12);
        }
    }
}
