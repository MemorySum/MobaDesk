using FluentFTP;
using FluentFTP.Exceptions;
using MobaDesk.Models;

namespace MobaDesk.Services.FileSystems;

public sealed class FtpRemoteFileSystem : IRemoteFileSystem
{
    private readonly ConnectionProfile _profile;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FtpClient? _client;
    private string _initialPath = "/";

    public FtpRemoteFileSystem(ConnectionProfile profile)
    {
        _profile = profile;
    }

    public string KindLabel => _profile.UseTls ? "FTPS" : "FTP";
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

                var config = new FtpConfig
                {
                    EncryptionMode = _profile.UseTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None,
                    // Keep TLS server authentication enabled for FTPS connections.
                    ValidateAnyCertificate = false,
                    DataConnectionType = FtpDataConnectionType.AutoPassive,
                    ConnectTimeout = Math.Max(5000, App.Settings.ConnectionTimeoutSeconds * 1000),
                    ReadTimeout = 30000,
                    DataConnectionConnectTimeout = 30000,
                    DataConnectionReadTimeout = 30000,
                    LogToConsole = false
                };

                var client = new FtpClient(_profile.Host, _profile.Username, credentials.Password ?? "",
                    _profile.Port, config);
                client.Connect();
                _client = client;
                _initialPath = string.IsNullOrWhiteSpace(client.GetWorkingDirectory())
                    ? "/"
                    : client.GetWorkingDirectory();
                Log.Info($"FTP connected to {Endpoint}, path={_initialPath}");
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
            var listing = client.GetListing(path);
            Dictionary<string, int>? permissionMap = null;

            // MLSD often omits Unix permissions; fall back to LIST when the server supports it.
            if (listing.Length > 0 && listing.All(item => ReadUnixMode(item) < 0))
            {
                try
                {
                    permissionMap = client.GetListing(path, FtpListOption.ForceList)
                        .GroupBy(item => item.FullName, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => ReadUnixMode(group.First()),
                            StringComparer.Ordinal);
                }
                catch (Exception ex)
                {
                    Log.Warn($"FTP LIST permission fallback failed: {ex.Message}");
                }
            }

            var entries = new List<RemoteEntry>();
            foreach (var item in listing)
            {
                var mode = permissionMap != null &&
                           permissionMap.TryGetValue(item.FullName, out var fromList)
                    ? fromList
                    : ReadUnixMode(item);

                entries.Add(new RemoteEntry
                {
                    Name = item.Name,
                    FullPath = string.IsNullOrEmpty(item.FullName) ? RemotePath.Combine(path, item.Name) : item.FullName,
                    IsDirectory = item.Type == FtpObjectType.Directory,
                    IsSymlink = item.Type == FtpObjectType.Link,
                    Size = item.Type == FtpObjectType.Directory ? 0 : Math.Max(0, item.Size),
                    Modified = item.Modified,
                    UnixMode = mode
                });
            }

            return (IReadOnlyList<RemoteEntry>)entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static int ReadUnixMode(FtpListItem item)
    {
        var symbolic = FormatUtil.ModeFromSymbolic(item.RawPermissions);
        if (symbolic >= 0)
        {
            return symbolic;
        }

        return item.Chmod > 0 ? item.Chmod & 0xFFF : -1;
    }

    public async Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        return await RunAsync(() =>
        {
            using var memory = new MemoryStream();
            if (!RequireClient().DownloadStream(memory, path))
            {
                throw new IOException(Localization.T("File.Error.FtpDownload", path));
            }

            return memory.ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteFileAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        await RunAsync(() =>
        {
            var status = RequireClient().UploadBytes(content, path, FtpRemoteExists.Overwrite, false);
            if (status == FtpStatus.Failed)
            {
                throw new IOException(Localization.T("File.Error.FtpUpload", path));
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool?> CanWriteAsync(string path, CancellationToken cancellationToken)
    {
        return await RunAsync<bool?>(() =>
        {
            try
            {
                // APPE opens an existing file for writing without truncating it.
                using var stream = RequireClient().OpenAppend(path, FtpDataType.Binary, true);
                return true;
            }
            catch (FtpCommandException ex) when (ex.CompletionCode is "550" or "532" or "553")
            {
                return false;
            }
            catch (Exception ex)
            {
                Log.Warn($"FTP write-permission probe failed for {path}: {ex.Message}");
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
                var status = client.DownloadFile(tempPath, entry.FullPath, FtpLocalExists.Overwrite,
                    FtpVerify.None, p => progress?.Report(Math.Clamp(p.Progress, 0, 100)));
                if (status == FtpStatus.Failed)
                {
                    throw new IOException(Localization.T("File.Error.FtpDownload", entry.FullPath));
                }

                File.Move(tempPath, localPath, true);
                return true;
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Best effort.
                }

                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadAsync(string localPath, string remotePath, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await RunAsync(() =>
        {
            var client = RequireClient();
            var status = client.UploadFile(localPath, remotePath, FtpRemoteExists.Overwrite, true,
                FtpVerify.None, p => progress?.Report(Math.Clamp(p.Progress, 0, 100)));
            if (status == FtpStatus.Failed)
            {
                throw new IOException(Localization.T("File.Error.FtpUpload", remotePath));
            }

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
                client.DeleteDirectory(entry.FullPath, FtpListOption.Recursive);
            }
            else
            {
                client.DeleteFile(entry.FullPath);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
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
            var client = RequireClient();
            if (entry.IsDirectory)
            {
                client.MoveDirectory(entry.FullPath, newPath);
            }
            else
            {
                client.MoveFile(entry.FullPath, newPath);
            }

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

    private FtpClient RequireClient() =>
        _client ?? throw new InvalidOperationException(Localization.T("Error.FtpNotConnected"));

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
                Log.Warn($"FTP disconnect failed: {ex.Message}");
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
