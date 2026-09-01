using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FellowOakDicom;
using FellowOakDicom.IO.Buffer;
using FellowOakDicom.Imaging;
using InterviewTrea.Core.Geometry;

namespace InterviewTrea.Dicom.Tests.TestData;

/// <summary>
/// Builds a CT series as in-memory <see cref="DicomDataset"/> objects, with a knob for
/// every failure mode the loader has to reject (spec 8.2).
/// </summary>
/// <remarks>
/// <para>
/// The validator's whole job is refusing malformed input, so it needs a source of
/// malformed input that is deliberate rather than imagined. Defaults produce a clean,
/// well-formed axial series; each property below breaks exactly one thing.
/// </para>
/// <para>
/// This lives in the test project, not in <c>InterviewTrea.TestData</c>, because that
/// assembly references Core only and fo-dicom must not leak into it.
/// </para>
/// </remarks>
public sealed class SyntheticSeries
{
    private const string DefaultStudyUid = "1.2.826.0.1.3680043.9.7133.10";
    private const string DefaultSeriesUid = "1.2.826.0.1.3680043.9.7133.11";
    private const string DefaultFrameOfReferenceUid = "1.2.826.0.1.3680043.9.7133.12";

    public int Columns { get; init; } = 8;

    public int Rows { get; init; } = 6;

    public int SliceCount { get; init; } = 5;

    /// <summary>PixelSpacing[0]: millimetres between adjacent rows, so the y step.</summary>
    public double AdjacentRowSpacing { get; init; } = 0.7;

    /// <summary>PixelSpacing[1]: millimetres between adjacent columns, so the x step.</summary>
    public double AdjacentColumnSpacing { get; init; } = 0.5;

    /// <summary>Millimetres between slice positions along the stacking direction.</summary>
    public double SliceSpacing { get; init; } = 3.0;

    public Vector3D RowCosine { get; init; } = new(1, 0, 0);

    public Vector3D ColumnCosine { get; init; } = new(0, 1, 0);

    public Point3D FirstSlicePosition { get; init; } = new(-100, -80, -60);

    public double RescaleSlope { get; init; } = 1.0;

    public double RescaleIntercept { get; init; } = -1024.0;

    /// <summary>PixelRepresentation (0028,0103): false is unsigned, true is two-s complement.</summary>
    public bool SignedPixels { get; init; }

    public ushort BitsAllocated { get; init; } = 16;

    public ushort BitsStored { get; init; } = 16;

    public string Modality { get; init; } = "CT";

    /// <summary>
    /// Drops SeriesDescription, PatientName, PatientID and StudyDate entirely - the normal
    /// state of de-identified public data (DI-3).
    /// </summary>
    public bool OmitOptionalTags { get; init; }

    /// <summary>Zero-based index of a slice given a different FrameOfReferenceUID (FR-105).</summary>
    public int? FrameOfReferenceMismatchAtSlice { get; init; }

    /// <summary>
    /// Millimetres added to the position of one slice onward, breaking spacing uniformity
    /// (FR-106). Applied at <see cref="SpacingJitterAtSlice"/> and every slice after it,
    /// so the run stays monotonic and exactly one interval is wrong.
    /// </summary>
    public double SpacingJitterMm { get; init; }

    public int SpacingJitterAtSlice { get; init; } = 2;

    /// <summary>
    /// Rotates the stacking direction away from the slice normal, about the row axis
    /// (FR-107b). The direction cosines stay orthonormal - which is exactly why the
    /// orthogonality of ImageOrientationPatient cannot detect tilt.
    /// </summary>
    public double GantryTiltDegrees { get; init; }

    /// <summary>
    /// Skews the column cosine toward the row cosine, so their dot product is no longer
    /// zero. A malformed header rather than a tilted gantry (FR-107a).
    /// </summary>
    public double OrientationSkew { get; init; }

    /// <summary>
    /// Stored pixel value before rescale, as a function of (column, row, slice). The
    /// default gives every axis a distinct weight so a transposed index cannot go
    /// unnoticed.
    /// </summary>
    public Func<int, int, int, int> StoredValueAt { get; init; } =
        (i, j, k) => 1024 + i + (10 * j) + (100 * k);

    public string SeriesInstanceUid { get; init; } = DefaultSeriesUid;

    /// <summary>
    /// Numbers the instances backwards, so that sorting by InstanceNumber gives the
    /// opposite of the correct order (FR-103).
    /// </summary>
    public bool ReverseInstanceNumbers { get; init; }

    /// <summary>Patient-space direction the slice positions actually step along.</summary>
    public Vector3D StackingDirection
    {
        get
        {
            // Slice normal is row cosine x column cosine, in that order - the cross
            // product is anti-commutative, so the DICOM ordering is load bearing.
            Vector3D normal = RowCosine.Cross(ColumnCosine).Normalized();

            if (GantryTiltDegrees == 0)
            {
                return normal;
            }

            // Lean the stack toward the column axis. That is what a tilted gantry
            // physically does: the table advances along one direction while the imaging
            // plane is rotated away from perpendicular to it.
            double radians = GantryTiltDegrees * Math.PI / 180.0;
            return (normal * Math.Cos(radians)) + (ColumnCosine.Normalized() * Math.Sin(radians));
        }
    }

    public IReadOnlyList<DicomDataset> Build()
    {
        List<DicomDataset> slices = new(SliceCount);
        Vector3D step = StackingDirection * SliceSpacing;

        for (int k = 0; k < SliceCount; k++)
        {
            double extra = SpacingJitterMm != 0 && k >= SpacingJitterAtSlice ? SpacingJitterMm : 0;
            Point3D position = FirstSlicePosition + (step * k) + (StackingDirection * extra);
            slices.Add(BuildSlice(k, position));
        }

        return slices;
    }

    /// <summary>Writes the series to disk as Part 10 files, one per slice.</summary>
    public IReadOnlyList<string> WriteTo(string directory)
    {
        Directory.CreateDirectory(directory);
        List<string> paths = new(SliceCount);
        int index = 0;

        foreach (DicomDataset slice in Build())
        {
            string name = string.Format(CultureInfo.InvariantCulture, "slice-{0:D4}.dcm", index++);
            string path = Path.Combine(directory, name);
            new DicomFile(slice).Save(path);
            paths.Add(path);
        }

        return paths;
    }

    private DicomDataset BuildSlice(int k, Point3D position)
    {
        Vector3D column = ColumnCosine + (RowCosine * OrientationSkew);

        DicomDataset dataset = new()
        {
            { DicomTag.SOPClassUID, DicomUID.CTImageStorage },
            { DicomTag.SOPInstanceUID, DicomUID.Generate() },
            { DicomTag.StudyInstanceUID, DefaultStudyUid },
            { DicomTag.SeriesInstanceUID, SeriesInstanceUid },
            { DicomTag.Modality, Modality },
            { DicomTag.InstanceNumber, ReverseInstanceNumbers ? SliceCount - k : k + 1 },
            { DicomTag.ImagePositionPatient, position.X, position.Y, position.Z },
            {
                DicomTag.ImageOrientationPatient,
                RowCosine.X, RowCosine.Y, RowCosine.Z, column.X, column.Y, column.Z
            },

            // PixelSpacing is [between rows, between columns] - y first, then x. Invisible
            // on square pixels, which is why the defaults here are deliberately not square.
            { DicomTag.PixelSpacing, AdjacentRowSpacing, AdjacentColumnSpacing },
            { DicomTag.SliceThickness, SliceSpacing },
            {
                DicomTag.FrameOfReferenceUID,
                k == FrameOfReferenceMismatchAtSlice
                    ? DefaultFrameOfReferenceUid + ".9"
                    : DefaultFrameOfReferenceUid
            },
            { DicomTag.Rows, (ushort)Rows },
            { DicomTag.Columns, (ushort)Columns },
            { DicomTag.SamplesPerPixel, (ushort)1 },
            { DicomTag.PhotometricInterpretation, "MONOCHROME2" },
            { DicomTag.BitsAllocated, BitsAllocated },
            { DicomTag.BitsStored, BitsStored },
            { DicomTag.HighBit, (ushort)(BitsStored - 1) },
            { DicomTag.PixelRepresentation, (ushort)(SignedPixels ? 1 : 0) },
            { DicomTag.RescaleSlope, RescaleSlope },
            { DicomTag.RescaleIntercept, RescaleIntercept },
        };

        if (!OmitOptionalTags)
        {
            dataset.Add(DicomTag.SeriesDescription, "SYNTHETIC CHEST");
            dataset.Add(DicomTag.PatientName, "LIDC-IDRI-0142");
            dataset.Add(DicomTag.PatientID, "LIDC-IDRI-0142");
            dataset.Add(DicomTag.StudyDate, "20000101");
            dataset.Add(DicomTag.WindowCenter, 40.0);
            dataset.Add(DicomTag.WindowWidth, 400.0);
        }

        DicomPixelData.Create(dataset, newPixelData: true).AddFrame(
            new MemoryByteBuffer(EncodeFrame(k)));

        return dataset;
    }

    private byte[] EncodeFrame(int k)
    {
        byte[] bytes = new byte[Rows * Columns * 2];
        int offset = 0;

        for (int j = 0; j < Rows; j++)
        {
            for (int i = 0; i < Columns; i++)
            {
                // Little endian, low byte first. Truncation to the allocated width happens
                // the same way a scanner does it; masking to BitsStored is the decoder's
                // job, not the writer's.
                ushort stored = unchecked((ushort)StoredValueAt(i, j, k));
                bytes[offset++] = (byte)(stored & 0xFF);
                bytes[offset++] = (byte)(stored >> 8);
            }
        }

        return bytes;
    }
}
