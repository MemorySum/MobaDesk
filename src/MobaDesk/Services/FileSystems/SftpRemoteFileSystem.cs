using MobaDesk.Models;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace MobaDesk.Services.FileSystems;

public sealed class SftpRemoteFileSystem : IRemoteFileSystem
{
    private readonly ConnectionProfile _profile;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SftpClient? _client;
    private string _initialPath = "/";

    public SftpRemoteFileSystem(ConnectionProfile profile)
    {
        _profile = profile;
    }

    public string KindLabel => "SFTP";
    public string Endpoint => $"{_profile.Username}@{_profile.Host}:{_profile.Port}";
    public string InitialPath => _initialPath;

    public async Task ConnectAsync(SshCredentials credentials, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                _client?.Dispose();
                var connectionInfo = SshAuth.CreateConnectionInfo(_profile, credentials);
                var client = new SftpClient(connectionInfo)
                {
                    OperationTimeout = TimeSpan.FromSeconds(30)
                };
                SshAuth.AttachHostKeyHandler(client, _profile);
                client.Connect();
                _client = client;
                _initialPath = string.IsNullOrWhiteSpace(client.WorkingDirectory) ? "/" : client.WorkingDirectory;
                Log.Info($"SFTP connected to {Endpoint}, home={_initialPath}");
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken)
    {
        return await RunAsync(() =>
        {
            var client = RequireClient();
            var entries = new List<RemoteEntry>();
            foreach (var file in client.ListDirectory(path))
            {
                if (file.Name is "." or "..")
                {
                    continue;
                }

                entries.Add(new RemoteEntry
                {
                    Name = file.Name,
                    FullPath = string.IsNullOrEmpty(file.FullName) ? RemotePath.Combine(path, file.Name) : file.FullName,
                    IsDirectory = file.IsDirectory,
                    IsSymlink = file.IsSymbolicLink,
                    Size = file.IsDirectory ? 0 : (long)file.Length,
                    Modified = file.LastWriteTimeUtc,
                    UnixMode = ReadUnixMode(file.Attributes)
                });
            }

            return (IReadOnlyList<RemoteEntry>)entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static int ReadUnixMode(SftpFileAttributes attributes)
    {
        var mode = 0;
        if (attributes.OwnerCanRead) mode |= 0x100;
        if (attributes.OwnerCanWrite) mode |= 0x080;
        if (attributes.OwnerCanExecute) mode |= 0x040;
        if (attributes.GroupCanRead) mode |= 0x020;
        if (attributes.GroupCanWrite) mode |= 0x010;
        if (attributes.GroupCanExecute) mode |= 0x008;
        if (attributes.OthersCanRead) mode |= 0x004;
        if (attributes.OthersCanWrite) mode |= 0x002;
        if (attributes.OthersCanExecute) mode |= 0x001;
        if (attributes.IsUIDBitSet) mode |= 0x800;
        if (attributes.IsGroupIDBitSet) mode |= 0x400;
        if (attributes.IsStickyBitSet) mode |= 0x200;
        return mode;
    }

    public async Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        return await RunAsync(() =>
        {
            using var stream = RequireClient().OpenRead(path);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteFileAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        await RunAsync(() =>
        {
            var client = RequireClient();

            // Replacing a symlink with a regular file would detach (for example)
            // sites-enabled -> sites-available, so write through the link instead.
            if (IsSymbolicLink(client, path))
            {
                WriteDirect(client, path, content);
                return true;
            }

            // Write to a sibling first so a broken connection cannot truncate the original file.
            var tempPath = RemotePath.Combine(RemotePath.Parent(path),
                $".mobadesk-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = client.OpenWrite(tempPath))
                {
                    stream.Write(content, 0, content.Length);
                }

                PreservePermissions(client, path, tempPath);
                // POSIX rename replaces the destination atomically.
                client.RenameFile(tempPath, path, true);
                return true;
            }
            catch (Exception ex) when (ex is SftpPermissionDeniedException or UnauthorizedAccessException)
            {
                // Some servers allow writing the file but not creating siblings; preserve
                // the old behaviour rather than breaking saves in such directories.
                TryDeleteFile(client, tempPath);
                WriteDirect(client, path, content);
                return true;
            }
            catch
            {
                TryDeleteFile(client, tempPath);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteDirect(SftpClient client, string path, byte[] content)
    {
        using var stream = client.OpenWrite(path);
        stream.Write(content, 0, content.Length);
    }

    private static bool IsSymbolicLink(SftpClient client, string path)
    {
        try
        {
            // This SSH.NET version has no readlink; a directory listing reports the
            // link flag without following the link.
            var parent = RemotePath.Parent(path);
            var name = RemotePath.Name(path);
            return client.ListDirectory(parent)
                .Any(entry => entry.Name == name && entry.IsSymbolicLink);
        }
        catch
        {
            return false;
        }
    }

    private static void PreservePermissions(SftpClient client, string sourcePath, string tempPath)
    {
        try
        {
            var source = client.GetAttributes(sourcePath);
            var temp = client.GetAttributes(tempPath);
            temp.OwnerCanRead = source.OwnerCanRead;
            temp.OwnerCanWrite = source.OwnerCanWrite;
            temp.OwnerCanExecute = source.OwnerCanExecute;
            temp.GroupCanRead = source.GroupCanRead;
            temp.GroupCanWrite = source.GroupCanWrite;
            temp.GroupCanExecute = source.GroupCanExecute;
            temp.OthersCanRead = source.OthersCanRead;
            temp.OthersCanWrite = source.OthersCanWrite;
            temp.OthersCanExecute = source.OthersCanExecute;
            temp.IsUIDBitSet = source.IsUIDBitSet;
            temp.IsGroupIDBitSet = source.IsGroupIDBitSet;
            temp.IsStickyBitSet = source.IsStickyBitSet;
            client.SetAttributes(tempPath, temp);
        }
        catch (Exception ex)
        {
            // Permission bits are best effort; never fail a save over them.
            Log.Warn($"Could not preserve permissions for {sourcePath}: {ex.Message}");
        }
    }

    private static void TryDeleteFile(SftpClient client, string path)
    {
        try
        {
            client.DeleteFile(path);
        }
        catch
        {
            // Best effort; the temp file is hidden and will be cleaned up later.
        }
    }

    public async Task<bool?> CanWriteAsync(string path, CancellationToken cancellationToken)
    {
        return await RunAsync<bool?>(() =>
        {
            try
            {
                // FileMode.Open never truncates; it only asks the server to open the existing file for writing.
                using var stream = RequireClient().Open(path, FileMode.Open, FileAccess.Write);
                return true;
            }
            catch (SftpPermissionDeniedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (Exception ex) when (ex is SshException or IOException)
            {
                Log.Warn($"SFTP write-permission probe failed for {path}: {ex.Message}");
                return null;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadAsync(RemoteEntry entry, string localPath, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await RunAsync(() =>
        {
            var client = RequireClient();
            var tempPath = localPath + ".part";
            try
            {
                using (var stream = File.Create(tempPath))
                {
                    var size = Math.Max(0, entry.Size);
                    client.DownloadFile(entry.FullPath, stream, transferred =>
                    {
                        if (size > 0)
                        {
                            progress?.Report(Math.Min(100, transferred * 100.0 / size));
                        }
                    });
                }

                File.Move(tempPath, localPath, true);
                return true;
            }
            catch
            {
                TryDeleteLocalFile(tempPath);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void TryDeleteLocalFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    public async Task UploadAsync(string localPath, string remotePath, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await RunAsync(() =>
        {
            var client = RequireClient();
            using var stream = File.OpenRead(localPath);
            var size = stream.Length;
            client.UploadFile(stream, remotePath, true, transferred =>
            {
                if (size > 0)
                {
                    progress?.Report(Math.Min(100, transferred * 100.0 / size));
                }
            });
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken)
    {
        await RunAsync(() =>
        {
            var client = RequireClient();
            if (entry.IsDirectory)
            {
                DeleteDirectoryRecursive(client, entry.FullPath);
            }
            else
            {
                client.DeleteFile(entry.FullPath);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteDirectoryRecursive(SftpClient client, string path)
    {
        foreach (var file in client.ListDirectory(path))
        {
            if (file.Name is "." or "..")
            {
                continue;
            }

            if (file.IsDirectory)
            {
                DeleteDirectoryRecursive(client, file.FullName);
            }
            else
            {
                client.DeleteFile(file.FullName);
            }
        }

        client.DeleteDirectory(path);
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        await RunAsync(() =>
        {
            RequireClient().CreateDirectory(path);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameAsync(RemoteEntry entry, string newPath, CancellationToken cancellationToken)
    {
        await RunAsync(() =>
        {
            RequireClient().RenameFile(entry.FullPath, newPath, true);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(action, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private SftpClient RequireClient() =>
        _client ?? throw new InvalidOperationException(Localization.T("Error.SftpNotConnected"));

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                if (_client?.IsConnected == true)
                {
                    _client.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"SFTP disconnect failed: {ex.Message}");
            }

            _client?.Dispose();
            _client = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
