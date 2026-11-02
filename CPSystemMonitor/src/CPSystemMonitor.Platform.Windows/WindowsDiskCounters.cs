using System.Runtime.InteropServices;
namespace CPSystemMonitor.Platform.Windows;

/// <summary>English PDH paths are independent of the user's Windows display language.</summary>
internal sealed class WindowsDiskCounters : IDisposable
{
    private nint _query, _read, _write;
    private string? _volume;
    public (long Read, long Write)? Read(string volume)
    {
        if (_volume != volume)
        {
            Dispose(); _volume = volume;
            if (PdhOpenQuery(null, 0, out _query) != 0) { _query = 0; return null; }
            string instance = volume.TrimEnd('\\');
            if (PdhAddEnglishCounter(_query, $@"\LogicalDisk({instance})\Disk Read Bytes/sec", 0, out _read) != 0
                || PdhAddEnglishCounter(_query, $@"\LogicalDisk({instance})\Disk Write Bytes/sec", 0, out _write) != 0) { Dispose(); return null; }
        }
        if (_query == 0 || PdhCollectQueryData(_query) != 0) return null;
        if (PdhGetRawCounterValue(_read, out _, out var read) != 0 || PdhGetRawCounterValue(_write, out _, out var write) != 0
            || read.Status > 1 || write.Status > 1) return null;
        return (read.First, write.First);
    }
    public void Dispose() { if (_query != 0) PdhCloseQuery(_query); _query = _read = _write = 0; _volume = null; }
    [StructLayout(LayoutKind.Sequential)] private struct RawCounter { public uint Status; public System.Runtime.InteropServices.ComTypes.FILETIME Time; public long First, Second; public uint MultipleCount; }
    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, nuint user, out nint query);
    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(nint query, string path, nuint user, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] private static extern uint PdhGetRawCounterValue(nint counter, out uint type, out RawCounter value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
}
