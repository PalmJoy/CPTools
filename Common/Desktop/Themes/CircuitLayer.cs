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
/// <summary>四隅の静的な回路。中央のアイコン領域に配線を広げない。</summary>
internal sealed class CircuitLayer : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<CircuitLayer, IBrush?>(nameof(Stroke));
    static CircuitLayer() => AffectsRender<CircuitLayer>(StrokeProperty);
    public CircuitLayer() { IsHitTestVisible = false; ClipToBounds = true; }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public override void Render(DrawingContext context)
    {
        if (Stroke is null || Bounds.Width < 160 || Bounds.Height < 120) return;
        var pen = new Pen(Stroke, 1);
        foreach (bool right in new[] { false, true })
        foreach (bool bottom in new[] { false, true })
        {
            Point P(double x, double y) => new(right ? Bounds.Width-x : x, bottom ? Bounds.Height-y : y);
            for (int i = 0; i < 3; i++)
            {
                double start = 16 + i * 7, end = 68 + i * 13, y = 47 + i * 8;
                context.DrawLine(pen, P(start, 34), P(start, y - 8));
                context.DrawLine(pen, P(start, y - 8), P(start + 8, y));
                context.DrawLine(pen, P(start + 8, y), P(end, y));
                context.DrawEllipse(null, pen, P(end + 3, y), 3, 3);
            }
        }
    }
}
