using System.Reflection;
using System.Text.Json;

namespace CHDSharp;

/// <summary>
/// Sends a single usage hit to the ApplicationStats API at application launch.
/// Fire-and-forget: never blocks startup or throws.
/// </summary>
internal static class ApplicationStatsService
{
    private const string Endpoint = "https://www.purelogiccode.com/ApplicationStats/stats";
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Records a usage hit for the given application. Intended to be called once at startup.
    /// </summary>
    /// <param name="applicationId">Unique application identifier (e.g. "chdsharpcli").</param>
    public static void TrackLaunch(string applicationId)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        _ = Task.Run(() => SendAsync(applicationId, version));
    }

    private static async Task SendAsync(string applicationId, string version)
    {
        try
        {
            var payload = new { applicationId, version };
            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var _ = await Client.SendAsync(request).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: swallow all failures.
        }
    }
}
