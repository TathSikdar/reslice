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

        // A crosshair line is coloured by the plane it represents, not by the plane it is
        // drawn on. A vertical line holds the row axis constant, so it is where the plane
        // whose normal is that axis cuts through this one - in the axial view, a vertical
        // line is the sagittal plane. The convention holds in all three panes, and it is
        // what lets you read at a glance which view a line will move.
        (VerticalLineBrushKey, HorizontalLineBrushKey) = orientation switch
        {
            PlaneOrientation.Axial => ("Brush.Plane.Sagittal", "Brush.Plane.Coronal"),
            PlaneOrientation.Coronal => ("Brush.Plane.Sagittal", "Brush.Plane.Axial"),
            _ => ("Brush.Plane.Coronal", "Brush.Plane.Axial"),
        };

        BorderBrushKey = "Brush.Plane." + orientation;
    }

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
    public void Update(Volume volume, Point3D crosshair, double pixelSizeMillimetres, SlabMode slabMode, double slabThicknessMillimetres)
    {
        Crosshair = crosshair;
        Plane = ReslicePlane.Through(volume, Orientation, crosshair, pixelSizeMillimetres);

        ModeLabel = IsSlab
            ? string.Create(CultureInfo.InvariantCulture, $"{ModeName(slabMode)} {slabThicknessMillimetres:0.#} mm")
            : string.Empty;

        // The position is quoted on the patient axis this plane cuts, which is the one its
        // normal points along. Naming the axis matters: "z -12.4 mm" is checkable against
        // the DICOM header, where "slice 42 of 128" is not.
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
