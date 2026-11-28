using System.Text.Json.Serialization;
using MobaDesk.Services;

namespace MobaDesk.Models;

public sealed class AppSettings
{
    /// <summary>
    /// True when settings.json existed but could not be read. Saving is disabled in that
    /// case so the defaults do not overwrite the user's original file.
    /// </summary>
    [JsonIgnore]
    public bool LoadFailed { get; internal set; }

    public int TerminalFontSize { get; set; } = 14;
    public string TerminalFontFamily { get; set; } = "Cascadia Mono, Consolas, monospace";
    public int ScrollbackLines { get; set; } = 8000;
    public int MonitorIntervalSeconds { get; set; } = 2;
    public int ConnectionTimeoutSeconds { get; set; } = 20;
    public string Theme { get; set; } = "System";
    public string Material { get; set; } = "Mica";
    /// <summary>"Elegant" (soft animations and translucent surfaces) or "Simple" (flat, instant).</summary>
    public string InterfaceMode { get; set; } = "Elegant";
    public string Language { get; set; } = "zh-CN";
    public bool CopyOnSelect { get; set; }

    public bool CheckUpdatesOnStartup { get; set; } = true;
    /// <summary>ISO-8601 UTC timestamp of the last successful startup update check.</summary>
    public string LastUpdateCheckUtc { get; set; } = "";

    public static AppSettings Load()
    {
        if (JsonStore.TryRead(AppPaths.SettingsFile, MobaDeskJsonContext.Default.AppSettings,
                out var settings, out var failed) && settings != null)
        {
            settings.Normalize();
            return settings;
        }

        return new AppSettings { LoadFailed = failed };
    }

    public void Save()
    {
        if (LoadFailed)
        {
            Log.Error($"Refusing to overwrite unreadable settings file '{AppPaths.SettingsFile}'.");
            return;
        }

        Normalize();
        JsonStore.Write(AppPaths.SettingsFile, this, MobaDeskJsonContext.Default.AppSettings);
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        TerminalFontSize = Math.Clamp(TerminalFontSize, 9, 32);
        ScrollbackLines = Math.Clamp(ScrollbackLines, 1000, 100000);
        MonitorIntervalSeconds = Math.Clamp(MonitorIntervalSeconds, 1, 60);
        ConnectionTimeoutSeconds = Math.Clamp(ConnectionTimeoutSeconds, 5, 120);
        if (string.IsNullOrWhiteSpace(TerminalFontFamily))
        {
            TerminalFontFamily = "Cascadia Mono, Consolas, monospace";
        }

        if (Theme is not ("System" or "Dark" or "Light"))
        {
            Theme = "System";
        }

        if (Material is not ("Mica" or "MicaAlt" or "Acrylic" or "Solid"))
        {
            Material = "Mica";
        }

        if (InterfaceMode is not ("Elegant" or "Simple"))
        {
            InterfaceMode = "Elegant";
        }

        if (string.IsNullOrWhiteSpace(Language))
        {
            Language = "zh-CN";
        }
        else
        {
            Language = Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";
        }

        LastUpdateCheckUtc = LastUpdateCheckUtc ?? "";
    }
}
