using System.Globalization;
using MobaDesk.Models;

namespace MobaDesk.Services;

/// <summary>
/// Lightweight runtime localization backed by JSON resource files
/// (Resources/Lang/zh-CN.json and Resources/Lang/en-US.json).
/// All UI text can be switched without restarting the app.
/// </summary>
public static class Localization
{
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Dictionary<string, string>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static string Language { get; private set; } = Chinese;

    public static bool IsEnglish => Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(string? language, bool notify = true)
    {
        var normalized = string.IsNullOrWhiteSpace(language)
            ? Chinese
            : language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? English : Chinese;

        var changed = normalized != Language;
        Language = normalized;
        if (notify && changed)
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static string T(string key)
    {
        if (StringsFor(Language).TryGetValue(key, out var value))
        {
            return value;
        }

        if (!string.Equals(Language, Chinese, StringComparison.OrdinalIgnoreCase) &&
            StringsFor(Chinese).TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    public static string T(string key, params object?[] args)
    {
        var template = T(key);
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, args);
    }

    private static Dictionary<string, string> StringsFor(string language)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(language, out var cached))
            {
                return cached;
            }

            var loaded = Load(language);
            Cache[language] = loaded;
            return loaded;
        }
    }

    private static Dictionary<string, string> Load(string language)
    {
        var path = AppPaths.LanguageFile(language);
        if (!File.Exists(path))
        {
            Log.Warn($"Localization resource file not found: {path}");
            return Empty();
        }

        if (!JsonStore.TryRead(path, MobaDeskJsonContext.Default.DictionaryStringString,
                out var strings) || strings == null)
        {
            return Empty();
        }

        return new Dictionary<string, string>(strings, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> Empty() =>
        new(StringComparer.OrdinalIgnoreCase);
}
