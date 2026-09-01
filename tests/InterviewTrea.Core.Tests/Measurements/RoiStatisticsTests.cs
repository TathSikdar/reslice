using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Measurements;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;
using InterviewTrea.TestData;
using Xunit;

namespace InterviewTrea.Core.Tests.Measurements;

/// <summary>
/// ROI statistics (FR-403, FR-404). Every expected value is derived from the phantom by
/// hand. A statistic that is wrong is invisible on screen - the outline is drawn from the
/// two corners, not from the numbers - so the numbers are the only thing under test.
/// </summary>
public sealed class RoiStatisticsTests
{
    /// <summary>
    /// An axial region from <paramref name="start"/> to <paramref name="end"/> in the
    /// z = 0 plane, given as (x, y) millimetres in patient space.
    /// </summary>
    private static Measurement Region(
        MeasurementKind kind,
        (double X, double Y) start,
        (double X, double Y) end)
    {
        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(PlaneOrientation.Axial);

        return new Measurement(
            kind,
            new MeasurementFrame(new Point3D(start.X, start.Y, 0), row, column),
            new Point3D(start.X, start.Y, 0),
            new Point3D(end.X, end.Y, 0));
    }

    /// <summary>
    /// Uniform tissue: the spread is zero and the extremes are the mean. The count is
    /// asserted too, because a region that sampled one voxel would pass everything else.
    /// </summary>
    [Fact]
    public void AUniformRegionHasNoSpread()
    {
        Volume volume = Phantoms.Uniform(Phantoms.SoftTissue, spacing: Phantoms.IsotropicSpacing);

        RoiStatistics statistics = RoiStatistics.Compute(
            Region(MeasurementKind.Rectangle, (-4, -2), (4, 2)), volume);

        statistics.SampleCount.Should().Be(8 * 4);
        statistics.MeanHounsfield.Should().BeApproximately(Phantoms.SoftTissue, 1e-12);
        statistics.StandardDeviationHounsfield.Should().Be(0);
        statistics.MinimumHounsfield.Should().Be(Phantoms.SoftTissue);
        statistics.MaximumHounsfield.Should().Be(Phantoms.SoftTissue);
    }

    /// <summary>
    /// A 100 HU-per-voxel ramp along x, sampled over eight columns of voxel centres from
    /// 400 to 1100 HU. The mean of an arithmetic sequence is its midpoint, 750, and its
    /// population variance is d^2 (n^2 - 1) / 12 = 10000 * 63 / 12 = 52500. The rows in y
    /// each repeat the same eight values, which changes neither.
    /// </summary>
    [Fact]
    public void ARampReportsItsMidpointAndItsAnalyticSpread()
    {
        Volume volume = Phantoms.GradientAlongX(spacing: Phantoms.IsotropicSpacing);

        RoiStatistics statistics = RoiStatistics.Compute(
            Region(MeasurementKind.Rectangle, (-4, -2), (4, 2)), volume, pitchMillimetres: 1.0);

        statistics.SampleCount.Should().Be(8 * 4);
        statistics.MeanHounsfield.Should().BeApproximately(750, 1e-9);
        statistics.StandardDeviationHounsfield.Should()
            .BeApproximately(Math.Sqrt(10000.0 * ((8.0 * 8.0) - 1) / 12), 1e-9);
        statistics.MinimumHounsfield.Should().Be(400);
        statistics.MaximumHounsfield.Should().Be(1100);
    }

    /// <summary>
    /// FR-403 against FR-404 on identical corners. The inscribed ellipse drops the corners,
    /// which on a ramp are the extreme values, so it must keep the mean - the mask is
    /// symmetric about the centre and so is the ramp - while reporting fewer samples and a
    /// smaller spread. An ellipse implemented as its bounding box would pass none of it.
    /// </summary>
    [Fact]
    public void TheEllipseDropsTheCornersTheRectangleKeeps()
    {
        Volume volume = Phantoms.GradientAlongX(spacing: Phantoms.IsotropicSpacing);

        RoiStatistics rectangle = RoiStatistics.Compute(
            Region(MeasurementKind.Rectangle, (-4, -2), (4, 2)), volume, pitchMillimetres: 1.0);
        RoiStatistics ellipse = RoiStatistics.Compute(
            Region(MeasurementKind.Ellipse, (-4, -2), (4, 2)), volume, pitchMillimetres: 1.0);

        ellipse.SampleCount.Should().BeLessThan(rectangle.SampleCount);
        ellipse.MeanHounsfield.Should().BeApproximately(rectangle.MeanHounsfield, 1e-9);
        ellipse.StandardDeviationHounsfield.Should()
            .BeLessThan(rectangle.StandardDeviationHounsfield);
    }

    /// <summary>
    /// A region hanging off the edge of the volume. The tissue is uniform, so folding the
    /// out-of-bounds samples in as <see cref="Volume.OutsideValue"/> would drag the mean
    /// hundreds of Hounsfield units towards air while the region still looked plausible.
    /// </summary>
    [Fact]
    public void SamplesOutsideTheVolumeAreSkippedRatherThanCountedAsAir()
    {
        // 64 voxels of 1 mm centred on the origin: the data ends at x = +31.5.
        Volume volume = Phantoms.Uniform(Phantoms.SoftTissue, spacing: Phantoms.IsotropicSpacing);

        RoiStatistics statistics = RoiStatistics.Compute(
            Region(MeasurementKind.Rectangle, (25, -2), (45, 2)), volume);

        statistics.MeanHounsfield.Should().BeApproximately(Phantoms.SoftTissue, 1e-12);
        statistics.MinimumHounsfield.Should().Be(Phantoms.SoftTissue);
        statistics.SampleCount.Should().BeInRange(1, (20 * 4) - 1);
    }

    /// <summary>
    /// The pitch is a real knob, not decoration. A 1 mm sheet of bone in air is invisible
    /// to a 4 mm pitch - the samples land either side of it - and the maximum then reports
    /// air over a region that contains bone. Nearest-neighbour rounding is what puts the
    /// fine pitch on the sheet: a sample at x = -0.5 mm rounds onto the voxel at x = 0.
    /// </summary>
    [Fact]
    public void ACoarsePitchStepsOverAThinStructure()
    {
        Volume volume = Phantoms.SheetAcrossX(spacing: Phantoms.IsotropicSpacing);
        Measurement region = Region(MeasurementKind.Rectangle, (-8, -1), (8, 1));

        RoiStatistics fine = RoiStatistics.Compute(region, volume, pitchMillimetres: 1.0);
        RoiStatistics coarse = RoiStatistics.Compute(region, volume, pitchMillimetres: 4.0);

        fine.MaximumHounsfield.Should().Be(Phantoms.Bone);
        coarse.MaximumHounsfield.Should().Be(Phantoms.Air);
    }

    /// <summary>
    /// A region smaller than one sampling cell still reports the voxel it sits on. A drag
    /// of a millimetre or two is a real thing a user does on a small calcification, and a
    /// tool that answered "no samples" there would look broken.
    /// </summary>
    [Fact]
    public void ARegionThinnerThanThePitchStillReportsAVoxel()
    {
        Volume volume = Phantoms.Uniform(Phantoms.SoftTissue, spacing: Phantoms.IsotropicSpacing);

        RoiStatistics statistics = RoiStatistics.Compute(
            Region(MeasurementKind.Rectangle, (-0.2, -0.2), (0.2, 0.2)), volume);

        statistics.SampleCount.Should().Be(1);
        statistics.MeanHounsfield.Should().BeApproximately(Phantoms.SoftTissue, 1e-12);
    }

    /// <summary>A distance encloses nothing, so there is nothing to average.</summary>
    [Fact]
    public void ADistanceHasNoRegionAndSoNoStatistics()
    {
        Volume volume = Phantoms.Uniform(Phantoms.SoftTissue, spacing: Phantoms.IsotropicSpacing);

        RoiStatistics.Compute(Region(MeasurementKind.Distance, (-4, -2), (4, 2)), volume)
            .Should().Be(RoiStatistics.Empty);
    }

    [Fact]
    public void ANonPositivePitchIsRejectedRatherThanLoopingForever()
    {
        Volume volume = Phantoms.Uniform(Phantoms.SoftTissue, spacing: Phantoms.IsotropicSpacing);
        Measurement region = Region(MeasurementKind.Rectangle, (-4, -2), (4, 2));

        Action compute = () => RoiStatistics.Compute(region, volume, pitchMillimetres: 0);

        compute.Should().Throw<ArgumentOutOfRangeException>();
    }
}
