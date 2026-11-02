using System.Diagnostics;
using System.Globalization;
namespace CPSystemMonitor.Platform.MacOS;

/// <summary>Finite nettop CSV snapshots avoid stdio buffering in the OS tool's infinite mode.</summary>
internal sealed class MacNetworkAttribution : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancel;
    private Process? _process;
    private Dictionary<int, (double Receive, double Send)> _rates = new();
    private bool _valid;
    public bool Available { get { lock (_gate) return _valid; } }
    public string Status { get; private set; } = "disabled";
    public IReadOnlyDictionary<int, (double Receive, double Send)> Rates { get { lock (_gate) return _rates; } }
    public void SetEnabled(bool enabled)
    {
        Dispose(); if (!enabled) return;
        var cancel = new CancellationTokenSource();
        lock (_gate) { _cancel = cancel; Status = "starting"; }
        _ = Task.Run(() => LoopAsync(cancel.Token));
    }
    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var process = new Process { StartInfo = new("/usr/bin/nettop") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
                foreach (var a in new[] { "-P", "-L", "2", "-s", "1", "-x", "-n", "-J", "bytes_in,bytes_out" }) process.StartInfo.ArgumentList.Add(a);
                long started = Stopwatch.GetTimestamp();
                lock (_gate) { token.ThrowIfCancellationRequested(); process.Start(); _process = process; }
                var output = process.StandardOutput.ReadToEndAsync(token); var error = process.StandardError.ReadToEndAsync(token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); token.ThrowIfCancellationRequested(); throw new IOException("nettop timed out"); }
                string text = await output; await error;
                var snapshots = Parse(text);
                var next = new Dictionary<int, (double, double)>();
                if (process.ExitCode == 0 && snapshots.Count >= 2)
                {
                    double seconds = Math.Max(.5, Stopwatch.GetElapsedTime(started).TotalSeconds);
                    foreach (var (pid, current) in snapshots[^1])
                        if (snapshots[^2].TryGetValue(pid, out var previous)) next[pid] =
                            (Core.Monitoring.SamplingMath.Rate(previous.In, current.In, seconds), Core.Monitoring.SamplingMath.Rate(previous.Out, current.Out, seconds));
                    lock (_gate) { if (token.IsCancellationRequested) return; _rates = next; _valid = true; Status = "active"; _process = null; }
                }
                else
                {
                    lock (_gate) { if (token.IsCancellationRequested) return; _rates = new(); _valid = false; Status = "unavailable"; _process = null; }
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            lock (_gate) if (!token.IsCancellationRequested) { _valid = false; _rates = new(); Status = "unavailable"; }
        }
    }
    internal static List<Dictionary<int, (long In, long Out)>> Parse(string text)
    {
        var snapshots = new List<Dictionary<int, (long, long)>>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith(",bytes_in,bytes_out", StringComparison.Ordinal)) { snapshots.Add(new()); continue; }
            var fields = line.TrimEnd(',').Split(','); if (fields.Length < 3 || snapshots.Count == 0) continue;
            string name = string.Join(',', fields[..^2]); int dot = name.LastIndexOf('.');
            if (dot < 0 || !int.TryParse(name[(dot + 1)..], out int pid)
                || !long.TryParse(fields[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long rx)
                || !long.TryParse(fields[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long tx)) continue;
            snapshots[^1][pid] = (rx, tx);
        }
        return snapshots;
    }
    public void Dispose()
    {
        lock (_gate)
        {
            _cancel?.Cancel(); _cancel = null; _valid = false; _rates = new(); Status = "disabled";
            try { if (_process is { HasExited: false } p) p.Kill(true); } catch (InvalidOperationException) { }
            _process = null;
        }
    }
}
