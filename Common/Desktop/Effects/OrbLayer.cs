#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CPClipboard.Core.Effects;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CPSystemMonitor.Core.Effects;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using FileLauncher.Core.Effects;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 枠線の上を周回する光の玉（演出 frameOrb。spec/EFFECTS.md「常時の演出の詳細」）。
/// 玉 1 個 = 小さな部品（OrbSprite）1 つ。部品を RenderTransform で動かし、描き直す範囲を玉の周りだけにする
/// （層全体を描き直すと盤面全体の再描画になり CPU を 30% 前後使った。2026-10-04 計測）。
/// </summary>
internal sealed class OrbLayer : Canvas
{
    private readonly OrbSprite _first = new(), _second = new();
    private OrbPath? _path;
    private Size _pathSize;
    private double _pathRadius, _pathInset, _pathChamfer = -1;

    /// <summary>枠の面取り（BoardWindow が FrameChrome.Chamfer を入れる）。0 なら角丸矩形の経路。</summary>
    public double Chamfer { get; set; }
    private Color? _accent, _accent2;
    private readonly List<(double X, double Y)> _positions = [];

    public OrbLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        Children.Add(_first);
        Children.Add(_second);
    }

    public EffectSpec Spec { get; set; } = EffectSpec.None;

    /// <summary>周の中の位置（0〜1）。</summary>
    public double Phase { get; private set; }

    /// <summary>最後に置いた玉の中心（テスト用）。</summary>
    internal IReadOnlyList<(double X, double Y)> OrbPositions => _positions;

    public void Advance(double elapsedMs)
    {
        Phase = AmbientMath.Phase(elapsedMs, Spec.DurationMs);
        _positions.Clear();
        bool show = Spec.Kind is EffectKind.Orb or EffectKind.OrbTwin && Bounds.Width >= 1 && Bounds.Height >= 1;
        _first.IsVisible = show;
        _second.IsVisible = show && Spec.Kind == EffectKind.OrbTwin;
        if (!show) return;
        var path = PathFor(Bounds.Size);
        UpdateColors();
        Place(_first, path, Phase);
        if (_second.IsVisible) Place(_second, path, Phase + 0.5);
    }

    private void Place(OrbSprite sprite, OrbPath path, double phase)
    {
        double head = phase * path.Length;
        var (hx, hy) = Offset(path.PointAt(head));
        var tail = new Point[OrbSprite.TailPoints];
        for (int i = 1; i <= OrbSprite.TailPoints; i++)
        {
            var (tx, ty) = Offset(path.PointAt(head - OrbSprite.TailLength * i / OrbSprite.TailPoints));
            tail[i - 1] = new Point(tx - hx, ty - hy);
        }
        sprite.SetTail(tail);
        sprite.RenderTransform = new TranslateTransform(hx - OrbSprite.Half, hy - OrbSprite.Half);
        _positions.Add((hx, hy));
    }

    private (double X, double Y) Offset((double X, double Y) p) => (p.X + _pathInset, p.Y + _pathInset);

    /// <summary>
    /// 枠線の中心線に沿った経路（大きさ・角丸・面取りが変わったときだけ作り直す）。面取りの枠線は FrameChrome の八角形の Path で、
    /// Frame の枠の太さ t の内側の、さらに t/2 内側が中心線（面取りの量は同じ）。
    /// </summary>
    private OrbPath PathFor(Size size)
    {
        double thickness = Token<Thickness>("FlBoardBorderThickness").Left;
        double radius = Token<CornerRadius>("FlBoardCornerRadius").TopLeft;
        double inset = AmbientLayout.OrbInset(Chamfer, thickness);
        if (_path is null || size != _pathSize || radius != _pathRadius || inset != _pathInset || Chamfer != _pathChamfer)
        {
            _path = Chamfer > 0
                ? OrbPath.Octagon(size.Width - inset * 2, size.Height - inset * 2, Chamfer)
                : OrbPath.RoundedRect(size.Width - thickness, size.Height - thickness, Math.Max(0, radius - inset));
            _pathSize = size;
            _pathRadius = radius;
            _pathInset = inset;
            _pathChamfer = Chamfer;
        }
        return _path;
    }

    /// <summary>主色・副色（テーマ・基調色の切替に追従）。変わったときだけ部品のブラシを作り直す。</summary>
    private void UpdateColors()
    {
        var a1 = Token<Color?>("FlAccent") ?? Colors.Cyan;
        var a2 = Token<Color?>("FlAccent2") ?? Colors.Magenta;
        if (a1 != _accent) { _accent = a1; _first.SetColor(a1); }
        if (a2 != _accent2) { _accent2 = a2; _second.SetColor(a2); }
    }

    private T Token<T>(string key)
    {
        var variant = (TopLevel.GetTopLevel(this) as IThemeVariantHost)?.ActualThemeVariant ?? ThemeVariant.Default;
        if (!this.TryFindResource(key, variant, out var v)) return default!;
        return v switch
        {
            T t => t,
            ISolidColorBrush b when typeof(T) == typeof(Color?) => (T)(object)(Color?)b.Color,
            Color c when typeof(T) == typeof(Color?) => (T)(object)(Color?)c,
            _ => default!,
        };
    }
}

/// <summary>光の玉 1 個（芯 + 暈 + 尾）。中心がこの部品の真ん中。尾の位置は玉からの相対座標で受け取る。</summary>
internal sealed class OrbSprite : Control
{
    public const int TailPoints = AmbientLook.OrbTailPoints;
    public const double TailLength = AmbientLook.OrbTailLength;
    private const double CoreRadius = AmbientLook.OrbCoreRadius, HaloRadius = AmbientLook.OrbHaloRadius;
    public const double Half = TailLength + HaloRadius + 4;

    private IBrush _core = Brushes.White;
    private IBrush _halo = Brushes.Transparent;
    private Point[] _tail = [];

    public OrbSprite()
    {
        Width = Height = Half * 2;
        IsHitTestVisible = false;
        IsVisible = false;
    }

    public void SetColor(Color c)
    {
        var core = AmbientLook.OrbCoreColor(new(c.R, c.G, c.B));
        _core = new SolidColorBrush(Color.FromRgb(core.R, core.G, core.B)).ToImmutable();
        _halo = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)Math.Round(AmbientLook.OrbHaloAlpha * 255), c.R, c.G, c.B), 0),
                new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
            },
        }.ToImmutable();
        InvalidateVisual();
    }

    public void SetTail(Point[] tail)
    {
        _tail = tail;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var center = new Point(Half, Half);
        // 尾: 後ろへ行くほど小さく薄い暈だけ
        var steps = AmbientLook.OrbTailSteps(); // 尾の先端（i = 10）から
        for (int i = _tail.Length; i >= 1; i--)
        {
            var (r, opacity, _) = steps[TailPoints - i];
            using (context.PushOpacity(opacity))
                context.DrawEllipse(_halo, null, center + _tail[i - 1], r, r);
        }
        context.DrawEllipse(_halo, null, center, HaloRadius, HaloRadius);
        context.DrawEllipse(_core, null, center, CoreRadius, CoreRadius);
    }
}
