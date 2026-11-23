using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MobaDesk.Ui;
using MobaDesk.Models;
using MobaDesk.Services;
using Windows.UI;

namespace MobaDesk.Views;

public sealed partial class MonitorView : UserControl, IAsyncDisposable
{
    private readonly ConnectionProfile _profile;
    private readonly SshCredentials _credentials;
    private readonly MonitorService _service = new();
    private readonly DispatcherQueueTimer _timer;

    private bool _started;
    private bool _polling;
    private bool _paused;
    private bool _disposed;
    private ServerMetrics? _lastMetrics;
    private string _statusKey = "Monitor.Connecting";
    private StatusKind _statusKind = StatusKind.Busy;

    public MonitorView(ConnectionProfile profile, SshCredentials credentials)
    {
        InitializeComponent();
        _profile = profile;
        _credentials = credentials;

        HostLabel.Text = profile.Subtitle;
        Loc.Register(this);
        Localization.LanguageChanged += OnLanguageChanged;

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(App.Settings.MonitorIntervalSeconds);
        _timer.Tick += async (_, _) => await PollAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Loc.ApplyTree(this);
        ApplyStatus();
        if (_lastMetrics != null)
        {
            // Refresh text only; pushing here would duplicate a chart sample.
            UpdateTexts(_lastMetrics);
        }
        else
        {
            UpdatedText.Text = Localization.T("Monitor.Waiting");
        }
    }

    /// <summary>Applies a changed monitor refresh interval without disturbing the charts.</summary>
    public void ApplySettings()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Interval = TimeSpan.FromSeconds(App.Settings.MonitorIntervalSeconds);
        if (_lastMetrics != null)
        {
            UpdateTexts(_lastMetrics);
        }
    }

    public async Task StartAsync()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        SetStatus("Monitor.Connecting", StatusKind.Busy);
        try
        {
            await _service.ConnectAsync(_profile, _credentials);
            SetStatus("Monitor.Active", StatusKind.Success);
            await PollAsync();
            if (!_disposed)
            {
                _timer.Start();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Monitor connect failed for {_profile.Host}", ex);
            SetStatus("Monitor.ConnectFailed", StatusKind.Error);
            ErrorBar.Title = Localization.T("Monitor.ConnectFailed");
            ErrorBar.Message = SshAuth.Describe(ex);
            ErrorBar.Severity = InfoBarSeverity.Error;
            ErrorBar.IsOpen = true;
        }
    }

    private async Task PollAsync()
    {
        if (_polling || _disposed)
        {
            return;
        }

        _polling = true;
        try
        {
            var metrics = await _service.PollAsync();
            if (_disposed)
            {
                return;
            }

            Apply(metrics);
            ErrorBar.IsOpen = false;
            SetStatus("Monitor.Active", StatusKind.Success);
        }
        catch (Exception ex)
        {
            Log.Warn($"Monitor poll failed: {ex.Message}");
            ErrorBar.Title = Localization.T("Monitor.RefreshFailed");
            ErrorBar.Message = ex.Message;
            ErrorBar.Severity = InfoBarSeverity.Warning;
            ErrorBar.IsOpen = true;
            SetStatus("Monitor.Interrupted", StatusKind.Error);
        }
        finally
        {
            _polling = false;
        }
    }

    private void Apply(ServerMetrics metrics)
    {
        _lastMetrics = metrics;
        CpuChart.Push(metrics.CpuPercent);
        NetInChart.Push(metrics.NetRxBytesPerSecond);
        NetOutChart.Push(metrics.NetTxBytesPerSecond);
        UpdateTexts(metrics);
    }

    private void UpdateTexts(ServerMetrics metrics)
    {
        CpuValueText.Text = metrics.CpuText;
        CpuLoadText.Text = Localization.T("Monitor.Load", metrics.LoadText);

        MemValueText.Text = metrics.MemPercentText;
        MemBar.Value = metrics.MemPercent;
        MemDetailText.Text = metrics.MemText;
        SwapText.Text = "Swap  " + metrics.SwapText;

        NetInText.Text = metrics.NetInText;
        NetOutText.Text = metrics.NetOutText;
        NetTotalText.Text = Localization.T("Monitor.Cumulative",
            FormatUtil.Size(metrics.NetRxTotalBytes), FormatUtil.Size(metrics.NetTxTotalBytes));

        SystemText.Text = string.IsNullOrWhiteSpace(metrics.HostName)
            ? "—"
            : $"{metrics.HostName}    {metrics.OsName}\n{metrics.Kernel}";
        UptimeText.Text = Localization.T("Monitor.Uptime", metrics.UptimeText);
        RttText.Text = Localization.T("Monitor.Rtt", metrics.RttText);

        DiskList.ItemsSource = metrics.Disks;
        ProcessList.ItemsSource = metrics.Processes;
        UpdatedText.Text = Localization.T("Monitor.Updated", metrics.Timestamp,
            App.Settings.MonitorIntervalSeconds);
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

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        if (_paused)
        {
            _timer.Stop();
            PauseIcon.Glyph = "\uE768";
            ToolTipService.SetToolTip(PauseButton, Localization.T("Monitor.ResumeTooltip"));
            SetStatus("Monitor.Paused", StatusKind.Idle);
        }
        else
        {
            _timer.Start();
            PauseIcon.Glyph = "\uE769";
            ToolTipService.SetToolTip(PauseButton, Localization.T("Monitor.PauseTooltip"));
            SetStatus("Monitor.Active", StatusKind.Success);
            _ = PollAsync();
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await PollAsync();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        _timer.Stop();
        try
        {
            await _service.DisposeAsync();
        }
        catch (Exception ex)
        {
            Log.Warn($"Monitor dispose failed: {ex.Message}");
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
