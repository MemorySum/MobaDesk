using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MobaDesk.Dialogs;
using MobaDesk.Ui;
using MobaDesk.Models;
using MobaDesk.Services;
using MobaDesk.Views;
using Renci.SshNet;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace MobaDesk;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<SidebarRow> _rows = new();
    private readonly HashSet<string> _collapsedFolders = new();
    private readonly List<ConnectionProfile> _recentMultiSelection = new();
    private DateTime _recentMultiAt;
    private ConnectionProfile? _dragProfile;
    private bool _broadcastOn;
    private string _theme = "System";
    private string _material = "Mica";
    private string _interfaceMode = UiModeService.Elegant;
    private bool _sidebarVisible = true;
    private bool _closingConfirmed;
    private bool _closePromptActive;

    public ElementTheme CurrentElementTheme => RootGrid.RequestedTheme;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ConfigureWindow();

        ApplyTheme(App.Settings.Theme);
        ApplyMaterial(App.Settings.Material);

        _interfaceMode = App.Settings.InterfaceMode;
        UiModeService.ConfigureList(SessionsList, _interfaceMode);
        if (UiModeService.IsElegant(_interfaceMode))
        {
            RootGrid.Opacity = 0;
        }

        RootGrid.Loaded += OnRootLoaded;

        Loc.Register(RootGrid);
        Localization.LanguageChanged += OnLanguageChanged;

        SessionsList.ItemsSource = _rows;
        App.ProfileStore.Profiles.CollectionChanged += OnProfilesChanged;
        App.ProfileStore.Folders.CollectionChanged += OnProfilesChanged;

        // A multiline TextBox handles Enter itself, so a normal KeyDown handler is not
        // invoked. Register with handledEventsToo so Ctrl+Enter still reaches us.
        BroadcastCommandBox.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnBroadcastCommandKeyDown),
            handledEventsToo: true);

        BroadcastSplitter.DragMoved += OnBroadcastSplitterDragMoved;

        VersionLabel.Text = $"v{UpdateService.CurrentVersion}";
        RefreshFilter();
        UpdateChrome();
        SetStatus(Localization.T("Status.Ready"), StatusKind.Success);
        RegisterAccelerators();

        AppWindow.Closing += OnAppWindowClosing;
        Closed += OnWindowClosed;
    }

    /// <summary>Asks the user whether an unknown or changed SSH host key may be trusted.</summary>
    public async Task<bool> ConfirmHostKeyAsync(HostKeyInfo info, string? savedFingerprint)
    {
        if (RootGrid.XamlRoot == null)
        {
            return false;
        }

        var endpoint = $"{info.Host}:{info.Port}";
        var dialog = new ContentDialog
        {
            Title = Localization.T("HostKey.Title"),
            Content = savedFingerprint == null
                ? Localization.T("HostKey.FirstUse", endpoint, info.Fingerprint)
                : Localization.T("HostKey.Mismatch", endpoint, info.Fingerprint, savedFingerprint),
            PrimaryButtonText = Localization.T("HostKey.TrustSave"),
            CloseButtonText = Localization.T("Common.Cancel"),
            DefaultButton = savedFingerprint == null ? ContentDialogButton.Primary : ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        ThemeService.ApplyDialogTheme(dialog);

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public bool ApplyTheme(string theme)
    {
        _theme = theme;
        ThemeService.ApplyTheme(this, RootGrid, theme);
        return ThemeService.ApplyMaterial(this, RootGrid, _material, ThemeService.IsDarkEffective(theme));
    }

    public bool ApplyMaterial(string material)
    {
        _material = material;
        return ThemeService.ApplyMaterial(this, RootGrid, material, ThemeService.IsDarkEffective(_theme));
    }

    public void ApplyInterfaceMode(string mode)
    {
        _interfaceMode = mode;
        UiModeService.ConfigureList(SessionsList, mode);
        if (!UiModeService.IsElegant(mode))
        {
            RootGrid.Opacity = 1;
            EmptyState.Opacity = 1;
        }
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnRootLoaded;
        UiModeService.PlayEntrance(RootGrid, _interfaceMode, offset: 8, durationMs: 320);

        if (EmptyState.Visibility == Visibility.Visible)
        {
            UiModeService.PlayEntrance(EmptyState, _interfaceMode, offset: 16, durationMs: 300);
        }

        _ = ShowStorageWarningsAsync();
        _ = CheckForUpdatesOnStartupAsync();
    }

    /// <summary>Surfaces unreadable configuration files instead of silently starting empty.</summary>
    private async Task ShowStorageWarningsAsync()
    {
        var warnings = new List<string>();
        if (App.ProfileStore.ProfilesUnreadable)
        {
            warnings.Add(Localization.T("Storage.ProfilesUnreadable"));
        }

        if (App.ProfileStore.FoldersUnreadable)
        {
            warnings.Add(Localization.T("Storage.FoldersUnreadable"));
        }

        if (App.Settings.LoadFailed)
        {
            warnings.Add(Localization.T("Storage.SettingsUnreadable"));
        }

        if (warnings.Count == 0)
        {
            return;
        }

        try
        {
            var dialog = new ContentDialog
            {
                Title = Localization.T("Storage.WarningTitle"),
                Content = string.Join(Environment.NewLine + Environment.NewLine, warnings),
                CloseButtonText = Localization.T("Common.Close"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };
            ThemeService.ApplyDialogTheme(dialog);
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not show configuration warning: {ex.Message}");
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Loc.ApplyTree(RootGrid);
        foreach (var item in SessionTabs.TabItems)
        {
            if (item is TabViewItem tab)
            {
                Loc.ApplyTree(tab.Content as DependencyObject);
                ApplyTabToolTips(tab);
                ApplyTabFlyoutText(tab);

                if (tab.Content is ConcurrentTerminalView)
                {
                    tab.Header = Localization.T("Tab.Concurrent");
                }
            }
        }

        UpdateChrome();
        SetStatus(Localization.T("Status.Ready"), StatusKind.Success);
    }

    private void ConfigureWindow()
    {
        try
        {
            var iconPath = AppPaths.AssetFile("app.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to set window icon: {ex.Message}");
        }

        try
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var width = Math.Clamp(display.WorkArea.Width - 160, 1024, 1560);
            var height = Math.Clamp(display.WorkArea.Height - 120, 680, 980);
            AppWindow.Resize(new SizeInt32(width, height));
            AppWindow.Move(new PointInt32(
                display.WorkArea.X + (display.WorkArea.Width - width) / 2,
                display.WorkArea.Y + (display.WorkArea.Height - height) / 2));
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to size main window: {ex.Message}");
        }
    }

    private void RegisterAccelerators()
    {
        var newSession = new KeyboardAccelerator { Key = VirtualKey.T, Modifiers = VirtualKeyModifiers.Control };
        newSession.Invoked += async (_, args) =>
        {
            args.Handled = true;
            await CreateProfileAsync(ProtocolKind.Ssh);
        };
        RootGrid.KeyboardAccelerators.Add(newSession);

        var closeSession = new KeyboardAccelerator { Key = VirtualKey.W, Modifiers = VirtualKeyModifiers.Control };
        closeSession.Invoked += async (_, args) =>
        {
            if (SessionTabs.SelectedItem is TabViewItem tab)
            {
                args.Handled = true;
                await RequestCloseTabAsync(tab);
            }
        };
        RootGrid.KeyboardAccelerators.Add(closeSession);
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshFilter();

    private void RefreshFilter()
    {
        var query = (SearchBox.Text ?? "").Trim();

        bool Matches(ConnectionProfile profile, string folder) =>
            query.Length == 0 ||
            profile.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            profile.Host.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            profile.Username.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            folder.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            profile.ProtocolLabel.Contains(query, StringComparison.OrdinalIgnoreCase);

        _rows.Clear();

        foreach (var profile in App.ProfileStore.Profiles
                     .Where(p => string.IsNullOrEmpty(p.Group) && Matches(p, ""))
                     .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            _rows.Add(SidebarRow.ForProfile(profile, 0));
        }

        foreach (var folder in App.ProfileStore.Folders)
        {
            var items = App.ProfileStore.Profiles
                .Where(p => p.Group == folder && Matches(p, folder))
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (query.Length > 0 && items.Count == 0)
            {
                continue;
            }

            var expanded = !_collapsedFolders.Contains(folder);
            _rows.Add(SidebarRow.ForFolder(folder, items.Count, expanded));
            if (!expanded)
            {
                continue;
            }

            foreach (var profile in items)
            {
                _rows.Add(SidebarRow.ForProfile(profile, 16));
            }
        }

        ProfileCountText.Text = App.ProfileStore.Profiles.Count.ToString();
    }

    private void UpdateChrome()
    {
        var count = SessionTabs.TabItems.Count;
        var showingEmpty = count == 0;
        var wasEmpty = EmptyState.Visibility == Visibility.Visible;
        SessionTabs.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = showingEmpty ? Visibility.Visible : Visibility.Collapsed;

        if (showingEmpty && !wasEmpty && EmptyState.IsLoaded)
        {
            UiModeService.PlayEntrance(EmptyState, _interfaceMode, offset: 16, durationMs: 280);
        }
        else if (!showingEmpty)
        {
            EmptyState.Opacity = 1;
        }

        SessionCountText.Text = Localization.T("Status.Sessions", SessionCount());
        UpdateBroadcastBarVisibility();
        UpdateBroadcastState();

        if (count == 0 && _broadcastOn)
        {
            SetBroadcast(false);
        }
    }

    private int SessionCount() =>
        SessionTabs.TabItems
            .OfType<TabViewItem>()
            .Sum(tab => tab.Content is ConcurrentTerminalView concurrent ? concurrent.Count : 1);

    public void ReportStatus(string message, bool isError = false) =>
        SetStatus(message, isError ? StatusKind.Error : StatusKind.Success);

    public void UpdateTabHeader(object content, string title)
    {
        if (FindTab(content) is { } tab)
        {
            tab.Header = title;
        }
    }

    private TabViewItem? FindTab(object content) =>
        SessionTabs.TabItems
            .OfType<TabViewItem>()
            .FirstOrDefault(tab => ReferenceEquals(tab.Content, content));

    private void SetStatus(string message, StatusKind kind)
    {
        StatusText.Text = message;
        var color = kind switch
        {
            StatusKind.Busy => Color.FromArgb(255, 56, 189, 248),
            StatusKind.Success => Color.FromArgb(255, 52, 211, 153),
            StatusKind.Error => Color.FromArgb(255, 248, 113, 113),
            _ => Color.FromArgb(255, 148, 163, 184)
        };
        StatusDot.Fill = new SolidColorBrush(color);
    }

    private enum StatusKind
    {
        Idle,
        Busy,
        Success,
        Error
    }

    private TabViewItem AddTab(ConnectionProfile profile, object content)
    {
        var tab = new TabViewItem
        {
            Header = profile.Name,
            IconSource = new FontIconSource { Glyph = profile.ProtocolGlyph, FontSize = 13 },
            Content = content,
            IsClosable = true,
            Tag = profile,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        AttachTabContextMenu(tab);
        SessionTabs.TabItems.Add(tab);
        tab.Loaded += (_, _) => ApplyTabToolTips(tab);
        SessionTabs.SelectedItem = tab;
        UpdateChrome();
        return tab;
    }

    private TabViewItem AddConcurrentTab(IReadOnlyList<ConnectionProfile> profiles, ConcurrentTerminalView content,
        string? header = null)
    {
        var tab = new TabViewItem
        {
            Header = header ?? Localization.T("Tab.Concurrent"),
            IconSource = new FontIconSource { Glyph = "\uE756", FontSize = 13 },
            Content = content,
            IsClosable = true,
            Tag = profiles,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        AttachTabContextMenu(tab);
        ToolTipService.SetToolTip(tab, string.Join("\n", profiles.Select(profile => profile.Name)));

        SessionTabs.TabItems.Add(tab);
        tab.Loaded += (_, _) => ApplyTabToolTips(tab);

        content.PanesChanged += UpdateChrome;
        content.Empty += () =>
        {
            if (FindTab(content) is { } emptyTab)
            {
                _ = CloseTabAsync(emptyTab);
            }
        };

        SessionTabs.SelectedItem = tab;
        UpdateChrome();
        return tab;
    }

    private SshTerminalView CreateTerminal(ConnectionProfile profile, SshCredentials credentials)
    {
        var terminal = new SshTerminalView(profile, credentials);
        terminal.UserInput += OnTerminalUserInput;
        terminal.UserPasted += OnTerminalUserPasted;
        terminal.BroadcastMembershipChanged += _ => UpdateBroadcastState();
        return terminal;
    }

    /// <summary>Opens a new tab with two terminals of the same profile, side by side or stacked.</summary>
    public void OpenSplitTab(ConnectionProfile profile, SshCredentials credentials, bool horizontal)
    {
        var view = new ConcurrentTerminalView(
            horizontal ? TerminalSplitOrientation.Stacked : TerminalSplitOrientation.SideBySide);
        var terminals = new List<SshTerminalView>();

        for (var index = 0; index < 2; index++)
        {
            var terminal = CreateTerminal(profile, credentials);
            view.Add(profile, terminal);
            terminals.Add(terminal);
        }

        AddConcurrentTab(new[] { profile, profile }, view, Localization.T("Tab.Split", profile.Name));
        _ = Task.WhenAll(terminals.Select(terminal => terminal.StartAsync()));
        SetStatus(Localization.T("Sidebar.OpenedMany", 2), StatusKind.Success);
    }

    /// <summary>Opens a remote file in a new editor tab. Double-clicking a file in the browser calls this.</summary>
    public void OpenRemoteFileEditor(ConnectionProfile profile, SshCredentials credentials, RemoteEntry entry)
    {
        var editor = new RemoteEditorView(profile, credentials, entry);
        var tab = new TabViewItem
        {
            Header = entry.Name,
            IconSource = new FontIconSource { Glyph = "\uE70F", FontSize = 13 },
            Content = editor,
            IsClosable = true,
            Tag = entry,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        AttachTabContextMenu(tab);
        ToolTipService.SetToolTip(tab, entry.FullPath);

        SessionTabs.TabItems.Add(tab);
        tab.Loaded += (_, _) => ApplyTabToolTips(tab);
        SessionTabs.SelectedItem = tab;
        UpdateChrome();
        _ = editor.StartAsync();
    }

    private void ApplyTabToolTips(TabViewItem tab)
    {
        var close = FindDescendant<Button>(tab, "CloseButton");
        if (close != null)
        {
            var text = Localization.T("Tab.Close");
            ToolTipService.SetToolTip(close, text);
            AutomationProperties.SetName(close, text);
        }
    }

    private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && element.Name == name)
            {
                return element;
            }

            if (FindDescendant<T>(child, name) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private async Task OpenProfileAsync(ConnectionProfile profile)
    {
        try
        {
            SetStatus(Localization.T("Sidebar.Opening", profile.Name), StatusKind.Busy);

            if (profile.Protocol == ProtocolKind.Rdp)
            {
                var rdpView = new RdpView(profile);
                AddTab(profile, rdpView);
                SetStatus(Localization.T("Status.RdpReady", profile.Host), StatusKind.Success);
                return;
            }

            var credentials = await AcquireCredentialsAsync(profile);
            if (credentials == null)
            {
                SetStatus(Localization.T("Sidebar.Canceled"), StatusKind.Idle);
                return;
            }

            switch (profile.Protocol)
            {
                case ProtocolKind.Ssh:
                {
                    var terminal = CreateTerminal(profile, credentials);
                    AddTab(profile, terminal);
                    await terminal.StartAsync();
                    break;
                }
                case ProtocolKind.Sftp:
                case ProtocolKind.Ftp:
                {
                    var browser = new FileBrowserView(profile, credentials);
                    AddTab(profile, browser);
                    await browser.StartAsync();
                    break;
                }
            }

            profile.LastConnected = DateTime.Now;
            App.ProfileStore.Save();
            SetStatus(Localization.T("Sidebar.Opened", profile.Name), StatusKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open profile '{profile.Name}'", ex);
            SetStatus(SshAuth.Describe(ex), StatusKind.Error);
        }
    }

    private async Task OpenConcurrentAsync(IReadOnlyList<ConnectionProfile> profiles)
    {
        SetStatus(Localization.T("Sidebar.OpeningMany", profiles.Count), StatusKind.Busy);

        var view = new ConcurrentTerminalView();
        var opened = new List<ConnectionProfile>();

        foreach (var profile in profiles)
        {
            SetStatus(Localization.T("Sidebar.Opening", profile.Name), StatusKind.Busy);
            try
            {
                var credentials = await AcquireCredentialsAsync(profile);
                if (credentials == null)
                {
                    continue;
                }

                var terminal = CreateTerminal(profile, credentials);
                view.Add(profile, terminal);
                opened.Add(profile);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to open profile '{profile.Name}'", ex);
                SetStatus(SshAuth.Describe(ex), StatusKind.Error);
            }
        }

        if (opened.Count == 0)
        {
            SetStatus(Localization.T("Sidebar.Canceled"), StatusKind.Idle);
            return;
        }

        AddConcurrentTab(opened, view);
        await Task.WhenAll(view.Terminals.Select(terminal => terminal.StartAsync()));

        foreach (var profile in opened)
        {
            profile.LastConnected = DateTime.Now;
        }

        App.ProfileStore.Save();
        SetStatus(Localization.T("Sidebar.OpenedMany", opened.Count), StatusKind.Success);
    }

    private async Task<SshCredentials?> AcquireCredentialsAsync(ConnectionProfile profile)
    {
        if (profile.Auth == AuthKind.PrivateKey)
        {
            var passphrase = profile.GetPassphrase();
            if (string.IsNullOrEmpty(passphrase) && PrivateKeyNeedsPassphrase(profile.PrivateKeyPath))
            {
                var dialog = new PasswordDialog(
                    Localization.T("Password.PassphraseTitle"),
                    Localization.T("Password.PassphraseMessage", Path.GetFileName(profile.PrivateKeyPath)),
                    Localization.T("Password.Unlock"),
                    passwordPlaceholder: Localization.T("Password.PassphrasePlaceholder"))
                {
                    XamlRoot = RootGrid.XamlRoot
                };
                ThemeService.ApplyDialogTheme(dialog);

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return null;
                }

                passphrase = dialog.Password;
                if (dialog.RememberPassword)
                {
                    profile.SetPassphrase(passphrase);
                    App.ProfileStore.Save();
                }
            }

            return new SshCredentials(profile.GetPassword() ?? "", passphrase);
        }

        var password = profile.GetPassword();
        if (string.IsNullOrEmpty(password))
        {
            var dialog = new PasswordDialog(
                Localization.T("Password.ConnectTitle", profile.Host),
                Localization.T("Password.ConnectMessage", profile.Username, profile.Host),
                Localization.T("Password.Connect"))
            {
                XamlRoot = RootGrid.XamlRoot
            };
            ThemeService.ApplyDialogTheme(dialog);

            if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrEmpty(dialog.Password))
            {
                return null;
            }

            password = dialog.Password;
            if (dialog.RememberPassword)
            {
                profile.SetPassword(password);
                App.ProfileStore.Save();
            }
        }

        return new SshCredentials(password, profile.GetPassphrase());
    }

    private static bool PrivateKeyNeedsPassphrase(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            using var _ = new PrivateKeyFile(path);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private async Task CreateProfileAsync(ProtocolKind protocol)
    {
        var draft = new ConnectionProfile
        {
            Protocol = protocol,
            Port = protocol switch
            {
                ProtocolKind.Ftp => 21,
                ProtocolKind.Rdp => 3389,
                _ => 22
            }
        };

        var dialog = new ProfileDialog(draft, isNew: true) { XamlRoot = RootGrid.XamlRoot };
        ThemeService.ApplyDialogTheme(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result != null)
        {
            App.ProfileStore.Add(dialog.Result);
            App.ProfileStore.EnsureFolder(dialog.Result.Group);
            RefreshFilter();
            await OpenProfileAsync(dialog.Result);
        }
    }

    private async Task EditProfileAsync(ConnectionProfile profile)
    {
        var dialog = new ProfileDialog(profile.Clone()) { XamlRoot = RootGrid.XamlRoot };
        ThemeService.ApplyDialogTheme(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result != null)
        {
            profile.CopyEditableFrom(dialog.Result);
            App.ProfileStore.Save();
            App.ProfileStore.EnsureFolder(profile.Group);
            RefreshFilter();

            foreach (var item in SessionTabs.TabItems)
            {
                if (item is not TabViewItem tab)
                {
                    continue;
                }

                if (ReferenceEquals(tab.Tag, profile))
                {
                    tab.Header = profile.Name;
                }
                else if (tab.Tag is IReadOnlyList<ConnectionProfile> group &&
                         group.Contains(profile) &&
                         tab.Content is ConcurrentTerminalView concurrent)
                {
                    concurrent.UpdateTitle(profile);
                }
            }

            SetStatus(Localization.T("Sidebar.ProfileUpdated"), StatusKind.Success);
        }
    }

    private async Task DeleteProfileAsync(ConnectionProfile profile)
    {
        var dialog = new ContentDialog
        {
            Title = Localization.T("Sidebar.DeleteTitle"),
            Content = Localization.T("Sidebar.DeleteConfirm", profile.Name),
            PrimaryButtonText = Localization.T("Common.Delete"),
            CloseButtonText = Localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        ThemeService.ApplyDialogTheme(dialog);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.ProfileStore.Remove(profile);
            RefreshFilter();
            SetStatus(Localization.T("Sidebar.Deleted", profile.Name), StatusKind.Idle);
        }
    }

    /// <summary>Enables the tab menu for right-clicks on the tab header only, not on its content.</summary>
    private void AttachTabContextMenu(TabViewItem tab) =>
        tab.ContextRequested += OnTabContextRequested;

    private void OnTabContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not TabViewItem tab)
        {
            return;
        }

        if (IsWithinContent(args.OriginalSource as DependencyObject, tab.Content as DependencyObject))
        {
            // Terminals, file browsers and editors each provide their own menus.
            tab.ContextFlyout = null;
            return;
        }

        tab.ContextFlyout = CreateTabContextFlyout(tab);
    }

    private static bool IsWithinContent(DependencyObject? source, DependencyObject? content)
    {
        if (content == null)
        {
            return false;
        }

        for (var node = source; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, content))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds the right-click menu shown on a tab header.</summary>
    private MenuFlyout CreateTabContextFlyout(TabViewItem tab)
    {
        var flyout = new MenuFlyout();
        var closeCurrent = AddTabMenuItem(flyout, "Tab.CloseCurrent", "\uE711",
            () => RequestCloseTabAsync(tab));
        var closeOthers = AddTabMenuItem(flyout, "Tab.CloseOthers", "\uE8BB",
            () => CloseTabsAsync(OtherTabs(tab)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        var closeLeft = AddTabMenuItem(flyout, "Tab.CloseLeft", "\uE76B",
            () => CloseTabsAsync(TabsBefore(tab)));
        var closeRight = AddTabMenuItem(flyout, "Tab.CloseRight", "\uE76C",
            () => CloseTabsAsync(TabsAfter(tab)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddTabMenuItem(flyout, "Tab.CloseAll", "\uE8BB", () => CloseTabsAsync(AllTabs()));

        flyout.Opening += (_, _) =>
        {
            var index = SessionTabs.TabItems.IndexOf(tab);
            var count = SessionTabs.TabItems.Count;
            closeCurrent.IsEnabled = index >= 0;
            closeOthers.IsEnabled = count > 1;
            closeLeft.IsEnabled = index > 0;
            closeRight.IsEnabled = index >= 0 && index < count - 1;
        };

        return flyout;
    }

    private static MenuFlyoutItem AddTabMenuItem(MenuFlyout flyout, string key, string glyph, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Icon = new FontIcon { Glyph = glyph } };
        Loc.SetKey(item, key);
        item.Click += async (_, _) => await action();
        flyout.Items.Add(item);
        return item;
    }

    /// <summary>Re-applies localized text to an open tab's context menu after a language switch.</summary>
    private static void ApplyTabFlyoutText(TabViewItem tab)
    {
        if (tab.ContextFlyout is not MenuFlyout flyout)
        {
            return;
        }

        foreach (var entry in flyout.Items)
        {
            if (entry is MenuFlyoutItem item && Loc.GetKey(item) is { Length: > 0 } key)
            {
                item.Text = Localization.T(key);
            }
        }
    }

    private IEnumerable<TabViewItem> AllTabs() =>
        SessionTabs.TabItems.OfType<TabViewItem>().ToList();

    private IEnumerable<TabViewItem> OtherTabs(TabViewItem tab) =>
        SessionTabs.TabItems.OfType<TabViewItem>()
            .Where(item => !ReferenceEquals(item, tab))
            .ToList();

    private IEnumerable<TabViewItem> TabsBefore(TabViewItem tab)
    {
        var result = new List<TabViewItem>();
        foreach (var item in SessionTabs.TabItems)
        {
            if (ReferenceEquals(item, tab))
            {
                break;
            }

            if (item is TabViewItem other)
            {
                result.Add(other);
            }
        }

        return result;
    }

    private IEnumerable<TabViewItem> TabsAfter(TabViewItem tab)
    {
        var result = new List<TabViewItem>();
        var found = false;
        foreach (var item in SessionTabs.TabItems)
        {
            if (!found)
            {
                found = ReferenceEquals(item, tab);
                continue;
            }

            if (item is TabViewItem other)
            {
                result.Add(other);
            }
        }

        return result;
    }

    private async Task CloseTabsAsync(IEnumerable<TabViewItem> tabs)
    {
        foreach (var tab in tabs.ToList())
        {
            await RequestCloseTabAsync(tab);
        }
    }

    private async Task CloseTabAsync(TabViewItem tab)
    {
        SessionTabs.TabItems.Remove(tab);
        switch (tab.Content)
        {
            case IAsyncDisposable asyncDisposable:
                try
                {
                    await asyncDisposable.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Log.Warn($"Dispose session failed: {ex.Message}");
                }

                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }

        UpdateChrome();
    }

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) =>
        await RequestCloseTabAsync(args.Tab);

    private async Task RequestCloseTabAsync(TabViewItem tab)
    {
        if (tab.Content is RemoteEditorView editor && !await editor.ConfirmCloseAsync())
        {
            return;
        }

        await CloseTabAsync(tab);
    }

    // ---- Concurrent (broadcast) input ----

    private IEnumerable<SshTerminalView> TerminalViews() =>
        SessionTabs.TabItems
            .OfType<TabViewItem>()
            .SelectMany(tab => tab.Content switch
            {
                SshTerminalView terminal => (IEnumerable<SshTerminalView>)new[] { terminal },
                ConcurrentTerminalView concurrent => concurrent.Terminals,
                _ => Array.Empty<SshTerminalView>()
            });

    private void OnBroadcastToggleClick(object sender, RoutedEventArgs e) =>
        SetBroadcast(BroadcastToggle.IsChecked == true);

    private void SetBroadcast(bool on)
    {
        _broadcastOn = on;
        BroadcastToggle.IsChecked = on;
        UpdateBroadcastBarVisibility();
        UpdateBroadcastState();

        if (on)
        {
            BroadcastCommandBox.Focus(FocusState.Programmatic);
        }

        SetStatus(Localization.T(on ? "Broadcast.On" : "Broadcast.Off"),
            on ? StatusKind.Success : StatusKind.Idle);
    }

    /// <summary>A concurrent (or split) tab keeps the command bar visible without the global toggle.</summary>
    private bool IsConcurrentTabSelected =>
        (SessionTabs.SelectedItem as TabViewItem)?.Content is ConcurrentTerminalView;

    private void UpdateBroadcastBarVisibility()
    {
        var visible = _broadcastOn || IsConcurrentTabSelected;
        BroadcastBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BroadcastSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Drag the line above the command bar to resize the command area.</summary>
    private void OnBroadcastSplitterDragMoved(object? sender, double pointerY)
    {
        if (BroadcastBar.Parent is not FrameworkElement host || host.ActualHeight <= 0)
        {
            return;
        }

        const double minimum = 72;   // padding + one line of input
        const double terminalMinimum = 220;
        var maximum = Math.Max(minimum, host.ActualHeight - terminalMinimum);
        var target = host.ActualHeight - pointerY - BroadcastSplitter.ActualHeight;
        BroadcastBar.Height = Math.Clamp(target, minimum, maximum);
    }

    private void OnSessionTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBroadcastBarVisibility();
        UpdateBroadcastState();
    }

    /// <summary>
    /// Sessions a command-bar action targets. With the global toggle on this spans every tab;
    /// otherwise it stays inside the currently selected concurrent tab.
    /// </summary>
    private IEnumerable<SshTerminalView> BroadcastTargets()
    {
        if (_broadcastOn)
        {
            return TerminalViews();
        }

        return (SessionTabs.SelectedItem as TabViewItem)?.Content is ConcurrentTerminalView concurrent
            ? concurrent.Terminals
            : Array.Empty<SshTerminalView>();
    }

    private void UpdateBroadcastState()
    {
        if (BroadcastCountText == null || BroadcastSendButton == null || BroadcastCtrlCButton == null ||
            BroadcastClearTerminalsButton == null)
        {
            return;
        }

        var total = TerminalViews().Count();
        var members = BroadcastTargets().Count(view => view.IsBroadcastMember);
        BroadcastCountText.Text = total == 0
            ? Localization.T("Broadcast.None")
            : Localization.T("Broadcast.Members", members);
        BroadcastSendButton.IsEnabled = members > 0;
        BroadcastCtrlCButton.IsEnabled = members > 0;
        BroadcastClearTerminalsButton.IsEnabled = members > 0;
        BroadcastToggle.IsEnabled = total > 0;
    }

    private void OnTerminalUserInput(SshTerminalView source, byte[] data)
    {
        if (!source.IsBroadcastMember)
        {
            return;
        }

        var targets = _broadcastOn ? TerminalViews() : ConcurrentSiblings(source);
        foreach (var view in targets)
        {
            if (!ReferenceEquals(view, source) && view.IsBroadcastMember)
            {
                view.WriteBroadcast(data);
            }
        }
    }

    /// <summary>
    /// Mirrors a paste as text, not bytes: each session wraps it in bracketed-paste markers
    /// only when its own remote application asked for them, so a peer without support does
    /// not receive marker escape codes as literal input.
    /// </summary>
    private async void OnTerminalUserPasted(SshTerminalView source, string text)
    {
        if (!source.IsBroadcastMember)
        {
            return;
        }

        var targets = _broadcastOn ? TerminalViews() : ConcurrentSiblings(source);
        foreach (var view in targets)
        {
            if (!ReferenceEquals(view, source) && view.IsBroadcastMember)
            {
                await view.WritePastedTextAsync(text);
            }
        }
    }

    /// <summary>
    /// Terminals sharing the source's concurrent tab. A concurrent tab is itself a broadcast
    /// group, so its panes mirror typed and pasted input even when the global toggle is off.
    /// </summary>
    private IEnumerable<SshTerminalView> ConcurrentSiblings(SshTerminalView source)
    {
        foreach (var item in SessionTabs.TabItems)
        {
            if (item is TabViewItem { Content: ConcurrentTerminalView concurrent } && concurrent.Contains(source))
            {
                return concurrent.Terminals;
            }
        }

        return Array.Empty<SshTerminalView>();
    }

    private void OnBroadcastSendClick(object sender, RoutedEventArgs e) => SendBroadcastCommand();

    private void OnBroadcastCommandKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        // The box is multi-line: plain Enter inserts a line break, Ctrl+Enter sends.
        if (!IsKeyDown(VirtualKey.Control) &&
            !IsKeyDown(VirtualKey.LeftControl) &&
            !IsKeyDown(VirtualKey.RightControl))
        {
            return;
        }

        // PreviewKeyDown runs before the TextBox inserts the newline.
        e.Handled = true;
        SendBroadcastCommand();
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private void OnBroadcastClearClick(object sender, RoutedEventArgs e)
    {
        BroadcastCommandBox.Text = "";
        BroadcastCommandBox.Focus(FocusState.Programmatic);
    }

    private void OnBroadcastClearTerminalsClick(object sender, RoutedEventArgs e)
    {
        var members = BroadcastTargets().Where(view => view.IsBroadcastMember).ToList();
        if (members.Count == 0)
        {
            SetStatus(Localization.T("Broadcast.NoMember"), StatusKind.Error);
            return;
        }

        foreach (var view in members)
        {
            view.ClearTerminal();
        }

        BroadcastCommandBox.Focus(FocusState.Programmatic);
        SetStatus(Localization.T("Broadcast.TerminalsCleared", members.Count), StatusKind.Success);
    }

    private void OnBroadcastCtrlCClick(object sender, RoutedEventArgs e)
    {
        var members = BroadcastTargets().Where(view => view.IsBroadcastMember).ToList();
        if (members.Count == 0)
        {
            SetStatus(Localization.T("Broadcast.NoMember"), StatusKind.Error);
            return;
        }

        foreach (var view in members)
        {
            view.WriteBroadcast(new byte[] { 0x03 });
        }

        SetStatus(Localization.T("Broadcast.CtrlCSent", members.Count), StatusKind.Success);
    }

    private void SendBroadcastCommand()
    {
        var command = BroadcastCommandBox.Text;
        if (string.IsNullOrWhiteSpace(command))
        {
            SetStatus(Localization.T("Broadcast.EmptyCommand"), StatusKind.Error);
            return;
        }

        var members = BroadcastTargets().Where(view => view.IsBroadcastMember).ToList();
        if (members.Count == 0)
        {
            SetStatus(Localization.T("Broadcast.NoMember"), StatusKind.Error);
            return;
        }

        foreach (var view in members)
        {
            view.SendCommandLine(command);
        }

        BroadcastCommandBox.Text = "";
        SetStatus(Localization.T("Broadcast.Sent", members.Count), StatusKind.Success);
    }

    private async void OnSessionsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SelectedRow() is { IsFolder: true } folderRow && SessionsList.SelectedItems.Count <= 1)
        {
            ToggleFolder(folderRow.Folder);
            return;
        }

        await OpenSelectedAsync();
    }

    private async void OnSessionsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        if (SelectedRow() is { IsFolder: true } folderRow && SessionsList.SelectedItems.Count <= 1)
        {
            e.Handled = true;
            ToggleFolder(folderRow.Folder);
            return;
        }

        if (SelectedProfiles().Count > 0)
        {
            e.Handled = true;
            await OpenSelectedAsync();
        }
    }

    private void OnSessionsRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (item?.Content is SidebarRow row && !SessionsList.SelectedItems.Contains(row))
        {
            SessionsList.SelectedItem = row;
        }
    }

    private SidebarRow? SelectedRow() => SessionsList.SelectedItem as SidebarRow;

    private ConnectionProfile? SelectedProfile() => SelectedRow()?.Profile;

    /// <summary>All selected sessions (folder rows are ignored).</summary>
    private List<ConnectionProfile> SelectedProfiles() =>
        SessionsList.SelectedItems
            .OfType<SidebarRow>()
            .Select(row => row.Profile)
            .Where(profile => profile != null)
            .Select(profile => profile!)
            .Distinct()
            .ToList();

    /// <summary>
    /// Mouse/double-click handling in a multi-select ListView collapses the selection to the clicked
    /// item before DoubleTapped fires, so remember the last multi-selection and reuse it briefly.
    /// </summary>
    private void OnSessionsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var profiles = SelectedProfiles();
        if (profiles.Count > 1)
        {
            _recentMultiSelection.Clear();
            _recentMultiSelection.AddRange(profiles);
            _recentMultiAt = DateTime.UtcNow;
        }
        else if (profiles.Count == 0 || IsSelectionModifierDown())
        {
            // A deliberate Ctrl/Shift deselection must not keep the old multi-selection alive;
            // a plain click during a double-click collapses to one item and keeps it briefly.
            _recentMultiSelection.Clear();
        }
    }

    private static bool IsSelectionModifierDown()
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        return (ctrl & CoreVirtualKeyStates.Down) != 0 || (shift & CoreVirtualKeyStates.Down) != 0;
    }

    private List<ConnectionProfile> ProfilesToOpen()
    {
        var profiles = SelectedProfiles();
        if (profiles.Count == 1 &&
            _recentMultiSelection.Count > 1 &&
            DateTime.UtcNow - _recentMultiAt < TimeSpan.FromSeconds(4) &&
            _recentMultiSelection.Contains(profiles[0]))
        {
            return _recentMultiSelection.ToList();
        }

        return profiles;
    }

    /// <summary>Opens the selected sessions; multiple SSH sessions share one auto-arranged concurrent tab.</summary>
    private async Task OpenSelectedAsync()
    {
        var profiles = ProfilesToOpen();
        if (profiles.Count == 0)
        {
            return;
        }

        if (profiles.Count == 1)
        {
            await OpenProfileAsync(profiles[0]);
            return;
        }

        _recentMultiSelection.Clear();
        SetStatus(Localization.T("Sidebar.OpeningMany", profiles.Count), StatusKind.Busy);

        var sshProfiles = profiles.Where(profile => profile.Protocol == ProtocolKind.Ssh).ToList();
        if (sshProfiles.Count > 1)
        {
            await OpenConcurrentAsync(sshProfiles);

            foreach (var profile in profiles.Where(profile => profile.Protocol != ProtocolKind.Ssh))
            {
                await OpenProfileAsync(profile);
            }

            return;
        }

        foreach (var profile in profiles)
        {
            await OpenProfileAsync(profile);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null && element is not T)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        return element as T;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshFilter();

    private async void OnAddSsh(object sender, RoutedEventArgs e) => await CreateProfileAsync(ProtocolKind.Ssh);

    private async void OnAddSftp(object sender, RoutedEventArgs e) => await CreateProfileAsync(ProtocolKind.Sftp);

    private async void OnAddFtp(object sender, RoutedEventArgs e) => await CreateProfileAsync(ProtocolKind.Ftp);

    private async void OnAddRdp(object sender, RoutedEventArgs e) => await CreateProfileAsync(ProtocolKind.Rdp);

    private async void OnOpenContextClick(object sender, RoutedEventArgs e) => await OpenSelectedAsync();

    private async void OnEditContextClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile() is { } profile)
        {
            await EditProfileAsync(profile);
        }
    }

    private void OnDuplicateContextClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile() is { } profile)
        {
            var copy = profile.Clone(newIdentity: true);
            copy.Name = profile.Name + Localization.T("Sidebar.CopySuffix");
            App.ProfileStore.Add(copy);
            RefreshFilter();
            SetStatus(Localization.T("Sidebar.Duplicated", profile.Name), StatusKind.Success);
        }
    }

    private async void OnDeleteContextClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile() is { } profile)
        {
            await DeleteProfileAsync(profile);
        }
    }

    private void OnSessionsContextOpening(object sender, object e)
    {
        var openTargets = ProfilesToOpen();
        var profiles = SelectedProfiles();
        var single = profiles.Count == 1 && SessionsList.SelectedItems.Count == 1 && openTargets.Count == 1;
        var multi = openTargets.Count > 1;
        var isFolder = profiles.Count == 0 && SelectedRow() is { IsFolder: true } &&
                       SessionsList.SelectedItems.Count <= 1;

        OpenItem.Visibility = openTargets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OpenItem.Text = multi
            ? $"{Localization.T("Sidebar.OpenSelected")} ({openTargets.Count})"
            : Localization.T("Sidebar.Open");
        EditItem.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        DuplicateItem.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        MoveToItem.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        ProfileSeparator.Visibility = openTargets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DeleteItem.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        RenameFolderItem.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
        DeleteFolderItem.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;

        if (single)
        {
            PopulateMoveToMenu(profiles[0]);
        }
    }

    private void PopulateMoveToMenu(ConnectionProfile? profile)
    {
        MoveToItem.Items.Clear();
        if (profile == null)
        {
            return;
        }

        var current = profile.Group ?? "";
        var root = new MenuFlyoutItem
        {
            Text = Localization.T("Sidebar.Root"),
            Icon = new FontIcon { Glyph = "\uE8B7" },
            IsEnabled = current.Length > 0
        };
        root.Click += (_, _) => MoveProfile(profile, "");
        MoveToItem.Items.Add(root);

        if (App.ProfileStore.Folders.Count > 0)
        {
            MoveToItem.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (var folder in App.ProfileStore.Folders)
        {
            var item = new MenuFlyoutItem
            {
                Text = folder,
                Icon = new FontIcon { Glyph = "\uE8F4" },
                IsEnabled = folder != current
            };
            item.Click += (_, _) => MoveProfile(profile, folder);
            MoveToItem.Items.Add(item);
        }
    }

    private void MoveProfile(ConnectionProfile profile, string folder)
    {
        if (!App.ProfileStore.MoveToFolder(profile, folder))
        {
            return;
        }

        if (folder.Length > 0)
        {
            _collapsedFolders.Remove(folder);
        }

        RefreshFilter();
        var target = folder.Length == 0 ? Localization.T("Sidebar.Root") : folder;
        SetStatus(Localization.T("Sidebar.MovedTo", profile.Name, target), StatusKind.Success);
    }

    private void OnFolderToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string folder })
        {
            ToggleFolder(folder);
        }
    }

    private void ToggleFolder(string folder)
    {
        if (!_collapsedFolders.Remove(folder))
        {
            _collapsedFolders.Add(folder);
        }

        RefreshFilter();
    }

    private async void OnNewFolderClick(object sender, RoutedEventArgs e)
    {
        var name = await PromptFolderNameAsync(Localization.T("Sidebar.NewFolder"), "");
        if (name == null)
        {
            return;
        }

        if (!App.ProfileStore.EnsureFolder(name))
        {
            SetStatus(Localization.T("Sidebar.FolderExists", name), StatusKind.Error);
            return;
        }

        _collapsedFolders.Remove(name);
        RefreshFilter();
        SetStatus(Localization.T("Sidebar.FolderCreated", name), StatusKind.Success);
    }

    private async void OnRenameFolderClick(object sender, RoutedEventArgs e)
    {
        var row = SelectedRow();
        if (row is not { IsFolder: true })
        {
            return;
        }

        var name = await PromptFolderNameAsync(Localization.T("Sidebar.RenameFolder"), row.Folder);
        if (name == null || name == row.Folder)
        {
            return;
        }

        if (!App.ProfileStore.RenameFolder(row.Folder, name))
        {
            SetStatus(Localization.T("Sidebar.FolderExists", name), StatusKind.Error);
            return;
        }

        if (_collapsedFolders.Remove(row.Folder))
        {
            _collapsedFolders.Add(name);
        }

        RefreshFilter();
        SetStatus(Localization.T("Sidebar.FolderRenamed", name), StatusKind.Success);
    }

    private async void OnDeleteFolderClick(object sender, RoutedEventArgs e)
    {
        var row = SelectedRow();
        if (row is not { IsFolder: true })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = Localization.T("Sidebar.DeleteFolder"),
            Content = Localization.T("Sidebar.DeleteFolderConfirm", row.Folder),
            PrimaryButtonText = Localization.T("Common.Delete"),
            CloseButtonText = Localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        ThemeService.ApplyDialogTheme(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        App.ProfileStore.DeleteFolder(row.Folder);
        _collapsedFolders.Remove(row.Folder);
        RefreshFilter();
        SetStatus(Localization.T("Sidebar.FolderDeleted", row.Folder), StatusKind.Idle);
    }

    private async Task<string?> PromptFolderNameAsync(string title, string initial)
    {
        var box = new TextBox
        {
            Text = initial,
            PlaceholderText = Localization.T("Sidebar.FolderNamePlaceholder"),
            SelectionStart = initial.Length
        };
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = Localization.T("Common.Confirm"),
            CloseButtonText = Localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        ThemeService.ApplyDialogTheme(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var name = box.Text.Trim();
        return name.Length == 0 ? null : name;
    }

    private void OnSessionsDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count != 1 || e.Items[0] is not SidebarRow { Profile: { } profile })
        {
            _dragProfile = null;
            e.Cancel = true;
            return;
        }

        _dragProfile = profile;
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnSessionsDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        _dragProfile = null;

    private void OnRootDragOver(object sender, DragEventArgs e) => HandleDragOver(e);

    private void OnSessionsDragOver(object sender, DragEventArgs e) => HandleDragOver(e);

    private void HandleDragOver(DragEventArgs e)
    {
        e.Handled = true;
        if (_dragProfile == null)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        var folder = ResolveDropFolder(e);
        e.DragUIOverride.Caption = folder == null
            ? Localization.T("Sidebar.MoveToRoot")
            : Localization.T("Sidebar.MoveToCaption", folder);
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = false;
    }

    private void OnRootDrop(object sender, DragEventArgs e) => HandleDrop(e);

    private void OnSessionsDrop(object sender, DragEventArgs e) => HandleDrop(e);

    private void HandleDrop(DragEventArgs e)
    {
        e.Handled = true;
        var profile = _dragProfile;
        _dragProfile = null;
        if (profile == null)
        {
            return;
        }

        var folder = ResolveDropFolder(e) ?? "";
        MoveProfile(profile, folder);
    }

    private string? ResolveDropFolder(DragEventArgs e)
    {
        var row = RowAt(e);
        return row == null ? null : (row.IsFolder ? row.Folder : row.Profile?.Group ?? "");
    }

    private SidebarRow? RowAt(DragEventArgs e)
    {
        var point = e.GetPosition(SessionsList);
        if (point.X < 0 || point.Y < 0 || point.X > SessionsList.ActualWidth || point.Y > SessionsList.ActualHeight)
        {
            return null;
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            if (SessionsList.ContainerFromIndex(i) is not ListViewItem item)
            {
                continue;
            }

            var origin = item.TransformToVisual(SessionsList).TransformPoint(new Windows.Foundation.Point(0, 0));
            if (point.X >= origin.X && point.X <= origin.X + item.ActualWidth &&
                point.Y >= origin.Y && point.Y <= origin.Y + item.ActualHeight)
            {
                return item.Content as SidebarRow;
            }
        }

        return null;
    }

    private void OnToggleSidebarClick(object sender, RoutedEventArgs e)
    {
        _sidebarVisible = !_sidebarVisible;
        UiModeService.SetSidebarVisible(SidebarRoot, SidebarColumn, _sidebarVisible, _interfaceMode);
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e) => await OpenSettingsAsync();

    private async void OnAboutClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync(SettingsDialog.AboutCategory);

    private async Task OpenSettingsAsync(int category = 0)
    {
        var dialog = new SettingsDialog(App.Settings, category) { XamlRoot = RootGrid.XamlRoot };
        await dialog.ShowAsync();

        if (dialog.Result != null)
        {
            var loadFailed = App.Settings.LoadFailed;
            App.Settings = dialog.Result;
            // Preserve the protection flag: Save() must not overwrite an unreadable file.
            App.Settings.LoadFailed = loadFailed;
            App.Settings.Save();
            ApplySettingsToOpenViews();
            SetStatus(
                Localization.T(loadFailed ? "Status.SettingsNotSaved" : "Status.SettingsSaved"),
                loadFailed ? StatusKind.Error : StatusKind.Success);
        }

        if (dialog.PendingUpdate != null)
        {
            await DownloadAndInstallUpdateAsync(dialog.PendingUpdate);
        }
    }

    // ---- Updates ----

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!App.Settings.CheckUpdatesOnStartup)
        {
            return;
        }

        if (DateTimeOffset.TryParse(App.Settings.LastUpdateCheckUtc, out var lastCheck) &&
            DateTimeOffset.UtcNow - lastCheck.ToUniversalTime() < TimeSpan.FromHours(24))
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6));
            if (RootGrid.XamlRoot == null)
            {
                return;
            }

            var update = await UpdateService.CheckLatestReleaseAsync(CancellationToken.None);
            Log.Info($"Update check: current={UpdateService.CurrentVersion} latest={update.Manifest.Version} newer={update.IsNewer}");
            App.Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow.ToString("O");
            App.Settings.Save();

            if (!update.IsNewer || RootGrid.XamlRoot == null)
            {
                return;
            }

            var prompt = new ContentDialog
            {
                Title = Localization.T("Update.NewVersionTitle"),
                Content = Localization.T("Update.NewVersionMessage", update.Manifest.Version, update.Manifest.Notes),
                PrimaryButtonText = Localization.T("Update.InstallNow"),
                CloseButtonText = Localization.T("Update.Later"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };
            ThemeService.ApplyDialogTheme(prompt);

            if (await prompt.ShowAsync() == ContentDialogResult.Primary)
            {
                await DownloadAndInstallUpdateAsync(update);
            }
        }
        catch (Exception ex)
        {
            // A check failure is not worth interrupting startup.
            Log.Warn($"Startup update check failed: {ex.Message}");
        }
    }

    private async Task DownloadAndInstallUpdateAsync(UpdateCheckResult update)
    {
        if (RootGrid.XamlRoot == null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Width = 380 };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(status);
        content.Children.Add(progress);

        var dialog = new ContentDialog
        {
            Title = Localization.T("Update.Title"),
            Content = content,
            CloseButtonText = Localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        ThemeService.ApplyDialogTheme(dialog);
        dialog.CloseButtonClick += (_, _) => cancellation.Cancel();

        var showTask = dialog.ShowAsync();
        var reported = new Progress<double>(fraction =>
        {
            progress.Value = Math.Clamp(fraction * 100, 0, 100);
            status.Text = Localization.T("Update.Downloading", progress.Value);
        });

        string? installer = null;
        string? error = null;
        try
        {
            installer = await UpdateService.DownloadAsync(update, reported, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // The user closed the dialog.
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (installer == null)
        {
            if (error != null)
            {
                status.Text = Localization.T("Update.InstallFailed", error);
                dialog.CloseButtonText = Localization.T("Common.Close");
                await showTask;
            }

            return;
        }

        dialog.Hide();
        await showTask;

        if (!await ConfirmCloseEditorsAsync())
        {
            return;
        }

        try
        {
            UpdateService.LaunchInstaller(installer);
            _closingConfirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start the update installer", ex);
            var failed = new ContentDialog
            {
                Title = Localization.T("Update.Title"),
                Content = Localization.T("Update.InstallFailed", ex.Message),
                CloseButtonText = Localization.T("Common.Close"),
                XamlRoot = RootGrid.XamlRoot
            };
            ThemeService.ApplyDialogTheme(failed);
            await failed.ShowAsync();
        }
    }

    /// <summary>Asks each dirty editor tab to save or discard. False cancels the close.</summary>
    private async Task<bool> ConfirmCloseEditorsAsync()
    {
        var editors = SessionTabs.TabItems
            .OfType<TabViewItem>()
            .Select(tab => tab.Content)
            .OfType<RemoteEditorView>()
            .ToList();

        foreach (var editor in editors)
        {
            if (!await editor.ConfirmCloseAsync())
            {
                return false;
            }
        }

        return true;
    }

    private void ApplySettingsToOpenViews()
    {
        foreach (var terminal in TerminalViews())
        {
            terminal.ApplySettings();
        }
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closingConfirmed)
        {
            return;
        }

        args.Cancel = true;
        if (_closePromptActive)
        {
            return;
        }

        _closePromptActive = true;
        try
        {
            if (!await ConfirmCloseEditorsAsync())
            {
                return;
            }

            _closingConfirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to confirm unsaved editor changes before closing", ex);
        }
        finally
        {
            _closePromptActive = false;
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Localization.LanguageChanged -= OnLanguageChanged;
        App.ProfileStore.Save();
        foreach (var item in SessionTabs.TabItems)
        {
            if (item is not TabViewItem tab)
            {
                continue;
            }

            switch (tab.Content)
            {
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
                case IAsyncDisposable asyncDisposable:
                    try
                    {
                        asyncDisposable.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Dispose on shutdown failed: {ex.Message}");
                    }

                    break;
            }
        }
    }
}



