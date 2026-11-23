using System.Collections.ObjectModel;
using MobaDesk.Models;

namespace MobaDesk.Services;

public sealed class ProfileStore
{
    private bool _loaded;
    private bool _profilesUnreadable;
    private bool _foldersUnreadable;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();

    /// <summary>Session folders shown in the sidebar, ordered as created.</summary>
    public ObservableCollection<string> Folders { get; } = new();

    public string Path => AppPaths.ProfilesFile;

    /// <summary>True when profiles.json existed but could not be read; it will not be overwritten.</summary>
    public bool ProfilesUnreadable => _profilesUnreadable;

    /// <summary>True when folders.json existed but could not be read; it will not be overwritten.</summary>
    public bool FoldersUnreadable => _foldersUnreadable;

    public void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        LoadProfiles();
        LoadFolders();
        EnsureFoldersFromProfiles();
    }

    private void LoadProfiles()
    {
        if (!JsonStore.TryRead(AppPaths.ProfilesFile,
                MobaDeskJsonContext.Default.ListConnectionProfile, out var items, out var failed) || items == null)
        {
            if (failed)
            {
                _profilesUnreadable = true;
                Log.Error($"Profiles file '{AppPaths.ProfilesFile}' is unreadable; saving is disabled to protect it.");
            }

            return;
        }

        foreach (var item in items)
        {
            item.Rdp ??= new RdpSettings();
            Profiles.Add(item);
        }
    }

    private void LoadFolders()
    {
        if (!JsonStore.TryRead(AppPaths.FoldersFile, MobaDeskJsonContext.Default.ListString,
                out var items, out var failed) || items == null)
        {
            if (failed)
            {
                _foldersUnreadable = true;
                Log.Error($"Folders file '{AppPaths.FoldersFile}' is unreadable; saving is disabled to protect it.");
            }

            return;
        }

        foreach (var item in items)
        {
            var name = NormalizeFolderName(item);
            if (name.Length > 0 && !Folders.Contains(name))
            {
                Folders.Add(name);
            }
        }
    }

    public void Save()
    {
        if (_profilesUnreadable)
        {
            Log.Error($"Refusing to overwrite unreadable profiles file '{AppPaths.ProfilesFile}'.");
            return;
        }

        try
        {
            JsonStore.Write(AppPaths.ProfilesFile, Profiles.ToList(),
                MobaDeskJsonContext.Default.ListConnectionProfile);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save profiles", ex);
        }
    }

    public void SaveFolders()
    {
        if (_foldersUnreadable)
        {
            Log.Error($"Refusing to overwrite unreadable folders file '{AppPaths.FoldersFile}'.");
            return;
        }

        try
        {
            JsonStore.Write(AppPaths.FoldersFile, Folders.ToList(), MobaDeskJsonContext.Default.ListString);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save folders", ex);
        }
    }

    public void Add(ConnectionProfile profile)
    {
        Profiles.Add(profile);
        Save();
    }

    public void Remove(ConnectionProfile profile)
    {
        Profiles.Remove(profile);
        Save();
    }

    public ConnectionProfile? FindById(string id) => Profiles.FirstOrDefault(p => p.Id == id);

    /// <summary>Adds a folder if the name is new. Returns false when it already exists.</summary>
    public bool EnsureFolder(string? name)
    {
        var folder = NormalizeFolderName(name);
        if (folder.Length == 0 || Folders.Contains(folder))
        {
            return false;
        }

        Folders.Add(folder);
        SaveFolders();
        return true;
    }

    public bool MoveToFolder(ConnectionProfile profile, string? folder)
    {
        var target = NormalizeFolderName(folder);
        if (target.Length > 0)
        {
            EnsureFolder(target);
        }

        if ((profile.Group ?? "") == target)
        {
            return false;
        }

        profile.Group = target;
        Save();
        return true;
    }

    public bool RenameFolder(string oldName, string newName)
    {
        var target = NormalizeFolderName(newName);
        var index = Folders.IndexOf(oldName);
        if (index < 0 || target.Length == 0 || target == oldName || Folders.Contains(target))
        {
            return false;
        }

        Folders[index] = target;
        foreach (var profile in Profiles.Where(p => p.Group == oldName))
        {
            profile.Group = target;
        }

        SaveFolders();
        Save();
        return true;
    }

    /// <summary>Removes a folder; sessions inside are moved back to the root.</summary>
    public bool DeleteFolder(string name)
    {
        if (!Folders.Remove(name))
        {
            return false;
        }

        foreach (var profile in Profiles.Where(p => p.Group == name))
        {
            profile.Group = "";
        }

        SaveFolders();
        Save();
        return true;
    }

    private void EnsureFoldersFromProfiles()
    {
        var foldersAdded = false;
        var profilesChanged = false;
        foreach (var profile in Profiles)
        {
            var group = NormalizeFolderName(profile.Group);
            if (profile.Group != group)
            {
                // Write the normalized name back: the sidebar matches folders exactly,
                // so a value like " Work " would otherwise hide the session entirely.
                profile.Group = group;
                profilesChanged = true;
            }

            if (group.Length == 0)
            {
                continue;
            }

            if (!Folders.Contains(group))
            {
                Folders.Add(group);
                foldersAdded = true;
            }
        }

        if (foldersAdded)
        {
            SaveFolders();
        }

        if (profilesChanged)
        {
            Save();
        }
    }

    private static string NormalizeFolderName(string? name) => (name ?? "").Trim();
}
