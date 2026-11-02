#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>投影面の縁の一部だけに主色と副色の微小なずれを描く。文字・アイコンは複製しない。</summary>
internal sealed class HologramEdges : Control
{
    public static readonly StyledProperty<IBrush?> PrimaryProperty = AvaloniaProperty.Register<HologramEdges, IBrush?>(nameof(Primary));
    public static readonly StyledProperty<IBrush?> SecondaryProperty = AvaloniaProperty.Register<HologramEdges, IBrush?>(nameof(Secondary));
    static HologramEdges() => AffectsRender<HologramEdges>(PrimaryProperty, SecondaryProperty);
    public HologramEdges() { IsHitTestVisible = false; ClipToBounds = true; }
    public IBrush? Primary { get => GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public IBrush? Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 100 || h < 100) return;
        foreach (var (brush, offset) in new[] { (Primary, -0.8), (Secondary, 0.8) })
        {
            if (brush is null) continue;
            var pen = new Pen(brush, 1);
            foreach (double y in new[] { h * 0.26, h * 0.65 })
            {
                context.DrawLine(pen, new Point(3 + offset, y), new Point(3 + offset, y + 22));
                context.DrawLine(pen, new Point(w - 3 + offset, y + 9), new Point(w - 3 + offset, y + 31));
            }
            context.DrawLine(pen, new Point(w * 0.62 + offset, 3), new Point(w * 0.62 + 35 + offset, 3));
            context.DrawLine(pen, new Point(w * 0.3 + offset, h - 3), new Point(w * 0.3 + 35 + offset, h - 3));
        }
    }
}
