using CPSystemMonitor.Core.Monitoring;
namespace CPSystemMonitor.Platform;

public interface IMonitorSource : IDisposable
{
    CpuTicks[] ReadCpu();
    MemoryReading ReadMemory();
    (long Read, long Write)? ReadDisk(string volume);
    IReadOnlyList<ProcessReading> ReadProcesses();
    IReadOnlyDictionary<int, (double Receive, double Send)> NetworkByProcess { get; }
    bool DiskAttributionAvailable { get; }
    bool NetworkAttributionAvailable { get; }
    string AttributionStatus { get; }
    void ConfigureAttribution(bool enabled);
}
