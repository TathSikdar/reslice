using System;
using System.Collections.Generic;
using InterviewTrea.Applications.Abstractions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Applications.Histogram;

/// <summary>
/// FR-505. Marks the centre of the volume and names the selected Hounsfield band.
/// </summary>
/// <remarks>
/// <para>
/// What it draws is deliberately modest, and it is worth saying why rather than dressing
/// it up: a histogram is a statistic over the whole volume, so there is nothing about it
/// that belongs at any particular place in the patient. The marker sits at the centre of
/// the volume because that is the one point the statistic can honestly be attached to, and
/// the label beside it says which band is selected.
/// </para>
/// <para>
/// The pipeline it exercises is the real subject. The same layer is asked separately for
/// each of the three planes, answers in patient millimetres, and the shell maps that to
/// pixels - so the marker appears in axial, coronal and sagittal at once, follows zoom and
/// pan, and disappears from a pane the moment that pane's plane is scrolled away from it.
/// None of which this file knows anything about.
/// </para>
/// </remarks>
internal sealed class HistogramOverlay : IOverlayLayer
{
    /// <summary>Half-width of the marker square, in millimetres.</summary>
    private const double MarkerHalfWidthMm = 6.0;

    /// <summary>
    /// How close the plane has to pass to the marker for it to be drawn, in millimetres.
    /// The same idea as FR-406's tolerance on a measurement: a point has no thickness, so
    /// something has to say how near counts as on it.
    /// </summary>
    private const double VisibleWithinMm = 2.0;

    private readonly Point3D centre;
    private readonly HistogramPanelViewModel panel;

    public HistogramOverlay(Volume volume, HistogramPanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(volume);

        this.panel = panel;

        centre = volume.VoxelToPatient.Transform(
            (volume.DimX - 1) / 2.0, (volume.DimY - 1) / 2.0, (volume.DimZ - 1) / 2.0);
    }

    public string Id => "interviewtrea.histogram.selection";

    /// <summary>Nothing is drawn until the user picks a band.</summary>
    public bool IsVisible => panel.Selected is not null;

    public IReadOnlyList<OverlayShape> ShapesOn(ReslicePlane plane)
    {
        ArgumentNullException.ThrowIfNull(plane);

        if (panel.Selected is not HistogramBar bar ||
            Math.Abs(plane.SignedDistanceTo(centre)) > VisibleWithinMm)
        {
            return [];
        }

        // Built from the plane's own axes rather than from patient x and y, so the square
        // is square on screen in every pane, oblique ones included.
        Vector3D across = plane.RowStep.Normalized().Scale(MarkerHalfWidthMm);
        Vector3D down = plane.ColumnStep.Normalized().Scale(MarkerHalfWidthMm);

        return
        [
            new OverlayShape
            {
                Kind = OverlayShapeKind.Polyline,
                IsClosed = true,
                Points =
                [
                    centre - across - down,
                    centre + across - down,
                    centre + across + down,
                    centre - across + down,
                ],
            },
            new OverlayShape
            {
                Kind = OverlayShapeKind.Text,
                Points = [centre + across - down],
                Text = bar.Label,
            },
        ];
    }
}
