using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Applications.Histogram;

/// <summary>One bar of the chart: a Hounsfield band and how much of the volume is in it.</summary>
public sealed record HistogramBar(int Low, int High, int Count, double Share, double OfPeak)
{
    /// <summary>What the panel writes beside the selected bar.</summary>
    public string Label => string.Create(
        CultureInfo.InvariantCulture, $"{Low} .. {High} HU   {Count} voxels   {Share * 100:0.00}%");
}

/// <summary>
/// FR-504. The view model the shell docks on the right.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled <see cref="INotifyPropertyChanged"/> rather than an MVVM toolkit. It is
/// eight lines for one property, and it keeps the demonstration honest: a clinical
/// application needs a reference to the abstractions and to nothing else at all.
/// </para>
/// <para>
/// The shell finds the view for this by type, through a data template. That is the one
/// place Phase 1's seam is thinner than it looks: a plugin ships a view model and the
/// shell ships its template, so an application cannot yet bring its own control. Making
/// that possible means a plugin shipping a WPF resource dictionary, which is a decision
/// worth taking when there is a second application to justify it.
/// </para>
/// </remarks>
public sealed class HistogramPanelViewModel : INotifyPropertyChanged
{
    private HistogramBar? selected;

    internal HistogramPanelViewModel(VolumeHistogram histogram, string summary)
    {
        Summary = summary;

        List<HistogramBar> bars = new(histogram.Counts.Count);

        for (int bin = 0; bin < histogram.Counts.Count; bin++)
        {
            (int low, int high) = histogram.RangeOf(bin);
            int count = histogram.Counts[bin];

            bars.Add(new HistogramBar(
                low,
                high,
                count,
                histogram.Total == 0 ? 0 : (double)count / histogram.Total,

                // Against the busiest bin rather than against the total: air and soft
                // tissue outnumber everything else by orders of magnitude, and bars scaled
                // to the total would leave bone as a line one pixel high.
                histogram.Peak == 0 ? 0 : (double)count / histogram.Peak));
        }

        Bars = bars;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>One bar per bin, from -1024 HU upwards.</summary>
    public IReadOnlyList<HistogramBar> Bars { get; }

    /// <summary>Slice count and dimensions, for the top of the panel.</summary>
    public string Summary { get; }

    /// <summary>The bar the user picked, which is what the overlay names.</summary>
    public HistogramBar? Selected
    {
        get => selected;
        set
        {
            if (!Equals(selected, value))
            {
                selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Raised when <see cref="Selected"/> changes, for the session to relay.</summary>
    internal event EventHandler? SelectionChanged;
}
