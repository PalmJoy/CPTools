namespace CPSystemMonitor.Core.Monitoring;

public sealed record AppLoad(string Name, double CpuPercent, long MemoryBytes, double ReadBytesPerSecond,
    double WriteBytesPerSecond, double ReceiveBytesPerSecond, double SendBytesPerSecond);
public sealed record MonitorSample(DateTimeOffset Time, double? CpuPercent, double[] Cores,
    long TotalMemory, long AvailableMemory, string Drive, long DiskTotal, long DiskFree,
    double? ReadBytesPerSecond, double? WriteBytesPerSecond, double? ReceiveBytesPerSecond, double? SendBytesPerSecond,
    AppLoad? TopCpu, AppLoad? TopMemory, AppLoad? TopStorage, AppLoad? TopNetwork, string? Status = null)
{
    public double? AvailablePercent => TotalMemory > 0 ? Math.Clamp(100d * AvailableMemory / TotalMemory, 0, 100) : null;
    public double? DiskFreePercent => DiskTotal > 0 ? Math.Clamp(100d * DiskFree / DiskTotal, 0, 100) : null;
}
public readonly record struct CpuTicks(ulong Idle, ulong Total);
public static class SamplingMath
{
    public static double Cpu(CpuTicks previous, CpuTicks current)
    {
        if (current.Total <= previous.Total || current.Idle < previous.Idle) return 0;
        return Math.Clamp(100d * (1 - (double)(current.Idle - previous.Idle) / (current.Total - previous.Total)), 0, 100);
    }
    public static double Rate(long previous, long current, double seconds) =>
        seconds > 0 && current >= previous ? (current - previous) / seconds : 0;
}
public sealed class SampleHistory
{
    private readonly Queue<MonitorSample> _samples = new();
    public IReadOnlyList<MonitorSample> Samples => _samples.ToArray();
    public void Add(MonitorSample sample, int seconds)
    {
        _samples.Enqueue(sample);
        Trim(sample.Time, seconds);
        while (_samples.Count > 1202) _samples.Dequeue();
    }
    public void Trim(DateTimeOffset now, int seconds)
    {
        while (_samples.TryPeek(out var first) && first.Time < now.AddSeconds(-seconds)) _samples.Dequeue();
    }
}

public sealed record ProcessReading(int Pid, long Started, string Name, string? Executable, int Parent,
    long CpuNanoseconds, long MemoryBytes, long? DiskRead, long? DiskWrite);
public sealed record ProcessRates(int Pid, string Name, string? Executable, int Parent, double Cpu, long Memory,
    double Read, double Write, double Receive = 0, double Send = 0);
public static class AppAggregation
{
    // Group helper executables into their outer application bundle on macOS, or the same
    // application directory/parent tree on Windows. Never aggregate unrelated system hosts.
    public static string Key(ProcessRates p, bool mac)
    {
        if (p.Executable is not { Length: > 0 } path) return "pid:" + p.Pid;
        if (mac)
        {
            int app = path.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
            return app >= 0 ? path[..(app + 4)] : path;
        }
        return path.ToUpperInvariant();
    }
    public static IReadOnlyList<AppLoad> Aggregate(IReadOnlyList<ProcessRates> processes, bool mac)
    {
        var byPid = processes.ToDictionary(p => p.Pid);
        string Resolve(ProcessRates p)
        {
            string key = Key(p, mac);
            if (mac) return key;
            var seen = new HashSet<int> { p.Pid };
            while (byPid.TryGetValue(p.Parent, out var parent) && seen.Add(parent.Pid))
            {
                if (p.Executable is not { } childPath || parent.Executable is not { } parentPath) break;
                string? dir = Path.GetDirectoryName(parentPath);
                // Chromium/Electron children share an install directory, sometimes nested.
                if (dir is null || dir.Equals(Environment.GetFolderPath(Environment.SpecialFolder.System), StringComparison.OrdinalIgnoreCase)
                    || !childPath.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) break;
                p = parent; key = Key(parent, false);
            }
            return key;
        }
        return processes.GroupBy(Resolve, mac ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var root = g.OrderBy(p => p.Executable?.Length ?? int.MaxValue).First();
            string name = mac && g.Key.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(g.Key) : root.Name;
            return new AppLoad(name, Math.Min(100, g.Sum(p => p.Cpu)), g.Sum(p => p.Memory),
                g.Sum(p => p.Read), g.Sum(p => p.Write), g.Sum(p => p.Receive), g.Sum(p => p.Send));
        }).ToArray();
    }
}
