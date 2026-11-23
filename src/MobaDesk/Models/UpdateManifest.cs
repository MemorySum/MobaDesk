using System.Text.Json.Serialization;

namespace MobaDesk.Models;

/// <summary>
/// Update feed manifest (version.json). The URL may be absolute or relative to the manifest.
/// </summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = "";
}
