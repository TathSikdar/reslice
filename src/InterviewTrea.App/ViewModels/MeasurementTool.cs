using InterviewTrea.Core.Measurements;

namespace InterviewTrea.App.ViewModels;

/// <summary>
/// What the left button draws (FR-401, FR-403, FR-404), or <see cref="None"/> for the
/// navigation gestures.
/// </summary>
/// <remarks>
/// Not <see cref="MeasurementKind"/> with a null for off. The tool is a piece of view
/// state - which of four entries the dropdown is showing - and giving it a fourth named
/// entry keeps the domain enum meaning exactly what it says: the three shapes a
/// measurement can be. A nullable selection would also have to render an empty row.
/// </remarks>
public enum MeasurementTool
{
    None,
    Distance,
    Ellipse,
    Rectangle,
}

internal static class MeasurementToolExtensions
{
    public static MeasurementKind ToKind(this MeasurementTool tool) => tool switch
    {
        MeasurementTool.Ellipse => MeasurementKind.Ellipse,
        MeasurementTool.Rectangle => MeasurementKind.Rectangle,
        _ => MeasurementKind.Distance,
    };
}
