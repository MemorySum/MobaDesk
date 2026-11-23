using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MobaDesk.Services;

public enum JsonReadStatus
{
    NotFound,
    Success,
    Failed
}

/// <summary>
/// Single entry point for every JSON file the app reads or writes.
/// Uses the source-generated <see cref="Models.MobaDeskJsonContext"/> metadata so it stays
/// trimming- and AOT-safe.
/// </summary>
public static class JsonStore
{
    public static T? Read<T>(string path, JsonTypeInfo<T> typeInfo)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, typeInfo);
    }

    /// <summary>
    /// Reads a JSON file and distinguishes a missing file from an unreadable one.
    /// An unreadable file is copied to a ".corrupt-*.bak" sibling before the caller
    /// can decide whether to overwrite it.
    /// </summary>
    public static JsonReadStatus ReadStatus<T>(string path, JsonTypeInfo<T> typeInfo, out T? value)
    {
        value = default;
        if (!File.Exists(path))
        {
            return JsonReadStatus.NotFound;
        }

        try
        {
            value = Read(path, typeInfo);
            return value != null ? JsonReadStatus.Success : JsonReadStatus.Failed;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to read JSON '{path}'", ex);
            BackupUnreadable(path);
            return JsonReadStatus.Failed;
        }
    }

    /// <summary>Reads a JSON file, returning false for a missing or invalid file.</summary>
    public static bool TryRead<T>(string path, JsonTypeInfo<T> typeInfo, out T? value) =>
        TryRead(path, typeInfo, out value, out _);

    /// <summary>
    /// Reads a JSON file. <paramref name="failed"/> is true when the file exists but
    /// could not be parsed, so callers can avoid overwriting it with empty data.
    /// </summary>
    public static bool TryRead<T>(string path, JsonTypeInfo<T> typeInfo, out T? value, out bool failed)
    {
        var status = ReadStatus(path, typeInfo, out value);
        failed = status == JsonReadStatus.Failed;
        return status == JsonReadStatus.Success;
    }

    private static void BackupUnreadable(string path)
    {
        try
        {
            var backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".bak";
            File.Copy(path, backup, overwrite: false);
            Log.Warn($"Unreadable JSON backed up to '{backup}'.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not back up unreadable JSON '{path}': {ex.Message}");
        }
    }

    public static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(value, typeInfo);
        WriteAtomic(path, json);
    }

    /// <summary>Writes through a temporary file so a crash cannot leave a half-written document.</summary>
    public static void WriteAtomic(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, true);
    }
}
