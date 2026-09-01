using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Dicom.Tests.TestData;
using Xunit;

namespace InterviewTrea.Dicom.Tests;

/// <summary>
/// One test per rejection path, each driving a deliberately broken series from
/// <see cref="SyntheticSeries"/> and asserting the specific reason rather than merely
/// that something went wrong.
/// </summary>
public sealed class GeometryValidatorTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "itrea-validate-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Round-trips through the real parser so the headers are what a load would see.</summary>
    private IReadOnlyList<SliceHeader> Headers(SyntheticSeries series)
    {
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        series.WriteTo(directory);
        return new SeriesLoader().Scan(directory).Series.Single().Slices;
    }

    private static SeriesRejectionReason ReasonFor(Func<SeriesGeometry> act) =>
        act.Should().Throw<SeriesRejectedException>().Which.Reason;

    [Fact]
    public void AWellFormedSeries_Validates()
    {
        SeriesGeometry geometry = new GeometryValidator().Validate(Headers(new SyntheticSeries()));

        geometry.OrderedSlices.Should().HaveCount(5);
        geometry.Normal.Z.Should().BeApproximately(1.0, 1e-12);
        geometry.SliceStep.Length.Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>
    /// FR-103. The input is reversed and the InstanceNumbers are reversed with it, so
    /// anything that sorts by InstanceNumber - or that simply trusts the input order -
    /// produces the wrong stack.
    /// </summary>
    [Fact]
    public void SortsByPositionAndIgnoresInstanceNumber()
    {
        IReadOnlyList<SliceHeader> shuffled =
            Headers(new SyntheticSeries { ReverseInstanceNumbers = true }).Reverse().ToArray();

        SeriesGeometry geometry = new GeometryValidator().Validate(shuffled);

        double[] along = geometry.OrderedSlices
            .Select(s => s.DistanceAlong(geometry.Normal))
            .ToArray();

        along.Should().BeInAscendingOrder();
        along[0].Should().BeApproximately(-60.0, 1e-9);
    }

    [Fact]
    public void TooFewSlices_IsRejected() =>
        ReasonFor(() => new GeometryValidator().Validate(Headers(new SyntheticSeries { SliceCount = 2 })))
            .Should().Be(SeriesRejectionReason.TooFewSlices);

    /// <summary>FR-105.</summary>
    [Fact]
    public void MismatchedFrameOfReference_IsRejected()
    {
        SeriesRejectedException rejection = ((Func<SeriesGeometry>)(() =>
            new GeometryValidator().Validate(
                Headers(new SyntheticSeries { FrameOfReferenceMismatchAtSlice = 3 }))))
            .Should().Throw<SeriesRejectedException>().Which;

        rejection.Reason.Should().Be(SeriesRejectionReason.MismatchedFrameOfReference);
        rejection.Message.Should().Contain("0020,0052");
    }

    /// <summary>
    /// FR-106, missing-slice shape: one gap of twice the median, which is what a dropped
    /// file in a partial download looks like.
    /// </summary>
    [Fact]
    public void AMissingSlice_IsRejectedWithItsOwnMessage()
    {
        SeriesRejectedException rejection = ((Func<SeriesGeometry>)(() =>
            new GeometryValidator().Validate(
                Headers(new SyntheticSeries { SpacingJitterMm = 3.0 }))))
            .Should().Throw<SeriesRejectedException>().Which;

        rejection.Reason.Should().Be(SeriesRejectionReason.NonUniformSpacing);
        rejection.Message.Should().Contain("missing");
    }

    /// <summary>
    /// FR-106 in its plainest shape: gaps that are merely uneven, neither a duplicate nor
    /// a dropped slice, get the general message rather than a misdiagnosis.
    /// </summary>
    [Fact]
    public void MerelyUnevenSpacing_IsRejectedWithoutBeingMisdiagnosed()
    {
        SeriesRejectedException rejection = ((Func<SeriesGeometry>)(() =>
            new GeometryValidator().Validate(
                Headers(new SyntheticSeries { SpacingJitterMm = 1.5 }))))
            .Should().Throw<SeriesRejectedException>().Which;

        rejection.Reason.Should().Be(SeriesRejectionReason.NonUniformSpacing);
        rejection.Message.Should().Contain("gaps ranging");
    }

    /// <summary>
    /// Half a percent of jitter is under the 1% tolerance and must pass. Without this the
    /// uniformity check could be tightened to nothing and every rejection test would still
    /// be green.
    /// </summary>
    [Fact]
    public void SpacingWithinTolerance_IsAccepted() =>
        new GeometryValidator()
            .Validate(Headers(new SyntheticSeries { SliceSpacing = 3.0, SpacingJitterMm = 0.01 }))
            .OrderedSlices.Should().HaveCount(5);

    /// <summary>FR-106, duplicate-slice shape: a distinct message because the fix differs.</summary>
    [Fact]
    public void DuplicateSlicePositions_AreRejectedWithTheirOwnMessage()
    {
        SeriesRejectedException rejection = ((Func<SeriesGeometry>)(() =>
            new GeometryValidator().Validate(
                Headers(new SyntheticSeries { SpacingJitterMm = -3.0, SpacingJitterAtSlice = 2 }))))
            .Should().Throw<SeriesRejectedException>().Which;

        rejection.Reason.Should().Be(SeriesRejectionReason.NonUniformSpacing);
        rejection.Message.Should().Contain("duplicate");
    }

    /// <summary>FR-107a: a bad header, detected by the dot product of the direction cosines.</summary>
    [Fact]
    public void SkewedOrientation_IsRejectedAsMalformed()
    {
        SeriesRejectedException rejection = ((Func<SeriesGeometry>)(() =>
            new GeometryValidator().Validate(
                Headers(new SyntheticSeries { OrientationSkew = 0.01 }))))
            .Should().Throw<SeriesRejectedException>().Which;

        rejection.Reason.Should().Be(SeriesRejectionReason.MalformedOrientation);
        rejection.Message.Should().Contain("not a tilted gantry");
    }

    /// <summary>
    /// FR-107b, and the reason the spec's original wording could not work: this series has
    /// perfectly orthogonal direction cosines and is still unusable.
    /// </summary>
    [Fact]
    public void GantryTilt_IsRejectedEvenThoughOrientationIsOrthogonal()
    {
        IReadOnlyList<SliceHeader> tilted = Headers(new SyntheticSeries { GantryTiltDegrees = 15 });

        tilted[0].RowCosine.Dot(tilted[0].ColumnCosine).Should().BeApproximately(0.0, 1e-9);

        SeriesRejectedException rejection = ((Func<SeriesGeometry>)(() =>
            new GeometryValidator().Validate(tilted)))
            .Should().Throw<SeriesRejectedException>().Which;

        rejection.Reason.Should().Be(SeriesRejectionReason.GantryTilt);
        rejection.Message.Should().Contain("15");
    }

    /// <summary>
    /// One degree is inside the default 1e-3 tolerance (about 2.5 degrees) and passes.
    /// The tolerance is a calibration knob, so it needs a test on both sides of it.
    /// </summary>
    [Fact]
    public void TiltWithinTolerance_IsAccepted() =>
        new GeometryValidator()
            .Validate(Headers(new SyntheticSeries { GantryTiltDegrees = 1.0 }))
            .OrderedSlices.Should().HaveCount(5);

    [Fact]
    public void ATighterTiltTolerance_RejectsWhatTheDefaultAccepts() =>
        ReasonFor(() => new GeometryValidator(tiltTolerance: 1e-6)
                .Validate(Headers(new SyntheticSeries { GantryTiltDegrees = 1.0 })))
            .Should().Be(SeriesRejectionReason.GantryTilt);
}
