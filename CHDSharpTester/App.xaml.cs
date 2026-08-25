using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using CHDSharp;
using CHDSharpTester.Services;
using Serilog;
using Serilog.Extensions.Logging;

namespace CHDSharpTester;

/// <summary>The WPF application entry point. Configures Serilog logging on startup and flushes on exit.</summary>
public partial class App
{
    /// <summary>Configures Serilog file and debug logging when the application starts.</summary>
    /// <param name="e">The startup event arguments.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CHDSharpTester",
                "logs",
                "chdsharp-tester-.log"
            );

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    formatProvider: CultureInfo.InvariantCulture
                )
                .WriteTo.Sink(new BugReportSink(new EnvironmentSnapshot("CHDSharpTester")))
                .CreateLogger();

            Chd.LoggerFactory = new SerilogLoggerFactory(Log.Logger);

            ApplicationStatsService.TrackLaunch("chdsharptester");

            Log.Information("CHDSharpTester started");

            _ = Task.Run(async () =>
            {
                var message = await VersionCheckService.CheckAsync().ConfigureAwait(false);
                if (message != null)
                {
                    Log.Information("Version check: {Message}", message);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            message,
                            "Update Available",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"App.OnStartup failed: {ex}");
        }
    }

    /// <summary>Flushes and closes the Serilog logger when the application exits.</summary>
    /// <param name="e">The exit event arguments.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Log.Information("CHDSharpTester exiting");
            Log.CloseAndFlush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"App.OnExit failed: {ex}");
        }

        base.OnExit(e);
    }
}
