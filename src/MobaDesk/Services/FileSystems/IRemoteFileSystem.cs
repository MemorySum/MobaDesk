using MobaDesk.Models;

namespace MobaDesk.Services.FileSystems;

public interface IRemoteFileSystem : IAsyncDisposable
{
    string KindLabel { get; }
    string Endpoint { get; }
    string InitialPath { get; }

    Task ConnectAsync(SshCredentials credentials, CancellationToken cancellationToken);
    Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken);
    Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken);
    Task WriteFileAsync(string path, byte[] content, CancellationToken cancellationToken);
    Task<bool?> CanWriteAsync(string path, CancellationToken cancellationToken);
    Task DownloadAsync(RemoteEntry entry, string localPath, IProgress<double>? progress, CancellationToken cancellationToken);
    Task UploadAsync(string localPath, string remotePath, IProgress<double>? progress, CancellationToken cancellationToken);
    Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken);
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);
    Task RenameAsync(RemoteEntry entry, string newPath, CancellationToken cancellationToken);
}

public static class RemotePath
{
    public static string Combine(string directory, string name)
    {
        if (string.IsNullOrEmpty(directory) || directory == "/")
        {
            return "/" + name.TrimStart('/');
        }

        return directory.TrimEnd('/') + "/" + name.TrimStart('/');
    }

    public static string Parent(string path)
    {
        var trimmed = (path ?? "/").TrimEnd('/');
        if (string.IsNullOrEmpty(trimmed))
        {
            return "/";
        }

        var index = trimmed.LastIndexOf('/');
        return index <= 0 ? "/" : trimmed[..index];
    }

    public static string Name(string path)
    {
        var trimmed = (path ?? "").TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        path = path.Trim();
        return path.StartsWith('/') ? path : "/" + path;
    }
}
