using System;
using System.Linq;
using FluentAssertions;
using InterviewTrea.Core.Volumes;
using InterviewTrea.TestData;
using Xunit;

namespace InterviewTrea.Core.Tests.Volumes;

/// <summary>
/// FR-507. Every expected count below is derived from the phantom's definition rather
/// than read back from a run: a histogram that is wrong by one bin looks entirely
/// plausible drawn as bars, which is exactly why the numbers have to be checked.
/// </summary>
public sealed class HistogramTests
{
    /// <summary>
    /// 40 HU with 16-unit bins: (40 + 1024) / 16 = 66.5, so bin 66, which covers
    /// 32 to 47 HU. Every voxel of a uniform phantom lands there and nowhere else.
    /// </summary>
    [Fact]
    public void AUniformPhantomFillsExactlyOneBin()
    {
        Volume volume = Phantoms.Uniform(
            Phantoms.SoftTissue, dimX: 8, dimY: 8, dimZ: 4, spacing: Phantoms.IsotropicSpacing);

        Histogram histogram = Histogram.Compute(volume, binWidth: 16);

        histogram.Total.Should().Be(256);
        histogram.Counts[66].Should().Be(256);
        histogram.Counts.Sum().Should().Be(256);
        histogram.Peak.Should().Be(256);
        histogram.RangeOf(66).Should().Be((32, 47));
    }

    /// <summary>
    /// The ramp runs 0, 64, 128 … across x, so with 64-unit bins each column of the phantom
    /// is its own bin and every one of them holds dimY * dimZ voxels. This is what catches
    /// an off-by-one in the bin index: the whole distribution shifts by one bin and still
    /// looks like a comb. The step matches the bin width on purpose - a column landing
    /// halfway into a bin would pass whichever way the arithmetic rounded.
    /// </summary>
    [Fact]
    public void ARampSpreadsOneColumnIntoEachBin()
    {
        Volume volume = Phantoms.GradientAlongX(
            startHounsfield: 0, hounsfieldPerVoxel: 64, dimX: 8, dimY: 4, dimZ: 2);

        Histogram histogram = Histogram.Compute(volume, binWidth: 64);

        // 0 HU sits in bin (0 + 1024) / 64 = 16, and the eight columns run 0, 64 ... 448 HU
        // into bins 16 through 23.
        foreach (int column in Enumerable.Range(0, 8))
        {
            histogram.BinOf(column * 64).Should().Be(16 + column);
            histogram.Counts[16 + column].Should().Be(8);
        }

        histogram.Total.Should().Be(64);
        histogram.Counts.Sum().Should().Be(64);
    }

    /// <summary>
    /// A padding value below the scale is real data - GE writes -2048 outside the
    /// reconstruction circle - and it has to land somewhere. Dropping it would make the
    /// total disagree with the voxel count and every percentage taken from the histogram
    /// quietly wrong.
    /// </summary>
    [Fact]
    public void ValuesBelowTheScaleAreCountedIntoTheFirstBinRatherThanDropped()
    {
        Volume volume = Phantoms.Uniform(
            -2048, dimX: 4, dimY: 4, dimZ: 2, spacing: Phantoms.IsotropicSpacing);

        Histogram histogram = Histogram.Compute(volume, binWidth: 16);

        histogram.Counts[0].Should().Be(32);
        histogram.Total.Should().Be(32);
    }

    /// <summary>
    /// A bin width that does not divide the 4096-unit scale would make the last bin a
    /// different width from the rest, so a count in it would not be comparable with any
    /// other. Refused rather than silently truncated.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-16)]
    [InlineData(300)]
    public void ABinWidthThatDoesNotDivideTheScaleIsRefused(int binWidth)
    {
        Volume volume = Phantoms.Uniform(
            Phantoms.SoftTissue, dimX: 2, dimY: 2, dimZ: 2, spacing: Phantoms.IsotropicSpacing);

        Action compute = () => Histogram.Compute(volume, binWidth);

        compute.Should().Throw<ArgumentOutOfRangeException>();
    }
}
