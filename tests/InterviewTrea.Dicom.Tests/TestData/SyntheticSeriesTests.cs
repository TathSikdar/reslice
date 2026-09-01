using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FellowOakDicom;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Dicom.Tests.TestData;
using Xunit;

namespace InterviewTrea.Dicom.Tests.TestData;

/// <summary>
/// The generator is ground truth for every rejection test, so it is verified rather than
/// trusted: each knob must break the one thing it claims to break and nothing else.
/// </summary>
public class SyntheticSeriesTests
{
    private static double[] PositionsAlongNormal(IReadOnlyList<DicomDataset> slices)
    {
        Vector3D normal = new(0, 0, 1);
        return slices
            .Select(s => s.GetValues<double>(DicomTag.ImagePositionPatient))
            .Select(p => new Point3D(p[0], p[1], p[2]).AsVector().Dot(normal))
            .ToArray();
    }

    [Fact]
    public void Default_ProducesAWellFormedAxialSeries()
    {
        IReadOnlyList<DicomDataset> slices = new SyntheticSeries().Build();

        slices.Should().HaveCount(5);

        double[] along = PositionsAlongNormal(slices);
        for (int k = 1; k < along.Length; k++)
        {
            (along[k] - along[k - 1]).Should().BeApproximately(3.0, 1e-9);
        }
    }

    /// <summary>
    /// The point of FR-107a versus FR-107b in one assertion: a tilted gantry leaves
    /// ImageOrientationPatient perfectly orthogonal. Only comparing the stacking direction
    /// against the normal reveals it.
    /// </summary>
    [Fact]
    public void GantryTilt_LeavesTheDirectionCosinesOrthogonal()
    {
        SyntheticSeries series = new() { GantryTiltDegrees = 15 };
        double[] iop = series.Build()[0].GetValues<double>(DicomTag.ImageOrientationPatient);

        Vector3D row = new(iop[0], iop[1], iop[2]);
        Vector3D column = new(iop[3], iop[4], iop[5]);

        row.Dot(column).Should().BeApproximately(0.0, 1e-12);
        series.StackingDirection.Dot(row.Cross(column))
            .Should().BeApproximately(Math.Cos(15 * Math.PI / 180.0), 1e-12);
    }

    [Fact]
    public void OrientationSkew_BreaksOrthogonality()
    {
        double[] iop = new SyntheticSeries { OrientationSkew = 0.01 }
            .Build()[0].GetValues<double>(DicomTag.ImageOrientationPatient);

        new Vector3D(iop[0], iop[1], iop[2]).Dot(new Vector3D(iop[3], iop[4], iop[5]))
            .Should().BeApproximately(0.01, 1e-12);
    }

    /// <summary>One interval wrong, the rest correct, and the run still monotonic.</summary>
    [Fact]
    public void SpacingJitter_BreaksExactlyOneInterval()
    {
        double[] along = PositionsAlongNormal(
            new SyntheticSeries { SpacingJitterMm = 1.5, SpacingJitterAtSlice = 2 }.Build());

        double[] gaps = along.Zip(along.Skip(1), (a, b) => b - a).ToArray();

        gaps.Should().HaveCount(4);
        gaps[1].Should().BeApproximately(4.5, 1e-9);
        gaps.Where((_, i) => i != 1).Should().AllSatisfy(g => g.Should().BeApproximately(3.0, 1e-9));
    }

    [Fact]
    public void FrameOfReferenceMismatch_AffectsOnlyTheNamedSlice()
    {
        IReadOnlyList<DicomDataset> slices =
            new SyntheticSeries { FrameOfReferenceMismatchAtSlice = 3 }.Build();

        slices.Select(s => s.GetSingleValue<string>(DicomTag.FrameOfReferenceUID))
            .Distinct().Should().HaveCount(2);
    }

    /// <summary>DI-3: half the demographic tags are empty in public data.</summary>
    [Fact]
    public void OmitOptionalTags_LeavesTheRequiredOnesIntact()
    {
        DicomDataset slice = new SyntheticSeries { OmitOptionalTags = true }.Build()[0];

        slice.Contains(DicomTag.SeriesDescription).Should().BeFalse();
        slice.Contains(DicomTag.PatientName).Should().BeFalse();
        slice.GetSingleValue<string>(DicomTag.Modality).Should().Be("CT");
        slice.Contains(DicomTag.ImagePositionPatient).Should().BeTrue();
    }

    [Fact]
    public void WriteTo_ProducesFilesThatReadBackIdentically()
    {
        string directory = Path.Combine(Path.GetTempPath(), "itrea-" + Guid.NewGuid().ToString("N"));

        try
        {
            IReadOnlyList<string> paths = new SyntheticSeries().WriteTo(directory);

            paths.Should().HaveCount(5);

            DicomDataset reloaded = DicomFile.Open(paths[0]).Dataset;
            reloaded.GetValues<double>(DicomTag.ImagePositionPatient)
                .Should().Equal(-100.0, -80.0, -60.0);
            reloaded.GetValues<double>(DicomTag.PixelSpacing).Should().Equal(0.7, 0.5);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
