using System.Text.Json.Serialization;

namespace MobaDesk.Models;

/// <summary>
/// Source-generated JSON metadata. Keeps serialization free of runtime code generation,
/// which is required for Native AOT and trimming.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(List<ConnectionProfile>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(TerminalConfigureOptions))]
internal sealed partial class MobaDeskJsonContext : JsonSerializerContext
{
}

/// <summary>Payload sent to xterm.js when terminal settings change.</summary>
internal sealed class TerminalConfigureOptions
{
    // terminal.html reads camelCase keys; keep them in sync with Assets/terminal.html.
    [JsonPropertyName("fontSize")]
    public int FontSize { get; set; }

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "";

    [JsonPropertyName("scrollback")]
    public int Scrollback { get; set; }

    [JsonPropertyName("copyOnSelect")]
    public bool CopyOnSelect { get; set; }
}
