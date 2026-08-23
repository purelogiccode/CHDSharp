using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CHDSharp;

/// <summary>
/// Checks GitHub for a newer release and prints a notification to the console if one is available.
/// Fire-and-forget: never blocks startup or throws.
/// </summary>
internal static class VersionCheckService
{
    private const string RepoApiUrl = "https://api.github.com/repos/purelogiccode/CHDSharp/releases/latest";
    private const string RepoReleasesUrl = "https://github.com/purelogiccode/CHDSharp/releases";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Checks for a newer version on GitHub and prints a notification if available.
    /// Intended to be called once at startup, fire-and-forget.
    /// </summary>
    public static void CheckAndNotify()
    {
        _ = Task.Run(CheckAsync);
    }

    private static async Task CheckAsync()
    {
        try
        {
            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version;
            if (currentVersion == null)
                return;

            using var request = new HttpRequestMessage(HttpMethod.Get, RepoApiUrl);
            request.Headers.UserAgent.ParseAdd("CHDSharpCli");
            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var release = JsonSerializer.Deserialize(json, JsonContext.Default.GitHubRelease);
            if (release?.TagName == null)
                return;

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion == null)
                return;

            if (latestVersion <= currentVersion)
                return;

            var assetName = GetExpectedAssetName();
            var downloadUrl = release.Assets?
                .FirstOrDefault(a => string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase))
                ?.BrowserDownloadUrl;

            Console.WriteLine();
            Console.WriteLine($"  *** A new version of CHDSharpCli is available: v{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.MinorRevision} ***");
            if (downloadUrl != null)
                Console.WriteLine($"  *** Download: {downloadUrl} ***");
            else
                Console.WriteLine($"  *** Download: {RepoReleasesUrl} ***");
            Console.WriteLine();
        }
        catch
        {
            // Best-effort: swallow all failures.
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

        return $"CHDSharpCli_{arch}_v{Assembly.GetEntryAssembly()?.GetName().Version}.zip";
    }
}
