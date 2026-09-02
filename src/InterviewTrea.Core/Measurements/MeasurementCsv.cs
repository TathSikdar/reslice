using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Core.Measurements;

/// <summary>
/// FR-408. Turns the measurement list into a CSV document.
/// </summary>
/// <remarks>
/// <para>
/// Returns a string rather than writing a file: Core does no IO, and a string is what a
/// test can assert on without a temporary directory. The App layer chooses the path.
/// </para>
/// <para>
/// Every geometric column is in patient millimetres, including the frame's normal. That
/// normal is what makes a row reproducible - it says which plane the measurement was made
/// on, which for an oblique measurement is not recoverable from anything else in the file
/// and is exactly the information someone re-checking the number needs.
/// </para>
/// </remarks>
public static class MeasurementCsv
{
    /// <summary>
    /// The disclaimer, as a leading comment line. It costs one row that a naive parser
    /// will treat as data, and it buys a file that still says what it is after it has been
    /// mailed to someone who never saw the application. RQ-1 asks only for the banner and
    /// FR-409 only for the PNG, so this is a deliberate addition rather than a requirement.
    /// </summary>
    public const string Disclaimer =
        "# RESEARCH AND DEMONSTRATION USE ONLY - NOT A MEDICAL DEVICE. NOT FOR DIAGNOSTIC USE.";

    public const string Header =
        "id,kind,start_x_mm,start_y_mm,start_z_mm,end_x_mm,end_y_mm,end_z_mm," +
        "normal_x,normal_y,normal_z,length_mm,area_mm2,voxels,mean_hu,sd_hu,min_hu,max_hu";

    /// <summary>
    /// Writes every measurement as one row. A distance leaves the region columns empty
    /// rather than filling them with zeros, which would be five plausible numbers claiming
    /// that a line encloses an area of air.
    /// </summary>
    public static string Write(IEnumerable<Measurement> measurements, Volume volume)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(volume);

        StringBuilder csv = new();
        csv.AppendLine(Disclaimer);
        csv.AppendLine(Header);

        foreach (Measurement measurement in measurements)
        {
            csv.Append(measurement.Id.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(measurement.Kind).Append(',');
            Append(csv, measurement.Start);
            Append(csv, measurement.End);
            Append(csv, measurement.Frame.Normal);
            csv.Append(Number(measurement.LengthMillimetres)).Append(',');

            if (measurement.Kind == MeasurementKind.Distance)
            {
                csv.AppendLine(",,,,,");
                continue;
            }

            RoiStatistics statistics = RoiStatistics.Compute(measurement, volume);

            csv.Append(Number(measurement.AreaSquareMillimetres)).Append(',');
            csv.Append(statistics.SampleCount.ToString(CultureInfo.InvariantCulture)).Append(',');

            // A region can sit entirely off the end of the data. Its area is still true, so
            // it is still written; its statistics do not exist and are left blank.
            if (statistics.SampleCount == 0)
            {
                csv.AppendLine(",,,");
                continue;
            }

            csv.Append(Number(statistics.MeanHounsfield)).Append(',');
            csv.Append(Number(statistics.StandardDeviationHounsfield)).Append(',');
            csv.Append(statistics.MinimumHounsfield.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.AppendLine(statistics.MaximumHounsfield.ToString(CultureInfo.InvariantCulture));
        }

        return csv.ToString();
    }

    private static void Append(StringBuilder csv, Point3D point) =>
        csv.Append(Number(point.X)).Append(',')
            .Append(Number(point.Y)).Append(',')
            .Append(Number(point.Z)).Append(',');

    private static void Append(StringBuilder csv, Vector3D vector) =>
        csv.Append(Number(vector.X)).Append(',')
            .Append(Number(vector.Y)).Append(',')
            .Append(Number(vector.Z)).Append(',');

    /// <summary>
    /// Three decimals, invariant culture. Three because a micron is already past what any
    /// CT grid supports, and invariant because a comma decimal separator would split a
    /// number across two columns on a machine set to a European locale - a defect that is
    /// invisible to whoever exported the file and total for whoever opens it.
    /// </summary>
    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
