using System;
using System.Globalization;
using System.Linq;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Dicom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static System.FormattableString;

// The Iteration 1 harness. There is no window yet, so this is what proves the pipeline:
// point it at a directory and it prints what was loaded, or why it was refused.
//
// It also owns the Generic Host. Registering the loader, validator and builder here means
// the composition root is exercised before any WPF exists to hide it, and App.xaml.cs in
// Iteration 2 registers the same three services the same way.
if (args.Length != 1)
{
    Console.Error.WriteLine("usage: InterviewTrea.Probe <directory of DICOM files>");
    return 2;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<SeriesLoader>();
builder.Services.AddSingleton<GeometryValidator>();
builder.Services.AddSingleton<VolumeBuilder>();

using IHost host = builder.Build();
IServiceProvider services = host.Services;

DirectoryScan scan = services.GetRequiredService<SeriesLoader>().Scan(args[0]);

foreach (var group in scan.Skipped.GroupBy(s => s.Reason, StringComparer.Ordinal))
{
    Console.WriteLine(Invariant($"Skipped {group.Count()} file(s): {group.Key}"));
}

if (scan.Series.Count == 0)
{
    Console.Error.WriteLine("No DICOM series found.");
    return 1;
}

// FR-102 has no prompt to offer in a console, so every candidate is listed and the largest
// is loaded. Iteration 2 replaces this with the series picker.
if (scan.Series.Count > 1)
{
    Console.WriteLine(Invariant($"Found {scan.Series.Count} series; loading the largest:"));
    foreach (SeriesDescriptor candidate in scan.Series)
    {
        Console.WriteLine(Invariant(
            $"  {candidate.SliceCount,4} slices  {candidate.Metadata.SeriesDescription ?? "(no description)"}"));
    }
}

SeriesDescriptor series = scan.Series[0];

SeriesGeometry geometry;
try
{
    geometry = services.GetRequiredService<GeometryValidator>().Validate(series.Slices);
}
catch (SeriesRejectedException rejected)
{
    // The rejection path is a feature, not an error path to be swallowed. The reason is
    // printed alongside the message so the enum and the prose stay visibly connected.
    Console.Error.WriteLine(Invariant($"Rejected ({rejected.Reason}): {rejected.Message}"));
    return 1;
}

VolumeBuildResult result = services.GetRequiredService<VolumeBuilder>().Build(series, geometry);
Volume volume = result.Volume;

Console.WriteLine(Invariant($"Loaded {volume.DimZ} slices, {volume.DimX}x{volume.DimY}x{volume.DimZ}, spacing {volume.Spacing.X:0.##}x{volume.Spacing.Y:0.##}x{volume.Spacing.Z:0.##} mm, HU range {result.MinimumHounsfield}..{result.MaximumHounsfield}"));

Console.WriteLine(Invariant(
    $"{volume.ByteCount / (1024.0 * 1024.0):0.#} MB, series \"{series.Metadata.SeriesDescription ?? "(none)"}\", modality {series.Metadata.Modality}"));

if (result.SaturatedSampleCount > 0)
{
    Console.WriteLine(Invariant(
        $"WARNING: {result.SaturatedSampleCount} sample(s) clamped at the short bounds during rescale."));
}

// The single most useful sanity check on a real series, and the reason this harness exists
// at all. Air outside the patient must read about -1000. A minimum of 0 means the rescale
// intercept was missed, and every measurement taken from the volume would be wrong while
// the image still looked like a chest.
Console.WriteLine(result.MinimumHounsfield > -500
    ? Invariant($"WARNING: lowest value is {result.MinimumHounsfield} HU. Air should read about -1000; check RescaleIntercept (0028,1052).")
    : "Air reads about -1000 HU, so the rescale looks right.");

return 0;
