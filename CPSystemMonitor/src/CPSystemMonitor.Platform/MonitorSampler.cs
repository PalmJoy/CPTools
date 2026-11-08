using System.Diagnostics;
using System.Net.NetworkInformation;
using CPSystemMonitor.Core.Monitoring;

namespace CPSystemMonitor.Platform;

/// <summary>Single-worker sampling. Counter baselines are never shared with UI or overlapping polls.</summary>
public sealed class MonitorSampler(IMonitorSource source) : IDisposable
{
    private CpuTicks[]? _cpu;
    private long _lastTick;
    private string? _diskName;
    private (long Read, long Write)? _disk;
    private Dictionary<int, ProcessReading> _processes = new();
    private Dictionary<string, (long In, long Out)> _network = new();
    public string AttributionStatus => source.AttributionStatus;
    public void ConfigureAttribution(bool enabled) => source.ConfigureAttribution(enabled);
    public MonitorSample Sample(string selectedDrive)
    {
        long tick = Stopwatch.GetTimestamp();
        double elapsed = _lastTick == 0 ? 0 : Stopwatch.GetElapsedTime(_lastTick, tick).TotalSeconds;
        _lastTick = tick;
        string? status = null;
        CpuTicks[] ticks = [];
        try { ticks = source.ReadCpu(); } catch (Exception ex) when (Recoverable(ex)) { status = "CPU: " + ex.Message; }
        double[] cores = ticks.Length == _cpu?.Length ? ticks.Select((c, i) => SamplingMath.Cpu(_cpu[i], c)).ToArray() : [];
        double? total = null;
        if (cores.Length > 0 && _cpu is not null)
            total = SamplingMath.Cpu(new(_cpu.Aggregate(0UL, (a, c) => a + c.Idle), _cpu.Aggregate(0UL, (a, c) => a + c.Total)),
                new(ticks.Aggregate(0UL, (a, c) => a + c.Idle), ticks.Aggregate(0UL, (a, c) => a + c.Total)));
        _cpu = ticks;
        MemoryReading mem = default;
        try { mem = source.ReadMemory(); } catch (Exception ex) when (Recoverable(ex)) { status = "Memory: " + ex.Message; }
        string drive = string.IsNullOrEmpty(selectedDrive) ? SystemDrive : selectedDrive;
        long diskTotal = 0, diskFree = 0;
        double? read = null, write = null;
        try
        {
            var info = new DriveInfo(drive);
            diskTotal = info.TotalSize; diskFree = info.AvailableFreeSpace;
            var disk = source.ReadDisk(drive);
            if (disk.HasValue && _disk.HasValue && _diskName == drive && elapsed > 0)
            {
                read = SamplingMath.Rate(_disk.Value.Read, disk.Value.Read, elapsed);
                write = SamplingMath.Rate(_disk.Value.Write, disk.Value.Write, elapsed);
            }
            _disk = disk; _diskName = drive;
        }
        catch (Exception ex) when (Recoverable(ex)) { _disk = null; status = "Storage: " + ex.Message; }
        var network = new Dictionary<string, (long In, long Out)>();
        double? receive = null, send = null;
        try
        {
            double rx = 0, tx = 0; bool measured = false;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                try
                {
                    var counters = nic.GetIPStatistics();
                    network[nic.Id] = (counters.BytesReceived, counters.BytesSent);
                    if (elapsed > 0 && _network.TryGetValue(nic.Id, out var prev))
                    {
                        rx += SamplingMath.Rate(prev.In, counters.BytesReceived, elapsed);
                        tx += SamplingMath.Rate(prev.Out, counters.BytesSent, elapsed); measured = true;
                    }
                }
                catch (NetworkInformationException) { }
            }
            if (elapsed > 0 && (measured || network.Count == 0)) { receive = rx; send = tx; }
        }
        catch (NetworkInformationException ex) { status = "Network: " + ex.Message; }
        _network = network;
        var current = source.ReadProcesses();
        var net = source.NetworkByProcess;
        var rates = new List<ProcessRates>();
        foreach (var p in current)
        {
            if (!_processes.TryGetValue(p.Pid, out var prev) || prev.Started != p.Started || elapsed <= 0) continue;
            var np = net.GetValueOrDefault(p.Pid);
            rates.Add(new(p.Pid, p.Name, p.Executable, p.Parent,
                Math.Clamp(SamplingMath.Rate(prev.CpuNanoseconds, p.CpuNanoseconds, elapsed) / 1e9 / Math.Max(1, ticks.Length) * 100, 0, 100),
                p.MemoryBytes,
                p.DiskRead is { } pr && prev.DiskRead is { } qr ? SamplingMath.Rate(qr, pr, elapsed) : 0,
                p.DiskWrite is { } pw && prev.DiskWrite is { } qw ? SamplingMath.Rate(qw, pw, elapsed) : 0,
                np.Receive, np.Send));
        }
        _processes = current.ToDictionary(p => p.Pid);
        var apps = AppAggregation.Aggregate(rates, OperatingSystem.IsMacOS());
        AppLoad? Top(Func<AppLoad, double> value) => apps.Where(a => value(a) > 0).OrderByDescending(value).ThenBy(a => a.Name, StringComparer.Ordinal).FirstOrDefault();
        return new(DateTimeOffset.Now, total, cores, mem.Total, mem.Available, drive, diskTotal, diskFree,
            read, write, receive, send, Top(a => a.CpuPercent), Top(a => a.MemoryBytes),
            source.DiskAttributionAvailable ? Top(a => a.ReadBytesPerSecond + a.WriteBytesPerSecond) : null,
            source.NetworkAttributionAvailable ? Top(a => a.ReceiveBytesPerSecond + a.SendBytesPerSecond) : null, status, mem.Compressed, mem.SwapUsed,
            source.InaccessibleProcessCount);
    }
    public static string SystemDrive => OperatingSystem.IsWindows() ? Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\" : "/";
    private static bool Recoverable(Exception ex) => ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException;
    public void Dispose() => source.Dispose();
}
