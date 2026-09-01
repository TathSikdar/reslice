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

        builder.Services.AddSingleton<MainWindow>();

        host = builder.Build();
        host.Start();

        host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose();
        base.OnExit(e);
    }
}
