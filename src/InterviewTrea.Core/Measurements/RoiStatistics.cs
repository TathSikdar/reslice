using System;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Core.Measurements;

/// <summary>
/// The Hounsfield statistics FR-403 and FR-404 ask for, over the region a
/// <see cref="Measurement"/> encloses.
/// </summary>
/// <remarks>
/// Area is deliberately absent: it is <see cref="Measurement.AreaSquareMillimetres"/>,
/// computed in closed form from the two spans. Deriving it here from the sample count
/// instead would make the reported area depend on the sampling pitch, so a finer pitch
/// would change the size of a lesion that had not moved.
/// </remarks>
public readonly record struct RoiStatistics(
    int SampleCount,
    double MeanHounsfield,
    double StandardDeviationHounsfield,
    short MinimumHounsfield,
    short MaximumHounsfield)
{
    /// <summary>No voxels sampled: a distance tool, or a region entirely outside the volume.</summary>
    public static RoiStatistics Empty => default;

    /// <summary>
    /// Samples <paramref name="volume"/> over the measurement's region and reduces it to
    /// mean, standard deviation, minimum and maximum Hounsfield units.
    /// </summary>
    /// <param name="pitchMillimetres">
    /// Roughly how far apart to place samples in the plane. Null takes the volume's finest
    /// spacing, which cannot undersample any axis. A calibration knob rather than a derived
    /// value: it trades accuracy against the cost of a pass, and the honest default is the
    /// one that sees every voxel the region covers.
    /// </param>
    /// <remarks>
    /// <para>
    /// Nearest-neighbour, not trilinear. Interpolation invents values between voxel centres
    /// and pulls every sample towards its neighbours, which systematically deflates the
    /// standard deviation - the one statistic here whose whole purpose is to report how much
    /// the voxels differ. Mean and max would survive it; SD would quietly read low.
    /// </para>
    /// <para>
    /// Samples that fall outside the volume are skipped rather than counted as
    /// <see cref="Volume.OutsideValue"/>, the same rule the slab renderer follows. An ROI
    /// half off the edge of the data must report the tissue it does cover, not that tissue
    /// averaged with air that was never scanned.
    /// </para>
    /// <para>
    /// The region is divided into a whole number of cells along each axis and sampled at
    /// their centres, so the samples are symmetric about the region and cover its full
    /// extent. Stepping by a fixed pitch from one corner instead would drop a partial strip
    /// at the far edge and bias the mean towards the near one.
    /// </para>
    /// </remarks>
    public static RoiStatistics Compute(
        Measurement measurement,
        Volume volume,
        double? pitchMillimetres = null)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentNullException.ThrowIfNull(volume);

        if (measurement.Kind == MeasurementKind.Distance)
        {
            return Empty;
        }

        double pitch = pitchMillimetres
            ?? Math.Min(volume.Spacing.X, Math.Min(volume.Spacing.Y, volume.Spacing.Z));

        if (pitch <= 0 || double.IsNaN(pitch))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pitchMillimetres), pitch, "Sampling pitch must be positive.");
        }

        int cellsAcross = CellCount(measurement.SpanAcross, pitch);
        int cellsDown = CellCount(measurement.SpanDown, pitch);
        double stepAcross = measurement.SpanAcross / cellsAcross;
        double stepDown = measurement.SpanDown / cellsDown;

        int count = 0;
        double mean = 0;
        double sumSquaredDeviation = 0;
        short minimum = short.MaxValue;
        short maximum = short.MinValue;

        for (int row = 0; row < cellsDown; row++)
        {
            double down = (row + 0.5) * stepDown;

            for (int column = 0; column < cellsAcross; column++)
            {
                double across = (column + 0.5) * stepAcross;
                Point3D patient = measurement.Start
                    + measurement.Frame.Row.Scale(across)
                    + measurement.Frame.Column.Scale(down);

                if (!measurement.Contains(patient))
                {
                    continue;
                }

                Point3D voxel = volume.PatientToVoxel.Transform(patient);

                if (!volume.ContainsContinuous(voxel.X, voxel.Y, voxel.Z))
                {
                    continue;
                }

                short hounsfield = volume.SampleNearest(voxel.X, voxel.Y, voxel.Z);

                // Welford: mean and variance in one pass, without ever forming the sum of
                // squares. The naive form subtracts two large nearly equal numbers, and a
                // uniform ROI deep in bone is exactly the case where that loses its digits.
                count++;
                double delta = hounsfield - mean;
                mean += delta / count;
                sumSquaredDeviation += delta * (hounsfield - mean);

                minimum = Math.Min(minimum, hounsfield);
                maximum = Math.Max(maximum, hounsfield);
            }
        }

        if (count == 0)
        {
            return Empty;
        }

        // Divided by n, not n-1. The voxels inside the ROI are the entire population being
        // described, not a sample drawn from a larger one; Bessel's correction answers a
        // question nobody drawing a region of interest is asking.
        return new RoiStatistics(
            count,
            mean,
            Math.Sqrt(sumSquaredDeviation / count),
            minimum,
            maximum);
    }

    // At least one cell, so a region thinner than the pitch still reports the voxels on it
    // rather than nothing. Rounding rather than flooring keeps the cells close to the pitch.
    private static int CellCount(double span, double pitch) =>
        Math.Max(1, (int)Math.Round(Math.Abs(span) / pitch, MidpointRounding.AwayFromZero));
}
