using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MobaDesk.Ui;
using MobaDesk.Models;
using MobaDesk.Services;

namespace MobaDesk.Views;

public sealed partial class RdpView : UserControl, IDisposable
{
    private readonly ConnectionProfile _profile;

    public RdpView(ConnectionProfile profile)
    {
        InitializeComponent();
        _profile = profile;
        Loc.Register(this);
        Localization.LanguageChanged += OnLanguageChanged;
        Populate();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Loc.ApplyTree(this);
        UpdatePasswordPlaceholder();
    }

    public void Dispose() => Localization.LanguageChanged -= OnLanguageChanged;

    private void Populate()
    {
        var rdp = _profile.Rdp ?? new RdpSettings();
        HostBox.Text = _profile.Host;
        PortBox.Value = _profile.Port <= 0 ? 3389 : _profile.Port;
        UserBox.Text = _profile.Username;
        DomainBox.Text = rdp.Domain;
        WidthBox.Value = rdp.Width;
        HeightBox.Value = rdp.Height;
        FullScreenCheck.IsChecked = rdp.FullScreen;
        DrivesCheck.IsChecked = rdp.RedirectDrives;
        ClipboardCheck.IsChecked = rdp.RedirectClipboard;
        SavePasswordCheck.IsChecked = _profile.SavePassword;
        UpdatePasswordPlaceholder();
        SubtitleText.Text = string.IsNullOrWhiteSpace(_profile.Name)
            ? _profile.Subtitle
            : Localization.T("Rdp.Subtitle", _profile.Name, _profile.Subtitle);
    }

    private void UpdatePasswordPlaceholder()
    {
        PasswordInput.PlaceholderText = Localization.T(string.IsNullOrEmpty(_profile.EncryptedPassword)
            ? "Rdp.PasswordPlaceholder"
            : "Rdp.PasswordPlaceholderSaved");
    }

    private bool ApplyToProfile(out string? password)
    {
        password = null;
        StatusInfoBar.IsOpen = false;

        var host = HostBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            ShowStatus(Localization.T("Rdp.Validation.Host"), false);
            return false;
        }

        var port = double.IsNaN(PortBox.Value) ? 3389 : (int)PortBox.Value;
        if (port is < 1 or > 65535)
        {
            ShowStatus(Localization.T("Rdp.Validation.Port"), false);
            return false;
        }

        _profile.Host = host;
        _profile.Port = port;
        _profile.Username = UserBox.Text.Trim();
        _profile.Name = string.IsNullOrWhiteSpace(_profile.Name) ? host : _profile.Name;
        _profile.Rdp = new RdpSettings
        {
            Width = ToInt(WidthBox.Value, 1600),
            Height = ToInt(HeightBox.Value, 900),
            FullScreen = FullScreenCheck.IsChecked == true,
            RedirectDrives = DrivesCheck.IsChecked == true,
            RedirectClipboard = ClipboardCheck.IsChecked == true,
            Domain = DomainBox.Text.Trim()
        };

        var typedPassword = PasswordInput.Password;
        var remember = SavePasswordCheck.IsChecked == true;
        if (!string.IsNullOrEmpty(typedPassword))
        {
            if (remember)
            {
                _profile.SetPassword(typedPassword);
            }

            password = typedPassword;
        }
        else
        {
            password = _profile.GetPassword();
        }

        if (!remember)
        {
            _profile.SetPassword(null);
        }

        _profile.SavePassword = remember;
        SubtitleText.Text = string.IsNullOrWhiteSpace(_profile.Name)
            ? _profile.Subtitle
            : Localization.T("Rdp.Subtitle", _profile.Name, _profile.Subtitle);
        _profile.LastConnected = DateTime.Now;
        App.ProfileStore.Save();
        return true;
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (!ApplyToProfile(out var password))
        {
            return;
        }

        try
        {
            RdpLauncher.Launch(_profile, password);
            ShowStatus(Localization.T(string.IsNullOrEmpty(password)
                ? "Rdp.LaunchedNoPassword"
                : "Rdp.Launched"), true);
            App.Shell?.ReportStatus(Localization.T("Status.RdpStarted", $"{_profile.Host}:{_profile.Port}"));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to launch RDP", ex);
            ShowStatus(Localization.T("Rdp.LaunchFailed", ex.Message), false);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (ApplyToProfile(out _))
        {
            ShowStatus(Localization.T("Rdp.SettingsSaved"), true);
        }
    }

    private void ShowStatus(string message, bool success)
    {
        StatusInfoBar.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

    private static int ToInt(double value, int fallback) =>
        double.IsNaN(value) ? fallback : (int)Math.Clamp(value, int.MinValue, int.MaxValue);
}
