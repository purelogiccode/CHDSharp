using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CHDSharp;

/// <summary>
///     Sends a single usage hit to the ApplicationStats API at application launch.
///     Fire-and-forget: never blocks startup or throws.
/// </summary>
internal static class ApplicationStatsService
{
    private const string Endpoint = "https://www.purelogiccode.com/ApplicationStats/stats";
    private static readonly string ApiKey = DecodeApiKey();

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static string DecodeApiKey()
    {
        // Double-encoded to avoid plain-text in source
        const string encoded =
            "aGpoN3l1NnQ1NnR5cjU0MG85dTg3Njc2NzZyNTY3NDUzNDQ1MzIzNTI2NGM3NWI2dDdnZ2doZ2c3NnRyZjU2NGU=";
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }

    /// <summary>
    ///     Records a usage hit for the given application. Intended to be called once at startup.
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
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var _ = await Client.SendAsync(request).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: swallow all failures.
        }
    }
}
