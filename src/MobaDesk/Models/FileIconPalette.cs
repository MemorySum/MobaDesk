using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MobaDesk.Models;

/// <summary>Chooses a colored icon for a file list entry based on its type.</summary>
public static class FileIconPalette
{
    private const string FolderColor = "#FBBF24";
    private const string LinkColor = "#22D3EE";
    private const string DocumentColor = "#60A5FA";
    private const string CodeColor = "#34D399";
    private const string ConfigColor = "#FB923C";
    private const string ArchiveColor = "#A78BFA";
    private const string ImageColor = "#F472B6";
    private const string VideoColor = "#F87171";
    private const string AudioColor = "#E879F9";
    private const string BinaryColor = "#2DD4BF";
    private const string UnknownColor = "#94A3B8";

    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.Ordinal);

    public static Brush BrushFor(string name, bool isDirectory, bool isSymlink) =>
        BrushForHex(ColorFor(name, isDirectory, isSymlink));

    public static string ColorFor(string name, bool isDirectory, bool isSymlink)
    {
        if (isSymlink)
        {
            return LinkColor;
        }

        if (isDirectory)
        {
            return FolderColor;
        }

        var lower = (name ?? "").ToLowerInvariant();
        if (lower.StartsWith("dockerfile") || lower.StartsWith("makefile") ||
            lower is "cmakelists.txt" or "vagrantfile" or "jenkinsfile" or "procfile")
        {
            return ConfigColor;
        }

        var dot = lower.LastIndexOf('.');
        var extension = dot > 0 && dot < lower.Length - 1 ? lower[(dot + 1)..] : "";
        if (extension.Length == 0)
        {
            return lower.StartsWith('.') ? CodeColor : UnknownColor;
        }

        return extension switch
        {
            "zip" or "rar" or "7z" or "tar" or "gz" or "tgz" or "bz2" or "xz" or "zst" or "lz" or
                "lz4" or "cab" or "iso" => ArchiveColor,
            "png" or "jpg" or "jpeg" or "gif" or "bmp" or "webp" or "svg" or "ico" or "tif" or "tiff" or
                "psd" or "raw" or "heic" => ImageColor,
            "mp4" or "mkv" or "avi" or "mov" or "webm" or "wmv" or "flv" or "m4v" or "mpg" or "mpeg" => VideoColor,
            "mp3" or "wav" or "flac" or "ogg" or "m4a" or "aac" or "wma" or "opus" or "mid" or "midi" => AudioColor,
            "cs" or "csx" or "c" or "h" or "cpp" or "cc" or "cxx" or "hpp" or "js" or "mjs" or "cjs" or
                "ts" or "tsx" or "jsx" or "py" or "java" or "kt" or "kts" or "go" or "rs" or "rb" or "php" or
                "swift" or "m" or "mm" or "scala" or "lua" or "pl" or "r" or "dart" or "vue" or "svelte" or
                "html" or "htm" or "css" or "scss" or "sass" or "less" or "sh" or "bash" or "zsh" or "fish" or
                "ps1" or "bat" or "cmd" or "sql" => CodeColor,
            "json" or "jsonc" or "xml" or "yaml" or "yml" or "toml" or "ini" or "conf" or "cfg" or
                "properties" or "env" or "lock" or "gradle" or "mk" or "cmake" => ConfigColor,
            "txt" or "md" or "markdown" or "rst" or "log" or "pdf" or "doc" or "docx" or "xls" or "xlsx" or
                "ppt" or "pptx" or "csv" or "tsv" or "rtf" or "odt" or "ods" or "odp" => DocumentColor,
            "exe" or "dll" or "so" or "dylib" or "o" or "a" or "lib" or "bin" or "apk" or "deb" or "rpm" or
                "msi" or "dmg" or "jar" or "war" or "class" or "pyc" => BinaryColor,
            _ => UnknownColor
        };
    }

    private static Brush BrushForHex(string hex)
    {
        lock (Brushes)
        {
            if (Brushes.TryGetValue(hex, out var brush))
            {
                return brush;
            }

            brush = new SolidColorBrush(ParseColor(hex));
            Brushes[hex] = brush;
            return brush;
        }
    }

    private static Color ParseColor(string hex)
    {
        hex = (hex ?? "").TrimStart('#');
        if (hex.Length == 6)
        {
            return Color.FromArgb(255,
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }

        return Color.FromArgb(255, 0x94, 0xA3, 0xB8);
    }
}
