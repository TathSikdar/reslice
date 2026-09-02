using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering3D;
using InterviewTrea.TestData;
using Xunit;

namespace InterviewTrea.Rendering3D.Tests;

public sealed class VolumeClipTests
{
    private const int Size = 128;

    // The same 40 mm cube of bone in a 64 mm box of 1 mm voxels the ray caster's own tests
    // use, so every expected number below comes from those two figures.
    private static Volume Cube() => Phantoms.Cube(
        edgeMm: 40, insideHounsfield: 1000, outsideHounsfield: -1000,
        dimX: 65, dimY: 65, dimZ: 65, spacing: Phantoms.IsotropicSpacing);

    private static TransferFunction Solid() => new(
    [
        new TransferFunctionPoint(TransferFunction.MinimumHounsfield, Rgb.Black, 0),
        new TransferFunctionPoint(499, new Rgb(255, 255, 255), 0),
        new TransferFunctionPoint(500, new Rgb(255, 255, 255), 1.0),
        new TransferFunctionPoint(TransferFunction.MaximumHounsfield, new Rgb(255, 255, 255), 1.0),
    ]);

    /// <summary>
    /// The left lateral view: azimuth zero puts the eye on +x, so the image's horizontal
    /// axis is patient y and the clip trims from the right-hand edge of the picture. That
    /// is the whole reason this camera and not the frontal one the other tests use - a
    /// posterior clip is invisible head-on.
    /// </summary>
    private static Camera3D FromTheSide() => new()
    {
        Target = Point3D.Origin,
        Azimuth = 0,
        Elevation = 0,
        ViewHeightMm = Size,
    };

    private static byte[] Render(double clipMm)
    {
        byte[] pixels = new byte[Size * Size * VolumeRaycaster.BytesPerPixel];

        VolumeRaycaster.Render(
            Cube(), FromTheSide(), Solid(),
            new RaycastSettings { StepMm = 0.5, ClipPosteriorMm = clipMm },
            Size, Size, pixels);

        return pixels;
    }

    private static int LitColumnsInTheMiddleRow(byte[] pixels)
    {
        int lit = 0;
        for (int column = 0; column < Size; column++)
        {
            if (pixels[(((Size / 2 * Size) + column) * VolumeRaycaster.BytesPerPixel) + 2] > 0)
            {
                lit++;
            }
        }

        return lit;
    }

    [Fact]
    public void ThePosteriorExtentIsTheBackFaceOfTheAcquiredBox()
    {
        // 65 voxels at 1 mm, centred on the origin: -32 mm to +32 mm on every axis.
        VolumeClip.PosteriorExtent(Cube()).Should().BeApproximately(32, 1e-9);
        VolumeClip.AnteroposteriorSpan(Cube()).Should().BeApproximately(64, 1e-9);
    }

    [Fact]
    public void ANarrowingBehindEverythingLeavesTheIntervalAlone()
    {
        // A ray travelling posteriorly from y = -50, clipped at y = +32: it leaves the
        // half-space at t = 82, well beyond the interval it already had.
        double enter = 0;
        double exit = 40;

        VolumeClip.TryNarrow(new Point3D(0, -50, 0), new Vector3D(0, 1, 0), 32, ref enter, ref exit)
            .Should().BeTrue();

        enter.Should().Be(0);
        exit.Should().Be(40);
    }

    [Fact]
    public void ARayTravellingPosteriorlyLeavesEarly()
    {
        // From y = -50 along +y, clipped at y = -20: the crossing is at t = 30, which is
        // inside the interval, so the exit moves back to it and the entry does not move.
        double enter = 0;
        double exit = 40;

        VolumeClip.TryNarrow(new Point3D(0, -50, 0), new Vector3D(0, 1, 0), -20, ref enter, ref exit)
            .Should().BeTrue();

        enter.Should().Be(0);
        exit.Should().BeApproximately(30, 1e-9);
    }

    [Fact]
    public void ARayTravellingAnteriorlyEntersLate()
    {
        // The converse, and the one that is easy to get backwards. From y = +50 along -y,
        // clipped at y = -20: the ray is behind the plane until t = 70, so it is the entry
        // that moves. Clipping the exit here would have kept the table and thrown away the
        // patient.
        double enter = 0;
        double exit = 100;

        VolumeClip.TryNarrow(new Point3D(0, 50, 0), new Vector3D(0, -1, 0), -20, ref enter, ref exit)
            .Should().BeTrue();

        enter.Should().BeApproximately(70, 1e-9);
        exit.Should().Be(100);
    }

    [Fact]
    public void ARayEntirelyBehindThePlaneSurvivesNothing()
    {
        double enter = 0;
        double exit = 40;

        // Parallel to the plane and on the far side of it.
        VolumeClip.TryNarrow(new Point3D(0, 10, 0), new Vector3D(1, 0, 0), -20, ref enter, ref exit)
            .Should().BeFalse();
    }

    [Fact]
    public void ClippingShorterThanTheGapToTheCubeChangesNothing()
    {
        // The cube's back face is at y = +20 and the box's is at y = +32, so the first
        // 12 mm of clip cut only air. 10 mm is inside that.
        LitColumnsInTheMiddleRow(Render(10)).Should().Be(LitColumnsInTheMiddleRow(Render(0)));
    }

    [Fact]
    public void ClippingPastTheBackFaceTakesExactlyAsMuchOfTheCubeAsItReaches()
    {
        // 22 mm in from y = +32 puts the plane at y = +10, ten millimetres into a cube that
        // spans -20 to +20. What is left is 30 mm wide, and at one pixel per millimetre
        // that is 30 columns. One pixel of tolerance for the trilinear ramp at the far face.
        LitColumnsInTheMiddleRow(Render(0)).Should().BeInRange(39, 41);
        LitColumnsInTheMiddleRow(Render(22)).Should().BeInRange(29, 31);
    }

    [Fact]
    public void ClippingThroughTheWholeVolumeLeavesNothingAtAll()
    {
        LitColumnsInTheMiddleRow(Render(64)).Should().Be(0);
    }
}
