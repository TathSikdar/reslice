using System;
using System.IO;
using System.Windows;
using InterviewTrea.Dicom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace InterviewTrea.App;

/// <summary>
/// The composition root. Everything is constructed here and nowhere else: no service
/// locator, no static singletons, no <c>new</c> of a service inside a view model.
/// </summary>
/// <remarks>
/// These are the same three services the Iteration 1 console probe registers, in the same
/// way. That was the point of building the probe over the Generic Host rather than as a
/// throwaway <c>Main</c> - the wiring was exercised before there was a window to hide it.
/// </remarks>
public partial class App : Application
{
    private IHost? host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton<SeriesLoader>();
        builder.Services.AddSingleton<GeometryValidator>();
        builder.Services.AddSingleton<VolumeBuilder>();

        builder.Services.AddSingleton<ISeriesPrompt, Views.SeriesPrompt>();

        builder.Services.AddSingleton<ViewModels.MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        host = builder.Build();
        host.Start();

        MainWindow window = host.Services.GetRequiredService<MainWindow>();
        window.Show();

        // Optional folder argument. Not a feature so much as a demo safeguard: on an
        // unfamiliar machine, clicking through a folder dialog to a path you have not
        // memorised is the most likely way to lose thirty seconds in a ten-minute slot.
        if (e.Args.Length == 1 && Directory.Exists(e.Args[0]))
        {
            _ = window.LoadAsync(e.Args[0]);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose();
        base.OnExit(e);
    }
}
