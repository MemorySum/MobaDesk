using System.Security.Cryptography;
using MobaDesk.Models;
using Renci.SshNet;

namespace MobaDesk.Services;

/// <summary>Host key presented by an SSH server, used for known_hosts verification.</summary>
public sealed record HostKeyInfo(string Host, int Port, string Fingerprint);

public sealed class SshCredentials
{
    public string? Password { get; set; }
    public string? Passphrase { get; set; }

    public SshCredentials()
    {
    }

    public SshCredentials(string? password, string? passphrase = null)
    {
        Password = password;
        Passphrase = passphrase;
    }
}

public static class SshAuth
{
    /// <summary>
    /// Verifies a server host key. The application installs a trust-on-first-use
    /// verifier at startup; when none is configured, keys are rejected.
    /// </summary>
    public static Func<HostKeyInfo, bool>? HostKeyVerifier { get; set; }

    public static ConnectionInfo CreateConnectionInfo(ConnectionProfile profile, SshCredentials credentials)
    {
        var password = credentials.Password ?? "";
        var methods = new List<AuthenticationMethod>();

        if (profile.Auth == AuthKind.PrivateKey)
        {
            if (string.IsNullOrWhiteSpace(profile.PrivateKeyPath))
            {
                throw new InvalidOperationException(Localization.T("Error.PrivateKeyPath"));
            }

            var keyPath = Environment.ExpandEnvironmentVariables(profile.PrivateKeyPath.Trim());
            if (!File.Exists(keyPath))
            {
                throw new FileNotFoundException(Localization.T("Error.PrivateKeyMissing", keyPath));
            }

            var keyFile = new PrivateKeyFile(keyPath, credentials.Passphrase ?? "");
            methods.Add(new PrivateKeyAuthenticationMethod(profile.Username, keyFile));
        }
        else
        {
            methods.Add(new PasswordAuthenticationMethod(profile.Username, password));
        }

        var keyboardInteractive = new KeyboardInteractiveAuthenticationMethod(profile.Username);
        keyboardInteractive.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
            {
                prompt.Response = password;
            }
        };
        methods.Add(keyboardInteractive);

        return new ConnectionInfo(profile.Host, profile.Port, profile.Username, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(App.Settings.ConnectionTimeoutSeconds)
        };
    }

    public static SshClient CreateClient(ConnectionProfile profile, SshCredentials credentials,
        Action<string>? onFingerprint = null)
    {
        var client = new SshClient(CreateConnectionInfo(profile, credentials));
        AttachHostKeyHandler(client, profile, onFingerprint);
        return client;
    }

    /// <summary>
    /// Wires host key verification into any SSH.NET client (SshClient, SftpClient, ...).
    /// Without a configured verifier the key is rejected instead of being trusted blindly.
    /// </summary>
    public static void AttachHostKeyHandler(BaseClient client, ConnectionProfile profile,
        Action<string>? onFingerprint = null)
    {
        client.HostKeyReceived += (_, e) =>
        {
            // HostKeyEventArgs.FingerPrint is the legacy MD5 digest; use the SHA256
            // property so the fingerprint shown to the user matches `ssh-keygen -l`.
            var fingerprint = !string.IsNullOrWhiteSpace(e.FingerPrintSHA256)
                ? "SHA256:" + e.FingerPrintSHA256
                : e.HostKey is { Length: > 0 }
                    ? "SHA256:" + Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=')
                    : "";

            if (onFingerprint != null && fingerprint.Length > 0)
            {
                onFingerprint(fingerprint);
            }

            var verifier = HostKeyVerifier;
            if (verifier == null)
            {
                Log.Error($"No host key verifier is configured; rejecting {profile.Host}:{profile.Port}.");
                e.CanTrust = false;
                return;
            }

            try
            {
                e.CanTrust = verifier(new HostKeyInfo(profile.Host, profile.Port, fingerprint));
            }
            catch (Exception ex)
            {
                Log.Error($"Host key verification failed for {profile.Host}:{profile.Port}", ex);
                e.CanTrust = false;
            }
        };
    }

    public static string Describe(Exception ex) => ex switch
    {
        Renci.SshNet.Common.SshAuthenticationException => Localization.T("Error.AuthFailed", ex.Message),
        Renci.SshNet.Common.SshConnectionException => Localization.T("Error.SshConnection", ex.Message),
        System.Net.Sockets.SocketException => Localization.T("Error.Network", ex.Message),
        _ => ex.Message
    };
}
