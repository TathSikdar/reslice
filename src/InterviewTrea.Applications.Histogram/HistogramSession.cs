using System;
using System.Collections.Generic;
using System.Globalization;
using InterviewTrea.Applications.Abstractions;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Applications.Histogram;

/// <summary>
/// FR-504, FR-505. One run of the histogram application against one study.
/// </summary>
/// <remarks>
/// The histogram is computed once, here, rather than on every redraw: it is a pass over
/// every voxel in the volume, and nothing about it changes while the study is open. What
/// does change is which bar is selected, and that only moves a label.
/// </remarks>
internal sealed class HistogramSession : IApplicationSession
{
    private readonly HistogramOverlay overlay;
    private readonly HistogramPanelViewModel panel;

    public HistogramSession(IApplicationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Volume volume = context.Volume;
        VolumeHistogram histogram = VolumeHistogram.Compute(volume);

        panel = new HistogramPanelViewModel(
            histogram,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{volume.DimX} x {volume.DimY} x {volume.DimZ} voxels, {histogram.Total:N0} total"));

        overlay = new HistogramOverlay(volume, panel);
        OverlayLayers = [overlay];

        panel.SelectionChanged += OnSelectionChanged;
    }

    public object ToolPanelViewModel => panel;

    public IReadOnlyList<IOverlayLayer> OverlayLayers { get; }

    public event EventHandler? OverlaysChanged;

    public void Dispose() => panel.SelectionChanged -= OnSelectionChanged;

    private void OnSelectionChanged(object? sender, EventArgs e) =>
        OverlaysChanged?.Invoke(this, EventArgs.Empty);
}
