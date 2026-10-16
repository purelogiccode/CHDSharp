using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Serilog.Core;
using Serilog.Events;

namespace CHDSharp;

/// <summary>
///     A Serilog sink that forwards every log event at <see cref="LogEventLevel.Warning" /> or above to the
///     Bug Report API. Each report embeds the full environment snapshot and, when present, the exception details.
///     The HTTP post is fire-and-forget so it never blocks application logging; in-flight posts are
///     given a bounded grace period on disposal so short-lived sessions still deliver their reports.
/// </summary>
internal sealed class BugReportSink : ILogEventSink, IDisposable
{
    private const string Endpoint = "https://www.purelogiccode.com/bugreport/api/send-bug-report";
    private static readonly HttpClient Client = new();
    private static readonly string ApiKey = DecodeApiKey();

    private readonly EnvironmentSnapshot _env;
    private readonly string _environmentLabel;

    /// <summary>Reports that have been queued but not yet delivered.</summary>
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

    /// <summary>
    ///     <c>CHDSHARP_NO_BUG_REPORTS=1</c> suppresses report delivery. Used by CI/tests so
    ///     intentionally-exercised warning paths never reach the production Bug Report API.
    /// </summary>
    private static readonly bool DisabledByEnvironment = string.Equals(
        Environment.GetEnvironmentVariable("CHDSHARP_NO_BUG_REPORTS"),
        "1",
        StringComparison.Ordinal
    );

    static BugReportSink()
    {
        Client.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>Initializes a new sink that reports on behalf of the supplied environment snapshot.</summary>
    /// <param name="env">The environment details to embed in every report.</param>
    public BugReportSink(EnvironmentSnapshot env)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _environmentLabel = $"{_env.WindowsVersion} ({EnvironmentSnapshot.Architecture} {_env.Bitness})";
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning)
            return;
        if (DisabledByEnvironment)
            return;
        if (logEvent.Exception is OperationCanceledException or TaskCanceledException)
            return;

        var message = BuildMessage(logEvent);

        // Fire-and-forget: never block the logging pipeline. The task is tracked so Dispose
        // can give in-flight reports a bounded grace period when the logger is closed.
        var send = Task.Run(() => SendAsync(message, logEvent));
        _inFlight.TryAdd(send, 0);
        _ = send.ContinueWith(
            completed => _inFlight.TryRemove(completed, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    /// <summary>
    ///     Gives in-flight bug reports a short grace period to reach the API when the logger is
    ///     closed (Serilog calls this from <c>Log.CloseAndFlush()</c>). Best-effort only: the wait
    ///     is bounded and any failure is ignored.
    /// </summary>
    [SuppressMessage(
        "ReSharper",
        "CA1031",
        Justification = "Bug-report delivery is best-effort and must never throw."
    )]
    public void Dispose()
    {
        try
        {
            Task.WhenAll(_inFlight.Keys).Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Best-effort delivery only.
        }
    }

    private static string DecodeApiKey()
    {
        // Double-encoded to avoid plain-text in source
        const string encoded =
            "aGpoN3l1NnQ1NnR5cjU0MG85dTg3Njc2NzZyNTY3NDUzNDQ1MzIzNTI2NGM3NWI2dDdnZ2doZ2c3NnRyZjU2NGU=";
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }

    private string BuildMessage(LogEvent logEvent)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Environment Details ===");
        sb.AppendLine($"Date: {_env.Date}");
        sb.AppendLine($"Application Name: {_env.ApplicationName}");
        sb.AppendLine($"Application Version: {_env.ApplicationVersion}");
        sb.AppendLine($"OS Version: {EnvironmentSnapshot.OsVersion}");
        sb.AppendLine($"Architecture: {EnvironmentSnapshot.Architecture}");
        sb.AppendLine($"Bitness: {_env.Bitness}");
        sb.AppendLine($"Windows, Linux or MacOsX Version: {_env.WindowsVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Processor Count: {EnvironmentSnapshot.ProcessorCount}");
        sb.AppendLine($"Base Directory: {EnvironmentSnapshot.BaseDirectory}");
        sb.AppendLine($"Temp Path: {EnvironmentSnapshot.TempPath}");
        sb.AppendLine($"Runtime: {EnvironmentSnapshot.RuntimeVersion}");

        sb.AppendLine();
        sb.AppendLine("=== Session Details ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Report Time (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        var uptime = DateTime.Now - _env.CreatedAt;
        sb.AppendLine(CultureInfo.InvariantCulture,
            $@"Session Uptime: {(long)uptime.TotalHours:00}\{uptime.Minutes:00}\{uptime.Seconds:00} (hh\mm\ss)");
        sb.AppendLine($"Elevated: {EnvironmentSnapshot.Elevated}");

        sb.AppendLine();
        sb.AppendLine("=== Error Details ===");
        sb.AppendLine($"Error message: {logEvent.RenderMessage()}");

        sb.AppendLine();
        sb.AppendLine("=== Log Context ===");
        sb.AppendLine($"Level: {logEvent.Level}");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Log Timestamp (UTC): {logEvent.Timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff}Z");
        if (logEvent.Properties.Count > 0)
        {
            sb.AppendLine("Properties:");
            foreach (var property in logEvent.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                sb.AppendLine(
                    $"  {property.Key} = {RenderPropertyValue(property.Value)}"
                );
        }
        else
        {
            sb.AppendLine("Properties: (none)");
        }

        sb.AppendLine();
        sb.AppendLine("=== Exception Details ===");
        var ex = logEvent.Exception;
        if (ex != null)
        {
            sb.AppendLine($"Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"Source: {ex.Source ?? string.Empty}");
            sb.AppendLine($"HResult: 0x{ex.HResult:X8}");
            sb.AppendLine($"StackTrace: {ex.StackTrace ?? string.Empty}");
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            {
                sb.AppendLine($"--- Inner Exception: {inner.GetType().FullName} ---");
                sb.AppendLine($"Message: {inner.Message}");
                sb.AppendLine($"StackTrace: {inner.StackTrace ?? string.Empty}");
            }
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

    /// <summary>
    ///     Renders a Serilog property value: scalar values render their payload, richer
    ///     values (structures, sequences) render through their <see cref="LogEventPropertyValue.ToString()" />
    ///     representation. Returns <c>?</c> for values that cannot be rendered.
    /// </summary>
    private static string RenderPropertyValue(LogEventPropertyValue value)
    {
        try
        {
            return value is ScalarValue scalar
                ? scalar.Value?.ToString() ?? "null"
                : value.ToString(null, CultureInfo.InvariantCulture);
        }
        catch
        {
            return "?";
        }
    }

    [SuppressMessage(
        "ReSharper",
        "CA1031",
        Justification = "Bug-report delivery is best-effort and must never throw."
    )]
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