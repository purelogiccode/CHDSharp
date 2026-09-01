using System.Text.Json.Serialization;

namespace CHDSharpTester.Models;

/// <summary>Represents the latest release information returned by the GitHub releases API.</summary>
internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }

    [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
}

/// <summary>Represents a downloadable asset attached to a GitHub release.</summary>
internal sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}