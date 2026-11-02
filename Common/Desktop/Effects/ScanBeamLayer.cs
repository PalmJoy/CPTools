#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using CPClipboard.Core.Effects;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using CPSystemMonitor.Core.Effects;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using FileLauncher.Core.Effects;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 走査線の帯（演出 scanBeam。SPEC §3.9 H14、spec/EFFECTS.md「常時の演出の詳細」）。主色の縦グラデーションの帯が面を上から下へ流れ続ける。
/// 動かすのは帯の TranslateTransform だけ（層全体を描き直さない）。ブラシは主色が変わったときだけ作り直す。
/// </summary>
internal sealed class ScanBeamLayer : Canvas
{
    public const double BandHeight = AmbientLook.BeamBandHeight;

    private readonly Rectangle _band = new() { Height = BandHeight, IsHitTestVisible = false, IsVisible = false };
    private readonly TranslateTransform _move = new();
    private Color? _accent;

    public ScanBeamLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _band.RenderTransform = _move;
        Children.Add(_band);
        SizeChanged += (_, e) => _band.Width = e.NewSize.Width;
    }

    public EffectSpec Spec { get; set; } = EffectSpec.None;

    public double Phase { get; private set; }

    /// <summary>帯の上端の y（テスト用）。</summary>
    internal double BandY => _move.Y;

    internal bool BandVisible => _band.IsVisible;

    public void Advance(double elapsedMs)
    {
        Phase = AmbientMath.Phase(elapsedMs, Spec.DurationMs);
        _band.IsVisible = Spec.Kind == EffectKind.Beam && Bounds.Height >= 1;
        if (!_band.IsVisible) return;
        UpdateColor();
        _move.Y = AmbientMath.SweepOffset(Phase, Bounds.Height, BandHeight);
    }

    private void UpdateColor()
    {
        var variant = (TopLevel.GetTopLevel(this) as IThemeVariantHost)?.ActualThemeVariant ?? ThemeVariant.Default;
        var c = this.TryFindResource("FlAccent", variant, out var v) && v is ISolidColorBrush b ? b.Color : Colors.Cyan;
        if (c == _accent) return;
        _accent = c;
        // 上端は透明、下へ行くほど明るい（AmbientLook.BeamStops）
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        foreach (var (offset, alpha) in AmbientLook.BeamStops) brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), offset));
        _band.Fill = brush.ToImmutable();
    }
}
