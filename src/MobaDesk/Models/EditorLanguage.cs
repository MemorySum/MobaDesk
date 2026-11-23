namespace MobaDesk.Models;

/// <summary>CodeMirror mode and display label for a file name.</summary>
public sealed record EditorLanguage(string Mode, string Label);

/// <summary>Maps file names to a syntax highlighting mode.</summary>
public static class EditorLanguages
{
    private static readonly EditorLanguage Plain = new("", "Plain Text");

    public static EditorLanguage Detect(string? fileName)
    {
        var name = (fileName ?? "").Trim();
        if (name.Length == 0)
        {
            return Plain;
        }

        var lower = name.ToLowerInvariant();
        if (lower.StartsWith("dockerfile", StringComparison.Ordinal))
        {
            return new EditorLanguage("dockerfile", "Dockerfile");
        }

        switch (lower)
        {
            case "makefile":
            case "cmakelists.txt":
                return new EditorLanguage("text/x-cmake", "CMake");
            case "gemfile":
            case "rakefile":
                return new EditorLanguage("text/x-ruby", "Ruby");
            case "nginx.conf":
                return new EditorLanguage("text/x-nginx-conf", "Nginx");
            case ".bashrc":
            case ".bash_profile":
            case ".bash_aliases":
            case ".bash_logout":
            case ".profile":
            case ".zshrc":
            case ".zprofile":
            case ".zshenv":
            case ".kshrc":
            case ".env":
                return new EditorLanguage("text/x-sh", "Shell");
        }

        // Files such as ".bashrc.old" still read best as shell scripts.
        if (lower.Contains("bashrc", StringComparison.Ordinal) ||
            lower.Contains("bash_profile", StringComparison.Ordinal) ||
            lower.Contains("zshrc", StringComparison.Ordinal))
        {
            return new EditorLanguage("text/x-sh", "Shell");
        }

        return Path.GetExtension(lower) switch
        {
            ".sh" or ".bash" or ".zsh" or ".ksh" or ".fish" or ".command" or ".bat" or ".cmd"
                => new EditorLanguage("text/x-sh", "Shell"),
            ".py" or ".pyw" or ".pyi" => new EditorLanguage("text/x-python", "Python"),
            ".js" or ".mjs" or ".cjs" => new EditorLanguage("text/javascript", "JavaScript"),
            ".jsx" => new EditorLanguage("text/jsx", "JavaScript JSX"),
            ".ts" or ".mts" or ".cts" => new EditorLanguage("text/typescript", "TypeScript"),
            ".tsx" => new EditorLanguage("text/typescript", "TypeScript JSX"),
            ".json" or ".jsonc" or ".webmanifest" => new EditorLanguage("application/json", "JSON"),
            ".html" or ".htm" => new EditorLanguage("text/html", "HTML"),
            ".xml" or ".svg" or ".xaml" or ".csproj" or ".props" or ".targets" or ".resx" or ".plist"
                => new EditorLanguage("text/xml", "XML"),
            ".css" => new EditorLanguage("text/css", "CSS"),
            ".scss" => new EditorLanguage("text/x-scss", "SCSS"),
            ".less" => new EditorLanguage("text/x-less", "LESS"),
            ".md" or ".markdown" => new EditorLanguage("text/x-markdown", "Markdown"),
            ".yml" or ".yaml" => new EditorLanguage("text/x-yaml", "YAML"),
            ".sql" => new EditorLanguage("text/x-sql", "SQL"),
            ".php" => new EditorLanguage("application/x-httpd-php", "PHP"),
            ".go" => new EditorLanguage("text/x-go", "Go"),
            ".rs" => new EditorLanguage("text/x-rustsrc", "Rust"),
            ".rb" or ".rake" => new EditorLanguage("text/x-ruby", "Ruby"),
            ".pl" or ".pm" => new EditorLanguage("text/x-perl", "Perl"),
            ".ps1" or ".psm1" or ".psd1" => new EditorLanguage("application/x-powershell", "PowerShell"),
            ".properties" or ".ini" or ".conf" or ".cfg" or ".cnf" or ".editorconfig"
                => new EditorLanguage("text/x-properties", "Properties"),
            ".toml" => new EditorLanguage("text/x-toml", "TOML"),
            ".c" => new EditorLanguage("text/x-csrc", "C"),
            ".h" => new EditorLanguage("text/x-c++hdr", "C/C++ Header"),
            ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hh" or ".hxx"
                => new EditorLanguage("text/x-c++src", "C++"),
            ".cs" => new EditorLanguage("text/x-csharp", "C#"),
            ".java" => new EditorLanguage("text/x-java", "Java"),
            ".kt" or ".kts" => new EditorLanguage("text/x-java", "Kotlin"),
            ".m" or ".mm" => new EditorLanguage("text/x-objectivec", "Objective-C"),
            ".swift" => new EditorLanguage("text/x-swift", "Swift"),
            ".lua" => new EditorLanguage("text/x-lua", "Lua"),
            ".vb" or ".vbs" => new EditorLanguage("text/x-vb", "Visual Basic"),
            ".r" => new EditorLanguage("text/x-rsrc", "R"),
            ".dart" => new EditorLanguage("application/dart", "Dart"),
            ".proto" => new EditorLanguage("text/x-protobuf", "Protocol Buffers"),
            ".diff" or ".patch" => new EditorLanguage("text/x-diff", "Diff"),
            ".cmake" => new EditorLanguage("text/x-cmake", "CMake"),
            ".nginx" => new EditorLanguage("text/x-nginx-conf", "Nginx"),
            _ => Plain
        };
    }
}
