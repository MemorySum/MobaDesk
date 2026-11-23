using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using MobaDesk.Models;
using MobaDesk.Services;
using MobaDesk.Services.FileSystems;
using MobaDesk.Ui;

namespace MobaDesk.Views;

/// <summary>
/// Remote text editor tab. Syntax highlighting runs in CodeMirror inside a WebView2;
/// write permission is probed before enabling save.
/// </summary>
public sealed partial class RemoteEditorView : UserControl, IAsyncDisposable
{
    private const long MaxFileSize = 2 * 1024 * 1024;
    private const int WriteMask = 0x092;
    private const int EditorFontSize = 13;
    private const string EditorFontFamily = "Cascadia Mono, Consolas, 'Courier New', monospace";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private enum DecodeFailure
    {
        None,
        Binary,
        UnsupportedEncoding
    }

    private readonly ConnectionProfile _profile;
    private readonly SshCredentials _credentials;
    private readonly RemoteEntry _entry;
    private readonly EditorLanguage _language;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _changeTimer;
    private readonly TaskCompletionSource _webReadySource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IRemoteFileSystem? _fileSystem;
    private Encoding _encoding = new UTF8Encoding(false);
    private string _newLine = "\n";
    private bool _writeBom;
    private bool _started;
    private bool _connected;
    private bool _dirty;
    private bool _suppressDirty;
    private string _loadedText = "";
    private bool _readOnly = true;
    private bool _saving;
    private bool _disposed;
    private bool _webReady;
    private string _statusKey = "Editor.Connecting";
    private object?[] _statusArgs = Array.Empty<object?>();

    public RemoteEditorView(ConnectionProfile profile, SshCredentials credentials, RemoteEntry entry)
    {
        InitializeComponent();

        _profile = profile;
        _credentials = credentials;
        _entry = entry;
        _language = EditorLanguages.Detect(entry.Name);
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        Loc.Register(this);
        Localization.LanguageChanged += OnLanguageChanged;

        FileNameText.Text = entry.Name;
        PathText.Text = entry.FullPath;
        FileInfoText.Text = entry.SizeText;
        LanguageText.Text = _language.Label;
        SetReadOnly(true);

        _changeTimer = _dispatcher.CreateTimer();
        _changeTimer.Interval = TimeSpan.FromMilliseconds(250);
        _changeTimer.IsRepeating = false;
        _changeTimer.Tick += async (_, _) => await UpdateDirtyAsync();

        var save = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.S,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        };
        save.Invoked += async (_, args) =>
        {
            args.Handled = true;
            await SaveAsync();
        };
        KeyboardAccelerators.Add(save);
    }

    public bool HasUnsavedChanges => _dirty;

    public async Task StartAsync()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        await EnsureEditorAsync();
        if (await ConnectAsync())
        {
            await LoadFileAsync();
        }
    }

    private async Task<bool> EnsureEditorAsync()
    {
        if (_webReady)
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.WebViewDataDirectory);
            await EditorWeb.EnsureCoreWebView2Async();

            var core = EditorWeb.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.WebMessageReceived += OnEditorMessage;

            var assets = AppPaths.AssetsDirectory;
            core.SetVirtualHostNameToFolderMapping("mobadesk.local", assets, CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate("https://mobadesk.local/editor/index.html");

            await _webReadySource.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return true;
        }
        catch (TimeoutException)
        {
            ShowError(Localization.T("Editor.LoadFailed"), Localization.T("Ssh.WebViewHint"));
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to initialize the code editor WebView2", ex);
            ShowError(Localization.T("Editor.LoadFailed"), ex.Message);
            return false;
        }
    }

    private void OnEditorMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        string? json;
        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "ready":
                    _webReady = true;
                    _webReadySource.TrySetResult();
                    break;

                case "change":
                    if (!_readOnly)
                    {
                        // Mark dirty immediately so a close within the 250 ms debounce
                        // window cannot silently discard the edit. The timer then
                        // recomputes the exact value against the loaded text.
                        if (!_suppressDirty)
                        {
                            _dirty = true;
                            UpdateHeader();
                        }

                        _changeTimer.Stop();
                        _changeTimer.Start();
                    }

                    break;

                case "save":
                    _ = SaveAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Editor message failed: {ex.Message}");
        }
    }

    private async Task<bool> ConnectAsync()
    {
        SetStatus("Editor.Connecting");
        try
        {
            if (_fileSystem != null)
            {
                try
                {
                    await _fileSystem.DisposeAsync();
                }
                catch
                {
                    // ignored
                }
            }

            _fileSystem = _profile.Protocol == ProtocolKind.Ftp
                ? new FtpRemoteFileSystem(_profile)
                : new SftpRemoteFileSystem(_profile);
            await _fileSystem.ConnectAsync(_credentials, _cts.Token);
            _connected = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Editor connect failed for {_profile.Host}", ex);
            ShowError(Localization.T("Editor.LoadFailed"), SshAuth.Describe(ex));
            SetReadOnly(true);
            return false;
        }
    }

    private async Task LoadFileAsync()
    {
        if (_fileSystem == null || _disposed)
        {
            return;
        }

        SetStatus("Editor.Loading");
        try
        {
            if (_entry.Size > MaxFileSize)
            {
                ShowError(Localization.T("Editor.LoadFailed"),
                    Localization.T("Editor.TooLarge", FormatUtil.Size(_entry.Size)));
                SetReadOnly(true);
                return;
            }

            var bytes = await _fileSystem.ReadFileAsync(_entry.FullPath, _cts.Token);
            if (bytes.Length > MaxFileSize)
            {
                ShowError(Localization.T("Editor.LoadFailed"),
                    Localization.T("Editor.TooLarge", FormatUtil.Size(bytes.Length)));
                SetReadOnly(true);
                return;
            }

            var decoded = TryDecode(bytes, out var failure);
            if (decoded == null)
            {
                var messageKey = failure == DecodeFailure.Binary
                    ? "Editor.Binary"
                    : "Editor.EncodingUnsupported";
                ShowError(Localization.T("Editor.LoadFailed"), Localization.T(messageKey));
                SetReadOnly(true);
                return;
            }

            _encoding = decoded.Value.Encoding;
            _writeBom = decoded.Value.HasBom;
            _newLine = DetectNewLine(decoded.Value.Text);

            _loadedText = NormalizeLineEndings(decoded.Value.Text);
            _suppressDirty = true;
            try
            {
                await SetEditorContentAsync(decoded.Value.Text);
            }
            finally
            {
                // Release the flag after the queued change notification has been handled.
                _dispatcher.TryEnqueue(() => _suppressDirty = false);
            }

            _dirty = false;
            UpdateHeader();

            FileInfoText.Text = $"{FormatUtil.Size(bytes.Length)} · {_encoding.WebName.ToUpperInvariant()}";
            StatusInfoBar.IsOpen = false;
            await UpdateWritabilityAsync();
            SetStatus("Editor.Loaded", FormatUtil.Size(bytes.Length));
        }
        catch (OperationCanceledException)
        {
            // Closing the tab cancels the read.
        }
        catch (Exception ex)
        {
            Log.Error($"Editor read failed for {_entry.FullPath}", ex);
            ShowError(Localization.T("Editor.LoadFailed"), ex.Message);
            SetReadOnly(true);
        }
    }

    private async Task SetEditorContentAsync(string text)
    {
        var core = _webReady ? EditorWeb.CoreWebView2 : null;
        if (core == null)
        {
            return;
        }

        try
        {
            var script =
                $"window.editorHost.setContent({JsonSerializer.Serialize(text, MobaDeskJsonContext.Default.String)}, " +
                $"{JsonSerializer.Serialize(_language.Mode, MobaDeskJsonContext.Default.String)}, " +
                $"{(_readOnly ? "true" : "false")}, " +
                $"{JsonSerializer.Serialize(EditorFontFamily, MobaDeskJsonContext.Default.String)}, {EditorFontSize});";
            await core.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Log.Warn($"Editor content update failed: {ex.Message}");
        }
    }

    private async Task<string?> GetEditorTextAsync()
    {
        var core = _webReady ? EditorWeb.CoreWebView2 : null;
        if (core == null)
        {
            return null;
        }

        try
        {
            var json = await core.ExecuteScriptAsync("window.editorHost.getValue()");
            return JsonSerializer.Deserialize(json, MobaDeskJsonContext.Default.String);
        }
        catch (Exception ex)
        {
            Log.Warn($"Editor read failed: {ex.Message}");
            return null;
        }
    }

    private async Task UpdateDirtyAsync()
    {
        if (_disposed || _readOnly)
        {
            return;
        }

        var text = await GetEditorTextAsync();
        if (text == null)
        {
            return;
        }

        var dirty = !string.Equals(NormalizeLineEndings(text), _loadedText, StringComparison.Ordinal);
        if (dirty != _dirty)
        {
            _dirty = dirty;
            UpdateHeader();
        }

        UpdateSaveButton();
    }

    private async Task UpdateWritabilityAsync()
    {
        if (_fileSystem == null)
        {
            SetReadOnly(true);
            return;
        }

        var writable = _entry.UnixMode < 0 || (_entry.UnixMode & WriteMask) != 0;
        if (writable)
        {
            try
            {
                var probe = await _fileSystem.CanWriteAsync(_entry.FullPath, _cts.Token);
                writable = probe ?? writable;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warn($"Editor write probe failed for {_entry.FullPath}: {ex.Message}");
            }
        }

        SetReadOnly(!writable);
    }

    private void SetReadOnly(bool readOnly)
    {
        _readOnly = readOnly;
        if (_webReady && EditorWeb.CoreWebView2 != null)
        {
            _ = EditorWeb.CoreWebView2.ExecuteScriptAsync(
                $"window.editorHost.setReadOnly({(readOnly ? "true" : "false")});");
        }

        ReadOnlyIcon.Visibility = readOnly ? Visibility.Visible : Visibility.Collapsed;
        ReadOnlyText.Visibility = readOnly ? Visibility.Visible : Visibility.Collapsed;
        UpdateSaveButton();
    }

    private void UpdateSaveButton()
    {
        SaveButton.IsEnabled = !_readOnly && !_saving && _connected;
    }

    private void UpdateHeader()
    {
        App.Shell?.UpdateTabHeader(this, _entry.Name + (_dirty ? " *" : ""));
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();

    private async Task<bool> SaveAsync()
    {
        if (_fileSystem == null || _readOnly || _saving || !_connected || _disposed)
        {
            return false;
        }

        _saving = true;
        UpdateSaveButton();
        SetStatus("Editor.Saving");
        try
        {
            var editorText = await GetEditorTextAsync();
            if (editorText == null)
            {
                return false;
            }

            var text = NormalizeLineEndings(editorText);
            var payload = Encode(text);
            await _fileSystem.WriteFileAsync(_entry.FullPath, payload, _cts.Token);
            FileInfoText.Text = $"{FormatUtil.Size(payload.Length)} · {_encoding.WebName.ToUpperInvariant()}";
            _loadedText = text;
            _dirty = false;
            UpdateHeader();
            StatusInfoBar.IsOpen = false;
            SetStatus("Editor.Saved");
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Editor save failed for {_entry.FullPath}", ex);
            await ProbeReadOnlyAfterFailureAsync();
            ShowError(Localization.T("Editor.SaveFailed"), ex.Message);
            return false;
        }
        finally
        {
            _saving = false;
            UpdateSaveButton();
        }
    }

    private async Task ProbeReadOnlyAfterFailureAsync()
    {
        if (_fileSystem == null)
        {
            SetReadOnly(true);
            return;
        }

        try
        {
            if (await _fileSystem.CanWriteAsync(_entry.FullPath, _cts.Token) == false)
            {
                SetReadOnly(true);
            }
        }
        catch
        {
            // Keep the current state when the probe itself fails.
        }
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_disposed || _saving || !_started)
        {
            return;
        }

        if (!_readOnly)
        {
            await UpdateDirtyAsync();
        }

        if (_dirty && !await ConfirmDiscardAsync())
        {
            return;
        }

        if (!_connected && !await ConnectAsync())
        {
            return;
        }

        await LoadFileAsync();
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        // Always compare the live editor text before closing: the debounced _dirty
        // flag lags behind the most recent keystrokes.
        if (!_readOnly)
        {
            await UpdateDirtyAsync();
        }

        if (!_dirty)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            Title = Localization.T("Editor.UnsavedTitle"),
            Content = Localization.T("Editor.UnsavedMessage", _entry.Name),
            PrimaryButtonText = Localization.T("Common.Save"),
            SecondaryButtonText = Localization.T("Editor.Discard"),
            CloseButtonText = Localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        ThemeService.ApplyDialogTheme(dialog);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await SaveAsync();
            return !_dirty;
        }

        return result == ContentDialogResult.Secondary;
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        var dialog = new ContentDialog
        {
            Title = Localization.T("Editor.UnsavedTitle"),
            Content = Localization.T("Editor.UnsavedMessage", _entry.Name),
            PrimaryButtonText = Localization.T("Editor.Discard"),
            CloseButtonText = Localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        ThemeService.ApplyDialogTheme(dialog);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private string NormalizeLineEndings(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return _newLine == "\n" ? normalized : normalized.Replace("\n", _newLine);
    }

    private static string DetectNewLine(string text)
    {
        if (text.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        return text.Contains('\r') ? "\r" : "\n";
    }

    private byte[] Encode(string text)
    {
        var body = _encoding.GetBytes(text);
        if (!_writeBom)
        {
            return body;
        }

        var preamble = _encoding.GetPreamble();
        if (preamble.Length == 0)
        {
            return body;
        }

        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    private static (string Text, Encoding Encoding, bool HasBom)? TryDecode(byte[] bytes,
        out DecodeFailure failure)
    {
        failure = DecodeFailure.None;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            try
            {
                // Keep an encoding whose preamble is the BOM so Encode() re-adds it.
                return (StrictUtf8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true), true);
            }
            catch (DecoderFallbackException)
            {
                failure = DecodeFailure.UnsupportedEncoding;
                return null;
            }
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode, true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2),
                Encoding.BigEndianUnicode, true);
        }

        var sample = Math.Min(bytes.Length, 8192);
        for (var index = 0; index < sample; index++)
        {
            if (bytes[index] == 0)
            {
                failure = DecodeFailure.Binary;
                return null;
            }
        }

        try
        {
            // Refuse legacy encodings (GBK, GB18030, Latin-1, ...) instead of decoding
            // invalid bytes to U+FFFD -- saving those replacement characters would
            // permanently destroy the original file contents.
            return (StrictUtf8.GetString(bytes), new UTF8Encoding(false), false);
        }
        catch (DecoderFallbackException)
        {
            failure = DecodeFailure.UnsupportedEncoding;
            return null;
        }
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        ApplyStatus();
    }

    private void ApplyStatus() => StatusText.Text = Localization.T(_statusKey, _statusArgs);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Loc.ApplyTree(this);
        ApplyStatus();
    }

    private void ShowError(string title, string message)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = InfoBarSeverity.Error;
        StatusInfoBar.IsOpen = true;
        Log.Warn($"{title}: {message}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        _changeTimer.Stop();
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // ignored
        }

        if (_fileSystem != null)
        {
            try
            {
                await _fileSystem.DisposeAsync();
            }
            catch (Exception ex)
            {
                Log.Warn($"Editor dispose failed: {ex.Message}");
            }

            _fileSystem = null;
        }

        _cts.Dispose();
    }
}
