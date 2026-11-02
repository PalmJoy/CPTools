using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CPSystemMonitor.Core.Monitoring;
namespace CPSystemMonitor.App;
internal sealed class TelemetryGraph : Control
{
    public static readonly StyledProperty<IBrush?> PrimaryProperty = AvaloniaProperty.Register<TelemetryGraph, IBrush?>(nameof(Primary), Brushes.Cyan);
    public static readonly StyledProperty<IBrush?> SecondaryProperty = AvaloniaProperty.Register<TelemetryGraph, IBrush?>(nameof(Secondary), Brushes.Magenta);
    public IBrush? Primary { get => GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public IBrush? Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public MonitorSection Section { get; init; }
    public IReadOnlyList<MonitorSample> Samples { get; set; } = [];
    public int Seconds { get; set; } = 60;
    static TelemetryGraph() => AffectsRender<TelemetryGraph>(PrimaryProperty, SecondaryProperty);
    public TelemetryGraph()
    {
        ClipToBounds = true;
        this[!PrimaryProperty] = Themed.Res("FlAccent");
        this[!SecondaryProperty] = Themed.Res("FlAccent2");
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var pen = new Pen(Primary, .5);
        using (context.PushOpacity(.18))
        {
            context.DrawRectangle(null, pen, rect);
            for (int x = 1; x < 12; x++) context.DrawLine(pen, new(rect.Width * x / 12, 0), new(rect.Width * x / 12, rect.Height));
            for (int y = 1; y < 4; y++) context.DrawLine(pen, new(0, rect.Height * y / 4), new(rect.Width, rect.Height * y / 4));
        }
        double? Value(MonitorSample s, bool second) => Section switch
        {
            MonitorSection.Cpu => s.CpuPercent,
            MonitorSection.Memory => s.AvailablePercent is { } a ? 100 - a : null,
            MonitorSection.Storage => second ? s.WriteBytesPerSecond : s.ReadBytesPerSecond,
            _ => second ? s.SendBytesPerSecond : s.ReceiveBytesPerSecond,
        };
        bool dual = Section is MonitorSection.Storage or MonitorSection.Network;
        double max = dual ? Math.Max(1024, Samples.SelectMany(s => new[] { Value(s, false) ?? 0, Value(s, true) ?? 0 }).DefaultIfEmpty(0).Max() * 1.15) : 100;
        DateTimeOffset end = Samples.LastOrDefault()?.Time ?? DateTimeOffset.Now;
        void Draw(bool second)
        {
            Point? previous = null;
            var line = new Pen(second ? Secondary : Primary, 1.4);
            foreach (var s in Samples)
            {
                if (Value(s, second) is not { } value) { previous = null; continue; }
                var point = new Point(Math.Clamp(1 - (end - s.Time).TotalSeconds / Seconds, 0, 1) * rect.Width,
                    (1 - Math.Clamp(value / max, 0, 1)) * (rect.Height - 3) + 1);
                if (previous is { } p) context.DrawLine(line, p, point);
                previous = point;
            }
        }
        Draw(false); if (dual) Draw(true);
    }
}
internal sealed class MeterBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<MeterBar, double>(nameof(Value));
    public bool Vertical { get; init; }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<MeterBar, IBrush?>(nameof(Accent), Brushes.Cyan);
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    static MeterBar() => AffectsRender<MeterBar>(ValueProperty, AccentProperty);
    public MeterBar() { Height = 7; this[!AccentProperty] = Themed.Res("FlAccent"); }
    public override void Render(DrawingContext context)
    {
        double width = Math.Max(0, Bounds.Width), height = Math.Max(0, Bounds.Height);
        var rect = new Rect(0, 0, width, height);
        using (context.PushOpacity(.2)) context.DrawRectangle(Accent, null, rect);
        double fraction = Math.Clamp(Value, 0, 100) / 100;
        context.DrawRectangle(Accent, null, Vertical ? new Rect(0, height * (1 - fraction), width, height * fraction) : new Rect(0, 0, width * fraction, height));
    }
}
