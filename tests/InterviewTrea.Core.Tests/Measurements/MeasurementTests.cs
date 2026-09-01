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
/// The measurement domain (FR-401 to FR-404, FR-406). Every expected value below is
/// derived from the geometry by hand; a measurement that is wrong by a scale factor still
/// draws exactly the same outline on screen, so the number is the only thing under test.
/// </summary>
public sealed class MeasurementTests
{
    private const double PixelSize = 0.7;

    private static Volume Chest() => Phantoms.Uniform(
        Phantoms.SoftTissue, dimX: 64, dimY: 64, dimZ: 32, spacing: Phantoms.ChestSpacing);

    private static MeasurementFrame FrameOf(PlaneOrientation orientation, Point3D? anchor = null)
    {
        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(orientation);
        return new MeasurementFrame(anchor ?? Point3D.Origin, row, column);
    }

    private static Measurement Rect(double across, double down, PlaneOrientation orientation = PlaneOrientation.Axial)
    {
        MeasurementFrame frame = FrameOf(orientation);
        return new Measurement(
            MeasurementKind.Rectangle,
            frame,
            Point3D.Origin,
            Point3D.Origin + frame.Row.Scale(across) + frame.Column.Scale(down));
    }

    // ---- FR-401, FR-402: distance ----

    /// <summary>
    /// 3-4-12 is a Pythagorean quadruple: 9 + 16 + 144 = 169, so the distance is exactly
    /// 13 mm. Chosen over an axis-aligned pair because a length computed on one axis and
    /// broadcast to the others would still pass that.
    /// </summary>
    [Fact]
    public void DistanceIsTheStraightLineThroughPatientSpace()
    {
        Measurement measurement = new(
            MeasurementKind.Distance, FrameOf(PlaneOrientation.Axial),
            new Point3D(1, 2, 3), new Point3D(4, 6, 15));

        measurement.LengthMillimetres.Should().BeApproximately(13.0, 1e-12);
    }

    /// <summary>
    /// FR-402. The same two anatomical points measured on an oblique frame must give the
    /// same number, because the frame describes how the drag was drawn and not where the
    /// points are. This is the property a pixel-space implementation fails.
    /// </summary>
    [Fact]
    public void DistanceDoesNotDependOnTheFrameItWasDrawnOn()
    {
        Point3D start = new(-11, 5, 2);
        Point3D end = new(-8, 9, 14);

        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(PlaneOrientation.Coronal);
        MeasurementFrame oblique = new(
            start,
            row.RotatedAbout(Vector3D.UnitY, 0.6),
            column.RotatedAbout(Vector3D.UnitY, 0.6));

        Measurement onStandard = new(MeasurementKind.Distance, FrameOf(PlaneOrientation.Coronal), start, end);
        Measurement onOblique = new(MeasurementKind.Distance, oblique, start, end);

        onOblique.LengthMillimetres.Should().BeApproximately(onStandard.LengthMillimetres, 1e-12);
        onOblique.LengthMillimetres.Should().BeApproximately(13.0, 1e-12);
    }

    // ---- FR-403, FR-404: area ----

    [Fact]
    public void ARectangleEnclosesTheProductOfItsSpans() =>
        Rect(20, 30).AreaSquareMillimetres.Should().BeApproximately(600.0, 1e-12);

    /// <summary>
    /// The ellipse is inscribed in the dragged box, so its semi-axes are 10 and 15 and its
    /// area is pi * 10 * 15 = 471.24 mm^2 - pi/4 of the rectangle, not pi times the spans.
    /// That factor of four is the error that survives visual inspection, because both
    /// versions draw the identical outline.
    /// </summary>
    [Fact]
    public void AnEllipseEnclosesPiOverFourOfItsBoundingBox() =>
        (Rect(20, 30) with { Kind = MeasurementKind.Ellipse })
            .AreaSquareMillimetres.Should().BeApproximately(Math.PI * 10 * 15, 1e-12);

    [Fact]
    public void AreaIsUnsignedSoADragUpAndLeftMeasuresTheSame() =>
        Rect(-20, -30).AreaSquareMillimetres.Should().BeApproximately(600.0, 1e-12);

    [Fact]
    public void ADistanceEnclosesNothing() =>
        new Measurement(
            MeasurementKind.Distance, FrameOf(PlaneOrientation.Axial),
            Point3D.Origin, new Point3D(10, 10, 0))
            .AreaSquareMillimetres.Should().Be(0);

    // ---- Containment, which is what the statistics pass walks ----

    /// <summary>
    /// The four corners of the bounding box are the points that separate an ellipse from
    /// the rectangle around it. Inside the rectangle, outside the ellipse, both times.
    /// </summary>
    [Fact]
    public void TheBoxCornerIsInsideTheRectangleAndOutsideTheEllipse()
    {
        Measurement rectangle = Rect(20, 30);
        Measurement ellipse = rectangle with { Kind = MeasurementKind.Ellipse };
        Point3D corner = rectangle.End;

        rectangle.Contains(corner).Should().BeTrue();
        ellipse.Contains(corner).Should().BeFalse();
    }

    [Fact]
    public void TheCentreIsInsideBothShapes()
    {
        Measurement rectangle = Rect(20, 30);
        Point3D centre = new(10, 15, 0);

        rectangle.Contains(centre).Should().BeTrue();
        (rectangle with { Kind = MeasurementKind.Ellipse }).Contains(centre).Should().BeTrue();
    }

    /// <summary>
    /// The end of a semi-axis is the one place the ellipse touches its box, so a point
    /// just inside it is in and just outside is out. This is what catches a containment
    /// test written against the full spans rather than the half spans - it would call the
    /// whole box inside and the statistics would silently be the rectangle's.
    /// </summary>
    [Theory]
    [InlineData(19.99, true)]
    [InlineData(20.01, false)]
    public void TheEllipseBoundaryIsAtTheEndOfTheSemiAxis(double across, bool inside) =>
        (Rect(20, 30) with { Kind = MeasurementKind.Ellipse })
            .Contains(new Point3D(across, 15, 0)).Should().Be(inside);

    [Fact]
    public void ADragUpAndLeftStillContainsItsOwnMidpoint() =>
        Rect(-20, -30).Contains(new Point3D(-10, -15, 0)).Should().BeTrue();

    // ---- FR-406: when a measurement belongs on the plane in front of you ----

    [Fact]
    public void AMeasurementIsVisibleOnThePlaneItWasDrawnOn()
    {
        ReslicePlane plane = ReslicePlane.Through(
            Chest(), PlaneOrientation.Axial, Point3D.Origin, PixelSize);

        Rect(20, 30).IsVisibleOn(plane, toleranceMillimetres: 1.5).Should().BeTrue();
    }

    [Fact]
    public void AMeasurementFurtherAwayThanTheToleranceIsHidden()
    {
        // The axial plane through z = 5 mm, with the measurement anchored at z = 0.
        ReslicePlane plane = ReslicePlane.Through(
            Chest(), PlaneOrientation.Axial, new Point3D(0, 0, 5), PixelSize);

        Rect(20, 30).IsVisibleOn(plane, toleranceMillimetres: 1.5).Should().BeFalse();
        Rect(20, 30).IsVisibleOn(plane, toleranceMillimetres: 6.0).Should().BeTrue();
    }

    /// <summary>
    /// The case distance alone cannot catch. Every measurement sits on the crosshair and
    /// the crosshair is on all three planes at once, so an axial measurement is exactly
    /// zero millimetres from the sagittal plane - and would be drawn there, edge-on and
    /// meaningless, if parallelism were not also required.
    /// </summary>
    [Fact]
    public void AnAxialMeasurementDoesNotAppearInTheSagittalPaneDespiteTouchingIt()
    {
        ReslicePlane sagittal = ReslicePlane.Through(
            Chest(), PlaneOrientation.Sagittal, Point3D.Origin, PixelSize);

        sagittal.SignedDistanceTo(Point3D.Origin).Should().BeApproximately(0, 1e-12);
        Rect(20, 30).IsVisibleOn(sagittal, toleranceMillimetres: 1.5).Should().BeFalse();
    }

    /// <summary>
    /// A plane and its reverse are the same plane, so the sign of the normal must not
    /// decide whether a measurement is drawn. The sagittal display normal is deliberately
    /// negative, which makes this an easy sign to get wrong somewhere downstream.
    /// </summary>
    [Fact]
    public void AFrameFacingTheOppositeWayIsStillTheSamePlane()
    {
        ReslicePlane plane = ReslicePlane.Through(
            Chest(), PlaneOrientation.Axial, Point3D.Origin, PixelSize);

        // Swapping the axes reverses the normal and leaves the plane exactly where it was.
        Measurement flipped = Rect(20, 30) with
        {
            Frame = new MeasurementFrame(Point3D.Origin, Vector3D.UnitY, Vector3D.UnitX),
        };

        flipped.Frame.Normal.Dot(plane.Normal).Should().BeApproximately(-1, 1e-12);
        flipped.IsVisibleOn(plane, toleranceMillimetres: 1.5).Should().BeTrue();
    }

    /// <summary>
    /// A rotation small enough to be a slip of the hand still has to hide the measurement,
    /// or an oblique drag would leave old measurements floating over a plane they were
    /// never drawn on.
    /// </summary>
    [Fact]
    public void ARotatedPlaneNoLongerCarriesTheMeasurement()
    {
        Volume volume = Chest();
        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(PlaneOrientation.Axial);

        ReslicePlane tilted = ReslicePlane.Through(
            volume,
            (row.RotatedAbout(Vector3D.UnitX, 0.05), column.RotatedAbout(Vector3D.UnitX, 0.05)),
            Point3D.Origin,
            PixelSize);

        Rect(20, 30).IsVisibleOn(tilted, toleranceMillimetres: 1.5).Should().BeFalse();
    }
}
