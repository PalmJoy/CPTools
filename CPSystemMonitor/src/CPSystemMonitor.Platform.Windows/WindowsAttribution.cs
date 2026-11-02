using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace CPSystemMonitor.Platform.Windows;

/// <summary>Optional OS ETW tracing. No packet content or filenames are stored.</summary>
internal sealed class WindowsAttribution : IDisposable
{
    private readonly object _gate = new();
    private TraceEventSession? _session;
    private Task? _worker;
    private bool _enabled;
    private readonly Dictionary<int, (long Read, long Write)> _disk = new();
    private readonly Dictionary<int, (long In, long Out)> _network = new();
    private Dictionary<int, (long In, long Out)> _previous = new();
    private long _last;
    public bool Available { get; private set; }
    public string Status { get; private set; } = "disabled";
    public IReadOnlyDictionary<int, (long Read, long Write)> DiskCounters { get { lock (_gate) return new Dictionary<int, (long, long)>(_disk); } }
    public IReadOnlyDictionary<int, (double Receive, double Send)> NetworkRates
    {
        get
        {
            lock (_gate)
            {
                long now = Stopwatch.GetTimestamp(); double seconds = _last == 0 ? 0 : Stopwatch.GetElapsedTime(_last, now).TotalSeconds;
                var result = new Dictionary<int, (double, double)>();
                foreach (var (pid, value) in _network)
                {
                    if (seconds > 0 && _previous.TryGetValue(pid, out var p)) result[pid] = ((value.In - p.In) / seconds, (value.Out - p.Out) / seconds);
                }
                _previous = new(_network); _last = now;
                return result;
            }
        }
    }
    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        Dispose(); _enabled = enabled;
        if (!enabled) return;
        if (TraceEventSession.IsElevated() != true) { Status = "permission-required"; return; }
        try
        {
            var session = new TraceEventSession("CPSystemMonitor-" + Environment.ProcessId) { StopOnDispose = true };
            _session = session;
            void Disk(int pid, int bytes, bool write)
            {
                if (pid <= 0 || bytes < 0) return;
                lock (_gate) { var p = _disk.GetValueOrDefault(pid); _disk[pid] = write ? (p.Read, p.Write + bytes) : (p.Read + bytes, p.Write); }
            }
            void Net(int pid, int bytes, bool send)
            {
                if (pid <= 0 || bytes < 0) return;
                lock (_gate) { var p = _network.GetValueOrDefault(pid); _network[pid] = send ? (p.In, p.Out + bytes) : (p.In + bytes, p.Out); }
            }
            var kernel = session.Source.Kernel;
            kernel.DiskIORead += e => Disk(e.ProcessID, e.TransferSize, false);
            kernel.DiskIOWrite += e => Disk(e.ProcessID, e.TransferSize, true);
            kernel.TcpIpRecv += e => Net(e.ProcessID, e.size, false);
            kernel.TcpIpSend += e => Net(e.ProcessID, e.size, true);
            kernel.TcpIpRecvIPV6 += e => Net(e.ProcessID, e.size, false);
            kernel.TcpIpSendIPV6 += e => Net(e.ProcessID, e.size, true);
            kernel.UdpIpRecv += e => Net(e.ProcessID, e.size, false);
            kernel.UdpIpSend += e => Net(e.ProcessID, e.size, true);
            kernel.UdpIpRecvIPV6 += e => Net(e.ProcessID, e.size, false);
            kernel.UdpIpSendIPV6 += e => Net(e.ProcessID, e.size, true);
            kernel.ProcessStop += e => { lock (_gate) { _disk.Remove(e.ProcessID); _network.Remove(e.ProcessID); _previous.Remove(e.ProcessID); } };
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.DiskIO | KernelTraceEventParser.Keywords.DiskIOInit
                | KernelTraceEventParser.Keywords.NetworkTCPIP | KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.Thread);
            Available = true; Status = "active";
            _worker = Task.Run(() =>
            {
                try { session.Source.Process(); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                finally { if (ReferenceEquals(_session, session)) { Available = false; Status = "unavailable"; } }
            });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _session?.Dispose(); _session = null; Available = false; Status = "unavailable";
        }
    }
    public void Dispose()
    {
        _enabled = false; Available = false; Status = "disabled";
        var session = _session; _session = null; session?.Dispose();
        lock (_gate) { _disk.Clear(); _network.Clear(); _previous.Clear(); _last = 0; }
    }
}
