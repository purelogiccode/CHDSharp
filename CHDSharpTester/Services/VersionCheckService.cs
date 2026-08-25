using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CHDSharpTester.Services;

/// <summary>
///     Checks GitHub for a newer release and returns version info if available.
///     Fire-and-forget: never blocks startup or throws.
/// </summary>
internal static class VersionCheckService
{
    private const string RepoApiUrl =
        "https://api.github.com/repos/purelogiccode/CHDSharp/releases/latest";

    private const string RepoReleasesUrl = "https://github.com/purelogiccode/CHDSharp/releases";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    ///     Checks for a newer version on GitHub. Returns a notification message if a new version is available, or null if
    ///     up-to-date.
    /// </summary>
    public static async Task<string?> CheckAsync()
    {
        try
        {
            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version;
            if (currentVersion == null)
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, RepoApiUrl);
            request.Headers.UserAgent.ParseAdd("CHDSharpTester");
            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var release = JsonSerializer.Deserialize(json, JsonContext.Default.GitHubRelease);
            if (release?.TagName == null)
                return null;

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion == null)
                return null;

            if (latestVersion <= currentVersion)
                return null;

            var assetName = GetExpectedAssetName();
            var downloadUrl = release
                .Assets?.FirstOrDefault(a =>
                    string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase)
                )
                ?.BrowserDownloadUrl;

            var url = downloadUrl ?? RepoReleasesUrl;
            return
                $"A new version of CHDSharpTester is available: v{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.Build}\nDownload: {url}";
        }
        catch
        {
            // Best-effort: swallow all failures.
            return null;
        }
    }

    private static Version? ParseVersion(string tagName)
    {
        var tag = tagName.TrimStart('v', 'V');
        return Version.TryParse(tag, out var v) ? v : null;
    }

    private static string GetExpectedAssetName()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            _ => "win-x64"
        };

        return $"CHDSharpTester_{arch}_v{Assembly.GetEntryAssembly()?.GetName().Version}.zip";
    }
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }

    [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
internal partial class JsonContext : JsonSerializerContext;