using System.Text.Json.Serialization;

namespace CHDSharpTestGen.Models;

internal sealed class ManifestEntry
{
    [JsonPropertyName("file")] public string File { get; set; } = "";

    [JsonPropertyName("version")] public uint Version { get; set; }

    [JsonPropertyName("parent")] public string? Parent { get; set; }

    [JsonPropertyName("expect")] public string Expect { get; set; } = "ok";

    [JsonPropertyName("note")] public string Note { get; set; } = "";
}