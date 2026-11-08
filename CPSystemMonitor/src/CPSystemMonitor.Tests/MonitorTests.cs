using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Core.Monitoring;
using CPSystemMonitor.Core.Storage;
using CPSystemMonitor.Platform;
using CPSystemMonitor.Platform.MacOS;
using CPSystemMonitor.Platform.Windows;
namespace CPSystemMonitor.Tests;
public sealed class MonitorTests
{
    [Fact] public async Task Cancelled_process_permission_request_keeps_normal_collection()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var source = new MacMonitorSource();
        await source.RequestProcessAccessAsync("/not-launched", null, new CancellationToken(true));
        Assert.Equal("cancelled", source.ProcessAccessStatus);
        Assert.Contains(source.ReadProcesses(), p => p.Pid == Environment.ProcessId);
    }
    [Fact] public async Task Mac_privileged_helper_reads_system_processes_without_elevating_the_parent_when_requested()
    {
        string? executable = Environment.GetEnvironmentVariable("CPSM_HELPER_EXE");
        if (!OperatingSystem.IsMacOS() || string.IsNullOrEmpty(executable)) return;
        bool privilegedBefore = Environment.IsPrivilegedProcess;
        using var source = new MacMonitorSource();
        source.ReadProcesses(); int deniedBefore = source.InaccessibleProcessCount;
        await source.RequestProcessAccessAsync(executable, null, CancellationToken.None);
        Assert.Equal("active", source.ProcessAccessStatus);
        var first = source.ReadProcesses();
        Assert.Contains(first, p => p.Pid == Environment.ProcessId);
        Assert.True(source.InaccessibleProcessCount <= deniedBefore);
        if (deniedBefore > 0) Assert.True(source.InaccessibleProcessCount < deniedBefore);
        await Task.Delay(100);
        Assert.NotEmpty(source.ReadProcesses());
        Assert.Equal(privilegedBefore, Environment.IsPrivilegedProcess);
        source.ReleaseProcessAccess();
        Assert.Equal("limited", source.ProcessAccessStatus);
        Assert.NotEmpty(source.ReadProcesses());
    }
    [Theory]
    [InlineData(24_000_000UL, 12_000_000UL, 125U, 3U, 1_500_000_000L)]
    [InlineData(1_000_000_000UL, 500_000_000UL, 1U, 1U, 1_500_000_000L)]
    [InlineData(120_000_000_000_000_000UL, 60_000_000_000_000_000UL, 125U, 3U, 7_500_000_000_000_000_000L)]
    public void Mach_cpu_time_is_converted_without_intermediate_overflow(ulong user, ulong system, uint numer, uint denom, long expected)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Assert.Equal(expected, MacMonitorSource.CpuNanoseconds(user, system, numer, denom));
    }
    [Fact] public void Native_mac_process_cpu_time_matches_managed_cpu_time()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("CPSM_NATIVE_TESTS") != "1") return;
        using var source = new MacMonitorSource();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        long Read() => source.ReadProcesses().Single(p => p.Pid == Environment.ProcessId).CpuNanoseconds;
        long before = Read();
        process.Refresh();
        var managedBefore = process.TotalProcessorTime;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 300) System.Threading.Thread.SpinWait(1000);
        long delta = Read() - before;
        process.Refresh();
        double managedDelta = (process.TotalProcessorTime - managedBefore).TotalNanoseconds;
        Assert.True(managedDelta > 0);
        Assert.InRange(delta / managedDelta, 0.5, 2);
    }
    [Fact] public void Cpu_rates_handle_idle_reset_and_no_elapsed_ticks()
    {
        Assert.Equal(75, SamplingMath.Cpu(new(100, 1000), new(125, 1100)));
        Assert.Equal(0, SamplingMath.Cpu(new(100, 1000), new(10, 100)));
        Assert.Equal(0, SamplingMath.Rate(1000, 10, 1));
        Assert.Equal(500, SamplingMath.Rate(100, 1100, 2));
    }
    [Fact] public void Helpers_are_grouped_into_outer_mac_application()
    {
        var rows = new[]
        {
            new ProcessRates(1,"Chrome","/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",0,3,100,10,20),
            new ProcessRates(2,"Helper","/Applications/Google Chrome.app/Contents/Frameworks/Helper.app/Contents/MacOS/Helper",1,5,200,20,30),
            new ProcessRates(3,"Other","/Applications/Other.app/Contents/MacOS/Other",0,1,40,0,0),
        };
        var apps = AppAggregation.Aggregate(rows, true);
        Assert.Equal(2, apps.Count);
        var chrome = Assert.Single(apps, a => a.Name == "Google Chrome");
        Assert.Equal(8, chrome.CpuPercent); Assert.Equal(300, chrome.MemoryBytes); Assert.Equal(50, chrome.WriteBytesPerSecond);
    }
    [Fact] public void History_uses_wall_time_instead_of_number_of_samples()
    {
        var h = new SampleHistory(); var t = DateTimeOffset.UtcNow;
        h.Add(Sample(t), 60); h.Add(Sample(t.AddSeconds(30)), 60); h.Add(Sample(t.AddSeconds(61)), 60);
        Assert.Equal(2, h.Samples.Count); h.Trim(t.AddSeconds(100), 60); Assert.Single(h.Samples);
    }
    [Fact] public void Monitor_settings_roundtrip_without_other_product_data()
    {
        string root = Path.Combine(Path.GetTempPath(), "cpsm-test-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var store = new AppDataStore(new(root, true)); var s = new AppSettings();
            s.Monitor.Mode = MonitorMode.Compact; s.Monitor.Network.Visible = false; s.Monitor.Cpu.TopApp = false; s.Monitor.X = 123; s.Monitor.RefreshSeconds = 2;
            store.SaveSettings(s);
            var actual = new AppDataStore(new(root, true)).SettingsStore.Load().Value;
            Assert.Equal(MonitorMode.Compact, actual.Monitor.Mode); Assert.False(actual.Monitor.Network.Visible); Assert.False(actual.Monitor.Cpu.TopApp);
            Assert.Equal(123, actual.Monitor.X); Assert.Equal(2, actual.Monitor.RefreshSeconds);
            Assert.DoesNotContain("templates", File.ReadAllText(store.Paths.SettingsFile));
            s.Monitor.RefreshSeconds = double.NaN; s.Monitor.HistorySeconds = int.MaxValue; s.Normalize();
            Assert.Equal(1, s.Monitor.RefreshSeconds); Assert.Equal(600, s.Monitor.HistorySeconds);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public void Mac_memory_reports_compressed_RAM_and_current_swap_usage()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var source = new MacMonitorSource();
        var memory = source.ReadMemory();
        Assert.True(memory.Total > 0);
        Assert.InRange(memory.Available, 0, memory.Total);
        Assert.NotNull(memory.Compressed);
        Assert.InRange(memory.Compressed.Value, 0, memory.Total);
        // A denied sysctl must leave swap unavailable rather than reporting a false zero.
        if (memory.SwapUsed is { } swap) Assert.True(swap >= 0);
        Console.WriteLine($"Memory: compressed={memory.Compressed}, swap={memory.SwapUsed}");
    }
    [Fact] public async Task Native_source_reports_real_metrics_when_requested()
    {
        if (Environment.GetEnvironmentVariable("CPSM_NATIVE_TESTS") != "1") return;
        IMonitorSource source = OperatingSystem.IsMacOS() ? new MacMonitorSource() : new WindowsMonitorSource();
        using var sampler = new MonitorSampler(source);
        sampler.ConfigureAttribution(true);
        var first = sampler.Sample("");
        Assert.True(first.TotalMemory > 0); Assert.InRange(first.AvailableMemory, 0, first.TotalMemory);
        Assert.True(first.DiskTotal > 0); Assert.True(first.DiskFree > 0);
        await Task.Delay(3500);
        var next = sampler.Sample("");
        Assert.NotEmpty(next.Cores); Assert.InRange(next.CpuPercent!.Value, 0, 100);
        Assert.NotNull(next.ReadBytesPerSecond); Assert.NotNull(next.ReceiveBytesPerSecond); Assert.NotNull(next.TopMemory);
        if (OperatingSystem.IsMacOS()) Assert.True(source.NetworkAttributionAvailable, source.AttributionStatus);
        if (OperatingSystem.IsWindows() && Microsoft.Diagnostics.Tracing.Session.TraceEventSession.IsElevated() == true)
        {
            Assert.True(source.DiskAttributionAvailable, source.AttributionStatus);
            Assert.True(source.NetworkAttributionAvailable, source.AttributionStatus);
        }
        Console.WriteLine($"Native: CPU={next.CpuPercent:F1}% cores={next.Cores.Length}, available={next.AvailableMemory}/{next.TotalMemory}, disk={next.DiskFree}/{next.DiskTotal}, R/W={next.ReadBytesPerSecond}/{next.WriteBytesPerSecond}, attribution={source.AttributionStatus}");
    }
    internal static MonitorSample Sample(DateTimeOffset t) => new(t, 24, [25,23], 32L<<30,20L<<30,"/",1L<<40,400L<<30,1000,100,2000,200,null,null,null,null);
}
