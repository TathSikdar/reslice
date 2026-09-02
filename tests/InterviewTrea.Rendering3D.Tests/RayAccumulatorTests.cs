using FluentAssertions;
using InterviewTrea.Rendering3D;

namespace InterviewTrea.Rendering3D.Tests;

public sealed class RayAccumulatorTests
{
    [Fact]
    public void TwoHalfOpaqueSamplesStopThreeQuartersOfTheLightAndNotAllOfIt()
    {
        RayAccumulator ray = default;
        ray.Add(255, 255, 255, 0.5);
        ray.Add(255, 255, 255, 0.5);

        // The second sample is only half visible, because the first is in front of it:
        // 0.5 + 0.5 * 0.5 = 0.75. Adding the opacities instead would give 1.0, and every
        // rendering would come out too solid in a way that looks like a good picture.
        ray.Opacity.Should().BeApproximately(0.75, 1e-12);

        // 0.5 * 255 + 0.5 * 0.5 * 255 = 191.25, which rounds to 191.
        ray.OverBlack().Should().Be(new Rgb(191, 191, 191));
    }

    [Fact]
    public void OpacityApproachesOneWithoutEverReachingItThroughFiniteSamples()
    {
        RayAccumulator ray = default;
        for (int i = 0; i < 200; i++)
        {
            ray.Add(255, 255, 255, 0.5);
        }

        ray.Opacity.Should().BeLessThanOrEqualTo(1);
        ray.Opacity.Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void FrontToBackAndBackToFrontAgreeOnTheSameSamples()
    {
        // The proof that the accumulation is right. Front to back is what the renderer
        // does, because it can stop early; back to front is the textbook form and needs no
        // running opacity. They are the same operator and must give the same pixel.
        (byte Colour, double Alpha)[] samples =
        [
            (200, 0.30),
            (60, 0.55),
            (255, 0.10),
            (120, 0.80),
        ];

        RayAccumulator frontToBack = default;
        foreach ((byte colour, double alpha) in samples)
        {
            frontToBack.Add(colour, colour, colour, alpha);
        }

        // C_dst = C_src * A_src + C_dst * (1 - A_src), walked from the far end.
        double back = 0;
        double backOpacity = 0;
        for (int i = samples.Length - 1; i >= 0; i--)
        {
            (byte colour, double alpha) = samples[i];
            back = (colour * alpha) + (back * (1 - alpha));
            backOpacity = alpha + (backOpacity * (1 - alpha));
        }

        frontToBack.Opacity.Should().BeApproximately(backOpacity, 1e-12);
        frontToBack.OverBlack().R.Should().Be((byte)System.Math.Round(back, System.MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void ATransparentSampleChangesNothing()
    {
        RayAccumulator ray = default;
        ray.Add(255, 0, 0, 0.4);
        RayAccumulator before = ray;

        ray.Add(0, 255, 0, 0);

        ray.Should().Be(before);
    }

    [Fact]
    public void AFullyOpaqueFirstSampleHidesEverythingBehindIt()
    {
        RayAccumulator ray = default;
        ray.Add(10, 20, 30, 1.0);
        ray.Add(255, 255, 255, 1.0);

        ray.Opacity.Should().Be(1);
        ray.OverBlack().Should().Be(new Rgb(10, 20, 30));
    }
}
