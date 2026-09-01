using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using InterviewTrea.Dicom.Tests.TestData;
using Xunit;

namespace InterviewTrea.Dicom.Tests;

/// <summary>
/// Exercises the real file-system path rather than handing datasets straight to the
/// loader, because recursion, unreadable files and header-only parsing are the parts that
/// go wrong and none of them exist in an in-memory test.
/// </summary>
public sealed class SeriesLoaderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "itrea-scan-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_FindsASeriesAndItsGeometry()
    {
        new SyntheticSeries().WriteTo(root);

        DirectoryScan scan = new SeriesLoader().Scan(root);

        scan.Series.Should().ContainSingle();
        SeriesDescriptor series = scan.Series[0];
        series.SliceCount.Should().Be(5);
        series.Metadata.Modality.Should().Be("CT");
        series.Metadata.SeriesDescription.Should().Be("SYNTHETIC CHEST");
        series.Metadata.WindowCenter.Should().Be(40.0);

        SliceHeader slice = series.Slices[0];
        slice.Rows.Should().Be(6);
        slice.Columns.Should().Be(8);

        // The crossover: PixelSpacing was written as [0.7, 0.5], so 0.7 is the row-to-row
        // step and 0.5 the column-to-column step. Reading them the other way round is
        // undetectable on square pixels, which is why these differ.
        slice.AdjacentRowSpacing.Should().Be(0.7);
        slice.AdjacentColumnSpacing.Should().Be(0.5);
    }

    [Fact]
    public void Scan_RecursesAndGroupsBySeriesInstanceUid()
    {
        new SyntheticSeries { SliceCount = 5 }.WriteTo(Path.Combine(root, "study", "series-a"));
        new SyntheticSeries
        {
            SliceCount = 3,
            SeriesInstanceUid = "1.2.826.0.1.3680043.9.7133.99",
        }.WriteTo(Path.Combine(root, "study", "series-b"));

        DirectoryScan scan = new SeriesLoader().Scan(root);

        // FR-102: every candidate is returned, largest first, and the caller chooses.
        scan.Series.Select(s => s.SliceCount).Should().Equal(5, 3);
        scan.Skipped.Should().BeEmpty();
    }

    [Fact]
    public void Scan_SkipsNonDicomFilesAndSaysWhy()
    {
        new SyntheticSeries().WriteTo(root);
        File.WriteAllText(Path.Combine(root, "README.txt"), "not dicom");

        DirectoryScan scan = new SeriesLoader().Scan(root);

        scan.Series.Should().ContainSingle().Which.SliceCount.Should().Be(5);
        scan.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Be("not a DICOM file");
    }

    /// <summary>DI-3: absent optional tags are normal in public data and must not fail a load.</summary>
    [Fact]
    public void Scan_ToleratesAbsentOptionalTags()
    {
        new SyntheticSeries { OmitOptionalTags = true }.WriteTo(root);

        SeriesDescriptor series = new SeriesLoader().Scan(root).Series.Should().ContainSingle().Subject;

        series.SliceCount.Should().Be(5);
        series.Metadata.SeriesDescription.Should().BeNull();
        series.Metadata.PatientName.Should().BeNull();
        series.Metadata.StudyDate.Should().BeNull();
        series.Metadata.WindowCenter.Should().BeNull();
    }

    [Fact]
    public void Scan_OnAMissingDirectory_Throws()
    {
        Action act = () => new SeriesLoader().Scan(Path.Combine(root, "nope"));

        act.Should().Throw<DirectoryNotFoundException>();
    }
}
