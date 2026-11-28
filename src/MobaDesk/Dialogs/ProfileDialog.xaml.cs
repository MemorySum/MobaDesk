using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using MobaDesk.Models;
using MobaDesk.Services;

namespace MobaDesk.Dialogs;

public sealed partial class ProfileDialog : ContentDialog
{
    private readonly ConnectionProfile _draft;
    private readonly bool _isNew;
    private int _lastProtocolIndex;

    public ConnectionProfile? Result { get; private set; }

    public ProfileDialog(ConnectionProfile draft, bool isNew = false)
    {
        InitializeComponent();
        _draft = draft;
        _isNew = isNew;

        Title = Localization.T(isNew ? "Profile.NewTitle" : "Profile.EditTitle");
        PrimaryButtonText = Localization.T(isNew ? "Profile.SaveConnect" : "Common.Save");
        CloseButtonText = Localization.T("Common.Cancel");
        DefaultButton = ContentDialogButton.Primary;

        Populate();

        ProtocolCombo.SelectionChanged += (_, _) => OnProtocolChanged();
        AuthCombo.SelectionChanged += (_, _) => UpdateSections();
        PrimaryButtonClick += OnSaveClick;

        UpdateSections();
    }

    private void Populate()
    {
        NameBox.Text = _draft.Name;
        GroupBox.Text = _draft.Group;
        ProtocolCombo.SelectedIndex = (int)_draft.Protocol;
        _lastProtocolIndex = ProtocolCombo.SelectedIndex;
        HostBox.Text = _draft.Host;
        PortBox.Value = _draft.Port;
        UserBox.Text = _draft.Username;
        AuthCombo.SelectedIndex = _draft.Auth == AuthKind.PrivateKey ? 1 : 0;
        KeyPathBox.Text = _draft.PrivateKeyPath;
        SavePasswordCheck.IsChecked = _draft.SavePassword;
        UseTlsCheck.IsChecked = _draft.UseTls;

        PasswordInput.PlaceholderText = Localization.T(string.IsNullOrEmpty(_draft.EncryptedPassword)
            ? "Profile.PasswordPlaceholderEmpty"
            : "Profile.PasswordPlaceholder");

        var rdp = _draft.Rdp ?? new RdpSettings();
        RdpWidthBox.Value = rdp.Width;
        RdpHeightBox.Value = rdp.Height;
        FullScreenCheck.IsChecked = rdp.FullScreen;
        RedirectDrivesCheck.IsChecked = rdp.RedirectDrives;
        RedirectClipboardCheck.IsChecked = rdp.RedirectClipboard;
        DomainBox.Text = rdp.Domain;

        var color = _draft.ColorHex;
        var colorIndex = 0;
        for (var i = 0; i < ColorCombo.Items.Count; i++)
        {
            if (ColorCombo.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag as string, color, StringComparison.OrdinalIgnoreCase))
            {
                colorIndex = i;
                break;
            }
        }

        ColorCombo.SelectedIndex = colorIndex;
    }

    private void OnProtocolChanged()
    {
        var index = Math.Max(0, ProtocolCombo.SelectedIndex);
        var previousDefault = _lastProtocolIndex switch
        {
            2 => 21,
            3 => 3389,
            _ => 22
        };

        var newDefault = index switch
        {
            2 => 21,
            3 => 3389,
            _ => 22
        };

        if (_isNew || double.IsNaN(PortBox.Value) || (int)PortBox.Value == previousDefault)
        {
            PortBox.Value = newDefault;
        }

        if (_draft.ColorHex == ConnectionProfile.DefaultColorFor((ProtocolKind)_lastProtocolIndex))
        {
            var defaultColor = ConnectionProfile.DefaultColorFor((ProtocolKind)index);
            for (var i = 0; i < ColorCombo.Items.Count; i++)
            {
                if (ColorCombo.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, defaultColor, StringComparison.OrdinalIgnoreCase))
                {
                    ColorCombo.SelectedIndex = i;
                    break;
                }
            }
        }

        _lastProtocolIndex = index;
        UpdateSections();
    }

    private void UpdateSections()
    {
        var protocol = (ProtocolKind)Math.Max(0, ProtocolCombo.SelectedIndex);
        var sshLike = protocol is ProtocolKind.Ssh or ProtocolKind.Sftp;
        var useKey = AuthCombo.SelectedIndex == 1;

        AuthCombo.Visibility = sshLike ? Visibility.Visible : Visibility.Collapsed;
        KeyPanel.Visibility = sshLike && useKey ? Visibility.Visible : Visibility.Collapsed;
        UseTlsCheck.Visibility = protocol == ProtocolKind.Ftp ? Visibility.Visible : Visibility.Collapsed;
        RdpPanel.Visibility = protocol == ProtocolKind.Rdp ? Visibility.Visible : Visibility.Collapsed;
        PortBox.Value = double.IsNaN(PortBox.Value) ? 22 : PortBox.Value;
    }

    private void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ErrorBar.IsOpen = false;
        var protocol = (ProtocolKind)Math.Max(0, ProtocolCombo.SelectedIndex);
        var host = HostBox.Text.Trim();
        var user = UserBox.Text.Trim();

        if (string.IsNullOrEmpty(host))
        {
            Fail(args, Localization.T("Profile.Validation.Host"));
            return;
        }

        if (protocol != ProtocolKind.Rdp && string.IsNullOrEmpty(user))
        {
            Fail(args, Localization.T("Profile.Validation.Username"));
            return;
        }

        var port = double.IsNaN(PortBox.Value) ? 22 : (int)PortBox.Value;
        if (port is < 1 or > 65535)
        {
            Fail(args, Localization.T("Profile.Validation.Port"));
            return;
        }

        var useKey = protocol is ProtocolKind.Ssh or ProtocolKind.Sftp && AuthCombo.SelectedIndex == 1;
        if (useKey && string.IsNullOrWhiteSpace(KeyPathBox.Text))
        {
            Fail(args, Localization.T("Profile.Validation.Key"));
            return;
        }

        _draft.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? host : NameBox.Text.Trim();
        var group = GroupBox.Text.Trim();
        _draft.Group = group.Length == 0 || group == Localization.T("Common.DefaultGroup") ? "" : group;
        _draft.Protocol = protocol;
        _draft.Host = host;
        _draft.Port = port;
        _draft.Username = user;
        _draft.Auth = useKey ? AuthKind.PrivateKey : AuthKind.Password;
        _draft.PrivateKeyPath = KeyPathBox.Text.Trim();
        _draft.UseTls = UseTlsCheck.IsChecked == true;

        var remember = SavePasswordCheck.IsChecked == true;
        var typedPassword = PasswordInput.Password;
        if (!string.IsNullOrEmpty(typedPassword))
        {
            _draft.SetPassword(typedPassword);
        }

        if (!remember)
        {
            _draft.SetPassword(null);
            _draft.SetPassphrase(null);
        }
        else if (!string.IsNullOrEmpty(PassphraseInput.Password))
        {
            _draft.SetPassphrase(PassphraseInput.Password);
        }

        _draft.SavePassword = remember;
        _draft.ColorHex = (ColorCombo.SelectedItem as ComboBoxItem)?.Tag as string
                          ?? ConnectionProfile.DefaultColorFor(protocol);

        _draft.Rdp = new RdpSettings
        {
            Width = ToInt(RdpWidthBox.Value, 1600),
            Height = ToInt(RdpHeightBox.Value, 900),
            FullScreen = FullScreenCheck.IsChecked == true,
            RedirectDrives = RedirectDrivesCheck.IsChecked == true,
            RedirectClipboard = RedirectClipboardCheck.IsChecked == true,
            Domain = DomainBox.Text.Trim()
        };

        Result = _draft;
    }

    private void Fail(ContentDialogButtonClickEventArgs args, string message)
    {
        args.Cancel = true;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private static int ToInt(double value, int fallback) =>
        double.IsNaN(value) ? fallback : (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    private async void OnBrowseKeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(App.Shell!.AppWindow.Id);
            picker.FileTypeFilter.Add("*");
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                KeyPathBox.Text = file.Path;
            }
        }
        catch (Exception ex)
        {
            ErrorBar.Message = Localization.T("Profile.Validation.Picker", ex.Message);
            ErrorBar.IsOpen = true;
        }
    }
}
