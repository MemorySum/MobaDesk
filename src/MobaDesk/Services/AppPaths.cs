using System.Text;

namespace MobaDesk.Services;

public static class AppPaths
{
    /// <summary>
    /// Data folder. Set MOBADESK_DATA_DIR to run in portable/isolated mode.
    /// </summary>
    public static string DataDirectory
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("MOBADESK_DATA_DIR");
            return string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MobaDesk")
                : custom;
        }
    }

    public static string ProfilesFile => Path.Combine(DataDirectory, "profiles.json");
    public static string FoldersFile => Path.Combine(DataDirectory, "folders.json");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string HostKeysFile => Path.Combine(DataDirectory, "known_hosts.json");
    public static string LogFile => Path.Combine(DataDirectory, "logs", "app.log");
    public static string WebViewDataDirectory => Path.Combine(DataDirectory, "webview");

    /// <summary>Static assets (terminal, editor, icons) shipped next to the executable.</summary>
    public static string AssetsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets");

    public static string AssetFile(string relativePath) => Path.Combine(AssetsDirectory, relativePath);

    /// <summary>Language resource files shipped next to the executable.</summary>
    public static string LanguageDirectory => Path.Combine(AppContext.BaseDirectory, "Resources", "Lang");

    public static string LanguageFile(string language) => Path.Combine(LanguageDirectory, $"{language}.json");
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(AppPaths.LogFile)!;
                Directory.CreateDirectory(dir);
                if (File.Exists(AppPaths.LogFile) && new FileInfo(AppPaths.LogFile).Length > 2 * 1024 * 1024)
                {
                    File.Move(AppPaths.LogFile, AppPaths.LogFile + ".old", true);
                }

                File.AppendAllText(AppPaths.LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
