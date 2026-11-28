using System.Text.Json.Serialization;

namespace MobaDesk.Models;

/// <summary>
/// Subset of the GitHub Releases API "get the latest release" response used by the updater.
/// https://docs.github.com/rest/releases/releases#get-the-latest-release
/// </summary>
internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("published_at")]
    public string PublishedAt { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset>? Assets { get; set; }
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}
