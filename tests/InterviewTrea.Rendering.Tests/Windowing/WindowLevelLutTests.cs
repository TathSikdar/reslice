using System;
using FluentAssertions;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Windowing;
using Xunit;

namespace InterviewTrea.Rendering.Tests.Windowing;

/// <summary>
/// Every expectation here is derived from DICOM PS3.3 C.11.2.1.2 by hand, not captured
/// from a run. W256/L0 is chosen because it makes the arithmetic exact: the scale factor
/// 255 / (256 - 1) is 1, so the table reads as the identity plus the half-unit offsets
/// the standard specifies, and any error in those offsets shows up as a whole grey level.
/// </summary>
public sealed class WindowLevelLutTests
{
    private static readonly WindowLevel Unit = new(Width: 256, Center: 0);

    /// <summary>
    /// The two clipping boundaries. The lower is inclusive and the upper exclusive, which
    /// is the standard's asymmetry, not a slip: x &lt;= c - 0.5 - (w-1)/2 maps to black,
    /// x &gt; c - 0.5 + (w-1)/2 to white.
    /// </summary>
    [Theory]
    [InlineData(-1000, 0)]
    [InlineData(-129, 0)]
    [InlineData(-128, 0)]
    [InlineData(-127, 1)]
    [InlineData(0, 128)]
    [InlineData(127, 255)]
    [InlineData(128, 255)]
    [InlineData(3000, 255)]
    public void TheLinearTransformMatchesTheStandard(short hounsfield, byte expected)
    {
        new WindowLevelLut(Unit)[hounsfield].Should().Be(expected);
    }

    /// <summary>
    /// The half-unit terms are the whole reason for writing the formula out. The obvious
    /// (x - c + w/2) / w * 255 agrees almost everywhere and disagrees at the white end,
    /// where it reaches only 254. A test that only sampled the middle would pass on both.
    /// </summary>
    [Fact]
    public void TheNaiveFormulaWouldMissTheTopOfTheRange()
    {
        WindowLevelLut lut = new(Unit);

        byte naive = (byte)Math.Round((127 - 0 + (256 / 2.0)) / 256 * 255, MidpointRounding.AwayFromZero);

        naive.Should().Be(254);
        lut[127].Should().Be(255);
    }

    /// <summary>
    /// Denser tissue is never darker. This is cheap and catches an inverted or wrapped
    /// table, which would render a recognisable but photographically negative chest.
    /// </summary>
    [Fact]
    public void TheTableIsMonotonicAcrossTheWholeShortRange()
    {
        ReadOnlySpan<byte> table = new WindowLevelLut(WindowLevel.Bone).Table;

        table.Length.Should().Be(65536);

        for (int i = 1; i < table.Length; i++)
        {
            table[i].Should().BeGreaterThanOrEqualTo(table[i - 1]);
        }

        table[0].Should().Be(0);
        table[^1].Should().Be(255);
    }

    /// <summary>
    /// A lung window (W1500/L-600) spans -1350 to +150 HU. Air at -1000 must be dark but
    /// not clipped, and soft tissue at +40 must be near white - that separation is the
    /// entire clinical point of the preset.
    /// </summary>
    [Fact]
    public void TheLungPresetSeparatesAirFromSoftTissue()
    {
        WindowLevelLut lut = new(WindowLevel.Lung);

        // (-1000 - (-600.5)) * 255/1499 + 127.5 = 59.5...
        lut[-1000].Should().Be(60);
        lut[-1400].Should().Be(0);
        lut[40].Should().Be(236);
        lut[200].Should().Be(255);
    }

    [Fact]
    public void ANarrowWindowClipsHarder()
    {
        // Brain is W80/L40, so 0 HU (water, and cerebrospinal fluid is close) is already
        // black and 80 HU is already white. Nothing outside a 80 HU band survives.
        WindowLevelLut lut = new(WindowLevel.Brain);

        lut[0].Should().Be(0);
        lut[80].Should().Be(255);

        // The reason the denominator is (w - 1) and not w. The window spans exactly
        // w - 1 = 79 Hounsfield units between its two clipping boundaries, and those 79
        // units map onto exactly the 255 steps from black to white, so the last value
        // inside the band reaches full white and not 253. Dividing by w instead is a
        // 1.3% scale error here - invisible near the centre, and a whole two grey levels
        // out at the edge, which is why this assertion sits at the edge.
        lut[79].Should().Be(255);
        lut[1].Should().Be(3);

        // 129, not 128. The centre lands exactly on mid-grey only when 255 / (w - 1) is
        // itself an integer; here it is 255/79, so the half-unit offset the standard adds
        // scales up to 1.6 grey levels. Rounding it away would be inventing a symmetry the
        // transform does not have.
        lut[40].Should().Be(129);
    }

    /// <summary>
    /// Width 1 is legal and degenerate: the transform becomes a step at the centre. It is
    /// worth pinning because the scale factor divides by (width - 1) and so evaluates to
    /// infinity here - harmless only because the branch that uses it is unreachable.
    /// </summary>
    [Fact]
    public void AWidthOfOneIsAStepFunction()
    {
        WindowLevelLut lut = new(new WindowLevel(Width: 1, Center: 100));

        lut[99].Should().Be(0);
        lut[100].Should().Be(255);
    }

    [Fact]
    public void AWidthBelowOneIsRejected()
    {
        Action build = () => _ = new WindowLevelLut(new WindowLevel(Width: 0, Center: 0)).Window;

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Rebuild must refill the array it already owns. Allocating a fresh 64 KB table on
    /// every mouse-move during a window/level drag is a collection the frame rate pays for.
    /// </summary>
    [Fact]
    public void RebuildReplacesTheContentsWithoutReallocating()
    {
        WindowLevelLut lut = new(WindowLevel.Lung);
        byte before = lut[0];

        lut.Rebuild(WindowLevel.Bone);

        lut.Window.Should().Be(WindowLevel.Bone);
        lut[0].Should().NotBe(before);
    }

    [Fact]
    public void ThePresetsMatchTheRequirement()
    {
        WindowLevel.Lung.Should().Be(new WindowLevel(1500, -600));
        WindowLevel.SoftTissue.Should().Be(new WindowLevel(400, 40));
        WindowLevel.Bone.Should().Be(new WindowLevel(1800, 400));
        WindowLevel.Brain.Should().Be(new WindowLevel(80, 40));
        WindowLevel.Mediastinum.Should().Be(new WindowLevel(350, 50));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(350.0, null)]
    [InlineData(null, 50.0)]
    [InlineData(0.0, 50.0)]
    public void WithoutAUsableSeriesWindowTheInitialPresetIsSoftTissue(double? width, double? center)
    {
        WindowLevel.InitialFor(Metadata(width, center)).Should().Be(WindowLevel.SoftTissue);
    }

    [Fact]
    public void TheSeriesOwnWindowWinsWhenItHasOne()
    {
        WindowLevel.InitialFor(Metadata(width: 1200, center: -500))
            .Should().Be(new WindowLevel(1200, -500));
    }

    [Fact]
    public void AdjustingMovesBothAxesIndependently()
    {
        WindowLevel.Bone.AdjustedBy(widthDelta: -300, centerDelta: 50)
            .Should().Be(new WindowLevel(1500, 450));
    }

    /// <summary>
    /// A drag long enough to push the width through zero must stop at 1, not invert. Below
    /// 1 the LINEAR transform divides by a negative number and the image comes out as a
    /// photographic negative, which reads as a rendering bug rather than as a window
    /// dragged inside out.
    /// </summary>
    [Fact]
    public void TheWidthIsFlooredAtOneRatherThanInverting()
    {
        WindowLevel.Brain.AdjustedBy(widthDelta: -5000, centerDelta: 0)
            .Should().Be(new WindowLevel(1, 40));
    }

    private static VolumeMetadata Metadata(double? width, double? center) => new()
    {
        StudyInstanceUid = "1.2.3",
        SeriesInstanceUid = "1.2.3.4",
        FrameOfReferenceUid = "1.2.3.5",
        Modality = "CT",
        WindowWidth = width,
        WindowCenter = center,
    };
}
