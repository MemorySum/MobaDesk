using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MobaDesk.Ui;
using MobaDesk.Models;
using MobaDesk.Services;
using MobaDesk.Services.FileSystems;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;

namespace MobaDesk.Views;

public sealed partial class FileBrowserView : UserControl, IAsyncDisposable
{
    private readonly ConnectionProfile _profile;
    private readonly SshCredentials _credentials;
    private readonly bool _embedded;
    private readonly CancellationTokenSource _cts = new();

    private IRemoteFileSystem? _fileSystem;
    private string _currentPath = "/";
    private bool _started;
    private bool _busy;
    private bool _disposed;
    private int _itemCount;
    private RemoteEntry? _contextEntry;
    private RemoteEntry? _contextFolder;
    private string _statusKey = "File.Connecting";
    private object?[] _statusArgs = Array.Empty<object?>();
    private StatusKind _statusKind = StatusKind.Busy;

    public FileBrowserView(ConnectionProfile profile, SshCredentials credentials, bool embedded = false)
    {
        InitializeComponent();
        _profile = profile;
        _credentials = credentials;
        _embedded = embedded;

        Loc.Register(this);
        Localization.LanguageChanged += OnLanguageChanged;

        TitleText.Text = $"{profile.ProtocolLabel} · {profile.Name}";
        EndpointText.Text = profile.Subtitle;
        HeaderPanel.Visibility = embedded ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Loc.ApplyTree(this);
        ApplyStatus();
        ItemCountText.Text = Localization.T("File.Items", _itemCount);
    }

    public async Task StartAsync()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _fileSystem = _profile.Protocol == ProtocolKind.Ftp
            ? new FtpRemoteFileSystem(_profile)
            : new SftpRemoteFileSystem(_profile);

        SetStatus("File.Connecting", StatusKind.Busy);
        SetBusy(true);
        try
        {
            await _fileSystem.ConnectAsync(_credentials, _cts.Token);
            _currentPath = string.IsNullOrWhiteSpace(_fileSystem.InitialPath) ? "/" : _fileSystem.InitialPath;
            PathBox.Text = _currentPath;
            SetStatus("File.Connected", StatusKind.Success);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"File browser connect failed for {_profile.Host}", ex);
            var message = SshAuth.Describe(ex);
            ShowError(Localization.T("File.ConnectFailed"), message);
            SetStatus("File.ConnectFailed", StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshAsync()
    {
        if (_fileSystem == null || _disposed)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var entries = await _fileSystem.ListAsync(_currentPath, _cts.Token);
            EntriesList.ItemsSource = entries;
            PathBox.Text = _currentPath;
            _itemCount = entries.Count;
            ItemCountText.Text = Localization.T("File.Items", _itemCount);
            StatusInfoBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ShowError(Localization.T("File.Error.List"), ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task NavigateAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _currentPath = RemotePath.Normalize(path);
        await RefreshAsync();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string key, StatusKind kind, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        _statusKind = kind;
        ApplyStatus();
    }

    private void ApplyStatus()
    {
        var text = Localization.T(_statusKey, _statusArgs);
        TitleText.Text = $"{_profile.ProtocolLabel} · {_profile.Name}";
        EndpointText.Text = string.IsNullOrEmpty(text)
            ? _profile.Subtitle
            : $"{_profile.Subtitle} · {text}";
        StatusDot.Fill = new SolidColorBrush(_statusKind switch
        {
            StatusKind.Busy => Color.FromArgb(255, 56, 189, 248),
            StatusKind.Success => Color.FromArgb(255, 52, 211, 153),
            StatusKind.Error => Color.FromArgb(255, 248, 113, 113),
            _ => Color.FromArgb(255, 148, 163, 184)
        });
    }

    private void ShowError(string title, string message)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = InfoBarSeverity.Error;
        StatusInfoBar.IsOpen = true;
        Log.Warn($"{title}: {message}");
    }

    private async void OnUpClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        await NavigateAsync(RemotePath.Parent(_currentPath));
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnPathKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await NavigateAsync(PathBox.Text);
        }
    }

    private async void OnNewFolderClick(object sender, RoutedEventArgs e) => await CreateFolderAsync(_currentPath);

    private async Task CreateFolderAsync(string parentPath)
    {
        if (_fileSystem == null || _busy)
        {
            return;
        }

        var name = await PromptTextAsync(
            Localization.T("File.NewFolderTitle"),
            Localization.T("File.FolderNamePlaceholder"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        SetBusy(true);
        try
        {
            await _fileSystem.CreateDirectoryAsync(RemotePath.Combine(parentPath, name), _cts.Token);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError(Localization.T("File.Error.NewFolder"), ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnUploadClick(object sender, RoutedEventArgs e) => await UploadAsync(_currentPath);

    private async Task UploadAsync(string targetDirectory)
    {
        if (_fileSystem == null || _busy)
        {
            return;
        }

        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.Shell));
            picker.FileTypeFilter.Add("*");
            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0)
            {
                return;
            }

            SetBusy(true);
            ProgressPanel.Visibility = Visibility.Visible;
            var index = 0;
            foreach (var file in files)
            {
                index++;
                var remotePath = RemotePath.Combine(targetDirectory, file.Name);
                var progress = new Progress<double>(value =>
                {
                    TransferProgress.Value = value;
                    ProgressText.Text = Localization.T("File.Uploading", file.Name, index, files.Count, value);
                });
                await _fileSystem.UploadAsync(file.Path, remotePath, progress, _cts.Token);
            }

            ProgressText.Text = Localization.T("File.Uploaded", files.Count);
            TransferProgress.Value = 100;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError(Localization.T("File.Error.Upload"), ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e) => await DownloadSelectedAsync();

    private async Task DownloadSelectedAsync()
    {
        if (_fileSystem == null || _busy)
        {
            return;
        }

        var selected = EntriesList.SelectedItems.OfType<RemoteEntry>().ToList();
        if (selected.Count == 0)
        {
            ShowError(Localization.T("File.Download"), Localization.T("File.SelectForDownload"));
            return;
        }

        try
        {
            var picker = new FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.Shell));
            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
            {
                return;
            }

            SetBusy(true);
            ProgressPanel.Visibility = Visibility.Visible;
            foreach (var entry in selected)
            {
                if (entry.IsDirectory)
                {
                    await DownloadDirectoryAsync(entry, folder.Path);
                }
                else
                {
                    var localPath = SafeLocalPath(folder.Path, entry.Name);
                    var progress = new Progress<double>(value =>
                    {
                        TransferProgress.Value = value;
                        ProgressText.Text = Localization.T("File.Downloading", entry.Name, value);
                    });
                    await _fileSystem.DownloadAsync(entry, localPath, progress, _cts.Token);
                }
            }

            TransferProgress.Value = 100;
            ProgressText.Text = Localization.T("File.Downloaded", selected.Count, folder.Path);
        }
        catch (Exception ex)
        {
            ShowError(Localization.T("File.Error.Download"), ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DownloadDirectoryAsync(RemoteEntry directory, string localParent)
    {
        var target = SafeLocalPath(localParent, directory.Name);
        Directory.CreateDirectory(target);
        ProgressText.Text = Localization.T("File.DownloadFolder", directory.Name);

        var children = await _fileSystem!.ListAsync(directory.FullPath, _cts.Token);
        foreach (var child in children)
        {
            if (child.IsDirectory)
            {
                await DownloadDirectoryAsync(child, target);
            }
            else
            {
                var localPath = SafeLocalPath(target, child.Name);
                var progress = new Progress<double>(value =>
                {
                    TransferProgress.Value = value;
                    ProgressText.Text = Localization.T("File.Downloading", child.Name, value);
                });
                await _fileSystem.DownloadAsync(child, localPath, progress, _cts.Token);
            }
        }
    }

    /// <summary>
    /// Resolves a server-supplied name under <paramref name="parent"/>, rejecting names that
    /// contain path separators or would escape the chosen download folder.
    /// </summary>
    private static string SafeLocalPath(string parent, string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name is "." or ".." ||
            name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException(Localization.T("File.Error.UnsafeName", name));
        }

        var root = Path.GetFullPath(parent);
        var full = Path.GetFullPath(Path.Combine(root, name));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Localization.T("File.Error.UnsafeName", name));
        }

        return full;
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    private async Task DeleteSelectedAsync()
    {
        if (_fileSystem == null || _busy)
        {
            return;
        }

        var selected = EntriesList.SelectedItems.OfType<RemoteEntry>().ToList();
        if (selected.Count == 0)
        {
            ShowError(Localization.T("File.Delete"), Localization.T("File.SelectForDelete"));
            return;
        }

        var dialog = new ContentDialog
        {
            Title = Localization.T("File.ConfirmDeleteTitle"),
            Content = Localization.T("File.ConfirmDeleteMessage", selected.Count),
            PrimaryButtonText = Localization.T("Common.Delete"),
            CloseButtonText = Localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        ThemeService.ApplyDialogTheme(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        SetBusy(true);
        try
        {
            foreach (var entry in selected)
            {
                await _fileSystem.DeleteAsync(entry, _cts.Token);
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError(Localization.T("File.Error.Delete"), ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e) => await RenameSelectedAsync();

    private async Task RenameSelectedAsync()
    {
        if (_fileSystem == null || _busy)
        {
            return;
        }

        var selected = EntriesList.SelectedItems.OfType<RemoteEntry>().ToList();
        if (selected.Count != 1)
        {
            ShowError(Localization.T("Common.Rename"), Localization.T("File.SelectOneForRename"));
            return;
        }

        var entry = selected[0];
        var name = await PromptTextAsync(
            Localization.T("File.RenameTitle"),
            Localization.T("File.NewNamePlaceholder"),
            entry.Name);
        if (string.IsNullOrWhiteSpace(name) || name == entry.Name)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await _fileSystem.RenameAsync(entry, RemotePath.Combine(RemotePath.Parent(entry.FullPath), name),
                _cts.Token);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError(Localization.T("File.Error.Rename"), ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnEntriesDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_busy || _fileSystem == null || _disposed)
        {
            return;
        }

        var entry = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject)?.Content as RemoteEntry;
        if (entry == null)
        {
            return;
        }

        await OpenEntryAsync(entry);
    }

    private async void OnEntriesKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || _busy || _fileSystem == null || _disposed)
        {
            return;
        }

        if (EntriesList.SelectedItems.OfType<RemoteEntry>().FirstOrDefault() is { } entry)
        {
            e.Handled = true;
            await OpenEntryAsync(entry);
        }
    }

    private async Task OpenEntryAsync(RemoteEntry entry)
    {
        if (entry.IsDirectory)
        {
            await NavigateAsync(entry.FullPath);
        }
        else
        {
            App.Shell?.OpenRemoteFileEditor(_profile, _credentials, entry);
        }
    }

    private void OnEntriesRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        _contextEntry = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject)?.Content as RemoteEntry;
        if (_contextEntry != null && !EntriesList.SelectedItems.Contains(_contextEntry))
        {
            EntriesList.SelectedItem = _contextEntry;
        }
    }

    private void OnEntriesContextOpening(object sender, object e)
    {
        var selected = EntriesList.SelectedItems.OfType<RemoteEntry>().ToList();
        var target = _contextEntry != null && selected.Contains(_contextEntry)
            ? _contextEntry
            : selected.Count == 1 ? selected[0] : null;

        _contextFolder = target is { IsDirectory: true } ? target : null;
        var folder = _contextFolder != null;
        var hasSelection = selected.Count > 0;

        OpenEntryItem.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;
        EditEntryItem.Visibility = selected.Count == 1 && selected[0].IsDirectory == false
            ? Visibility.Visible
            : Visibility.Collapsed;
        DownloadEntryItem.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        UploadHereItem.Visibility = folder || !hasSelection ? Visibility.Visible : Visibility.Collapsed;
        NewFolderHereItem.Visibility = folder || !hasSelection ? Visibility.Visible : Visibility.Collapsed;
        FolderActionsSeparator.Visibility = folder || !hasSelection ? Visibility.Visible : Visibility.Collapsed;
        RenameEntryItem.Visibility = selected.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        CopyPathItem.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        RefreshEntryItem.Visibility = Visibility.Visible;
        DeleteSeparator.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        DeleteEntryItem.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnOpenEntryContextClick(object sender, RoutedEventArgs e)
    {
        if (_contextFolder is { } folder)
        {
            await NavigateAsync(folder.FullPath);
        }
    }

    private void OnEditContextClick(object sender, RoutedEventArgs e)
    {
        var entry = EntriesList.SelectedItems.OfType<RemoteEntry>().FirstOrDefault(item => !item.IsDirectory);
        if (entry != null)
        {
            App.Shell?.OpenRemoteFileEditor(_profile, _credentials, entry);
        }
    }

    private async void OnDownloadContextClick(object sender, RoutedEventArgs e) => await DownloadSelectedAsync();

    private async void OnUploadHereContextClick(object sender, RoutedEventArgs e) => await UploadAsync(TargetFolderPath());

    private async void OnNewFolderHereContextClick(object sender, RoutedEventArgs e) =>
        await CreateFolderAsync(TargetFolderPath());

    private void OnCopyPathContextClick(object sender, RoutedEventArgs e)
    {
        var selected = EntriesList.SelectedItems.OfType<RemoteEntry>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(string.Join(Environment.NewLine, selected.Select(entry => entry.FullPath)));
            Clipboard.SetContent(package);
            SetStatus("File.PathsCopied", StatusKind.Success, selected.Count);
        }
        catch (Exception ex)
        {
            ShowError(Localization.T("File.CopyPath"), ex.Message);
        }
    }

    private async void OnRenameContextClick(object sender, RoutedEventArgs e) => await RenameSelectedAsync();

    private async void OnRefreshContextClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnDeleteContextClick(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    private string TargetFolderPath() => _contextFolder?.FullPath ?? _currentPath;

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null && element is not T)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        return element as T;
    }

    private async void OnReconnectClick(object sender, RoutedEventArgs e)
    {
        if (_fileSystem == null || _busy || _disposed)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await _fileSystem.DisposeAsync();
            _fileSystem = _profile.Protocol == ProtocolKind.Ftp
                ? new FtpRemoteFileSystem(_profile)
                : new SftpRemoteFileSystem(_profile);
            await _fileSystem.ConnectAsync(_credentials, _cts.Token);
            _currentPath = string.IsNullOrWhiteSpace(_fileSystem.InitialPath) ? "/" : _fileSystem.InitialPath;
            SetStatus("File.Reconnected", StatusKind.Success);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError(Localization.T("File.Error.Reconnect"), ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<string?> PromptTextAsync(string title, string placeholder, string initial = "")
    {
        var box = new TextBox
        {
            Text = initial,
            PlaceholderText = placeholder,
            SelectionStart = initial.Length
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = Localization.T("Common.Confirm"),
            CloseButtonText = Localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        ThemeService.ApplyDialogTheme(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return box.Text.Trim();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
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
                Log.Warn($"File browser dispose failed: {ex.Message}");
            }

            _fileSystem = null;
        }

        _cts.Dispose();
    }

    private enum StatusKind
    {
        Idle,
        Busy,
        Success,
        Error
    }
}
