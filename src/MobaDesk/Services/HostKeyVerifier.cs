using MobaDesk.Models;

namespace MobaDesk.Services;

/// <summary>
/// Trust-on-first-use SSH host key store. Unknown keys and changed keys require an
/// explicit user decision; trusted fingerprints are persisted in known_hosts.json.
/// </summary>
public static class HostKeyVerifier
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _known;

    public static bool Verify(HostKeyInfo info)
    {
        lock (Gate)
        {
            var known = Load();
            var key = $"{info.Host}:{info.Port}";
            known.TryGetValue(key, out var saved);

            if (saved != null && string.Equals(saved, info.Fingerprint, StringComparison.Ordinal))
            {
                return true;
            }

            if (!Prompt(info, saved))
            {
                Log.Warn($"Host key for {key} was not trusted.");
                return false;
            }

            known[key] = info.Fingerprint;
            Save(known);
            return true;
        }
    }

    private static bool Prompt(HostKeyInfo info, string? saved)
    {
        var shell = App.Shell;
        if (shell == null)
        {
            Log.Error("Cannot prompt for an SSH host key because the main window is not ready.");
            return false;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!shell.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                completion.TrySetResult(await shell.ConfirmHostKeyAsync(info, saved));
            }
            catch (Exception ex)
            {
                Log.Error("Host key confirmation dialog failed", ex);
                completion.TrySetResult(false);
            }
        }))
        {
            Log.Error("Cannot enqueue the host key confirmation dialog.");
            return false;
        }

        // The caller is an SSH background thread; blocking here keeps CanTrust
        // synchronous, as required by SSH.NET's HostKeyReceived event. The timeout
        // prevents a hung connection if the window is closing.
        if (!completion.Task.Wait(TimeSpan.FromMinutes(2)))
        {
            Log.Warn($"Host key confirmation for {info.Host}:{info.Port} timed out.");
            return false;
        }

        return completion.Task.Result;
    }

    private static Dictionary<string, string> Load()
    {
        if (_known != null)
        {
            return _known;
        }

        if (JsonStore.TryRead(AppPaths.HostKeysFile, MobaDeskJsonContext.Default.DictionaryStringString,
                out var stored) && stored != null)
        {
            _known = new Dictionary<string, string>(stored, StringComparer.Ordinal);
        }
        else
        {
            _known = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return _known;
    }

    private static void Save(Dictionary<string, string> known)
    {
        try
        {
            JsonStore.Write(AppPaths.HostKeysFile, known, MobaDeskJsonContext.Default.DictionaryStringString);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save known SSH host keys", ex);
        }
    }
}
