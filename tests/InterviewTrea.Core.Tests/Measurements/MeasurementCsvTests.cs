using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Measurements;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;
using InterviewTrea.TestData;
using Xunit;

namespace InterviewTrea.Core.Tests.Measurements;

/// <summary>
/// FR-408. The export is where a measurement stops being a shape on screen and becomes a
/// number someone else reads, so the columns are checked by position and the values by
/// hand - a row that is one column out is still a valid CSV file.
/// </summary>
public sealed class MeasurementCsvTests
{
    private static Volume Uniform() => Phantoms.Uniform(
        Phantoms.SoftTissue, dimX: 64, dimY: 64, dimZ: 32, spacing: Phantoms.IsotropicSpacing);

    private static Measurement On(MeasurementKind kind, Point3D start, Point3D end)
    {
        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(PlaneOrientation.Axial);
        return new Measurement(kind, new MeasurementFrame(start, row, column), start, end);
    }

    private static string[] RowsOf(string csv) =>
        csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private static string[] Fields(string row) => row.Split(',');

    [Fact]
    public void TheFileLeadsWithTheDisclaimerAndTheHeader()
    {
        string[] rows = RowsOf(MeasurementCsv.Write([], Uniform()));

        rows.Should().HaveCount(2);
        rows[0].Should().StartWith("#").And.Contain("NOT FOR DIAGNOSTIC USE");
        rows[1].Should().Be(MeasurementCsv.Header);
    }

    /// <summary>
    /// Every row has to carry as many fields as the header names them, whichever branch
    /// wrote it. A short row is the defect this format invites: the region columns are
    /// filled by one branch and skipped by another, and a spreadsheet will open a ragged
    /// file without complaining and silently shift the last column left.
    /// </summary>
    [Fact]
    public void EveryRowHasAsManyFieldsAsTheHeader()
    {
        Volume volume = Uniform();

        string csv = MeasurementCsv.Write(
            [
                On(MeasurementKind.Distance, Point3D.Origin, new Point3D(3, 4, 12)),
                On(MeasurementKind.Rectangle, Point3D.Origin, new Point3D(10, 4, 0)),
                On(MeasurementKind.Ellipse, Point3D.Origin, new Point3D(10, 4, 0)),

                // Entirely off the end of the data: 64 isotropic voxels centred on the
                // origin reach to +/- 31.5 mm.
                On(MeasurementKind.Rectangle, new Point3D(100, 100, 0), new Point3D(110, 104, 0)),
            ],
            volume);

        int expected = Fields(MeasurementCsv.Header).Length;

        foreach (string row in RowsOf(csv).Skip(2))
        {
            Fields(row).Should().HaveCount(expected, "row was: {0}", row);
        }
    }

    /// <summary>
    /// 3-4-12 is 13 mm exactly, and a line encloses nothing - so the five region columns
    /// are empty rather than zero. Writing zeros there would be five plausible numbers
    /// claiming a line has an area and a mean Hounsfield value of air.
    /// </summary>
    [Fact]
    public void ADistanceCarriesItsLengthAndNoRegionColumns()
    {
        string row = RowsOf(MeasurementCsv.Write(
            [On(MeasurementKind.Distance, Point3D.Origin, new Point3D(3, 4, 12))],
            Uniform()))[2];

        string[] fields = Fields(row);

        fields[0].Should().Be("Distance");
        fields[4..7].Should().Equal("3", "4", "12");

        // The axial display normal, which is what says the plane this was drawn on.
        fields[7..10].Should().Equal("0", "0", "1");

        fields[10].Should().Be("13");
        fields[11..].Should().AllBe(string.Empty);
    }

    /// <summary>
    /// A 10 x 4 mm rectangle on uniform soft tissue: 40 mm^2, 40 isotropic voxels at 1 mm
    /// pitch, and every one of them 40 HU - so the mean is 40, the spread is exactly zero
    /// and both extremes are 40. Each of those five numbers is derived from the phantom
    /// rather than read back from a previous run.
    /// </summary>
    [Fact]
    public void ARectangleCarriesItsAreaAndItsStatistics()
    {
        string row = RowsOf(MeasurementCsv.Write(
            [On(MeasurementKind.Rectangle, Point3D.Origin, new Point3D(10, 4, 0))],
            Uniform()))[2];

        string[] fields = Fields(row);

        fields[0].Should().Be("Rectangle");
        // sqrt(116) = 10.7703..., to three decimals.
        fields[10].Should().Be("10.77");
        fields[11].Should().Be("40");
        fields[12].Should().Be("40");
        fields[13].Should().Be("40");
        fields[14].Should().Be("0");
        fields[15].Should().Be("40");
        fields[16].Should().Be("40");
    }

    /// <summary>
    /// The one formatting decision that can quietly corrupt the file. On a machine whose
    /// locale writes 1,5 for one and a half, a culture-sensitive format splits a single
    /// value across two columns - invisible to whoever exported it, total for whoever
    /// opens it.
    /// </summary>
    [Fact]
    public void NumbersUseTheInvariantDecimalPointWhateverTheMachineDoes()
    {
        string row = string.Empty;

        // On its own thread rather than by assigning CurrentCulture here: the test runner
        // shares threads between tests, and a culture left behind on one would surface as
        // an unrelated failure somewhere else in the suite.
        Thread worker = new(() =>
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            row = RowsOf(MeasurementCsv.Write(
                [On(MeasurementKind.Distance, Point3D.Origin, new Point3D(1.5, 0, 0))],
                Uniform()))[2];
        });

        worker.Start();
        worker.Join();

        Fields(row)[4].Should().Be("1.5");
    }
}
