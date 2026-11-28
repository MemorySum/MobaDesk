using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using MobaDesk.Ui;
using MobaDesk.Models;
using MobaDesk.Services;
using MobaDesk.Services.FileSystems;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;

namespace MobaDesk.Views;

public sealed partial class SshTerminalView : IAsyncDisposable
{
    private readonly ConnectionProfile _profile;
    private readonly SshCredentials _credentials;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _flushTimer;
    private readonly DispatcherQueueTimer _fontSaveTimer;
    private readonly object _pendingLock = new();
    private readonly Queue<byte[]> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private SshSession _session = new();
    private CancellationTokenSource? _connectCts;
    private FileBrowserView? _sftpView;
    private MonitorView? _monitorView;
    private string _activeSide = "sftp";
    private int _pendingBytes;
    private int _columns = 100;
    private int _rows = 30;
    private readonly TaskCompletionSource _startup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _webReady;
    private bool _connecting;
    private bool _disposed;
    private bool _overlayVisible;
    private bool _overlayBusy;
    private bool _overlayRetry;
    private bool _hasSelection;
    private string _statusKey = "Ssh.Connecting";
    private StatusKind _statusKind = StatusKind.Busy;
    private string _overlayKey = "Ssh.BusyTitle";
    private string? _overlayDetail;

    public SshTerminalView(ConnectionProfile profile, SshCredentials credentials)
    {
        InitializeComponent();
        _profile = profile;
        _credentials = credentials;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        Loc.Register(this);
        Localization.LanguageChanged += OnLanguageChanged;

        EndpointLabel.Text = profile.Subtitle;
        AttachSession(_session);

        _flushTimer = _dispatcher.CreateTimer();
        _flushTimer.Interval = TimeSpan.FromMilliseconds(24);
        _flushTimer.Tick += (_, _) => FlushOutput();

        _fontSaveTimer = _dispatcher.CreateTimer();
        _fontSaveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _fontSaveTimer.IsRepeating = false;
        _fontSaveTimer.Tick += (_, _) =>
        {
            try
            {
                App.Settings.Save();
            }
            catch (Exception ex)
            {
                Log.Warn($"Save zoomed font size failed: {ex.Message}");
            }
        };

        BroadcastCheck.Checked += OnBroadcastMembershipToggled;
        BroadcastCheck.Unchecked += OnBroadcastMembershipToggled;

        SidePanelSplitter.DragMoved += OnSidePanelSplitterDragMoved;

        Loaded += OnLoaded;
    }

    /// <summary>Raised for every keystroke block typed into this terminal.</summary>
    public event Action<SshTerminalView, byte[]>? UserInput;

    /// <summary>Raised with the text of a paste so peers can mirror it through their own paste path.</summary>
    public event Action<SshTerminalView, string>? UserPasted;

    /// <summary>Raised when the "in broadcast" checkbox changes.</summary>
    public event Action<SshTerminalView>? BroadcastMembershipChanged;

    /// <summary>False when the user temporarily excluded this session from broadcast input.</summary>
    public bool IsBroadcastMember => BroadcastCheck.IsChecked == true;

    public string ProfileName => _profile.Name;

    private void OnBroadcastMembershipToggled(object sender, RoutedEventArgs e)
    {
        BroadcastMembershipChanged?.Invoke(this);
        FocusTerminal();
    }

    /// <summary>Writes raw bytes without echoing them locally (used for broadcast input).</summary>
    public void WriteBroadcast(byte[] data)
    {
        try
        {
            if (!_disposed)
            {
                _session.Write(data);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Broadcast write failed for {_profile.Name}: {ex.Message}");
        }
    }

    /// <summary>Sends pasted text to the remote shell, as a bracketed paste when it asked for one.</summary>
    public async Task WritePastedTextAsync(string text)
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            var bracketedPaste = await IsBracketedPasteActiveAsync();
            _session.Write(Encoding.UTF8.GetBytes(BuildPastePayload(text, bracketedPaste)));
        }
        catch (Exception ex)
        {
            Log.Warn($"Paste write failed for {_profile.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Queries the terminal emulator (xterm.js) for its parsed bracketed-paste mode, instead of
    /// scanning the raw SSH output stream, which cannot tell DECSET 2004 apart from echoed input
    /// or program output.
    /// </summary>
    private async Task<bool> IsBracketedPasteActiveAsync()
    {
        if (!_webReady || TerminalWeb.CoreWebView2 == null)
        {
            return false;
        }

        try
        {
            var json = await TerminalWeb.CoreWebView2.ExecuteScriptAsync(
                "window.term.getBracketedPasteMode()");
            return json == "true";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Apps that support bracketed paste (bash 5.1+, vim, ...) announce DECSET 2004 and expect
    /// the text to be wrapped, so every line break is inserted into the line editor instead of
    /// being read as an Enter that runs each pasted line as its own command. The envelope is
    /// sanitized so a clipboard containing ESC[201~ cannot close it early and replay the rest
    /// of the paste as live keystrokes.
    /// </summary>
    private static string BuildPastePayload(string text, bool bracketedPaste)
    {
        if (!bracketedPaste)
        {
            return text;
        }

        var sanitized = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '\u001b' || (ch < ' ' && ch != '\t' && ch != '\n' && ch != '\r'))
            {
                continue;
            }

            sanitized.Append(ch);
        }

        return "\u001b[200~" + sanitized.Replace('\r', '\n') + "\u001b[201~";
    }

    /// <summary>Sends one or more command lines to the remote shell.</summary>
    public void SendCommandLine(string command)
    {
        try
        {
            if (!_disposed)
            {
                // Multi-line input: every line break becomes the Enter key (\r), not a literal LF.
                var normalized = command.Replace("\r\n", "\n").Replace('\n', '\r').TrimEnd('\r');
                _session.Write(Encoding.UTF8.GetBytes(normalized + "\r"));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Broadcast command failed for {_profile.Name}: {ex.Message}");
        }
    }

    public Task StartAsync() => _startup.Task;

    /// <summary>Applies changed terminal and monitor settings to this already-open view.</summary>
    public void ApplySettings()
    {
        if (_disposed)
        {
            return;
        }

        if (_webReady)
        {
            _ = ConfigureTerminalAsync();
        }

        _monitorView?.ApplySettings();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Loc.ApplyTree(this);
        ApplyStatus();
        if (_overlayVisible)
        {
            ApplyOverlay();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            await InitializeTerminalAsync();
        }
        finally
        {
            _startup.TrySetResult();
        }
    }

    private async Task InitializeTerminalAsync()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.WebViewDataDirectory);
            await TerminalWeb.EnsureCoreWebView2Async();

            var core = TerminalWeb.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            core.WebMessageReceived += OnWebMessageReceived;

            var assets = AppPaths.AssetsDirectory;
            core.SetVirtualHostNameToFolderMapping("mobadesk.local", assets, CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate("https://mobadesk.local/terminal.html");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to initialize WebView2 terminal", ex);
            SetStatus("Ssh.InitFailed", StatusKind.Error);
            ShowOverlay("Ssh.TerminalInitFailed",
                ex.Message + "\n" + Localization.T("Ssh.WebViewHint"), false, false);
            App.Shell?.ReportStatus(Localization.T("Status.WebViewFailed", ex.Message), true);
        }
    }

    private void AttachSession(SshSession session)
    {
        // Session events from a previous (replaced) connection must not affect this view.
        session.Output += data =>
        {
            if (ReferenceEquals(session, _session))
            {
                OnSessionOutput(data);
            }
        };
        session.Closed += message =>
        {
            if (ReferenceEquals(session, _session))
            {
                OnSessionClosed(message);
            }
        };
        session.HostKey += fingerprint => _dispatcher.TryEnqueue(() =>
        {
            if (ReferenceEquals(session, _session))
            {
                EndpointLabel.Text = $"{_profile.Subtitle} · {fingerprint}";
            }
        });
    }

    private void OnSessionOutput(byte[] data)
    {
        while (!_disposed)
        {
            lock (_pendingLock)
            {
                if (_pendingBytes < 4_000_000)
                {
                    _pending.Enqueue(data);
                    _pendingBytes += data.Length;
                    return;
                }
            }

            Thread.Sleep(4);
        }
    }

    private void OnSessionClosed(string message)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed)
            {
                return;
            }

            _flushTimer.Stop();
            SetStatus("Ssh.Disconnected", StatusKind.Error);
            ShowOverlay("Ssh.Disconnected", message, true, false);
            _ = WriteTextToTerminalAsync(
                $"\r\n\u001b[31m{Localization.T("Ssh.DisconnectBanner", message)}\u001b[0m\r\n");
            App.Shell?.ReportStatus(Localization.T("Status.SshDisconnected", _profile.Name), true);
        });
    }

    private async Task ConnectAsync()
    {
        if (_connecting || _disposed)
        {
            return;
        }

        _connecting = true;
        var cts = new CancellationTokenSource();
        _connectCts = cts;
        var session = _session;
        SetStatus("Ssh.Connecting", StatusKind.Busy);
        ShowOverlay("Ssh.BusyTitle", $"{_profile.Username}@{_profile.Host}:{_profile.Port}", false, true);

        try
        {
            await session.ConnectAsync(_profile, _credentials, _columns, _rows, cts.Token);
            if (_disposed || cts.IsCancellationRequested ||
                !ReferenceEquals(session, _session) || !session.IsConnected)
            {
                return;
            }

            HideOverlay();
            SetStatus("Ssh.Connected", StatusKind.Success);
            _flushTimer.Start();
            _ = WriteTextToTerminalAsync(
                $"\u001b[38;5;111m{Localization.T("Ssh.ConnectBanner", $"{_profile.Username}@{_profile.Host}:{_profile.Port}")}\u001b[0m\r\n");
            FocusTerminal();
            App.Shell?.ReportStatus(Localization.T("Status.SshConnected", _profile.Name));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Superseded by reconnect or tab close.
        }
        catch (Exception ex)
        {
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(session, _session))
            {
                return;
            }

            Log.Error($"SSH connect failed for {_profile.Host}", ex);
            var message = SshAuth.Describe(ex);
            SetStatus("Ssh.ConnectFailed", StatusKind.Error);
            ShowOverlay("Ssh.ConnectFailed", message, true, false);
            _ = WriteTextToTerminalAsync($"\r\n\u001b[31m{message}\u001b[0m\r\n");
            App.Shell?.ReportStatus(Localization.T("Status.SshFailed", _profile.Name), true);
        }
        finally
        {
            if (ReferenceEquals(_connectCts, cts))
            {
                _connectCts = null;
                _connecting = false;
            }

            cts.Dispose();
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
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
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "ready":
                    _webReady = true;
                    _columns = Math.Max(20, GetInt(root, "cols", 100));
                    _rows = Math.Max(5, GetInt(root, "rows", 30));
                    Log.Info($"Terminal ready {_columns}x{_rows}, view {TerminalWeb.ActualWidth:0}x{TerminalWeb.ActualHeight:0}");
                    await ConfigureTerminalAsync();
                    await ConnectAsync();
                    break;

                case "input":
                    if (root.TryGetProperty("data", out var dataElement) && dataElement.GetString() is { } base64)
                    {
                        try
                        {
                            var bytes = Convert.FromBase64String(base64);
                            _session.Write(bytes);
                            UserInput?.Invoke(this, bytes);
                        }
                        catch (FormatException)
                        {
                            // Ignore malformed input.
                        }
                    }

                    break;

                case "resize":
                    _columns = Math.Max(20, GetInt(root, "cols", _columns));
                    _rows = Math.Max(5, GetInt(root, "rows", _rows));
                    _session.Resize(_columns, _rows);
                    break;

                case "copy":
                    if (root.TryGetProperty("text", out var textElement))
                    {
                        CopyToClipboard(textElement.GetString() ?? "");
                    }

                    break;

                case "paste":
                    await PasteClipboardAsync();
                    break;

                case "selection":
                    _hasSelection = root.TryGetProperty("hasSelection", out var hasSelection) &&
                                    hasSelection.ValueKind == JsonValueKind.True;
                    break;

                case "contextmenu":
                    var x = root.TryGetProperty("x", out var xElement) && xElement.TryGetDouble(out var parsedX)
                        ? parsedX
                        : 0;
                    var y = root.TryGetProperty("y", out var yElement) && yElement.TryGetDouble(out var parsedY)
                        ? parsedY
                        : 0;
                    ShowTerminalMenu(x, y);
                    break;

                case "find":
                    ShowFindBar();
                    break;

                case "clear":
                    OnClearClick(this, new RoutedEventArgs());
                    break;

                case "zoom":
                    ApplyZoom(GetInt(root, "fontSize", App.Settings.TerminalFontSize));
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Terminal message failed: {ex.Message}");
        }
    }

    private void ApplyZoom(int fontSize)
    {
        var clamped = Math.Clamp(fontSize, 9, 32);
        if (clamped == App.Settings.TerminalFontSize)
        {
            return;
        }

        App.Settings.TerminalFontSize = clamped;
        Log.Info($"Terminal font size changed to {clamped}");
        _fontSaveTimer.Stop();
        _fontSaveTimer.Start();
    }

    private async Task ConfigureTerminalAsync()
    {
        try
        {
            var options = JsonSerializer.Serialize(
                new TerminalConfigureOptions
                {
                    FontSize = App.Settings.TerminalFontSize,
                    FontFamily = App.Settings.TerminalFontFamily,
                    Scrollback = App.Settings.ScrollbackLines,
                    CopyOnSelect = App.Settings.CopyOnSelect
                },
                MobaDeskJsonContext.Default.TerminalConfigureOptions);
            await TerminalWeb.CoreWebView2.ExecuteScriptAsync($"window.term.configure({options});");
        }
        catch (Exception ex)
        {
            Log.Warn($"Terminal configure failed: {ex.Message}");
        }
    }

    private async void FlushOutput()
    {
        byte[][] chunks;
        lock (_pendingLock)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            chunks = _pending.ToArray();
            _pending.Clear();
            _pendingBytes = 0;
        }

        var total = chunks.Sum(c => c.Length);
        if (total == 0)
        {
            return;
        }

        var buffer = new byte[total];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, buffer, offset, chunk.Length);
            offset += chunk.Length;
        }

        var base64 = Convert.ToBase64String(buffer);
        await _writeGate.WaitAsync();
        try
        {
            if (_webReady && !_disposed && TerminalWeb.CoreWebView2 != null)
            {
                await TerminalWeb.CoreWebView2.ExecuteScriptAsync($"window.term.write('{base64}');");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Terminal write failed: {ex.Message}");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task WriteTextToTerminalAsync(string text)
    {
        if (!_webReady || TerminalWeb.CoreWebView2 == null)
        {
            return;
        }

        try
        {
            var script = $"window.term.writeText({JsonSerializer.Serialize(text, MobaDeskJsonContext.Default.String)});";
            await TerminalWeb.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            // The terminal may be closing.
        }
    }

    private void FocusTerminal()
    {
        try
        {
            TerminalWeb.Focus(FocusState.Programmatic);
            TerminalWeb.CoreWebView2?.ExecuteScriptAsync("window.term.focus();");
        }
        catch
        {
            // ignored
        }
    }

    private void OnTerminalAreaPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        FocusTerminal();

    private void OnTerminalAreaTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
        FocusTerminal();

    private static int GetInt(JsonElement root, string property, int fallback) =>
        root.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    private static void CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            Log.Warn($"Clipboard copy failed: {ex.Message}");
        }
    }

    private static async Task<string> ReadClipboardTextAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                return await content.GetTextAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Clipboard read failed: {ex.Message}");
        }

        return "";
    }

    private void SetStatus(string key, StatusKind kind)
    {
        _statusKey = key;
        _statusKind = kind;
        ApplyStatus();
    }

    private void ApplyStatus()
    {
        StatusLabel.Text = Localization.T(_statusKey);
        StatusDot.Fill = new SolidColorBrush(_statusKind switch
        {
            StatusKind.Busy => Color.FromArgb(255, 56, 189, 248),
            StatusKind.Success => Color.FromArgb(255, 52, 211, 153),
            StatusKind.Error => Color.FromArgb(255, 248, 113, 113),
            _ => Color.FromArgb(255, 148, 163, 184)
        });
    }

    private void ShowOverlay(string key, string? detail, bool showRetry, bool busy)
    {
        _overlayVisible = true;
        _overlayKey = key;
        _overlayDetail = detail;
        _overlayRetry = showRetry;
        _overlayBusy = busy;
        ApplyOverlay();
    }

    private void ApplyOverlay()
    {
        BusyOverlay.Visibility = Visibility.Visible;
        BusyTitle.Text = Localization.T(_overlayKey);
        BusyDetail.Text = _overlayDetail ?? "";
        BusyDetail.Visibility = string.IsNullOrEmpty(_overlayDetail)
            ? Visibility.Collapsed
            : Visibility.Visible;
        BusyRing.Visibility = _overlayBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.IsActive = _overlayBusy;
        RetryButton.Visibility = _overlayRetry ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideOverlay()
    {
        _overlayVisible = false;
        BusyOverlay.Visibility = Visibility.Collapsed;
    }

    private void ToggleSidePanel(string kind)
    {
        if (SidePanel.Visibility == Visibility.Visible && _activeSide == kind)
        {
            SidePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            SidePanel.Visibility = Visibility.Visible;
            _activeSide = kind;
            ShowSideContent(kind);
        }

        UpdateSideButtons();
        SidePanelSplitter.Visibility = SidePanel.Visibility;
        FocusTerminal();
    }

    private void ShowSideContent(string kind)
    {
        if (kind == "sftp")
        {
            if (_sftpView == null)
            {
                _sftpView = new FileBrowserView(_profile, _credentials, embedded: true);
                _ = _sftpView.StartAsync();
            }

            SideContent.Content = _sftpView;
        }
        else
        {
            if (_monitorView == null)
            {
                _monitorView = new MonitorView(_profile, _credentials);
                _ = _monitorView.StartAsync();
            }

            SideContent.Content = _monitorView;
        }
    }

    private void UpdateSideButtons()
    {
        var sftpOpen = SidePanel.Visibility == Visibility.Visible && _activeSide == "sftp";
        var monitorOpen = SidePanel.Visibility == Visibility.Visible && _activeSide == "monitor";
        SftpButton.IsChecked = sftpOpen;
        MonitorButton.IsChecked = monitorOpen;
        SftpToggle.IsChecked = sftpOpen;
        MonitorToggle.IsChecked = monitorOpen;
    }

    private void OnSftpToggleClick(object sender, RoutedEventArgs e) => ToggleSidePanel("sftp");

    private void OnMonitorToggleClick(object sender, RoutedEventArgs e) => ToggleSidePanel("monitor");

    private void OnCloseSidePanelClick(object sender, RoutedEventArgs e)
    {
        SidePanel.Visibility = Visibility.Collapsed;
        SidePanelSplitter.Visibility = Visibility.Collapsed;
        UpdateSideButtons();
        FocusTerminal();
    }

    private void OnSidePanelSplitterDragMoved(object? sender, double pointerX)
    {
        if (SidePanel.Visibility != Visibility.Visible || SidePanel.Parent is not FrameworkElement container)
        {
            return;
        }

        var panelLeft = pointerX + SidePanelSplitter.ActualWidth / 2;
        var max = Math.Max(280, container.ActualWidth * 0.8);
        SidePanel.Width = Math.Clamp(container.ActualWidth - panelLeft, 280, max);
    }

    private void OnTerminalMenuOpening(object sender, object e) => CopyMenuItem.IsEnabled = _hasSelection;

    private void ShowTerminalMenu(double x, double y)
    {
        try
        {
            CopyMenuItem.IsEnabled = _hasSelection;
            var options = new FlyoutShowOptions
            {
                Position = new Point(Math.Max(0, x), Math.Max(0, y))
            };
            TerminalMenu.ShowAt(TerminalArea, options);
        }
        catch (Exception ex)
        {
            Log.Warn($"Show terminal menu failed: {ex.Message}");
        }
    }

    private async void OnCopyMenuItemClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TerminalWeb.CoreWebView2 != null)
            {
                var json = await TerminalWeb.CoreWebView2.ExecuteScriptAsync("window.term.getSelection()");
                var text = JsonSerializer.Deserialize<string>(json);
                if (!string.IsNullOrEmpty(text))
                {
                    CopyToClipboard(text);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Copy terminal selection failed: {ex.Message}");
        }
        finally
        {
            FocusTerminal();
        }
    }

    private async void OnPasteMenuItemClick(object sender, RoutedEventArgs e) => await PasteClipboardAsync();

    private async Task PasteClipboardAsync()
    {
        var text = await ReadClipboardTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            // The shell reads Enter as CR. Clipboard text carries Windows line endings, and
            // forwarding "\r\n" verbatim makes readline see two Enters per line: the first
            // ends a "\" continuation, the second runs the partial command, so a pasted
            // multi-line command is split and executed line by line.
            var normalized = text.Replace("\r\n", "\r").Replace('\n', '\r');
            await WritePastedTextAsync(normalized);
            UserPasted?.Invoke(this, normalized);
        }

        FocusTerminal();
    }

    private void OnFindMenuItemClick(object sender, RoutedEventArgs e) => ShowFindBar();

    private void ShowFindBar()
    {
        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus(FocusState.Programmatic);
        FindBox.SelectAll();
    }

    private void HideFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        ExecuteTerminalScript("window.term.clearSelection(); window.term.focus();");
    }

    private void OnFindCloseClick(object sender, RoutedEventArgs e) => HideFindBar();

    private void OnFindNextClick(object sender, RoutedEventArgs e) => RunFind(false);

    private void OnFindPreviousClick(object sender, RoutedEventArgs e) => RunFind(true);

    private void OnFindTextChanged(object sender, TextChangedEventArgs e)
    {
        if (FindBar.Visibility == Visibility.Visible && !string.IsNullOrEmpty(FindBox.Text))
        {
            RunFind(false);
        }
    }

    private void OnFindBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            RunFind(false);
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            HideFindBar();
        }
    }

    private void RunFind(bool backwards)
    {
        var query = FindBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        var literal = JsonSerializer.Serialize(query);
        ExecuteTerminalScript($"window.term.findNext({literal}, {(backwards ? "true" : "false")});");
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e) =>
        ExecuteTerminalScript("window.term.selectAll(); window.term.focus();");

    private void OnClearSelectionClick(object sender, RoutedEventArgs e) =>
        ExecuteTerminalScript("window.term.clearSelection(); window.term.focus();");

    private void OnSplitHorizontalClick(object sender, RoutedEventArgs e) =>
        App.Shell?.OpenSplitTab(_profile, _credentials, horizontal: true);

    private void OnSplitVerticalClick(object sender, RoutedEventArgs e) =>
        App.Shell?.OpenSplitTab(_profile, _credentials, horizontal: false);

    private void ExecuteTerminalScript(string script)
    {
        try
        {
            _ = TerminalWeb.CoreWebView2?.ExecuteScriptAsync(script);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>Clears the terminal screen and scrollback without moving keyboard focus.</summary>
    public void ClearTerminal() => ExecuteTerminalScript("window.term.clear();");

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ClearTerminal();
        ExecuteTerminalScript("window.term.focus();");
    }

    private async void OnReconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _flushTimer.Stop();
        _connectCts?.Cancel();
        // Allow the new attempt to start even if the previous handshake is still unwinding.
        _connecting = false;

        _session.Dispose();
        _session = new SshSession();
        AttachSession(_session);

        await WriteTextToTerminalAsync(
            $"\r\n\u001b[38;5;111m{Localization.T("Ssh.Reconnecting")}\u001b[0m\r\n");
        await ConnectAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        _connectCts?.Cancel();
        _flushTimer.Stop();
        _fontSaveTimer.Stop();
        _session.Dispose();

        if (_sftpView != null)
        {
            try
            {
                await _sftpView.DisposeAsync();
            }
            catch (Exception ex)
            {
                Log.Warn($"Dispose SFTP view failed: {ex.Message}");
            }

            _sftpView = null;
        }

        if (_monitorView != null)
        {
            try
            {
                await _monitorView.DisposeAsync();
            }
            catch (Exception ex)
            {
                Log.Warn($"Dispose monitor view failed: {ex.Message}");
            }

            _monitorView = null;
        }

        SideContent.Content = null;

        try
        {
            TerminalWeb.Close();
        }
        catch
        {
            // ignored
        }
    }

    private enum StatusKind
    {
        Idle,
        Busy,
        Success,
        Error
    }
}

