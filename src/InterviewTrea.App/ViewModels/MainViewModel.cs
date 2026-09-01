using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Dicom;
using InterviewTrea.Rendering.Windowing;

namespace InterviewTrea.App.ViewModels;

/// <summary>
/// The whole of the Iteration 2 application state: one volume, one slice, one window.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly SeriesLoader loader;
    private readonly GeometryValidator validator;
    private readonly VolumeBuilder builder;

    public MainViewModel(SeriesLoader loader, GeometryValidator validator, VolumeBuilder builder)
    {
        this.loader = loader;
        this.validator = validator;
        this.builder = builder;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVolume))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBanner))]
    [NotifyPropertyChangedFor(nameof(SliceLabel))]
    private Volume? volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SliceLabel))]
    private int sliceIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowLabel))]
    private WindowLevel window = WindowLevel.SoftTissue;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private double loadProgress;

    /// <summary>
    /// The rejection message when a series will not load, and the load summary when it
    /// does. Spec 1.6 rule 3: a bad series has to fail in a way that can be narrated, not
    /// as a stack trace or a hang.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusBanner))]
    private string? status;

    /// <summary>
    /// Keeps the slice index inside the new volume. Loading a twelve-slice series after a
    /// three-hundred-slice one would otherwise leave the index pointing past the end.
    /// </summary>
    partial void OnVolumeChanged(Volume? value)
    {
        if (value is not null && SliceIndex >= value.DimZ)
        {
            SliceIndex = value.DimZ - 1;
        }
    }

    public bool HasVolume => Volume is not null;

    /// <summary>
    /// Whether the message belongs in the middle of the empty viewport rather than in the
    /// corner overlay. A rejection is a paragraph and has to be readable; a load summary
    /// is one line and belongs out of the way of the image.
    /// </summary>
    public bool ShowStatusBanner => !HasVolume && !string.IsNullOrEmpty(Status);

    public string SliceLabel => Volume is null ? string.Empty : $"Slice {SliceIndex + 1}/{Volume.DimZ}";

    public string WindowLabel => $"W {Window.Width:0}  L {Window.Center:0}";

    /// <summary>Moves by whole slices and stops at the ends of the stack (FR-301).</summary>
    public void ScrollSlices(int delta)
    {
        if (Volume is Volume loaded)
        {
            SliceIndex = Math.Clamp(SliceIndex + delta, 0, loaded.DimZ - 1);
        }
    }

    public async Task LoadAsync(string directory)
    {
        IsLoading = true;
        LoadProgress = 0;
        Status = "Scanning...";

        // Captured on the UI thread, so Progress<T> marshals the callbacks back to it and
        // the background work never touches a bound property directly (FR-108).
        Progress<double> scanning = new(p => LoadProgress = p * ScanShare);
        Progress<double> decoding = new(p => LoadProgress = ScanShare + (p * (1 - ScanShare)));

        try
        {
            VolumeBuildResult result = await Task.Run(() =>
            {
                DirectoryScan scan = loader.Scan(directory, scanning);

                if (scan.Series.Count == 0)
                {
                    throw new SeriesRejectedException(
                        SeriesRejectionReason.TooFewSlices,
                        "No DICOM series was found in that folder.");
                }

                // FR-102 asks for a prompt when several series are present. The picker is
                // not in the approved control set for this iteration, so the largest wins
                // and the status line says how many were passed over.
                SeriesDescriptor series = scan.Series[0];
                SeriesGeometry geometry = validator.Validate(series.Slices);

                return builder.Build(series, geometry, decoding);
            }).ConfigureAwait(true);

            Volume = result.Volume;

            // FR-306: open on the scanner's own window when the series carries one. The
            // preset dropdown deliberately shows no selection at this point, because none
            // of the five presets is what is on screen.
            Window = WindowLevel.InitialFor(result.Volume.Metadata);

            // Middle of the stack. Opening on slice 1 shows the edge of the scan range,
            // which on a chest study is usually air.
            SliceIndex = result.Volume.DimZ / 2;

            Status = $"{result.Volume.DimZ} slices, {result.Volume.DimX}x{result.Volume.DimY}, "
                + $"HU {result.MinimumHounsfield}..{result.MaximumHounsfield}";
        }
        catch (SeriesRejectedException rejected)
        {
            Volume = null;
            Status = rejected.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Volume = null;
            Status = $"Could not read that folder: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 1;
        }
    }

    /// <summary>
    /// How much of the progress bar the header scan gets. Header parsing is roughly an
    /// order of magnitude cheaper than pixel decoding, so a bar that gave each phase half
    /// would sprint to the middle and then crawl - worse than no bar for judging whether
    /// the application has hung.
    /// </summary>
    private const double ScanShare = 0.15;
}
