using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FellowOakDicom;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Dicom;

/// <summary>A file the scan could not use, and why (shown in the load summary).</summary>
public sealed record SkippedFile(string FilePath, string Reason);

/// <summary>
/// One candidate series found in a directory: its metadata and the headers of its slices,
/// in the order the file system produced them. Sorting is <see cref="VolumeBuilder"/>'s
/// job, not the scanner's.
/// </summary>
public sealed record SeriesDescriptor(VolumeMetadata Metadata, IReadOnlyList<SliceHeader> Slices)
{
    public int SliceCount => Slices.Count;
}

/// <summary>Everything a directory scan found (FR-101, FR-102).</summary>
public sealed record DirectoryScan(
    IReadOnlyList<SeriesDescriptor> Series,
    IReadOnlyList<SkippedFile> Skipped);

/// <summary>
/// Enumerates a directory tree and groups the DICOM it finds into candidate series
/// (FR-101, FR-102).
/// </summary>
/// <remarks>
/// <para>
/// FR-102 says to prompt the user when a directory holds more than one series. There is
/// no user interface in this iteration, so the loader returns every candidate it found
/// and leaves the choosing to the caller. That keeps the loader a pure function of the
/// file system, which is worth more than the prompt.
/// </para>
/// <para>
/// Nothing here reads pixel data. A TCIA download is routinely a few hundred megabytes,
/// and the scan has to be fast enough to run before the user has chosen a series
/// (FR-109), so slices are opened with large tags skipped. <see cref="VolumeBuilder"/>
/// reopens the files it actually needs.
/// </para>
/// </remarks>
public sealed class SeriesLoader
{
    // CA1822 is right that this touches no instance state today. It stays an instance
    // method because the loader is registered in the container and will take an ILogger
    // and a progress callback (FR-108) in Iteration 2; making it static now would mean
    // changing every call site then.
#pragma warning disable CA1822
    public DirectoryScan Scan(string directory)
#pragma warning restore CA1822
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"No such directory: {directory}");
        }

        List<SkippedFile> skipped = [];
        List<(SliceHeader Header, DicomDataset Dataset)> parsed = [];

        // Recursive: TCIA archives unpack into a study folder containing one directory per
        // series, and pointing the viewer at the study root is the obvious thing to do.
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            DicomDataset? dataset = TryOpen(path, skipped);
            if (dataset is null)
            {
                continue;
            }

            SliceHeader? header = TryReadHeader(path, dataset, skipped);
            if (header is not null)
            {
                parsed.Add((header, dataset));
            }
        }

        IReadOnlyList<SeriesDescriptor> series = parsed
            .GroupBy(p => p.Header.SeriesInstanceUid, StringComparer.Ordinal)
            .Select(g => new SeriesDescriptor(
                ReadMetadata(g.First().Dataset, g.First().Header),
                g.Select(p => p.Header).ToArray()))

            // Largest first: when a study folder holds a scout and the real acquisition,
            // the real one is the one the caller almost certainly wants offered first.
            .OrderByDescending(s => s.SliceCount)
            .ThenBy(s => s.Metadata.SeriesInstanceUid, StringComparer.Ordinal)
            .ToArray();

        return new DirectoryScan(series, skipped);
    }

    private static DicomDataset? TryOpen(string path, List<SkippedFile> skipped)
    {
        try
        {
            // SkipLargeTags leaves PixelData on disk. Without it a scan of a 400 slice
            // series would materialise the whole study just to read its geometry.
            return DicomFile.Open(path, FileReadOption.SkipLargeTags).Dataset;
        }
        catch (DicomFileException)
        {
            skipped.Add(new SkippedFile(path, "not a DICOM file"));
            return null;
        }
        catch (IOException ex)
        {
            skipped.Add(new SkippedFile(path, $"unreadable: {ex.Message}"));
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            skipped.Add(new SkippedFile(path, $"unreadable: {ex.Message}"));
            return null;
        }
    }

    private static SliceHeader? TryReadHeader(
        string path,
        DicomDataset dataset,
        List<SkippedFile> skipped)
    {
        // Enhanced/multi-frame CT packs a whole volume into one file with its geometry in
        // nested sequences. Out of scope for this project - but it is rejected explicitly,
        // because parsing it as a single slice would silently produce a one-slice volume.
        if (dataset.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1) > 1)
        {
            skipped.Add(new SkippedFile(path, "enhanced multi-frame CT is not supported"));
            return null;
        }

        if (!dataset.TryGetValues(DicomTag.ImagePositionPatient, out double[] ipp) || ipp.Length < 3 ||
            !dataset.TryGetValues(DicomTag.ImageOrientationPatient, out double[] iop) || iop.Length < 6 ||
            !dataset.TryGetValues(DicomTag.PixelSpacing, out double[] spacing) || spacing.Length < 2 ||
            !dataset.TryGetSingleValue(DicomTag.SeriesInstanceUID, out string seriesUid) ||
            !dataset.TryGetSingleValue(DicomTag.FrameOfReferenceUID, out string frameUid) ||
            !dataset.TryGetSingleValue(DicomTag.Rows, out ushort rows) ||
            !dataset.TryGetSingleValue(DicomTag.Columns, out ushort columns))
        {
            skipped.Add(new SkippedFile(path, "missing geometry tags"));
            return null;
        }

        return new SliceHeader(
            FilePath: path,
            SeriesInstanceUid: seriesUid,
            FrameOfReferenceUid: frameUid,
            Position: new Point3D(ipp[0], ipp[1], ipp[2]),
            RowCosine: new Vector3D(iop[0], iop[1], iop[2]),
            ColumnCosine: new Vector3D(iop[3], iop[4], iop[5]),

            // The crossover. PixelSpacing[0] is the distance between adjacent rows, which
            // is a step in y; [1] is between adjacent columns, a step in x.
            AdjacentRowSpacing: spacing[0],
            AdjacentColumnSpacing: spacing[1],
            Rows: rows,
            Columns: columns);
    }

    /// <summary>
    /// Reads the descriptive tags. Every one of these is optional in de-identified public
    /// data, so each is read with a default and none of them can fail a load (DI-3).
    /// </summary>
    private static VolumeMetadata ReadMetadata(DicomDataset dataset, SliceHeader header) => new()
    {
        StudyInstanceUid = dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty),
        SeriesInstanceUid = header.SeriesInstanceUid,
        FrameOfReferenceUid = header.FrameOfReferenceUid,
        Modality = dataset.GetSingleValueOrDefault(DicomTag.Modality, string.Empty),
        SeriesDescription = NullIfBlank(dataset.GetSingleValueOrDefault(DicomTag.SeriesDescription, string.Empty)),
        PatientId = NullIfBlank(dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty)),
        PatientName = NullIfBlank(dataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty)),

        // Kept as the raw DICOM string rather than parsed to a date. Public collections
        // shift dates to de-identify, so a parsed value would look more meaningful than
        // it is (DI-2).
        StudyDate = NullIfBlank(dataset.GetSingleValueOrDefault(DicomTag.StudyDate, string.Empty)),
        WindowCenter = FirstOrNull(dataset, DicomTag.WindowCenter),
        WindowWidth = FirstOrNull(dataset, DicomTag.WindowWidth),
    };

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// WindowCenter and WindowWidth are multi-valued: a scanner often stores several
    /// presets. The first is the one to open with.
    /// </summary>
    private static double? FirstOrNull(DicomDataset dataset, DicomTag tag) =>
        dataset.TryGetValues(tag, out double[] values) && values.Length > 0 ? values[0] : null;
}
