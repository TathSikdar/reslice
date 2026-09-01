using System;
using FellowOakDicom;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Dicom;

/// <summary>
/// A reconstructed volume and what was learned while reconstructing it.
/// </summary>
/// <param name="SaturatedSampleCount">
/// Samples clamped at the <see cref="short"/> bounds during rescale. Non-zero is not
/// necessarily wrong - dense metal really does exceed the range - but it is worth
/// reporting rather than hiding.
/// </param>
public sealed record VolumeBuildResult(
    Volume Volume,
    short MinimumHounsfield,
    short MaximumHounsfield,
    long SaturatedSampleCount);

/// <summary>
/// Assembles a validated series into a <see cref="Volume"/>: the voxel-to-patient affine
/// from the geometry, and the Hounsfield units from the pixel data (FR-103, FR-104).
/// </summary>
public sealed class VolumeBuilder
{
    /// <param name="progress">
    /// Fraction of slices decoded, 0 to 1 (FR-108). Optional: the console probe has no
    /// use for it.
    /// </param>
    // CA1822: instance rather than static because the builder is a registered service and
    // will take an ILogger in a later iteration.
#pragma warning disable CA1822
    public VolumeBuildResult Build(
        SeriesDescriptor series,
        SeriesGeometry geometry,
        IProgress<double>? progress = null)
#pragma warning restore CA1822
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(geometry);

        SliceHeader first = geometry.OrderedSlices[0];
        int dimX = first.Columns;
        int dimY = first.Rows;
        int dimZ = geometry.OrderedSlices.Count;

        // Allocated once, up front, from the header dimensions. Decoding into per-slice
        // arrays and copying afterwards would peak at twice the volume plus the raw byte
        // buffers, which is the whole of the NFR-102 budget spent on bookkeeping.
        short[] voxels = new short[(long)dimX * dimY * dimZ];

        short minimum = short.MaxValue;
        short maximum = short.MinValue;
        long saturated = 0;
        int sliceStride = dimX * dimY;

        for (int k = 0; k < dimZ; k++)
        {
            SliceHeader header = geometry.OrderedSlices[k];
            progress?.Report((double)k / dimZ);

            // Reopened with pixel data this time; the scan deliberately skipped it. Reading
            // each file twice costs one extra header parse and saves holding a study in
            // memory to answer a question about its geometry.
            DicomDataset dataset = DicomFile.Open(header.FilePath, FileReadOption.ReadLargeOnDemand).Dataset;

            DecodeStatistics statistics = PixelDecoder.DecodeInto(
                dataset,
                voxels.AsSpan(k * sliceStride, sliceStride));

            minimum = Math.Min(minimum, statistics.Minimum);
            maximum = Math.Max(maximum, statistics.Maximum);
            saturated += statistics.Saturated;
        }

        Matrix4x4Affine voxelToPatient = Matrix4x4Affine.FromImagePlane(
            rowCosine: first.RowCosine,
            columnCosine: first.ColumnCosine,

            // The crossover, once more: PixelSpacing[0] is the gap between rows and so
            // scales the column cosine, [1] is the gap between columns and scales the row
            // cosine. The parameter names carry the meaning so the call site cannot drift.
            adjacentRowSpacing: first.AdjacentRowSpacing,
            adjacentColumnSpacing: first.AdjacentColumnSpacing,

            // Measured from the slice positions, not assumed to be spacing * normal.
            // Validation has already established that the two agree to within the tilt
            // tolerance, so this is the more honest of two equal answers - and it stays
            // correct if that tolerance is ever relaxed.
            sliceStep: geometry.SliceStep,
            origin: first.Position);

        Volume volume = new(voxels, dimX, dimY, dimZ, voxelToPatient, series.Metadata);

        return new VolumeBuildResult(volume, minimum, maximum, saturated);
    }
}
