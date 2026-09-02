using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering3D;
using InterviewTrea.TestData;

namespace InterviewTrea.Rendering3D.Tests;

public sealed class RayBoxTests
{
    private const double Tolerance = 1e-9;

    // 41 x 41 x 41 samples at 1 mm, centred on the origin: the sampling domain runs from
    // -20 mm to +20 mm on every axis. Every expected value below is read off that. The
    // contents are irrelevant here - this test is about extent, not about voxels.
    private static Volume Box() =>
        Phantoms.Uniform(0, dimX: 41, dimY: 41, dimZ: 41, spacing: Phantoms.IsotropicSpacing);

    [Fact]
    public void ARayDownAnAxisEntersAndExitsAtTheMillimetreTheGeometrySays()
    {
        bool hit = RayBox.TryIntersect(
            Box(), new Point3D(0, -100, 0), Vector3D.UnitY, out double enter, out double exit);

        hit.Should().BeTrue();
        enter.Should().BeApproximately(80, Tolerance);
        exit.Should().BeApproximately(120, Tolerance);
    }

    [Fact]
    public void ARayThatStartsInsideEntersAtANegativeParameter()
    {
        // The orthographic camera does exactly this, and clamping the entry to zero here
        // would render only the half of the volume behind the image plane.
        bool hit = RayBox.TryIntersect(
            Box(), Point3D.Origin, Vector3D.UnitY, out double enter, out double exit);

        hit.Should().BeTrue();
        enter.Should().BeApproximately(-20, Tolerance);
        exit.Should().BeApproximately(20, Tolerance);
    }

    [Fact]
    public void ARayAlongsideTheVolumeMissesIt()
    {
        bool hit = RayBox.TryIntersect(
            Box(), new Point3D(25, -100, 0), Vector3D.UnitY, out _, out _);

        hit.Should().BeFalse();
    }

    [Fact]
    public void ARayParallelToTwoFacesAndOutsideThemMissesWithoutDividingByZero()
    {
        // Direction has an exactly zero x component and the origin is off the box in x.
        bool hit = RayBox.TryIntersect(
            Box(), new Point3D(21, -100, 0), new Vector3D(0, 1, 0), out _, out _);

        hit.Should().BeFalse();
    }

    [Fact]
    public void ADiagonalRayCrossesTheBodyDiagonal()
    {
        // Corner to corner of a 40 mm cube along (1,1,1)/sqrt(3): the chord is 40*sqrt(3).
        Vector3D direction = new Vector3D(1, 1, 1).Normalized();

        bool hit = RayBox.TryIntersect(
            Box(), Point3D.Origin - direction.Scale(100), direction, out double enter, out double exit);

        hit.Should().BeTrue();
        (exit - enter).Should().BeApproximately(40 * System.Math.Sqrt(3), 1e-9);
    }

    [Fact]
    public void AnisotropicSpacingIsMeasuredInMillimetresNotVoxels()
    {
        // 41 samples on each axis at 0.7 x 0.7 x 3.0 mm: 28 mm across x and 120 mm along z
        // for the same voxel count. The parameter is patient millimetres, so it has to come
        // out as the millimetres and not as the 40 voxel steps both rays take.
        Volume volume = Phantoms.Uniform(0, dimX: 41, dimY: 41, dimZ: 41, spacing: Phantoms.ChestSpacing);

        RayBox.TryIntersect(volume, new Point3D(0, 0, -200), Vector3D.UnitZ, out double downZ, out double outZ)
            .Should().BeTrue();
        RayBox.TryIntersect(volume, new Point3D(-200, 0, 0), Vector3D.UnitX, out double downX, out double outX)
            .Should().BeTrue();

        (outZ - downZ).Should().BeApproximately(120, 1e-6);
        (outX - downX).Should().BeApproximately(28, 1e-6);
    }
}
