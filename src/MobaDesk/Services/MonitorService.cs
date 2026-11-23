using System.Diagnostics;
using System.Globalization;
using MobaDesk.Models;
using Renci.SshNet;

namespace MobaDesk.Services;

/// <summary>
/// Collects Linux metrics through a single SSH command channel. Every poll returns a full snapshot.
/// </summary>
public sealed class MonitorService : IAsyncDisposable
{
    private const string Script = """
        export LC_ALL=C 2>/dev/null
        echo '@@STAT'
        awk '/^cpu /{print; exit}' /proc/stat 2>/dev/null
        sleep 0.3
        awk '/^cpu /{print; exit}' /proc/stat 2>/dev/null
        echo '@@MEM'
        cat /proc/meminfo 2>/dev/null
        echo '@@LOADAVG'
        cat /proc/loadavg 2>/dev/null
        echo '@@UPTIME'
        cat /proc/uptime 2>/dev/null
        echo '@@NET'
        awk 'NR>2{print}' /proc/net/dev 2>/dev/null
        echo '@@DISK'
        df -kP 2>/dev/null | grep -v -E '^(Filesystem|none|tmpfs|devtmpfs|overlay|shm)'
        echo '@@PROC'
        (ps -eo pid,comm,pcpu,pmem --sort=-pcpu 2>/dev/null || ps aux 2>/dev/null) | head -n 12
        echo '@@SYS'
        hostname 2>/dev/null
        uname -srm 2>/dev/null
        cat /etc/os-release 2>/dev/null | head -n 4
        echo '@@END'
        """;

    private static readonly HashSet<string> PseudoFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "tmpfs", "devtmpfs", "sysfs", "proc", "cgroup", "cgroup2", "overlay", "none", "shm", "ramfs",
        "squashfs", "iso9660", "nsfs", "fuse.lxcfs", "fusectl", "securityfs", "pstore", "debugfs",
        "tracefs", "configfs", "mqueue", "hugetlbfs", "autofs", "binfmt_misc", "rpc_pipefs",
        "efivarfs", "bpf", "devpts", "selinuxfs", "fuse.portal"
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SshClient? _client;
    private volatile bool _disposed;
    private long _previousTotal;
    private long _previousIdle;
    private long _previousRx = -1;
    private long _previousTx = -1;
    private DateTime _previousNetSample = DateTime.MinValue;

    public string Fingerprint { get; private set; } = "";
    public bool IsConnected => _client?.IsConnected == true;
    public string LastRawOutput { get; private set; } = "";

    public async Task ConnectAsync(ConnectionProfile profile, SshCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MonitorService));
        }

        var client = await Task.Run(() =>
        {
            var created = SshAuth.CreateClient(profile, credentials, fp => Fingerprint = fp);
            created.ConnectionInfo.Timeout = TimeSpan.FromSeconds(App.Settings.ConnectionTimeoutSeconds);
            try
            {
                created.Connect();
                return created;
            }
            catch
            {
                created.Dispose();
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // The monitor tab may have been closed while the handshake was running.
            if (_disposed)
            {
                client.Dispose();
                return;
            }

            var previous = _client;
            _client = client;
            previous?.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ServerMetrics> PollAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = _client ?? throw new InvalidOperationException(Localization.T("Error.MonitorNotConnected"));
            var stopwatch = Stopwatch.StartNew();
            var output = await Task.Run(() =>
            {
                // The shell script must use Unix line endings, otherwise "\r" becomes
                // part of the last token and breaks pipes/redirections on Linux.
                var script = Script.Replace("\r\n", "\n").Replace('\r', '\n');
                using var command = client.CreateCommand(script);
                command.CommandTimeout = TimeSpan.FromSeconds(20);
                return command.Execute();
            }, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            LastRawOutput = output;
            // The script intentionally sleeps 0.3s between the two CPU samples.
            var rtt = Math.Max(1, (int)stopwatch.ElapsedMilliseconds - 300);
            return Parse(output, rtt);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ServerMetrics Parse(string output, int rttMs)
    {
        var metrics = new ServerMetrics { Timestamp = DateTime.Now, RttMs = rttMs };
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? section = null;

        foreach (var rawLine in output.Replace("\r", "").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                section = line.Trim();
                sections[section] = new List<string>();
                continue;
            }

            if (section != null)
            {
                sections[section].Add(line);
            }
        }

        if (!sections.ContainsKey("@@END"))
        {
            throw new InvalidOperationException(Localization.T("Monitor.NotLinux"));
        }

        ParseCpu(sections, metrics);
        ParseMemory(sections, metrics);
        ParseLoadAndUptime(sections, metrics);
        ParseNetwork(sections, metrics);
        ParseDisks(sections, metrics);
        ParseProcesses(sections, metrics);
        ParseSystem(sections, metrics);
        return metrics;
    }

    private static List<string> Get(Dictionary<string, List<string>> sections, string key) =>
        sections.TryGetValue(key, out var lines) ? lines : new List<string>();

    private void ParseCpu(Dictionary<string, List<string>> sections, ServerMetrics metrics)
    {
        var lines = Get(sections, "@@STAT").Where(l => l.StartsWith("cpu ", StringComparison.Ordinal)).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        if (lines.Count >= 2)
        {
            var (total1, idle1) = ParseCpuLine(lines[0]);
            var (total2, idle2) = ParseCpuLine(lines[1]);
            var totalDelta = total2 - total1;
            var idleDelta = idle2 - idle1;
            if (totalDelta > 0)
            {
                metrics.CpuPercent = Math.Clamp(100.0 * (1 - idleDelta / (double)totalDelta), 0, 100);
            }
        }
        else
        {
            var (total, idle) = ParseCpuLine(lines[0]);
            if (_previousTotal > 0)
            {
                var totalDelta = total - _previousTotal;
                var idleDelta = idle - _previousIdle;
                if (totalDelta > 0)
                {
                    metrics.CpuPercent = Math.Clamp(100.0 * (1 - idleDelta / (double)totalDelta), 0, 100);
                }
            }

            _previousTotal = total;
            _previousIdle = idle;
        }
    }

    private static (long Total, long Idle) ParseCpuLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
        {
            return (0, 0);
        }

        var values = new List<long>();
        for (var i = 1; i < parts.Length && i < 9; i++)
        {
            if (!long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                break;
            }

            values.Add(value);
        }

        if (values.Count < 4)
        {
            return (0, 0);
        }

        var idle = values[3] + (values.Count > 4 ? values[4] : 0);
        var total = values.Sum();
        return (total, idle);
    }

    private static void ParseMemory(Dictionary<string, List<string>> sections, ServerMetrics metrics)
    {
        foreach (var line in Get(sections, "@@MEM"))
        {
            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }

            var key = line[..index].Trim();
            var valueText = line[(index + 1)..].Trim().Split(' ')[0];
            if (!long.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb))
            {
                continue;
            }

            switch (key)
            {
                case "MemTotal":
                    metrics.MemTotalBytes = kb * 1024;
                    break;
                case "MemAvailable":
                    metrics.MemAvailableBytes = kb * 1024;
                    break;
                case "MemFree":
                    metrics.MemUsedBytes = metrics.MemUsedBytes == 0 ? metrics.MemTotalBytes - kb * 1024 : metrics.MemUsedBytes;
                    break;
                case "SwapTotal":
                    metrics.SwapTotalBytes = kb * 1024;
                    break;
                case "SwapFree":
                    metrics.SwapUsedBytes = Math.Max(0, metrics.SwapTotalBytes - kb * 1024);
                    break;
            }
        }

        if (metrics.MemAvailableBytes > 0)
        {
            metrics.MemUsedBytes = Math.Max(0, metrics.MemTotalBytes - metrics.MemAvailableBytes);
        }

        if (metrics.MemTotalBytes > 0)
        {
            metrics.MemPercent = ClampPercent(metrics.MemUsedBytes * 100.0 / metrics.MemTotalBytes);
        }

        if (metrics.SwapTotalBytes > 0)
        {
            metrics.SwapPercent = ClampPercent(metrics.SwapUsedBytes * 100.0 / metrics.SwapTotalBytes);
        }
    }

    private static void ParseLoadAndUptime(Dictionary<string, List<string>> sections, ServerMetrics metrics)
    {
        var loadLine = Get(sections, "@@LOADAVG").FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (loadLine != null)
        {
            var parts = loadLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                metrics.Load1 = ParseDouble(parts[0]);
                metrics.Load5 = ParseDouble(parts[1]);
                metrics.Load15 = ParseDouble(parts[2]);
            }
        }

        var uptimeLine = Get(sections, "@@UPTIME").FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (uptimeLine != null)
        {
            var first = uptimeLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            metrics.UptimeSeconds = ParseDouble(first);
        }
    }

    private void ParseNetwork(Dictionary<string, List<string>> sections, ServerMetrics metrics)
    {
        long rx = 0;
        long tx = 0;
        foreach (var line in Get(sections, "@@NET"))
        {
            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }

            var name = line[..index].Trim();
            if (name == "lo")
            {
                continue;
            }

            var parts = line[(index + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9)
            {
                continue;
            }

            if (long.TryParse(parts[0], out var valueRx))
            {
                rx += valueRx;
            }

            if (long.TryParse(parts[8], out var valueTx))
            {
                tx += valueTx;
            }
        }

        metrics.NetRxTotalBytes = rx;
        metrics.NetTxTotalBytes = tx;
        var now = DateTime.UtcNow;
        if (_previousRx >= 0 && _previousNetSample != DateTime.MinValue)
        {
            var seconds = Math.Max(0.2, (now - _previousNetSample).TotalSeconds);
            metrics.NetRxBytesPerSecond = Math.Max(0, (rx - _previousRx) / seconds);
            metrics.NetTxBytesPerSecond = Math.Max(0, (tx - _previousTx) / seconds);
        }

        _previousRx = rx;
        _previousTx = tx;
        _previousNetSample = now;
    }

    private static void ParseDisks(Dictionary<string, List<string>> sections, ServerMetrics metrics)
    {
        foreach (var line in Get(sections, "@@DISK"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6)
            {
                continue;
            }

            var fileSystem = parts[0];
            if (PseudoFileSystems.Contains(fileSystem))
            {
                continue;
            }

            var mount = parts[^1];
            if (mount.StartsWith("/proc") || mount.StartsWith("/sys") || mount.StartsWith("/dev") ||
                mount.StartsWith("/snap") || mount.StartsWith("/run") || mount.StartsWith("/var/lib/docker"))
            {
                continue;
            }

            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalKb) ||
                !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var usedKb) ||
                !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var availableKb))
            {
                continue;
            }

            if (totalKb <= 0)
            {
                continue;
            }

            var percentText = parts[4].TrimEnd('%');
            var percent = ParseDouble(percentText);
            metrics.Disks.Add(new DiskUsage
            {
                FileSystem = fileSystem,
                MountPoint = mount,
                TotalBytes = totalKb * 1024,
                UsedBytes = usedKb * 1024,
                AvailableBytes = availableKb * 1024,
                UsedPercent = ClampPercent(percent)
            });
        }

        if (metrics.Disks.Count > 12)
        {
            metrics.Disks = metrics.Disks.Take(12).ToList();
        }
    }

    private static void ParseProcesses(Dictionary<string, List<string>> sections, ServerMetrics metrics)
    {
        var lines = Get(sections, "@@PROC").Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        // `ps -eo pid,comm,pcpu,pmem` starts with PID; procps `ps aux` starts with USER.
        // Checking only for "PID"/"%CPU" would classify the aux header as extended style,
        // because it also contains both words.
        var header = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var auxStyle = header.Length > 0 && header[0].Equals("USER", StringComparison.OrdinalIgnoreCase);
        var extendedStyle = !auxStyle &&
                            header.Length > 0 && header[0].Equals("PID", StringComparison.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(auxStyle || extendedStyle ? 1 : 0))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (extendedStyle)
            {
                if (parts.Length < 4 ||
                    !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ||
                    !double.TryParse(parts[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu) ||
                    !double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mem))
                {
                    continue;
                }

                var name = string.Join(' ', parts.Skip(1).Take(parts.Length - 3));
                metrics.Processes.Add(new ProcessInfo { Pid = pid, Name = name, CpuPercent = cpu, MemPercent = mem });
            }
            else
            {
                // ps aux: USER PID %CPU %MEM VSZ RSS TTY STAT START TIME COMMAND
                if (parts.Length < 11 ||
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ||
                    !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu) ||
                    !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var mem))
                {
                    continue;
                }

                var name = parts[10];
                metrics.Processes.Add(new ProcessInfo { Pid = pid, Name = name, CpuPercent = cpu, MemPercent = mem });
            }
        }

        metrics.Processes = metrics.Processes.Take(10).ToList();
    }

    private static void ParseSystem(Dictionary<string, List<string>> sections, ServerMetrics metrics)
    {
        var lines = Get(sections, "@@SYS");
        var nonEmpty = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonEmpty.Count > 0)
        {
            metrics.HostName = nonEmpty[0].Trim();
        }

        if (nonEmpty.Count > 1)
        {
            metrics.Kernel = nonEmpty[1].Trim();
        }

        foreach (var line in nonEmpty)
        {
            if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
            {
                metrics.OsName = line["PRETTY_NAME=".Length..].Trim().Trim('"');
                break;
            }

            if (line.StartsWith("NAME=", StringComparison.Ordinal) && string.IsNullOrEmpty(metrics.OsName))
            {
                metrics.OsName = line["NAME=".Length..].Trim().Trim('"');
            }
        }
    }

    private static double ParseDouble(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static double ClampPercent(double value) => Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 100);

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _client?.Dispose();
            _client = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
