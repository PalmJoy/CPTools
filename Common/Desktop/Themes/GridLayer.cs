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
/// <summary>盤面の背景グリッド（HUD 段階 C。SPEC §3.9 H6）。24 px 間隔の細線（FlHudGrid = 主色。濃さは設定 gridOpacity）。静的（大きさ・色が変わったときだけ描き直す）。</summary>
internal sealed class GridLayer : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<GridLayer, IBrush?>(nameof(Stroke));

    static GridLayer() => AffectsRender<GridLayer>(StrokeProperty);

    public const double Spacing = 24;
    private Pen? _pen;
    private IBrush? _penBrush;

    public GridLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Stroke is null) return;
        if (!ReferenceEquals(_penBrush, Stroke)) { _penBrush = Stroke; _pen = new Pen(Stroke, 1); }
        double w = Bounds.Width, h = Bounds.Height;
        for (double x = Spacing; x < w; x += Spacing) context.DrawLine(_pen!, new Point(x + 0.5, 0), new Point(x + 0.5, h));
        for (double y = Spacing; y < h; y += Spacing) context.DrawLine(_pen!, new Point(0, y + 0.5), new Point(w, y + 0.5));
    }
}
