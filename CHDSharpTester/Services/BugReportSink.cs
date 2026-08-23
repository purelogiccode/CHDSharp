using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Serilog.Core;
using Serilog.Events;

namespace CHDSharpTester.Services;

/// <summary>
/// A Serilog sink that forwards every log event at <see cref="LogEventLevel.Warning"/> or above to the
/// Bug Report API. Each report embeds the full environment snapshot and, when present, the exception details.
/// The HTTP post is fire-and-forget so it never blocks application logging.
/// </summary>
internal sealed class BugReportSink : ILogEventSink
{
    private static readonly HttpClient Client = new();

    private const string Endpoint = "https://www.purelogiccode.com/bugreport/api/send-bug-report";
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";

    private readonly EnvironmentSnapshot _env;
    private readonly string _environmentLabel;

    static BugReportSink()
    {
        Client.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>Initializes a new sink that reports on behalf of the supplied environment snapshot.</summary>
    /// <param name="env">The environment details to embed in every report.</param>
    public BugReportSink(EnvironmentSnapshot env)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _environmentLabel = $"{_env.WindowsVersion} ({_env.Architecture} {_env.Bitness})";
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent == null) return;
        if (logEvent.Level < LogEventLevel.Warning) return;
        if (logEvent.Exception is OperationCanceledException or TaskCanceledException) return;

        var message = BuildMessage(logEvent);

        // Fire-and-forget: never block the logging pipeline.
        _ = Task.Run(() => SendAsync(message, logEvent));
    }

    private string BuildMessage(LogEvent logEvent)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Environment Details ===");
        sb.AppendLine($"Date: {_env.Date}");
        sb.AppendLine($"Application Name: {_env.ApplicationName}");
        sb.AppendLine($"Application Version: {_env.ApplicationVersion}");
        sb.AppendLine($"OS Version: {_env.OsVersion}");
        sb.AppendLine($"Architecture: {_env.Architecture}");
        sb.AppendLine($"Bitness: {_env.Bitness}");
        sb.AppendLine($"Windows Version: {_env.WindowsVersion}");
        sb.AppendLine($"Processor Count: {_env.ProcessorCount}");
        sb.AppendLine($"Base Directory: {_env.BaseDirectory}");
        sb.AppendLine($"Temp Path: {_env.TempPath}");

        sb.AppendLine();
        sb.AppendLine("=== Error Details ===");
        sb.AppendLine($"Error message: {logEvent.RenderMessage()}");

        sb.AppendLine();
        sb.AppendLine("=== Exception Details ===");
        var ex = logEvent.Exception;
        if (ex != null)
        {
            sb.AppendLine($"Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"Source: {ex.Source ?? string.Empty}");
            sb.AppendLine($"StackTrace: {ex.StackTrace ?? string.Empty}");
        }
        else
        {
            sb.AppendLine("Type: (none)");
            sb.AppendLine("Message: (none)");
            sb.AppendLine("Source: (none)");
            sb.AppendLine("StackTrace: (none)");
        }

        return sb.ToString();
    }

    [SuppressMessage("ReSharper", "CA1031", Justification = "Bug-report delivery is best-effort and must never throw.")]
    private async Task SendAsync(string message, LogEvent logEvent)
    {
        try
        {
            var payload = new
            {
                message,
                applicationName = _env.ApplicationName,
                version = _env.ApplicationVersion,
                userInfo = (string?)null,
                environment = _environmentLabel,
                stackTrace = logEvent.Exception?.StackTrace
            };

            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("X-API-KEY", ApiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            // Response is intentionally ignored: the server may throttle (HTTP 429) and that is acceptable.
        }
        catch
        {
            // Best-effort reporting: swallow all failures so logging is never disrupted.
        }
    }
}
