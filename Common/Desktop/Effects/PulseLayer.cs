#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 発光の明滅（演出 glowPulse。spec/EFFECTS.md「常時の演出の詳細」）。枠の発光（FlBoardGlow）を 1 枚の画像に焼いておき、
/// その画像を Opacity 付きで重ねる。BoxShadow を持つ要素の Opacity を直接揺らすと、描き直しのたびにぼかしを計算し直して
/// CPU を 10〜30% 使った（2026-10-04 計測）。画像なら描き直しは貼るだけ。
/// 面取りした枠では静的な発光と同じ NeonGlowPlan（八角形に沿う）を焼く。大きさ・配色・面取りが変わったら Refresh で焼き直す。
/// 画像は枠の周りの上下左右 4 本の帯（<see cref="PulseStrip"/>、別の Visual）に分けて描く。明滅で汚れる範囲が枠の周りだけになり、
/// 部分塗り直し（UseRegionDirtyRectClipping）と組み合わせて透明な中央を塗り直さない（2026-10-08 計測。spec/EFFECTS.md）。
/// </summary>
internal sealed class PulseLayer : Canvas
{
    private readonly PulseStrip[] _strips;
    private RenderTargetBitmap? _bitmap;
    private Size _bakedSize;
    private double _bakedScale;
    private object? _bakedShadow;
    private double _bakedChamfer = -1;
    private double _margin;

    /// <summary>枠の面取り（BoardWindow が FrameChrome.Chamfer を入れる）。0 なら従来の角丸矩形の BoxShadow を焼く。</summary>
    public double Chamfer { get; set; }

    /// <summary>焼いた画像（テスト用）。</summary>
    internal Bitmap? BakedImage => _bitmap;

    public PulseLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        _strips = [new(this), new(this), new(this), new(this)];
        foreach (var strip in _strips) Children.Add(strip);
    }

    /// <summary>帯 4 本（テスト用）。</summary>
    internal IReadOnlyList<PulseStrip> Strips => _strips;

    internal Bitmap? Image => _bitmap;

    private double _level;

    /// <summary>
    /// 明るさ（0〜1）。要素の Opacity ではなく描くときに掛ける（要素の Opacity を 1 未満にすると、上を光の玉が通るたびに
    /// 層全体を合成し直して CPU を 25% 前後使った。2026-10-04 計測）。
    /// </summary>
    public double Level
    {
        get => _level;
        set
        {
            if (value == _level) return;
            _level = value;
            foreach (var strip in _strips) strip.InvalidateVisual(); // 自分（盤面全体の大きさ）は汚さない
        }
    }

    /// <summary>今の大きさ・テーマで画像が古ければ焼き直す（盤面が見えていて大きさが決まってから呼ぶ）。</summary>
    public void Refresh() => Refresh(Bounds.Size);

    /// <summary>大きさを指定して焼く（OS の層で描くとき。この層自体は隠れていてレイアウトされない）。</summary>
    public void Refresh(Size size)
    {
        if (size.Width < 1 || size.Height < 1) return;
        var variant = (TopLevel.GetTopLevel(this) as IThemeVariantHost)?.ActualThemeVariant ?? ThemeVariant.Default;
        this.TryFindResource("FlBoardGlow", variant, out var shadow);
        this.TryFindResource("FlBoardCornerRadius", variant, out var radius);
        this.TryFindResource("FlGlowMargin", variant, out var marginValue);
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        double margin = marginValue is double m ? m : 0;
        if (_bitmap is not null && size == _bakedSize && scale == _bakedScale && Equals(shadow, _bakedShadow)
            && Chamfer == _bakedChamfer && margin == _margin) return;

        _margin = margin;
        var full = new Size(size.Width + _margin * 2, size.Height + _margin * 2);
        var px = new PixelSize(Math.Max(1, (int)Math.Ceiling(full.Width * scale)), Math.Max(1, (int)Math.Ceiling(full.Height * scale)));
        var bitmap = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
        if (Chamfer > 0)
        {
            // 面取り: 静的な発光（FrameChrome の NeonGlowLayer）と同じ描き方を焼く。発光は枠線の外形（Frame の枠の太さの内側）から始まる
            this.TryFindResource("FlBoardBorderThickness", variant, out var thicknessValue);
            double t = thicknessValue is Thickness th ? th.Left : 1;
            var plan = NeonGlowPlan.Create(new Rect(t, t, size.Width - 2 * t, size.Height - 2 * t), Chamfer,
                shadow is BoxShadows glow ? glow : default, outer: true, inner: true);
            using (var ctx = bitmap.CreateDrawingContext())
            using (ctx.PushTransform(Matrix.CreateTranslation(_margin, _margin)))
                plan.Draw(ctx);
        }
        else
        {
            RenderRoundedRect(bitmap, size, full, shadow, radius);
        }
        _bitmap?.Dispose();
        _bitmap = bitmap;
        _bakedSize = size;
        _bakedScale = scale;
        _bakedShadow = shadow;
        _bakedChamfer = Chamfer;
        LayoutStrips(shadow is BoxShadows sh ? sh : default, scale, variant);
    }

    /// <summary>
    /// 帯の位置と、画像のどこを描くかを決める。帯の厚み = 余白 + 枠の太さ + 内側の光の深さ（デバイス px で整数に丸める）。
    /// 上下は全幅、左右はその間（重なりも隙間も無い）。画像の論理サイズを正とする。
    /// </summary>
    private void LayoutStrips(BoxShadows shadow, double scale, ThemeVariant variant)
    {
        if (_bitmap is null) return;
        this.TryFindResource("FlBoardBorderThickness", variant, out var thicknessValue);
        double t = thicknessValue is Thickness th ? th.Left : 1;
        double inner = NeonGlowPlan.Extents(shadow).Inner;
        double b = Math.Ceiling((_margin + t + Math.Ceiling(inner) + 1) * scale) / scale;
        double W = _bitmap.Size.Width, H = _bitmap.Size.Height, m = _margin;
        if (H <= 2 * b || W <= 2 * b)
        {
            // 小さすぎる盤面: 1 本で全体
            _strips[0].Place(new Rect(-m, -m, W, H), new Rect(0, 0, W, H));
            for (int i = 1; i < 4; i++) _strips[i].Place(default, default);
            return;
        }
        _strips[0].Place(new Rect(-m, -m, W, b), new Rect(0, 0, W, b));                         // 上
        _strips[1].Place(new Rect(-m, -m + H - b, W, b), new Rect(0, H - b, W, b));             // 下
        _strips[2].Place(new Rect(-m, -m + b, b, H - 2 * b), new Rect(0, b, b, H - 2 * b));     // 左
        _strips[3].Place(new Rect(-m + W - b, -m + b, b, H - 2 * b), new Rect(W - b, b, b, H - 2 * b)); // 右
    }

    /// <summary>面取りなし（角丸矩形。テスト用の Chamfer = 0）: BoxShadow を持つ Border をそのまま焼く。</summary>
    private void RenderRoundedRect(RenderTargetBitmap bitmap, Size size, Size full, object? shadow, object? radius)
    {
        var host = new Border
        {
            Width = size.Width,
            Height = size.Height,
            Margin = new Thickness(_margin),
            Background = Brushes.Transparent,
            CornerRadius = radius is CornerRadius cr ? cr : default,
            BoxShadow = shadow is BoxShadows bs ? bs : default,
        };
        host.Measure(full);
        host.Arrange(new Rect(full));
        bitmap.Render(host);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }
}

/// <summary>明滅の画像のうち、枠の 1 辺の帯だけを描く（<see cref="PulseLayer"/> の子）。</summary>
internal sealed class PulseStrip : Control
{
    private readonly PulseLayer _source;
    private Rect _sourceRect;

    public PulseStrip(PulseLayer source)
    {
        _source = source;
        IsHitTestVisible = false;
        IsVisible = false;
    }

    /// <summary>描く範囲（親の座標）。</summary>
    internal Rect Dest { get; private set; }

    /// <summary>画像のどこを描くか（画像の論理座標）。</summary>
    internal Rect SourceRect => _sourceRect;

    internal void Place(Rect dest, Rect source)
    {
        Dest = dest;
        _sourceRect = source;
        IsVisible = dest.Width > 0 && dest.Height > 0;
        Canvas.SetLeft(this, dest.X);
        Canvas.SetTop(this, dest.Y);
        Width = Math.Max(0, dest.Width);
        Height = Math.Max(0, dest.Height);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_source.Image is not { } image || _source.Level <= 0) return;
        using var _ = context.PushOpacity(_source.Level);
        context.DrawImage(image, _sourceRect, new Rect(Bounds.Size));
    }
}
