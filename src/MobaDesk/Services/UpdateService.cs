using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MobaDesk.Models;

namespace MobaDesk.Services;

public sealed record UpdateCheckResult(UpdateManifest Manifest, string InstallerUrl, bool IsNewer);

/// <summary>
/// Checks a version.json feed, downloads the installer, verifies its SHA-256 and starts
/// the (Inno Setup) silent upgrade. The feed may be an HTTP(S) URL, a local file or a folder.
/// </summary>
public static class UpdateService
{
    private const string UserAgentName = "MobaDesk-Updater";
    private const string DefaultManifestName = "version.json";
    private const string RepositoryOwner = "MemorySum";
    private const string RepositoryName = "MobaDesk";
    private const string LatestReleaseUrl =
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";

    public static string CurrentVersion
    {
        get
        {
            var assembly = typeof(UpdateService).Assembly;
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');
                return (plus >= 0 ? info[..plus] : info).Trim();
            }

            return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }
    }

    /// <summary>
    /// Detects the latest published GitHub release and compares it against the running
    /// version. The release's version.json asset is authoritative for the download URL and
    /// SHA-256; the release body becomes the update notes when the manifest has none.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var release = await ReadLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        var manifestAsset = release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, DefaultManifestName, StringComparison.OrdinalIgnoreCase));
        if (manifestAsset == null || string.IsNullOrWhiteSpace(manifestAsset.BrowserDownloadUrl))
        {
            throw new InvalidOperationException("GitHub 最新发布缺少 version.json 资产。");
        }

        var result = await CheckAsync(manifestAsset.BrowserDownloadUrl, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.Manifest.Notes))
        {
            result.Manifest.Notes = (release.Body ?? "").Trim();
        }

        return result;
    }

    private static async Task<GitHubRelease> ReadLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{UserAgentName}/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var json = await client.GetStringAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, MobaDeskJsonContext.Default.GitHubRelease)
            ?? throw new InvalidOperationException("无法解析 GitHub 最新发布信息。");
    }

    public static async Task<UpdateCheckResult> CheckAsync(string feed, CancellationToken cancellationToken)
    {
        var (json, feedUri) = await ReadManifestAsync(feed, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize(json, MobaDeskJsonContext.Default.UpdateManifest)
            ?? throw new InvalidOperationException("更新清单为空。");

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException("更新清单缺少 version 字段。");
        }

        var installerUrl = ResolveInstallerUrl(feedUri, manifest.Url);
        if (string.IsNullOrWhiteSpace(installerUrl))
        {
            throw new InvalidOperationException("更新清单缺少安装包地址。");
        }

        var current = ParseVersion(CurrentVersion);
        var latest = ParseVersion(manifest.Version);
        return new UpdateCheckResult(manifest, installerUrl, latest > current);
    }

    /// <summary>Downloads the installer and verifies its SHA-256. Returns the local path.</summary>
    public static async Task<string> DownloadAsync(UpdateCheckResult update, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var expectedHash = NormalizeHash(update.Manifest.Sha256);
        if (expectedHash.Length != 64)
        {
            throw new InvalidOperationException("更新清单缺少有效的 SHA256 校验值，已拒绝安装。");
        }

        var directory = Path.Combine(Path.GetTempPath(), "MobaDeskUpdate");
        Directory.CreateDirectory(directory);

        var fileName = Path.GetFileName(new Uri(update.InstallerUrl, UriKind.Absolute).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName = $"MobaDesk-Setup-{SanitizeVersion(update.Manifest.Version)}.exe";
        }

        var target = Path.Combine(directory, fileName);
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        using var sha = SHA256.Create();
        if (TryWebUri(update.InstallerUrl, out var webUri))
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"{UserAgentName}/{CurrentVersion}");
            using var response = await client
                .GetAsync(webUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? update.Manifest.Size;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyAndHashAsync(input, output, total, sha, progress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var source = new Uri(update.InstallerUrl, UriKind.Absolute).LocalPath;
            var length = new FileInfo(source).Length;
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyAndHashAsync(input, output, length, sha, progress, cancellationToken).ConfigureAwait(false);
        }

        var actualHash = Convert.ToHexString(sha.Hash ?? Array.Empty<byte>());
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(target);
            throw new InvalidOperationException("更新安装包校验失败（SHA256 不匹配），已删除下载文件。");
        }

        return target;
    }

    /// <summary>Starts the downloaded installer in silent upgrade mode and returns immediately.</summary>
    public static void LaunchInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath(),
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS"
        });
    }

    private static async Task<(string Json, Uri FeedUri)> ReadManifestAsync(string feed,
        CancellationToken cancellationToken)
    {
        if (TryWebUri(feed, out var webUri))
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"{UserAgentName}/{CurrentVersion}");
            var json = await client.GetStringAsync(webUri, cancellationToken).ConfigureAwait(false);
            return (json, webUri);
        }

        var path = feed;
        if (Uri.TryCreate(feed, UriKind.Absolute, out var localUri) && localUri.IsFile)
        {
            path = localUri.LocalPath;
        }
        else if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        path = Path.GetFullPath(path);
        if (Directory.Exists(path))
        {
            path = Path.Combine(path, DefaultManifestName);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"找不到更新清单：{path}", path);
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return (text, new Uri(path));
    }

    private static string ResolveInstallerUrl(Uri feedUri, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "";
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme is "http" or "https" or "file"))
        {
            return absolute.ToString();
        }

        if (feedUri.IsFile)
        {
            var directory = Path.GetDirectoryName(feedUri.LocalPath);
            return string.IsNullOrEmpty(directory) ? url : Path.GetFullPath(Path.Combine(directory, url));
        }

        return new Uri(feedUri, url).ToString();
    }

    private static async Task CopyAndHashAsync(Stream input, Stream output, long total, HashAlgorithm sha,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            sha.TransformBlock(buffer, 0, read, null, 0);
            copied += read;
            if (total > 0)
            {
                progress?.Report(Math.Clamp((double)copied / total, 0, 1));
            }
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        progress?.Report(1);
    }

    private static bool TryWebUri(string value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static Version ParseVersion(string value)
    {
        var text = value.Trim().TrimStart('v', 'V');
        var cut = text.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
        {
            text = text[..cut];
        }

        if (!Version.TryParse(text, out var version))
        {
            throw new FormatException($"无法识别的版本号：{value}");
        }

        return version;
    }

    private static string NormalizeHash(string hash) =>
        (hash ?? "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

    private static string SanitizeVersion(string version)
    {
        var chars = version.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-').ToArray();
        return chars.Length == 0 ? "update" : new string(chars);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // ignored
        }
    }
}
