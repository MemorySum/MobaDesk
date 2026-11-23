using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MobaDesk.Services;

public static class ThemeService
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;

    public static bool IsDarkEffective(string theme)
    {
        return theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsSystemDark()
        };
    }

    public static ElementTheme ToElementTheme(string theme) => theme switch
    {
        "Dark" => ElementTheme.Dark,
        "Light" => ElementTheme.Light,
        _ => ElementTheme.Default
    };

    public static void ApplyTheme(Window window, FrameworkElement root, string theme)
    {
        var isDark = IsDarkEffective(theme);
        root.RequestedTheme = ToElementTheme(theme);
        ApplyDwmDarkMode(window, isDark);
        ApplyCaptionButtonColors(window, isDark);
    }

    /// <summary>
    /// Applies the requested backdrop. Returns false when the material is unavailable
    /// and the app falls back to a solid background.
    /// </summary>
    public static bool ApplyMaterial(Window window, Panel root, string material, bool isDark)
    {
        SystemBackdrop? backdrop = null;
        try
        {
            backdrop = material switch
            {
                "Mica" when MicaController.IsSupported() => new MicaBackdrop { Kind = MicaKind.Base },
                "MicaAlt" when MicaController.IsSupported() => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                "Acrylic" when DesktopAcrylicController.IsSupported() => new DesktopAcrylicBackdrop(),
                _ => null
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"Backdrop '{material}' unavailable: {ex.Message}");
            backdrop = null;
        }

        var previous = window.SystemBackdrop;
        window.SystemBackdrop = backdrop;
        if (!ReferenceEquals(previous, backdrop))
        {
            try
            {
                (previous as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to dispose previous backdrop: {ex.Message}");
            }
        }

        root.Background = backdrop == null
            ? SolidFallback(isDark)
            : MaterialTint(material, isDark);

        return backdrop != null || material == "Solid";
    }

    /// <summary>
    /// Mica and Mica Alt sample the same wallpaper, so in practice they can look almost
    /// identical. A small theme-aware veil makes the difference between the materials
    /// visible while keeping the backdrop itself untouched.
    /// </summary>
    private static Brush MaterialTint(string material, bool isDark) => new SolidColorBrush(material switch
    {
        "MicaAlt" => isDark
            ? Color.FromArgb(0x1F, 0x38, 0xBD, 0xF8)
            : Color.FromArgb(0x1A, 0x38, 0xBD, 0xF8),
        "Acrylic" => isDark
            ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF),
        _ => Colors.Transparent
    });

    private static Brush SolidFallback(bool isDark) =>
        new SolidColorBrush(isDark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243));

    public static void ApplyDialogTheme(ContentDialog dialog)
    {
        dialog.RequestedTheme = App.Shell?.CurrentElementTheme ?? ElementTheme.Default;
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyCaptionButtonColors(Window window, bool isDark)
    {
        try
        {
            var titleBar = window.AppWindow.TitleBar;
            var foreground = isDark ? Colors.White : Color.FromArgb(255, 28, 28, 28);
            var inactive = isDark ? Color.FromArgb(255, 160, 160, 160) : Color.FromArgb(255, 120, 120, 120);

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = inactive;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Color.FromArgb(40, 255, 255, 255)
                : Color.FromArgb(24, 0, 0, 0);
            titleBar.ButtonPressedBackgroundColor = isDark
                ? Color.FromArgb(70, 255, 255, 255)
                : Color.FromArgb(40, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to set caption button colors: {ex.Message}");
        }
    }

    private static void ApplyDwmDarkMode(Window window, bool isDark)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var value = isDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeLegacy, ref value, sizeof(int));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to apply DWM dark mode: {ex.Message}");
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
