using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media;
using MobaDesk.Services;

namespace MobaDesk.Models;

public enum ProtocolKind
{
    Ssh,
    Sftp,
    Ftp,
    Rdp
}

public enum AuthKind
{
    Password,
    PrivateKey
}

public sealed class RdpSettings
{
    public int Width { get; set; } = 1600;
    public int Height { get; set; } = 900;
    public bool FullScreen { get; set; }
    public bool RedirectDrives { get; set; } = true;
    public bool RedirectClipboard { get; set; } = true;
    public string Domain { get; set; } = "";

    public RdpSettings Clone() => (RdpSettings)MemberwiseClone();
}

/// <summary>
/// A saved session. SSH profiles can host a terminal, an SFTP side panel and the monitor panel.
/// </summary>
public sealed class ConnectionProfile : INotifyPropertyChanged
{
    private string _name = "";
    private string _host = "";
    private int _port = 22;
    private string _username = "";
    private ProtocolKind _protocol = ProtocolKind.Ssh;
    private AuthKind _auth = AuthKind.Password;
    private string _group = "";
    private string _colorHex = "";
    private string _privateKeyPath = "";
    private DateTime? _lastConnected;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public ProtocolKind Protocol
    {
        get => _protocol;
        set
        {
            Set(ref _protocol, value);
            if (string.IsNullOrWhiteSpace(_colorHex))
            {
                Notify(nameof(ColorBrush));
            }
            Notify(nameof(ProtocolLabel));
            Notify(nameof(ProtocolGlyph));
            Notify(nameof(DefaultPort));
            Notify(nameof(ColorHex));
            Notify(nameof(ColorBrush));
        }
    }

    public string Host
    {
        get => _host;
        set
        {
            Set(ref _host, value);
            Notify(nameof(Subtitle));
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            Set(ref _port, value);
            Notify(nameof(Subtitle));
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            Set(ref _username, value);
            Notify(nameof(Subtitle));
        }
    }

    public AuthKind Auth
    {
        get => _auth;
        set
        {
            Set(ref _auth, value);
            Notify(nameof(AuthLabel));
        }
    }

    public string? EncryptedPassword { get; set; }

    public string PrivateKeyPath
    {
        get => _privateKeyPath;
        set => Set(ref _privateKeyPath, value);
    }

    public string? EncryptedPassphrase { get; set; }

    public string Group
    {
        get => _group;
        set => Set(ref _group, value);
    }

    public string ColorHex
    {
        get => string.IsNullOrWhiteSpace(_colorHex) ? DefaultColorFor(Protocol) : _colorHex;
        set
        {
            Set(ref _colorHex, value);
            Notify(nameof(ColorBrush));
        }
    }

    public bool SavePassword { get; set; } = true;

    public bool UseTls { get; set; }

    public RdpSettings Rdp { get; set; } = new();

    public DateTime? LastConnected
    {
        get => _lastConnected;
        set => Set(ref _lastConnected, value);
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string Subtitle => string.IsNullOrWhiteSpace(Username)
        ? $"{Host}:{Port}"
        : $"{Username}@{Host}:{Port}";

    [JsonIgnore]
    public string ProtocolLabel => Protocol switch
    {
        ProtocolKind.Ssh => "SSH",
        ProtocolKind.Sftp => "SFTP",
        ProtocolKind.Ftp => "FTP",
        ProtocolKind.Rdp => "RDP",
        _ => "?"
    };

    [JsonIgnore]
    public string ProtocolGlyph => Protocol switch
    {
        ProtocolKind.Ssh => "\uE756",
        ProtocolKind.Sftp => "\uE8B7",
        ProtocolKind.Ftp => "\uE896",
        ProtocolKind.Rdp => "\uE7F4",
        _ => "\uE774"
    };

    [JsonIgnore]
    public string AuthLabel => Localization.T(Auth == AuthKind.Password ? "Auth.Password" : "Auth.PrivateKey");

    [JsonIgnore]
    public int DefaultPort => Protocol switch
    {
        ProtocolKind.Ftp => 21,
        ProtocolKind.Rdp => 3389,
        _ => 22
    };

    [JsonIgnore]
    public Brush ColorBrush => new SolidColorBrush(ParseColor(ColorHex));

    [JsonIgnore]
    public bool SupportsMonitor => Protocol == ProtocolKind.Ssh;

    public string? GetPassword() => CredentialProtector.Unprotect(EncryptedPassword);

    public void SetPassword(string? password)
    {
        EncryptedPassword = string.IsNullOrEmpty(password) ? null : CredentialProtector.Protect(password);
        if (string.IsNullOrEmpty(password))
        {
            SavePassword = false;
        }
    }

    public string? GetPassphrase() => CredentialProtector.Unprotect(EncryptedPassphrase);

    public void SetPassphrase(string? passphrase)
    {
        EncryptedPassphrase = string.IsNullOrEmpty(passphrase) ? null : CredentialProtector.Protect(passphrase);
    }

    public ConnectionProfile Clone(bool newIdentity = false) => new()
    {
        Id = newIdentity ? Guid.NewGuid().ToString("N") : Id,
        Name = Name,
        Protocol = Protocol,
        Host = Host,
        Port = Port,
        Username = Username,
        Auth = Auth,
        EncryptedPassword = EncryptedPassword,
        PrivateKeyPath = PrivateKeyPath,
        EncryptedPassphrase = EncryptedPassphrase,
        Group = Group,
        ColorHex = _colorHex,
        SavePassword = SavePassword,
        UseTls = UseTls,
        Rdp = Rdp.Clone(),
        LastConnected = LastConnected,
        CreatedAt = CreatedAt
    };

    public void CopyEditableFrom(ConnectionProfile other)
    {
        Name = other.Name;
        Protocol = other.Protocol;
        Host = other.Host;
        Port = other.Port;
        Username = other.Username;
        Auth = other.Auth;
        EncryptedPassword = other.EncryptedPassword;
        PrivateKeyPath = other.PrivateKeyPath;
        EncryptedPassphrase = other.EncryptedPassphrase;
        Group = other.Group;
        ColorHex = other.ColorHex;
        SavePassword = other.SavePassword;
        UseTls = other.UseTls;
        Rdp = other.Rdp.Clone();
        Notify(nameof(ColorBrush));
        Notify(nameof(Subtitle));
    }

    public static string DefaultColorFor(ProtocolKind protocol) => protocol switch
    {
        ProtocolKind.Ssh => "#38BDF8",
        ProtocolKind.Sftp => "#A78BFA",
        ProtocolKind.Ftp => "#FBBF24",
        ProtocolKind.Rdp => "#34D399",
        _ => "#38BDF8"
    };

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = (hex ?? "").TrimStart('#');
        if (hex.Length == 6)
        {
            return Windows.UI.Color.FromArgb(255,
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }

        return Windows.UI.Color.FromArgb(255, 0x38, 0xBD, 0xF8);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Notify(propertyName);
    }

    private void Notify(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
