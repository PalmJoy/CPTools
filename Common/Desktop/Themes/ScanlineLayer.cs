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
/// <summary>静止した走査線（SPEC §3.9 H14）。Pitch px ごとの 1 px の横線（FlHudScanline = 主色。濃さは設定 scanlineOpacity）。静的（大きさ・色・間隔が変わったときだけ描き直す）。</summary>
internal sealed class ScanlineLayer : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<ScanlineLayer, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<double> PitchProperty = AvaloniaProperty.Register<ScanlineLayer, double>(nameof(Pitch), 3);

    static ScanlineLayer() => AffectsRender<ScanlineLayer>(StrokeProperty, PitchProperty);

    private Pen? _pen;
    private IBrush? _penBrush;

    public ScanlineLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>線の間隔 px（設定 appearance.hud.scanlinePitch）。</summary>
    public double Pitch
    {
        get => GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Stroke is null) return;
        if (!ReferenceEquals(_penBrush, Stroke)) { _penBrush = Stroke; _pen = new Pen(Stroke, 1); }
        double pitch = Math.Max(1, Pitch), w = Bounds.Width;
        for (double y = 0; y < Bounds.Height; y += pitch) context.DrawLine(_pen!, new Point(0, y + 0.5), new Point(w, y + 0.5));
    }
}
