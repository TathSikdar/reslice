using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering3D;
using InterviewTrea.TestData;

namespace InterviewTrea.Rendering3D.Tests;

public sealed class Camera3DTests
{
    private const double Tolerance = 1e-9;

    private static Camera3D Frontal(double viewHeightMm = 100) => new()
    {
        Target = Point3D.Origin,
        Azimuth = -Math.PI / 2,
        Elevation = 0,
        ViewHeightMm = viewHeightMm,
    };

    [Fact]
    public void TheFrontalViewLooksFromAnteriorTowardPosterior()
    {
        Vector3D forward = Frontal().Forward;

        // +Y is posterior, so an eye in front of the patient looks along +Y exactly.
        forward.X.Should().BeApproximately(0, Tolerance);
        forward.Y.Should().BeApproximately(1, Tolerance);
        forward.Z.Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void TheFrontalViewPutsThePatientsLeftOnTheRightOfTheImage()
    {
        // Radiological convention: the image is read as if facing the patient.
        Frontal().Right.X.Should().BeApproximately(1, Tolerance);
        Frontal().Up.Z.Should().BeApproximately(1, Tolerance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.4)]
    [InlineData(-1.2)]
    [InlineData(3.0)]
    public void TheImageAxesAreOrthonormalAtEveryOrbitAngle(double elevation)
    {
        Camera3D camera = Frontal().Orbited(1.1, elevation);

        camera.Forward.Length.Should().BeApproximately(1, Tolerance);
        camera.Up.Length.Should().BeApproximately(1, Tolerance);
        camera.Right.Length.Should().BeApproximately(1, Tolerance);

        camera.Forward.Dot(camera.Up).Should().BeApproximately(0, Tolerance);
        camera.Forward.Dot(camera.Right).Should().BeApproximately(0, Tolerance);
        camera.Up.Dot(camera.Right).Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void ElevationStopsShortOfThePoleWhereUpWouldBeUndefined()
    {
        Camera3D camera = Frontal() with { Elevation = Math.PI };

        camera.Elevation.Should().BeLessThan(Math.PI / 2);
        camera.Up.Length.Should().BeApproximately(1, Tolerance);
    }

    [Fact]
    public void AdjacentPixelsAreExactlyOnePixelPitchApart()
    {
        // 100 mm over 200 rows is 0.5 mm per pixel, in both directions: the projection is
        // square, so the pitch comes from the height and the width only sets the extent.
        Camera3D camera = Frontal(viewHeightMm: 100);

        camera.PixelPitch(200).Should().BeApproximately(0.5, Tolerance);

        Point3D a = camera.RayOrigin(10, 10, 300, 200);
        Point3D b = camera.RayOrigin(11, 10, 300, 200);
        Point3D c = camera.RayOrigin(10, 11, 300, 200);

        a.DistanceTo(b).Should().BeApproximately(0.5, Tolerance);
        a.DistanceTo(c).Should().BeApproximately(0.5, Tolerance);
    }

    [Fact]
    public void TheRayThroughTheCentreOfAnEvenSizedImagePassesBetweenTheFourMiddlePixels()
    {
        Camera3D camera = Frontal(viewHeightMm: 100);

        // With an even pixel count no ray hits the target dead on; the four around it are
        // half a pitch away on each axis, and their mean is the target exactly.
        Point3D upperLeft = camera.RayOrigin(99, 99, 200, 200);
        Point3D lowerRight = camera.RayOrigin(100, 100, 200, 200);

        Vector3D mean = (upperLeft.AsVector() + lowerRight.AsVector()).Scale(0.5);

        mean.X.Should().BeApproximately(0, Tolerance);
        mean.Z.Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void TheTopOfTheImageIsTowardTheHead()
    {
        Camera3D camera = Frontal(viewHeightMm: 100);

        // Row 0 is the top row, and 100 mm over 100 rows puts its centre 49.5 mm up.
        camera.RayOrigin(50, 0, 100, 100).Z.Should().BeApproximately(49.5, Tolerance);
        camera.RayOrigin(50, 99, 100, 100).Z.Should().BeApproximately(-49.5, Tolerance);
    }

    [Fact]
    public void PanningMovesTheTargetWithinTheImagePlaneAndNotAlongTheView()
    {
        Camera3D camera = Frontal().Orbited(0.7, 0.3);
        Camera3D panned = camera.Panned(rightMm: 12, upMm: -5);

        Vector3D moved = panned.Target - camera.Target;

        moved.Dot(camera.Right).Should().BeApproximately(12, Tolerance);
        moved.Dot(camera.Up).Should().BeApproximately(-5, Tolerance);
        moved.Dot(camera.Forward).Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void FramingAVolumeCentresOnItAndSpansItsDiagonal()
    {
        // 41 samples on a side at 1 mm is 40 mm centre to centre, so the body diagonal
        // is 40 * sqrt(3) = 69.282 mm.
        Volume volume = Phantoms.Uniform(0, dimX: 41, dimY: 41, dimZ: 41, spacing: Phantoms.IsotropicSpacing);
        Camera3D camera = Camera3D.Framing(volume);

        double expected = Math.Sqrt(
            Math.Pow((volume.DimX - 1) * volume.Spacing.X, 2) +
            Math.Pow((volume.DimY - 1) * volume.Spacing.Y, 2) +
            Math.Pow((volume.DimZ - 1) * volume.Spacing.Z, 2));

        camera.ViewHeightMm.Should().BeApproximately(expected, 1e-6);
        camera.Target.DistanceTo(Point3D.Origin).Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void AViewHeightOfZeroIsRejectedRatherThanProducingAZeroPixelPitch()
    {
        Action act = () => _ = Frontal() with { ViewHeightMm = 0 };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
