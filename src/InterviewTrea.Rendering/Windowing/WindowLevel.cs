using System;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Rendering.Windowing;

/// <summary>
/// A window into the Hounsfield scale: which interval of tissue densities gets mapped
/// onto the 256 grey levels a display can show (FR-305).
/// </summary>
/// <remarks>
/// CT covers roughly -1000 HU (air) to +3000 (dense bone and metal), which no monitor
/// can render at once without losing every distinction that matters. Windowing picks a
/// band: <paramref name="Center"/> is the density placed at mid-grey and
/// <paramref name="Width"/> is how much of the scale spans black to white. Everything
/// below the band clips to black, everything above to white. A lung window is wide and
/// low because air and soft tissue are far apart; a brain window is narrow because grey
/// and white matter differ by only a few HU.
/// </remarks>
public readonly record struct WindowLevel(double Width, double Center)
{
    /// <summary>Air against vessels and parenchyma.</summary>
    public static WindowLevel Lung { get; } = new(1500, -600);

    /// <summary>The default for abdominal and general reading.</summary>
    public static WindowLevel SoftTissue { get; } = new(400, 40);

    /// <summary>Wide enough that cortical bone does not saturate to flat white.</summary>
    public static WindowLevel Bone { get; } = new(1800, 400);

    /// <summary>Narrow: grey and white matter are about 10 HU apart.</summary>
    public static WindowLevel Brain { get; } = new(80, 40);

    public static WindowLevel Mediastinum { get; } = new(350, 50);

    /// <summary>
    /// FR-306: the series' own WindowCenter (0028,1050) and WindowWidth (0028,1051) when
    /// it carries them, otherwise soft tissue. The scanner's own setting is what the
    /// technologist chose while acquiring, so it is a better first guess than any preset.
    /// </summary>
    public static WindowLevel InitialFor(VolumeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        // Width must be at least 1: the DICOM LINEAR transform divides by (width - 1),
        // and a scanner writing 0 would otherwise take the render loop with it.
        return metadata is { WindowWidth: >= 1, WindowCenter: not null }
            ? new WindowLevel(metadata.WindowWidth.Value, metadata.WindowCenter.Value)
            : SoftTissue;
    }
}
