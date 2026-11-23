using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using MobaDesk.Services;

namespace MobaDesk.Models;

public sealed class RemoteEntry : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool IsSymlink { get; set; }
    public long Size { get; set; }
    public DateTime Modified { get; set; }

    /// <summary>Unix permission bits (0x0000-0x0FFF), or -1 when the server does not report them.</summary>
    public int UnixMode { get; set; } = -1;

    public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE8A5";
    public string IconColorHex => FileIconPalette.ColorFor(Name, IsDirectory, IsSymlink);
    public Brush IconBrush => FileIconPalette.BrushFor(Name, IsDirectory, IsSymlink);
    public string KindLabel => Localization.T(IsDirectory ? "File.Kind.Folder" : "File.Kind.File");
    public string SizeText => IsDirectory ? "—" : FormatUtil.Size(Size);
    public string ModifiedText => Modified == default ? "—" : Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string PermissionsText => FormatUtil.Permissions(UnixMode, IsDirectory, IsSymlink);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class FormatUtil
{
    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    /// <summary>Formats Unix permission bits as the familiar <c>drwxr-xr-x</c> string.</summary>
    public static string Permissions(int mode, bool isDirectory, bool isSymlink)
    {
        if (mode < 0)
        {
            return "—";
        }

        var text = new char[10];
        text[0] = isSymlink ? 'l' : isDirectory ? 'd' : '-';
        text[1] = (mode & 0x100) != 0 ? 'r' : '-';
        text[2] = (mode & 0x080) != 0 ? 'w' : '-';
        text[3] = ExecChar(mode, 0x040, 0x800, 's', 'S');
        text[4] = (mode & 0x020) != 0 ? 'r' : '-';
        text[5] = (mode & 0x010) != 0 ? 'w' : '-';
        text[6] = ExecChar(mode, 0x008, 0x400, 's', 'S');
        text[7] = (mode & 0x004) != 0 ? 'r' : '-';
        text[8] = (mode & 0x002) != 0 ? 'w' : '-';
        text[9] = ExecChar(mode, 0x001, 0x200, 't', 'T');
        return new string(text);
    }

    /// <summary>Parses a symbolic permission string such as <c>rwxr-xr-x</c> into Unix bits.</summary>
    public static int ModeFromSymbolic(string? permissions)
    {
        if (string.IsNullOrWhiteSpace(permissions))
        {
            return -1;
        }

        var text = permissions.Length >= 9 ? permissions[^9..] : permissions;
        if (text.Length != 9)
        {
            return -1;
        }

        int[] specialMasks = { 0x800, 0x400, 0x200 };
        var mode = 0;
        for (var index = 0; index < 9; index++)
        {
            var column = index % 3;
            var bit = 1 << (8 - index);
            var value = text[index];

            if (column == 0 && value is 'r' or 'R')
            {
                mode |= bit;
            }
            else if (column == 1 && value is 'w' or 'W')
            {
                mode |= bit;
            }
            else if (column == 2)
            {
                if (value is 'x' or 'X')
                {
                    mode |= bit;
                }
                else if (value is 's' or 'S' or 't' or 'T')
                {
                    mode |= specialMasks[index / 3];
                    if (value is 's' or 't')
                    {
                        mode |= bit;
                    }
                }
            }
        }

        return mode;
    }

    private static char ExecChar(int mode, int executeMask, int specialMask, char special, char specialNoExecute)
    {
        var canExecute = (mode & executeMask) != 0;
        if ((mode & specialMask) != 0)
        {
            return canExecute ? special : specialNoExecute;
        }

        return canExecute ? 'x' : '-';
    }

    public static string Rate(double bytesPerSecond) => Size((long)Math.Max(0, bytesPerSecond)) + "/s";

    public static string Uptime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalDays >= 1)
        {
            return Localization.T("Format.UptimeDays", (int)t.TotalDays, t.Hours, t.Minutes);
        }

        return Localization.T("Format.UptimeHours", t.Hours, t.Minutes);
    }
}
