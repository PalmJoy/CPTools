using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;
using CPSystemMonitor.Core.Monitoring;

namespace CPSystemMonitor.Platform.MacOS;

[SupportedOSPlatform("macos")]
public sealed class MacMonitorSource : IMonitorSource
{
    private const string Lib = "/usr/lib/libSystem.B.dylib";
    private const string Proc = "/usr/lib/libproc.dylib";
    private readonly MacNetworkAttribution _network = new();
    private readonly Dictionary<string, string[]> _physical = new();
    private bool _detailed;
    private readonly MachTimebase _timebase = ReadTimebase();
    public int InaccessibleProcessCount { get; private set; }
    private MacProcessAccess? _processAccess;
    private volatile bool _disposed;
    public string ProcessAccessStatus { get; private set; } = "limited";
    public async Task RequestProcessAccessAsync(string executable, string? assembly, CancellationToken cancellationToken)
    {
        if (_disposed) return;
        ProcessAccessStatus = "starting";
        try
        {
            var access = await MacProcessAccess.RequestAsync(executable, assembly, cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested) { access.Dispose(); return; }
            Interlocked.Exchange(ref _processAccess, access)?.Dispose();
            ProcessAccessStatus = "active";
        }
        catch (OperationCanceledException) { ProcessAccessStatus = "cancelled"; }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or System.ComponentModel.Win32Exception)
        { ProcessAccessStatus = "failed"; }
    }
    public void ReleaseProcessAccess()
    {
        Interlocked.Exchange(ref _processAccess, null)?.Dispose();
        ProcessAccessStatus = "limited";
    }

    private static MachTimebase ReadTimebase()
    {
        if (mach_timebase_info(out var timebase) != 0 || timebase.Denom == 0)
            throw new IOException("mach_timebase_info");
        return timebase;
    }

    // rusage CPU times use Mach absolute-time units, including on Apple silicon.
    // A wide intermediate preserves precision without overflowing before division.
    public static long CpuNanoseconds(ulong user, ulong system, uint numer, uint denom) =>
        checked((long)(((UInt128)user + system) * numer / denom));
    public CpuTicks[] ReadCpu()
    {
        uint host = mach_host_self();
        try
        {
            int result = host_processor_info(host, 2, out uint cpus, out nint data, out uint count);
            if (result != 0) throw new IOException("host_processor_info: " + result);
            try
            {
                var ticks = new CpuTicks[cpus];
                for (int i = 0; i < cpus; i++)
                {
                    uint user = unchecked((uint)Marshal.ReadInt32(data, i * 16));
                    uint system = unchecked((uint)Marshal.ReadInt32(data, i * 16 + 4));
                    uint idle = unchecked((uint)Marshal.ReadInt32(data, i * 16 + 8));
                    uint nice = unchecked((uint)Marshal.ReadInt32(data, i * 16 + 12));
                    ticks[i] = new(idle, (ulong)user + system + idle + nice);
                }
                return ticks;
            }
            finally { vm_deallocate(mach_task_self(), data, count * 4UL); }
        }
        finally { mach_port_deallocate(mach_task_self(), host); }
    }
    public MemoryReading ReadMemory()
    {
        ulong size = 8; long total = 0;
        if (sysctlbyname("hw.memsize", out total, ref size, 0, 0) != 0) throw new IOException("hw.memsize");
        byte[] data = new byte[256]; uint count = (uint)data.Length / 4;
        uint host = mach_host_self();
        try
        {
            if (host_statistics64(host, 4, data, ref count) != 0 || host_page_size(host, out uint page) != 0) throw new IOException("host_statistics64");
            // free_count already includes speculative_count. Inactive pages are reclaimable;
            // cached/compressed memory is not classified as permanently free.
            long available = ((long)BitConverter.ToUInt32(data, 0) + BitConverter.ToUInt32(data, 8)) * page;
            // vm_statistics64.compressor_page_count is at byte 128 (SDK mach/vm_statistics.h).
            long? compressed = count * 4 >= 132 ? (long)BitConverter.ToUInt32(data, 128) * page : null;
            ulong swapSize = (ulong)Marshal.SizeOf<SwapUsage>();
            long? swapUsed = sysctlbyname("vm.swapusage", out SwapUsage swap, ref swapSize, 0, 0) == 0
                && swapSize == (ulong)Marshal.SizeOf<SwapUsage>() && swap.Used <= long.MaxValue ? (long)swap.Used : null;
            return new(total, Math.Clamp(available, 0, total), compressed, swapUsed);
        }
        finally { mach_port_deallocate(mach_task_self(), host); }
    }
    public (long Read, long Write)? ReadDisk(string volume)
    {
        if (!_physical.TryGetValue(volume, out var disks))
        {
            var plist = Plist(Run("/usr/sbin/diskutil", ["info", "-plist", volume]));
            disks = plist?.Descendants("key").Where(k => k.Value == "APFSPhysicalStore").Select(k => k.ElementsAfterSelf().FirstOrDefault()?.Value ?? "").Where(s => s.Length > 0).ToArray() ?? [];
            if (disks.Length == 0 && Value(plist, "ParentWholeDisk") is { } disk) disks = [disk];
            disks = disks.Select(s => System.Text.RegularExpressions.Regex.Match(s, @"^disk\d+").Value).Where(s => s.Length > 0).Distinct().ToArray();
            if (disks.Length == 0) return null;
            _physical[volume] = disks;
        }
        var registry = Plist(Run("/usr/sbin/ioreg", ["-r", "-c", "IOBlockStorageDriver", "-a", "-l"]));
        if (registry is null) return null;
        long read = 0, write = 0; int found = 0;
        foreach (var driver in registry.Descendants("dict").Where(d => Value(d, "IOClass") == "IOBlockStorageDriver"))
        {
            var stats = Element(driver, "Statistics");
            bool matches = driver.Descendants("key").Where(k => k.Value == "BSD Name").Any(k => disks.Contains(k.ElementsAfterSelf().FirstOrDefault()?.Value));
            if (stats is null || !matches) continue;
            if (long.TryParse(Value(stats, "Bytes (Read)"), out long r) && long.TryParse(Value(stats, "Bytes (Write)"), out long w)) { read += r; write += w; found++; }
        }
        return found > 0 ? (read, write) : null;
    }
    private static XElement? Plist(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return XDocument.Parse(text).Root; } catch (System.Xml.XmlException) { return null; }
    }
    private static XElement? Element(XElement? dict, string key) => dict?.Elements("key").FirstOrDefault(k => k.Value == key)?.ElementsAfterSelf().FirstOrDefault();
    private static string? Value(XElement? dict, string key) => Element(dict, key)?.Value;
    private static string Run(string exe, string[] args)
    {
        using var p = new Process { StartInfo = new(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (string a in args) p.StartInfo.ArgumentList.Add(a);
        p.Start(); var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(2500)) { p.Kill(true); throw new IOException(exe + " timed out"); }
        string text = output.GetAwaiter().GetResult(); error.GetAwaiter().GetResult();
        return p.ExitCode == 0 ? text : "";
    }
    public IReadOnlyList<ProcessReading> ReadProcesses()
    {
        var access = _processAccess;
        if (access is not null)
        {
            try
            {
                var snapshot = access.Read();
                InaccessibleProcessCount = snapshot.InaccessibleCount;
                return snapshot.Processes;
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or ObjectDisposedException or System.Text.Json.JsonException)
            {
                if (Interlocked.CompareExchange(ref _processAccess, null, access) == access)
                { access.Dispose(); ProcessAccessStatus = "failed"; }
            }
        }
        return ReadLocalProcesses(_detailed);
    }
    internal IReadOnlyList<ProcessReading> ReadLocalProcesses(bool includeDisk)
    {
        int bytes = proc_listallpids(null, 0); int[] pids = new int[Math.Max(1024, bytes + 128)];
        int count = proc_listallpids(pids, pids.Length * 4);
        var readings = new List<ProcessReading>(); byte[] buf = new byte[160]; byte[] path = new byte[4096];
        InaccessibleProcessCount = 0;
        for (int i = 0; i < Math.Min(count, pids.Length); i++)
        {
            int pid = pids[i]; if (pid <= 0) continue;
            if (proc_pid_rusage(pid, 2, buf) != 0)
            {
                // Do not classify processes that exited during enumeration as denied.
                if (Marshal.GetLastPInvokeError() is 1 or 13) InaccessibleProcessCount++;
                continue;
            }
            int length = proc_pidpath(pid, path, (uint)path.Length);
            string? executable = length > 0 ? Encoding.UTF8.GetString(path, 0, Math.Max(0, Array.IndexOf(path, (byte)0))) : null;
            string name = executable is not null ? Path.GetFileNameWithoutExtension(executable) : "PID " + pid;
            long U(int offset) => unchecked((long)BitConverter.ToUInt64(buf, offset));
            readings.Add(new(pid, U(80), name, executable, 0,
                CpuNanoseconds(BitConverter.ToUInt64(buf, 16), BitConverter.ToUInt64(buf, 24), _timebase.Numer, _timebase.Denom),
                U(72), includeDisk ? U(144) : null, includeDisk ? U(152) : null));
        }
        return readings;
    }
    public IReadOnlyDictionary<int, (double Receive, double Send)> NetworkByProcess => _network.Rates;
    public bool DiskAttributionAvailable => _detailed;
    public bool NetworkAttributionAvailable => _network.Available;
    public string AttributionStatus => !_detailed ? "disabled" : _network.Status;
    public void ConfigureAttribution(bool enabled)
    {
        if (_detailed == enabled) return;
        _detailed = enabled; _network.SetEnabled(enabled);
    }
    public void Dispose() { _disposed = true; ReleaseProcessAccess(); _network.Dispose(); }
    [StructLayout(LayoutKind.Sequential)]
    private struct MachTimebase { public uint Numer, Denom; }
    [DllImport(Lib)] private static extern int mach_timebase_info(out MachTimebase info);
    [StructLayout(LayoutKind.Sequential)]
    private struct SwapUsage
    {
        public ulong Total, Available, Used;
        public uint PageSize;
        public int Encrypted;
    }
    [DllImport(Lib)] private static extern int sysctlbyname(string name, out SwapUsage value, ref ulong length, nint newValue, ulong newLength);
    [DllImport(Lib)] private static extern uint mach_host_self();
    [DllImport(Lib)] private static extern uint mach_task_self();
    [DllImport(Lib)] private static extern int mach_port_deallocate(uint task, uint port);
    [DllImport(Lib)] private static extern int host_processor_info(uint host, int flavor, out uint cpus, out nint info, out uint count);
    [DllImport(Lib)] private static extern int vm_deallocate(uint task, nint address, ulong size);
    [DllImport(Lib)] private static extern int host_statistics64(uint host, int flavor, [Out] byte[] info, ref uint count);
    [DllImport(Lib)] private static extern int host_page_size(uint host, out uint size);
    [DllImport(Lib)] private static extern int sysctlbyname(string name, out long value, ref ulong length, nint newValue, ulong newLength);
    [DllImport(Proc)] private static extern int proc_listallpids([Out] int[]? pids, int size);
    [DllImport(Proc, SetLastError = true)] private static extern int proc_pid_rusage(int pid, int flavor, [Out] byte[] buffer);
    [DllImport(Proc)] private static extern int proc_pidpath(int pid, [Out] byte[] path, uint size);
}
