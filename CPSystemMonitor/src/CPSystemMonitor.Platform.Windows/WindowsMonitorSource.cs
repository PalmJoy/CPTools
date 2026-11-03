using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using CPSystemMonitor.Core.Monitoring;

namespace CPSystemMonitor.Platform.Windows;
[SupportedOSPlatform("windows")]
public sealed class WindowsMonitorSource : IMonitorSource
{
    private readonly WindowsAttribution _attribution = new();
    private readonly WindowsDiskCounters _disk = new();
    public CpuTicks[] ReadCpu()
    {
        var data = new byte[48 * 4096];
        int result = NtQuerySystemInformation(8, data, data.Length, out int returned);
        if (result < 0) throw new IOException("NtQuerySystemInformation: " + result);
        return Enumerable.Range(0, returned / 48).Select(i =>
        {
            ulong idle = BitConverter.ToUInt64(data, i * 48), kernel = BitConverter.ToUInt64(data, i * 48 + 8), user = BitConverter.ToUInt64(data, i * 48 + 16);
            return new CpuTicks(idle, kernel + user);
        }).ToArray();
    }
    public MemoryReading ReadMemory()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new System.ComponentModel.Win32Exception();
        return new((long)memory.TotalPhysical, (long)memory.AvailablePhysical);
    }
    public (long Read, long Write)? ReadDisk(string volume)
    {
        if (_disk.Read(volume) is { } pdh) return pdh;
        string device = @"\\.\" + volume.TrimEnd('\\');
        using var handle = CreateFile(device, 0, 3, 0, 3, 0, 0);
        if (handle.IsInvalid) return null;
        byte[] performance = new byte[128];
        if (!DeviceIoControl(handle, 0x70020, 0, 0, performance, performance.Length, out _, 0)) return null;
        return (BitConverter.ToInt64(performance, 0), BitConverter.ToInt64(performance, 8));
    }
    public IReadOnlyList<ProcessReading> ReadProcesses()
    {
        var parents = Parents(); var counters = _attribution.DiskCounters;
        var result = new List<ProcessReading>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    int pid = p.Id; if (pid <= 0) continue;
                    string? path = null;
                    using var handle = OpenProcess(0x1000, false, pid);
                    if (!handle.IsInvalid)
                    {
                        var name = new System.Text.StringBuilder(32768); int len = name.Capacity;
                        if (QueryFullProcessImageName(handle, 0, name, ref len)) path = name.ToString();
                    }
                    var disk = counters.GetValueOrDefault(pid);
                    result.Add(new(pid, p.StartTime.ToUniversalTime().Ticks, p.ProcessName, path, parents.GetValueOrDefault(pid),
                        p.TotalProcessorTime.Ticks * 100, p.WorkingSet64,
                        _attribution.Available ? disk.Read : null, _attribution.Available ? disk.Write : null));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return result;
    }
    private static Dictionary<int, int> Parents()
    {
        var result = new Dictionary<int, int>();
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) return result;
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), ExeFile = "" };
        if (Process32First(snapshot, ref entry)) do { result[(int)entry.ProcessId] = (int)entry.ParentProcessId; } while (Process32Next(snapshot, ref entry));
        return result;
    }
    public IReadOnlyDictionary<int, (double Receive, double Send)> NetworkByProcess => _attribution.NetworkRates;
    public bool DiskAttributionAvailable => _attribution.Available;
    public bool NetworkAttributionAvailable => _attribution.Available;
    public string AttributionStatus => _attribution.Status;
    public void ConfigureAttribution(bool enabled) => _attribution.SetEnabled(enabled);
    public void Dispose() { _attribution.Dispose(); _disk.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId; public nuint DefaultHeapId; public uint ModuleId, Threads, ParentProcessId; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int kind, [Out] byte[] data, int size, out int length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, nint input, int inputSize, [Out] byte[] output, int outputSize, out int returned, nint overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, System.Text.StringBuilder name, ref int length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
}
