using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;
using Xunit;

namespace InterviewTrea.Core.Tests.Reslicing;

/// <summary>
/// The edge markers (FR-204). Cheap to test and worth testing: left and right look
/// identical on a chest, so a swapped letter is invisible on screen and is exactly the
/// mistake the markers exist to prevent.
/// </summary>
public sealed class AnatomicalDirectionTests
{
    [Theory]
    [InlineData(1, 0, 0, "L")]
    [InlineData(-1, 0, 0, "R")]
    [InlineData(0, 1, 0, "P")]
    [InlineData(0, -1, 0, "A")]
    [InlineData(0, 0, 1, "S")]
    [InlineData(0, 0, -1, "I")]
    public void EachPatientAxisGetsItsLetter(double x, double y, double z, string expected) =>
        AnatomicalDirection.Of(new Vector3D(x, y, z)).Should().Be(expected);

    /// <summary>
    /// An oblique direction is named by its dominant component. 40 degrees off the z axis
    /// towards posterior is still mostly superior.
    /// </summary>
    [Fact]
    public void AnObliqueDirectionTakesItsDominantAxis() =>
        AnatomicalDirection.Of(new Vector3D(0, 0.643, 0.766)).Should().Be("S");

    /// <summary>
    /// The markers on a standard plane are the four directions pointing out of its edges,
    /// which is the assertion that would catch a mirrored viewport.
    /// </summary>
    [Theory]
    [InlineData(PlaneOrientation.Axial, "R", "L", "A", "P")]
    [InlineData(PlaneOrientation.Coronal, "R", "L", "S", "I")]
    [InlineData(PlaneOrientation.Sagittal, "A", "P", "S", "I")]
    public void TheStandardPlanesCarryTheExpectedMarkers(
        PlaneOrientation orientation, string left, string right, string top, string bottom)
    {
        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(orientation);

        AnatomicalDirection.Of(row.Negate()).Should().Be(left);
        AnatomicalDirection.Of(row).Should().Be(right);
        AnatomicalDirection.Of(column.Negate()).Should().Be(top);
        AnatomicalDirection.Of(column).Should().Be(bottom);
    }
}
