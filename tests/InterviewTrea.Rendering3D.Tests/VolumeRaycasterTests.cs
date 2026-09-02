using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering3D;
using InterviewTrea.TestData;

namespace InterviewTrea.Rendering3D.Tests;

public sealed class VolumeRaycasterTests
{
    private const int Size = 128;

    // A 40 mm cube of bone in air, inside a 64 mm box of 1 mm voxels. Every expected value
    // below comes from those two numbers and nothing else.
    private static Volume Cube() => Phantoms.Cube(
        edgeMm: 40, insideHounsfield: 1000, outsideHounsfield: -1000,
        dimX: 65, dimY: 65, dimZ: 65, spacing: Phantoms.IsotropicSpacing);

    /// <summary>Nothing below 500 HU, a fixed opacity above it. The cube, and only the cube.</summary>
    private static TransferFunction Solid(double opacityPerMm) => new(
    [
        new TransferFunctionPoint(TransferFunction.MinimumHounsfield, Rgb.Black, 0),
        new TransferFunctionPoint(499, new Rgb(255, 255, 255), 0),
        new TransferFunctionPoint(500, new Rgb(255, 255, 255), opacityPerMm),
        new TransferFunctionPoint(TransferFunction.MaximumHounsfield, new Rgb(255, 255, 255), opacityPerMm),
    ]);

    /// <summary>The frontal view, framed so one output pixel is exactly one millimetre.</summary>
    private static Camera3D OneMillimetrePerPixel() => new()
    {
        Target = Point3D.Origin,
        Azimuth = -Math.PI / 2,
        Elevation = 0,
        ViewHeightMm = Size,
    };

    private static byte[] Render(
        Volume volume, TransferFunction function, double stepMm, double earlyTermination = 0.99,
        Camera3D? camera = null)
    {
        byte[] pixels = new byte[Size * Size * VolumeRaycaster.BytesPerPixel];

        VolumeRaycaster.Render(
            volume, camera ?? OneMillimetrePerPixel(), function,
            new RaycastSettings { StepMm = stepMm, EarlyTerminationOpacity = earlyTermination },
            Size, Size, pixels);

        return pixels;
    }

    private static byte Red(byte[] pixels, int column, int row) =>
        pixels[(((row * Size) + column) * VolumeRaycaster.BytesPerPixel) + 2];

    private static int LitPixels(byte[] pixels)
    {
        int lit = 0;
        for (int i = 0; i < pixels.Length; i += VolumeRaycaster.BytesPerPixel)
        {
            if (pixels[i] > 0 || pixels[i + 1] > 0 || pixels[i + 2] > 0)
            {
                lit++;
            }
        }

        return lit;
    }

    [Fact]
    public void AUniformCubeRendersARectangleOfTheSizeTheGeometryPredicts()
    {
        // The end-to-end geometry test. 40 mm across at 1 mm per pixel is 1600 pixels. The
        // silhouette sits where the trilinear ramp crosses 500 HU, a quarter of a
        // millimetre outside the last solid voxel, so allow one pixel of edge either way:
        // anything from 39x39 to 41x41.
        byte[] pixels = Render(Cube(), Solid(1.0), stepMm: 0.5);

        LitPixels(pixels).Should().BeInRange(39 * 39, 41 * 41);
    }

    [Fact]
    public void TheRectangleIsWhereTheCubeIsAndNotSomewhereElse()
    {
        byte[] pixels = Render(Cube(), Solid(1.0), stepMm: 0.5);

        // The image is 128 mm across centred on the cube, so the cube runs from pixel 44 to
        // pixel 83 in both axes. Just inside a corner is lit; just outside it is not.
        Red(pixels, 45, 45).Should().BeGreaterThan(0);
        Red(pixels, 82, 82).Should().BeGreaterThan(0);
        Red(pixels, 42, 64).Should().Be(0);
        Red(pixels, 85, 64).Should().Be(0);
        Red(pixels, 64, 42).Should().Be(0);
    }

    [Fact]
    public void ACentreRayAccumulatesWhatFortyMillimetresOfTissueShouldStop()
    {
        // 5% of the light per millimetre, 40 mm of cube: 1 - 0.95^40 = 0.8715, and the
        // tissue is white, so the pixel is 0.8715 * 255 = 222.2. The tolerance is a little
        // over one percent of full scale, which covers the quarter millimetre of trilinear
        // ramp at each face that the analytic figure ignores.
        byte[] pixels = Render(Cube(), Solid(0.05), stepMm: 1.0, earlyTermination: 1.0);

        double expected = (1 - Math.Pow(0.95, 40)) * 255;

        Red(pixels, 64, 64).Should().BeCloseTo((byte)Math.Round(expected), 4);
    }

    [Fact]
    public void HalvingTheStepLeavesTheImageAloneBecauseOpacityIsCorrectedForIt()
    {
        // FR-603. Progressive refinement changes the step, and a preview that resolves to a
        // different picture is worse than no preview.
        byte[] coarse = Render(Cube(), Solid(0.05), stepMm: 1.0, earlyTermination: 1.0);
        byte[] fine = Render(Cube(), Solid(0.05), stepMm: 0.25, earlyTermination: 1.0);

        Red(fine, 64, 64).Should().BeCloseTo(Red(coarse, 64, 64), 3);
    }

    [Fact]
    public void WithoutTheCorrectionTheSameChangeOfStepWouldChangeTheImage()
    {
        // The converse, which is what stops the test above passing vacuously. Accumulating
        // the same tissue with the raw table instead of the corrected one: 40 samples at
        // 0.05 gives 0.87, and 160 samples at 0.05 gives 0.9998 - a different picture.
        RayAccumulator coarse = default;
        for (int i = 0; i < 40; i++)
        {
            coarse.Add(255, 255, 255, 0.05);
        }

        RayAccumulator fine = default;
        for (int i = 0; i < 160; i++)
        {
            fine.Add(255, 255, 255, 0.05);
        }

        fine.OverBlack().R.Should().BeGreaterThan((byte)(coarse.OverBlack().R + 30));
    }

    [Fact]
    public void EarlyTerminationDoesNotChangeThePicture()
    {
        // FR-602. The optimization is only allowed if it is invisible: at 0.99 the samples
        // it skips could between them add at most 1% of full scale, under three levels.
        byte[] terminated = Render(Cube(), Solid(0.2), stepMm: 0.5, earlyTermination: 0.99);
        byte[] exhaustive = Render(Cube(), Solid(0.2), stepMm: 0.5, earlyTermination: 1.0);

        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column < Size; column++)
            {
                Red(terminated, column, row).Should().BeCloseTo(Red(exhaustive, column, row), 3);
            }
        }
    }

    [Fact]
    public void ACameraLookingAtNothingRendersBlackRatherThanFailing()
    {
        // Panned well clear of the volume: every ray misses the box.
        Camera3D away = OneMillimetrePerPixel().Panned(rightMm: 1000, upMm: 0);

        LitPixels(Render(Cube(), Solid(1.0), stepMm: 0.5, camera: away)).Should().Be(0);
    }

    [Fact]
    public void TheCubeIsTheSameSizeFromAnySideOfAnIsotropicVolume()
    {
        // Orthographic, so a 40 mm cube presents 40 mm square from any face - the one thing
        // a perspective projection would get wrong, and the reason radiology uses parallel.
        Camera3D fromTheSide = OneMillimetrePerPixel() with { Azimuth = 0 };
        Camera3D fromAbove = OneMillimetrePerPixel() with { Elevation = Math.PI / 2 };

        int front = LitPixels(Render(Cube(), Solid(1.0), stepMm: 0.5));
        int side = LitPixels(Render(Cube(), Solid(1.0), stepMm: 0.5, camera: fromTheSide));
        int above = LitPixels(Render(Cube(), Solid(1.0), stepMm: 0.5, camera: fromAbove));

        side.Should().BeCloseTo(front, 80);
        above.Should().BeCloseTo(front, 80);
    }

    [Fact]
    public void AnAnisotropicVolumeIsRenderedInMillimetresAndNotInVoxels()
    {
        // FR-208's problem in three dimensions. 0.7 x 0.7 x 3.0 mm voxels: a 40 mm cube is
        // 57 voxels across in x and 13 in z, and it still has to come out square. Sampled
        // by voxel index instead, it would be four times too tall.
        Volume volume = Phantoms.Cube(
            edgeMm: 40, insideHounsfield: 1000, outsideHounsfield: -1000,
            dimX: 95, dimY: 95, dimZ: 23, spacing: Phantoms.ChestSpacing);

        byte[] pixels = Render(volume, Solid(1.0), stepMm: 0.35);

        LitPixels(pixels).Should().BeInRange(38 * 38, 42 * 42);
    }

    [Fact]
    public void ADestinationOfTheWrongSizeIsRejectedRatherThanPartlyFilled()
    {
        Action act = () => VolumeRaycaster.Render(
            Cube(), OneMillimetrePerPixel(), Solid(1.0), RaycastSettings.For(Cube()),
            Size, Size, new byte[10]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheDefaultStepIsHalfTheShortestVoxelSide()
    {
        // Nyquist along the worst axis. A whole-voxel step lets a ray walk over a thin
        // structure, which is the 3D form of a coarse slab MIP missing a 1 mm vessel.
        RaycastSettings.For(Phantoms.Uniform(0, spacing: Phantoms.ChestSpacing)).StepMm
            .Should().BeApproximately(0.35, 1e-12);

        RaycastSettings.For(Phantoms.Uniform(0, spacing: Phantoms.ChestSpacing), coarsenBy: 4).StepMm
            .Should().BeApproximately(1.4, 1e-12);
    }
}
