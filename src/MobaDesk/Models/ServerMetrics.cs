using MobaDesk.Services;

namespace MobaDesk.Models;

public sealed class ServerMetrics
{
    public string HostName { get; set; } = "";
    public string OsName { get; set; } = "";
    public string Kernel { get; set; } = "";
    public double UptimeSeconds { get; set; }
    public double Load1 { get; set; }
    public double Load5 { get; set; }
    public double Load15 { get; set; }
    public double CpuPercent { get; set; }
    public long MemTotalBytes { get; set; }
    public long MemAvailableBytes { get; set; }
    public long MemUsedBytes { get; set; }
    public double MemPercent { get; set; }
    public long SwapTotalBytes { get; set; }
    public long SwapUsedBytes { get; set; }
    public double SwapPercent { get; set; }
    public double NetRxBytesPerSecond { get; set; }
    public double NetTxBytesPerSecond { get; set; }
    public long NetRxTotalBytes { get; set; }
    public long NetTxTotalBytes { get; set; }
    public int RttMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public List<DiskUsage> Disks { get; set; } = new();
    public List<ProcessInfo> Processes { get; set; } = new();

    public string UptimeText => FormatUtil.Uptime(UptimeSeconds);
    public string MemText => $"{FormatUtil.Size(MemUsedBytes)} / {FormatUtil.Size(MemTotalBytes)}";
    public string SwapText => SwapTotalBytes <= 0
        ? Localization.T("Monitor.SwapDisabled")
        : $"{FormatUtil.Size(SwapUsedBytes)} / {FormatUtil.Size(SwapTotalBytes)}";
    public string LoadText => $"{Load1:0.00}  {Load5:0.00}  {Load15:0.00}";
    public string NetInText => FormatUtil.Rate(NetRxBytesPerSecond);
    public string NetOutText => FormatUtil.Rate(NetTxBytesPerSecond);
    public string RttText => $"{RttMs} ms";
    public string CpuText => $"{CpuPercent:0.0}%";
    public string MemPercentText => $"{MemPercent:0.0}%";
}

public sealed class DiskUsage
{
    public string FileSystem { get; set; } = "";
    public string MountPoint { get; set; } = "";
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long AvailableBytes { get; set; }
    public double UsedPercent { get; set; }

    public string DetailText => $"{FormatUtil.Size(UsedBytes)} / {FormatUtil.Size(TotalBytes)}";
    public string PercentText => $"{UsedPercent:0}%";
}

public sealed class ProcessInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public double CpuPercent { get; set; }
    public double MemPercent { get; set; }
    public string CpuText => $"{CpuPercent:0.0}%";
    public string MemText => $"{MemPercent:0.0}%";
}
