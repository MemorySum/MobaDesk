using System.Diagnostics;
using System.Text;
using MobaDesk.Models;

namespace MobaDesk.Services;

/// <summary>
/// Launches the Windows RDP client (mstsc) with a generated .rdp file.
/// The password is embedded as a DPAPI blob, the same format mstsc writes itself.
/// </summary>
public static class RdpLauncher
{
    public static string CreateRdpFile(ConnectionProfile profile, string? password)
    {
        var directory = Path.Combine(AppPaths.DataDirectory, "rdp");
        Directory.CreateDirectory(directory);

        var safeName = string.Concat(profile.Name.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "session";
        }

        var path = Path.Combine(directory, $"{safeName}-{profile.Id}.rdp");
        var rdp = profile.Rdp ?? new RdpSettings();
        var lines = new List<string>
        {
            $"full address:s:{profile.Host}:{profile.Port}",
            $"username:s:{profile.Username}",
            $"domain:s:{rdp.Domain}",
            $"screen mode id:i:{(rdp.FullScreen ? 2 : 1)}",
            $"session bpp:i:32",
            $"compression:i:1",
            "bandwidthautodetect:i:1",
            "networkautodetect:i:1",
            "connection type:i:7",
            "autoreconnection enabled:i:1",
            // 2 = do not connect when server authentication fails (0 would suppress the warning).
            "authentication level:i:2",
            "negotiate security layer:i:1",
            "enablecredsspsupport:i:1",
            "promptcredentialonce:i:1",
            "bitmapcachepersistenable:i:1",
            "keyboardhook:i:2",
            "audiomode:i:0",
            "audiocapturemode:i:0",
            $"redirectclipboard:i:{(rdp.RedirectClipboard ? 1 : 0)}",
            "redirectprinters:i:0",
            "redirectcomports:i:0",
            "redirectsmartcards:i:0",
            $"redirectdrives:i:{(rdp.RedirectDrives ? 1 : 0)}",
            "smart sizing:i:1",
            "use multimon:i:0"
        };

        if (!rdp.FullScreen)
        {
            lines.Add($"desktopwidth:i:{Math.Clamp(rdp.Width, 640, 7680)}");
            lines.Add($"desktopheight:i:{Math.Clamp(rdp.Height, 480, 4320)}");
        }

        if (rdp.RedirectDrives)
        {
            lines.Add("drivestoredirect:s:*");
        }

        if (!string.IsNullOrEmpty(password))
        {
            lines.Add("password 51:b:" + CredentialProtector.ProtectToHex(password));
            lines.Add("prompt for credentials:i:0");
        }
        else
        {
            lines.Add("prompt for credentials:i:1");
        }

        File.WriteAllLines(path, lines, Encoding.Unicode);
        return path;
    }

    public static void Launch(ConnectionProfile profile, string? password)
    {
        var rdpFile = CreateRdpFile(profile, password);
        Process.Start(new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            Arguments = $"\"{rdpFile}\"",
            UseShellExecute = true
        });
        Log.Info($"RDP session launched for {profile.Host}:{profile.Port} using {rdpFile}");
    }
}
