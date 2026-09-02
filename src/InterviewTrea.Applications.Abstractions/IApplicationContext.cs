using System;
using System.Collections.Generic;
using InterviewTrea.Core.Measurements;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Applications.Abstractions;

/// <summary>
/// FR-504, FR-505. One running instance of an application.
/// </summary>
/// <remarks>
/// Separate from <see cref="IClinicalApplication"/> so the application itself can be a
/// singleton in the container while its per-study state is created and disposed with the
/// study. Closing the session is what releases whatever it built.
/// </remarks>
public interface IApplicationSession : IDisposable
{
    /// <summary>
    /// FR-504. The view model for the right-hand tool panel, bound by the shell through a
    /// data template.
    /// </summary>
    /// <remarks>
    /// Typed as <see cref="object"/> deliberately. A plugin brings its own view model type
    /// and the shell finds a template for it by type; naming a base class here would force
    /// every application to inherit from something in this assembly for no gain.
    /// </remarks>
    object ToolPanelViewModel { get; }

    /// <summary>FR-505. What this session draws over the viewports, if anything.</summary>
    IReadOnlyList<IOverlayLayer> OverlayLayers { get; }

    /// <summary>Raised when <see cref="OverlayLayers"/> has changed and needs redrawing.</summary>
    event EventHandler? OverlaysChanged;
}

/// <summary>
/// FR-503. What the viewer lends an application: the study, where it is currently looking,
/// and what has been measured.
/// </summary>
/// <remarks>
/// Read access throughout. An application can compute anything it likes from the volume,
/// but it cannot move the crosshair, change the window, or add a measurement - the user
/// drives the viewer, and a plugin that could take the view somewhere unasked is a plugin
/// that can make the viewer look broken.
/// </remarks>
public interface IApplicationContext
{
    /// <summary>The loaded study, in Hounsfield units.</summary>
    Volume Volume { get; }

    /// <summary>
    /// The plane of the active viewport (FR-409's notion of active): where the user is
    /// currently looking.
    /// </summary>
    ReslicePlane CurrentPlane { get; }

    /// <summary>What the user has measured, read-only (FR-401 to FR-404).</summary>
    IMeasurementStore Measurements { get; }

    /// <summary>Raised when <see cref="CurrentPlane"/> has moved.</summary>
    event EventHandler? PlaneChanged;
}
