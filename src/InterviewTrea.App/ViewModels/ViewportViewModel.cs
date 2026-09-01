using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Reslicing;

namespace InterviewTrea.App.ViewModels;

/// <summary>
/// One of the four panes (FR-201). Holds the plane it is currently showing and the text
/// that goes round the edge of it; the control that owns it does the drawing.
/// </summary>
public sealed partial class ViewportViewModel : ObservableObject
{
    public ViewportViewModel(PlaneOrientation orientation, bool isSlab)
    {
        Orientation = orientation;
        IsSlab = isSlab;

        (Vector3D row, Vector3D column) = ReslicePlane.DisplayAxes(orientation);

        // FR-204. The marker on an edge names the anatomical direction you would travel
        // by walking out through it, so the left edge is the negative row direction.
        LeftMarker = AnatomicalDirection.Of(row.Negate());
        RightMarker = AnatomicalDirection.Of(row);
        TopMarker = AnatomicalDirection.Of(column.Negate());
        BottomMarker = AnatomicalDirection.Of(column);

        // A crosshair line is the plane it represents, not a feature of the plane it is
        // drawn on: it is where another plane cuts through this one, and it is coloured for
        // that other plane so you can read at a glance which view dragging it will move.
        // Named for where the line starts out - in the axial view, the sagittal plane cuts
        // it along a vertical line - though FR-307 lets both tilt away from those names.
        (VerticalLinePlane, HorizontalLinePlane) = orientation switch
        {
            PlaneOrientation.Axial => (PlaneOrientation.Sagittal, PlaneOrientation.Coronal),
            PlaneOrientation.Coronal => (PlaneOrientation.Sagittal, PlaneOrientation.Axial),
            _ => (PlaneOrientation.Coronal, PlaneOrientation.Axial),
        };

        VerticalLineBrushKey = "Brush.Plane." + VerticalLinePlane;
        HorizontalLineBrushKey = "Brush.Plane." + HorizontalLinePlane;
        BorderBrushKey = "Brush.Plane." + orientation;
    }

    /// <summary>The plane whose intersection with this one draws the first crosshair line.</summary>
    public PlaneOrientation VerticalLinePlane { get; }

    /// <summary>The plane whose intersection with this one draws the second crosshair line.</summary>
    public PlaneOrientation HorizontalLinePlane { get; }

    public PlaneOrientation Orientation { get; }

    /// <summary>Whether this pane projects a slab (FR-207) rather than a single plane.</summary>
    public bool IsSlab { get; }

    public string LeftMarker { get; }

    public string RightMarker { get; }

    public string TopMarker { get; }

    public string BottomMarker { get; }

    public string VerticalLineBrushKey { get; }

    public string HorizontalLineBrushKey { get; }

    /// <summary>Names the pane by its own plane colour, so a maximized view is still identifiable.</summary>
    public string BorderBrushKey { get; }

    [ObservableProperty]
    private ReslicePlane? plane;

    /// <summary>Where the crosshair falls in this pane, in output pixels. Null when off it.</summary>
    [ObservableProperty]
    private Point3D crosshair;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string modeLabel = string.Empty;

    /// <summary>FR-205: the slice position, along whichever patient axis this plane cuts.</summary>
    [ObservableProperty]
    private string positionLabel = string.Empty;

    public string Title => IsSlab
        ? Orientation + " " + ModeLabel
        : Orientation.ToString();

    /// <summary>
    /// Recomputes the plane and its readouts for a new crosshair. Called for every pane on
    /// every crosshair change, which is what makes the views linked (FR-304).
    /// </summary>
    public void Update(Volume volume, (Vector3D Row, Vector3D Column) axes, Point3D crosshair, double pixelSizeMillimetres, SlabMode slabMode, double slabThicknessMillimetres)
    {
        Crosshair = crosshair;

        // The axes arrive rather than being looked up from the orientation, because after
        // an FR-307 rotation this pane's plane is no longer the standard one its
        // orientation names. The orientation stays as the pane's identity - which of the
        // three it is, what colour it wears - and stops being a claim about its geometry.
        Plane = ReslicePlane.Through(volume, axes, crosshair, pixelSizeMillimetres);

        ModeLabel = IsSlab
            ? string.Create(CultureInfo.InvariantCulture, $"{ModeName(slabMode)} {slabThicknessMillimetres:0.#} mm")
            : string.Empty;

        // The position is quoted on the patient axis this plane cuts, which is the one its
        // normal points along. Naming the axis matters: "z -12.4 mm" is checkable against
        // the DICOM header, where "slice 42 of 128" is not. On an oblique plane the normal
        // is between two axes and this names whichever it leans towards, so the label flips
        // as a rotation passes 45 degrees. That is honest but coarse; a true oblique
        // readout would be a distance along the normal from a named origin, and it is not
        // built because FR-205 does not ask for one.
        Vector3D normal = Plane.Normal;
        (string axis, double value) = AnatomicalDirection.Of(normal) switch
        {
            "L" or "R" => ("x", crosshair.X),
            "A" or "P" => ("y", crosshair.Y),
            _ => ("z", crosshair.Z),
        };

        PositionLabel = string.Create(CultureInfo.InvariantCulture, $"{axis} {value,7:0.0} mm");
    }

    public void Clear()
    {
        Plane = null;
        PositionLabel = string.Empty;
        ModeLabel = string.Empty;
    }

    private static string ModeName(SlabMode mode) => mode switch
    {
        SlabMode.Maximum => "MIP",
        SlabMode.Minimum => "MinIP",
        _ => "Average",
    };
}
