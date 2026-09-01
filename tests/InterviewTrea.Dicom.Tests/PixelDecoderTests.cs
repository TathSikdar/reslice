using System;
using FellowOakDicom;
using FluentAssertions;
using InterviewTrea.Dicom.Tests.TestData;
using Xunit;

namespace InterviewTrea.Dicom.Tests;

/// <summary>
/// FR-104. Every expected value here is the rescale equation applied by hand to a stored
/// value the generator was told to write.
/// </summary>
public class PixelDecoderTests
{
    private static short[] Decode(SyntheticSeries series, int sliceIndex, out DecodeStatistics statistics)
    {
        DicomDataset slice = series.Build()[sliceIndex];
        short[] destination = new short[series.Rows * series.Columns];
        statistics = PixelDecoder.DecodeInto(slice, destination);
        return destination;
    }

    /// <summary>
    /// Stored 1024 with slope 1 and intercept -1024 is 0 HU. If the intercept were skipped
    /// this would read 1024, which is the failure that makes air read 0 instead of -1000.
    /// </summary>
    [Fact]
    public void UnsignedPixels_AreRescaledToHounsfield()
    {
        short[] hu = Decode(new SyntheticSeries(), sliceIndex: 0, out DecodeStatistics stats);

        // StoredValueAt is 1024 + i + 10j + 100k, so voxel (0,0,0) is 1024 -> 0 HU.
        hu[0].Should().Be(0);

        // Column 3 of row 2 on slice 0: 1024 + 3 + 20 = 1047 -> 23 HU.
        hu[(2 * 8) + 3].Should().Be(23);

        stats.Minimum.Should().Be(0);
        stats.Maximum.Should().Be((short)(7 + 50));
        stats.Saturated.Should().Be(0);
    }

    /// <summary>
    /// The same values stored as two's complement with no intercept. Sign extension has to
    /// come from BitsStored, not from the storage type.
    /// </summary>
    [Fact]
    public void SignedPixels_AreRescaledToHounsfield()
    {
        SyntheticSeries series = new()
        {
            SignedPixels = true,
            RescaleIntercept = 0.0,
            StoredValueAt = (i, j, k) => -1000 + i + (10 * j) + (100 * k),
        };

        short[] hu = Decode(series, sliceIndex: 0, out DecodeStatistics stats);

        hu[0].Should().Be(-1000);
        hu[(2 * 8) + 3].Should().Be(-977);
        stats.Minimum.Should().Be(-1000);
    }

    /// <summary>
    /// A 12-bit-in-16 scanner leaves the top four bits undefined. They must be masked off
    /// rather than read, or a padding bit becomes several thousand Hounsfield units.
    /// </summary>
    [Fact]
    public void BitsStoredNarrowerThanBitsAllocated_MasksThePadding()
    {
        SyntheticSeries series = new()
        {
            BitsStored = 12,
            RescaleIntercept = 0.0,

            // 0xF000 is pure padding above the 12-bit run; 0x0ABC is the stored value.
            StoredValueAt = (_, _, _) => unchecked((int)0xF000) | 0x0ABC,
        };

        Decode(series, sliceIndex: 0, out DecodeStatistics stats);

        stats.Maximum.Should().Be(0x0ABC);
    }

    /// <summary>
    /// Signed 12-bit: bit 11 is the sign, and sign-extending from bit 15 instead would give
    /// a large positive number rather than a small negative one.
    /// </summary>
    [Fact]
    public void SignedNarrowPixels_SignExtendFromBitsStored()
    {
        SyntheticSeries series = new()
        {
            BitsStored = 12,
            SignedPixels = true,
            RescaleIntercept = 0.0,

            // 0xFFF is -1 in twelve-bit two's complement.
            StoredValueAt = (_, _, _) => 0x0FFF,
        };

        Decode(series, sliceIndex: 0, out DecodeStatistics stats);

        stats.Minimum.Should().Be(-1);
        stats.Maximum.Should().Be(-1);
    }

    /// <summary>
    /// A non-unit slope is unusual but legal, and it is the case where forgetting to
    /// multiply is invisible on a normal series.
    /// </summary>
    [Fact]
    public void ANonUnitSlopeIsApplied()
    {
        SyntheticSeries series = new()
        {
            RescaleSlope = 2.0,
            RescaleIntercept = -1024.0,
            StoredValueAt = (_, _, _) => 1024,
        };

        Decode(series, sliceIndex: 0, out DecodeStatistics stats);

        // 1024 * 2 - 1024 = 1024, not 0.
        stats.Maximum.Should().Be(1024);
    }

    /// <summary>
    /// Saturation is counted rather than allowed to wrap. A silent wrap turns the densest
    /// metal into air, which is the exact failure the spec warns about in 5.3.
    /// </summary>
    [Fact]
    public void ValuesBeyondShortRange_SaturateAndAreCounted()
    {
        SyntheticSeries series = new()
        {
            RescaleSlope = 1.0,
            RescaleIntercept = 0.0,
            StoredValueAt = (_, _, _) => 60000,
        };

        Decode(series, sliceIndex: 0, out DecodeStatistics stats);

        stats.Maximum.Should().Be(short.MaxValue);
        stats.Saturated.Should().Be(6 * 8);
    }

    [Fact]
    public void AMismatchedDestination_Throws()
    {
        DicomDataset slice = new SyntheticSeries().Build()[0];
        short[] tooSmall = new short[10];

        Action act = () => PixelDecoder.DecodeInto(slice, tooSmall);

        act.Should().Throw<ArgumentException>().WithMessage("*48*");
    }
}
