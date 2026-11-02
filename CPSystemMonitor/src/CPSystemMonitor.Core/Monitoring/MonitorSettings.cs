namespace CPSystemMonitor.Core.Monitoring;

public enum MonitorSection { Cpu, Memory, Storage, Network }
public enum MonitorMode { Standard, Compact }
public sealed class SectionSettings
{
    public bool Visible { get; set; } = true;
    public bool TopApp { get; set; } = true;
    public bool History { get; set; } = true;
}
public sealed class MonitorSettings
{
    public MonitorMode Mode { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool PerCore { get; set; } = true;
    public double RefreshSeconds { get; set; } = 1;
    public int HistorySeconds { get; set; } = 60;
    public string Drive { get; set; } = "";
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool DetailedAttribution { get; set; }
    public SectionSettings Cpu { get; set; } = new();
    public SectionSettings Memory { get; set; } = new();
    public SectionSettings Storage { get; set; } = new();
    public SectionSettings Network { get; set; } = new();
    public SectionSettings For(MonitorSection section) => section switch
    {
        MonitorSection.Cpu => Cpu, MonitorSection.Memory => Memory,
        MonitorSection.Storage => Storage, _ => Network,
    };
    public void Normalize()
    {
        if (!Enum.IsDefined(Mode)) Mode = MonitorMode.Standard;
        RefreshSeconds = double.IsFinite(RefreshSeconds) ? Math.Clamp(RefreshSeconds, .5, 10) : 1;
        HistorySeconds = Math.Clamp(HistorySeconds, 10, 600);
        Drive ??= "";
        Cpu ??= new(); Memory ??= new(); Storage ??= new(); Network ??= new();
    }
}
