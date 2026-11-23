using MobaDesk.Models;
using Renci.SshNet;

namespace MobaDesk.Services;

public sealed class SshSession : IDisposable
{
    private readonly object _stateLock = new();
    private SshClient? _client;
    private ShellStream? _shell;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;
    private int _closedRaised;

    public event Action<byte[]>? Output;
    public event Action<string>? Closed;
    public event Action<string>? HostKey;

    public string Fingerprint { get; private set; } = "";
    public bool IsConnected => _client?.IsConnected == true;

    public async Task ConnectAsync(ConnectionProfile profile, SshCredentials credentials, int columns, int rows,
        CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            CloseInternals();
        }

        var connection = await Task.Run(() =>
        {
            var client = SshAuth.CreateClient(profile, credentials, fp =>
            {
                Fingerprint = fp;
                HostKey?.Invoke(fp);
            });

            try
            {
                client.Connect();
                if (!client.IsConnected)
                {
                    throw new InvalidOperationException(Localization.T("Error.SshRejected"));
                }

                var shell = client.CreateShellStream("xterm-256color",
                    (uint)Math.Max(20, columns), (uint)Math.Max(5, rows), 0, 0, 32 * 1024);
                return (Client: client, Shell: shell);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);

        CancellationToken readToken;
        lock (_stateLock)
        {
            // The tab may have been closed while the handshake was still running.
            // Release the newly established connection instead of leaking it.
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                DisposeConnection(connection.Client, connection.Shell);
                return;
            }

            _client = connection.Client;
            _shell = connection.Shell;
            _cts = new CancellationTokenSource();
            readToken = _cts.Token;
            Interlocked.Exchange(ref _closedRaised, 0);
        }

        connection.Client.ErrorOccurred += (_, e) =>
        {
            if (ReferenceEquals(_client, connection.Client))
            {
                RaiseClosed(e.Exception.Message);
            }
        };
        _ = Task.Run(() => ReadLoop(readToken));
    }

    private static void DisposeConnection(SshClient client, ShellStream shell)
    {
        try
        {
            shell.Dispose();
        }
        catch
        {
            // ignored
        }

        try
        {
            client.Dispose();
        }
        catch
        {
            // ignored
        }
    }

    private void ReadLoop(CancellationToken token)
    {
        var shell = _shell;
        var client = _client;
        if (shell == null || client == null)
        {
            return;
        }

        var buffer = new byte[32 * 1024];
        try
        {
            while (!token.IsCancellationRequested && !_disposed)
            {
                var read = 0;
                try
                {
                    if (shell.DataAvailable)
                    {
                        read = shell.Read(buffer, 0, buffer.Length);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (read > 0)
                {
                    Output?.Invoke(buffer[..read]);
                    continue;
                }

                if (!client.IsConnected)
                {
                    if (ReferenceEquals(client, _client))
                    {
                        RaiseClosed(Localization.T("Error.SshDisconnected"));
                    }

                    break;
                }

                Thread.Sleep(8);
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_disposed && ReferenceEquals(client, _client))
            {
                RaiseClosed(SshAuth.Describe(ex));
            }
        }
    }

    public void Write(byte[] data)
    {
        var shell = _shell;
        if (shell == null || data.Length == 0)
        {
            return;
        }

        try
        {
            shell.Write(data, 0, data.Length);
            shell.Flush();
        }
        catch (Exception ex)
        {
            Log.Warn($"SSH write failed: {ex.Message}");
        }
    }

    public void WriteText(string text) => Write(System.Text.Encoding.UTF8.GetBytes(text));

    public void Resize(int columns, int rows)
    {
        try
        {
            _shell?.ChangeWindowSize((uint)Math.Max(20, columns), (uint)Math.Max(5, rows), 0, 0);
        }
        catch (Exception ex)
        {
            Log.Warn($"SSH resize failed: {ex.Message}");
        }
    }

    private void RaiseClosed(string message)
    {
        // A single disconnect can be observed by both ErrorOccurred and ReadLoop.
        if (_disposed || Interlocked.Exchange(ref _closedRaised, 1) != 0)
        {
            return;
        }

        Closed?.Invoke(message);
    }

    private void CloseInternals()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignored
        }

        try
        {
            _shell?.Dispose();
        }
        catch
        {
            // ignored
        }

        try
        {
            _client?.Dispose();
        }
        catch
        {
            // ignored
        }

        _shell = null;
        _client = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            _disposed = true;
            CloseInternals();
        }
    }
}
