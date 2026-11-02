#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CPClipboard.Core.Effects;
using CPClipboard.Core.Theming;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CPSystemMonitor.Core.Effects;
using CPSystemMonitor.Core.Theming;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using FileLauncher.Core.Effects;
using FileLauncher.Core.Theming;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 面取りした枠（八角形）に沿うネオンの発光（SPEC §3.9「面取りの仕様」、issue/CHAMFER_GLOW.md）。
/// BoxShadow は矩形しか描けないので、Core NeonProfile の断面（BoxShadow と同じガウス）を、八角形をオフセットした帯（0.5 px 幅）で描く。
/// Effect も BoxShadow も使わないので RenderTargetBitmap にそのまま焼ける（明滅 PulseLayer も同じ <see cref="NeonGlowPlan"/> を焼く）。
/// 自分の Bounds が枠の外形。外側の光は Bounds の外（窓の余白）へ描く。
/// <see cref="Bake"/> のときは描いた結果を画像に作り置きして貼るだけにする（盤面。明滅や光の玉で毎フレーム描き直すとき、
/// 約 80 本の帯を塗り直すのが重かった。2026-10-08 計測、spec/EFFECTS.md）。作るのは <see cref="Refresh"/> の中だけ。
/// </summary>
internal sealed class NeonGlowLayer : Control
{
    public enum GlowPart { Outer, Inner }

    public static readonly StyledProperty<BoxShadows> ShadowsProperty = AvaloniaProperty.Register<NeonGlowLayer, BoxShadows>(nameof(Shadows));

    static NeonGlowLayer() => AffectsRender<NeonGlowLayer>(ShadowsProperty);

    private NeonGlowPlan? _plan;
    private RenderTargetBitmap? _baked;
    private (NeonGlowPlan? Plan, double Scale, Vector Frac) _bakedKey;
    private double _bakedPad;
    private Vector _bakedFrac;
    private TopLevel? _topLevel;
    private bool _refreshPosted;

    public NeonGlowLayer(GlowPart part)
    {
        Part = part;
        IsHitTestVisible = false;
        ClipToBounds = false;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None); // 作り置きは等倍で貼る
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>作り置きを使うか（盤面だけ true。設定画面・ダイアログは常時の描き直しが無いので焼かない）。</summary>
    public bool Bake
    {
        get => _bake;
        set { if (value != _bake) { _bake = value; if (!value) Discard(); Refresh(); InvalidateVisual(); } }
    }
    private bool _bake;

    // テスト用
    internal Bitmap? BakedImage => _baked;
    internal int BakeCount { get; private set; }
    internal bool UsesFallback { get; private set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShadowsProperty) Refresh(); // 配色の変更
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null) _topLevel.ScalingChanged += OnScalingChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_topLevel is not null) _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        Discard();
    }

    private void OnScalingChanged(object? sender, EventArgs e) => Refresh();

    private NeonGlowPlan CurrentPlan()
    {
        var frame = new Rect(Bounds.Size);
        if (_plan is null || !_plan.Matches(frame, _chamfer, Shadows))
            _plan = NeonGlowPlan.Create(frame, _chamfer, Shadows, outer: Part == GlowPart.Outer, inner: Part == GlowPart.Inner);
        return _plan;
    }

    /// <summary>作り置きが古ければ作り直す（大きさ・面取り・配色・拡大率）。Render の中では作らない。</summary>
    public void Refresh()
    {
        _refreshPosted = false;
        if (!_bake || Bounds.Width < 1 || Bounds.Height < 1 || !IsVisible) { if (!_bake) Discard(); return; }
        var plan = CurrentPlan();
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var frac = DeviceFraction(scale);
        if (_baked is not null && ReferenceEquals(_bakedKey.Plan, plan) && _bakedKey.Scale == scale && _bakedKey.Frac == frac) return;
        try
        {
            // 外側の光は外形の外へ OutwardExtent まで広がる（内側の光は外形の内側だけ）
            double pad = Math.Ceiling((plan.OutwardExtent + 1) * scale) / scale; // デバイス画素の整数（画像の格子を盤面の格子に揃える）
            var full = new Rect(Bounds.Size).Inflate(pad);
            var px = new PixelSize(Math.Max(1, (int)Math.Ceiling(full.Width * scale)), Math.Max(1, (int)Math.Ceiling(full.Height * scale)));
            var bitmap = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
            using (var ctx = bitmap.CreateDrawingContext())
            // 層の位置が画素の途中（枠の太さ 1.5 など）なら、その端数ぶんずらして焼き、貼るときに画素の格子に合わせる
            using (ctx.PushTransform(Matrix.CreateTranslation(pad + frac.X, pad + frac.Y)))
                plan.Draw(ctx);
            Discard();
            _baked = bitmap;
            _bakedKey = (plan, scale, frac);
            _bakedPad = pad;
            _bakedFrac = frac;
            BakeCount++;
            UsesFallback = false;
            AppLog.Info($"枠の発光の作り置き（{Part}）: {px.Width}×{px.Height} px");
        }
        catch (Exception ex)
        {
            Discard();
            UsesFallback = true;
            AppLog.Info($"枠の発光の作り置きに失敗（ベクタで描く）: {ex.Message}");
        }
        InvalidateVisual();
    }

    /// <summary>この層の左上が、デバイス画素の格子からどれだけずれているか（論理 px）。</summary>
    private Vector DeviceFraction(double scale)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || this.TranslatePoint(default, top) is not { } p) return default;
        double fx = p.X * scale - Math.Floor(p.X * scale), fy = p.Y * scale - Math.Floor(p.Y * scale);
        return new Vector(Math.Round(fx / scale, 4), Math.Round(fy / scale, 4));
    }

    private void Discard()
    {
        _baked?.Dispose();
        _baked = null;
        _bakedKey = default;
    }

    public GlowPart Part { get; }

    public BoxShadows Shadows
    {
        get => GetValue(ShadowsProperty);
        set => SetValue(ShadowsProperty, value);
    }

    private double _chamfer;

    public double Chamfer
    {
        get => _chamfer;
        set { if (value != _chamfer) { _chamfer = value; Refresh(); InvalidateVisual(); } }
    }

    public override void Render(DrawingContext context)
    {
        var plan = CurrentPlan();
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (_bake && _baked is not null && ReferenceEquals(_bakedKey.Plan, plan) && _bakedKey.Scale == scale && _bakedKey.Frac == DeviceFraction(scale))
        {
            // 等倍で貼る（dest は画像の論理サイズ。拡大縮小しない。焼いたときの端数を戻して画素の格子に合わせる）
            context.DrawImage(_baked, new Rect(_baked.Size), new Rect(-_bakedPad - _bakedFrac.X, -_bakedPad - _bakedFrac.Y, _baked.Size.Width, _baked.Size.Height));
            return;
        }
        plan.Draw(context); // 最初のフレーム・拡大率が変わった直後・作り置きに失敗したとき
        if (_bake && !UsesFallback && !_refreshPosted)
        {
            _refreshPosted = true;
            Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
        }
    }
}

/// <summary>
/// 発光の描き方 1 組（ジオメトリとブラシを作り置きし、描くときは塗るだけ）。描画の唯一の実装。
/// 大きさ・面取り・影の値が変わったら作り直す（Render の中で毎回 new しない）。
/// </summary>
internal sealed class NeonGlowPlan
{
    private readonly Rect _frame;
    private readonly double _chamfer;
    private readonly BoxShadows _shadows;
    private readonly List<(Geometry Shape, IBrush Brush)> _bands = [];

    /// <summary>外側の光が外形から広がる距離（px。外側の層を描かないなら 0）。作り置きの余白に使う。</summary>
    public double OutwardExtent { get; private set; }

    /// <summary>影の値から、外側の光の広がりと内側の光の深さ（明滅の最大倍率込み。ジオメトリは作らない）。</summary>
    public static (double Outer, double Inner) Extents(BoxShadows shadows)
    {
        var (outer, _) = Layers(shadows, inset: false);
        var (inner, _) = Layers(shadows, inset: true);
        return (outer.Count > 0 ? NeonProfile.Extent(outer, AmbientLook.MaxPulseGain) : 0,
                inner.Count > 0 ? NeonProfile.Extent(inner, AmbientLook.MaxPulseGain) : 0);
    }

    private NeonGlowPlan(Rect frame, double chamfer, BoxShadows shadows)
    {
        _frame = frame;
        _chamfer = chamfer;
        _shadows = shadows;
    }

    public bool Matches(Rect frame, double chamfer, BoxShadows shadows) =>
        frame == _frame && chamfer == _chamfer && shadows == _shadows;

    /// <param name="frame">枠の外形（八角形を内接させる矩形）。</param>
    /// <param name="outer">外側の光（BoxShadow の外向きの層）を描くか。</param>
    /// <param name="inner">内側の光（IsInset の層）を描くか。</param>
    public static NeonGlowPlan Create(Rect frame, double chamfer, BoxShadows shadows, bool outer, bool inner)
    {
        var plan = new NeonGlowPlan(frame, chamfer, shadows);
        if (frame.Width < 1 || frame.Height < 1) return plan;
        var (outerLayers, outerColor) = Layers(shadows, inset: false);
        var (innerLayers, innerColor) = Layers(shadows, inset: true);
        // 帯 = 距離 Outer の八角形 − 距離 Inner の八角形（外側は膨らませ、内側は縮める）
        if (outer && outerLayers.Count > 0)
            plan.OutwardExtent = NeonProfile.Extent(outerLayers, AmbientLook.MaxPulseGain);
        if (outer && outerLayers.Count > 0)
            foreach (var (o, i, a) in NeonProfile.Bands(outerLayers, AmbientLook.MaxPulseGain))
                plan._bands.Add((Band(Offset(frame, o, chamfer), Offset(frame, i, chamfer)), Brush(outerColor, a)));
        if (inner && innerLayers.Count > 0)
            foreach (var (o, i, a) in NeonProfile.Bands(innerLayers, AmbientLook.MaxPulseGain))
                plan._bands.Add((Band(Offset(frame, -i, chamfer), Offset(frame, -o, chamfer)), Brush(innerColor, a)));
        return plan;
    }

    public void Draw(DrawingContext context)
    {
        foreach (var (shape, brush) in _bands) context.DrawGeometry(brush, null, shape);
    }

    /// <summary>外形を distance だけ外へ（負なら内へ）オフセットした八角形。縮めすぎて無くなれば null。</summary>
    private static Geometry? Offset(Rect frame, double distance, double chamfer)
    {
        var r = frame.Inflate(distance);
        return r.Width <= 0 || r.Height <= 0 ? null : Octagon(r, Core.Theming.Octagon.OffsetChamfer(chamfer, distance));
    }

    private static Geometry Band(Geometry? outer, Geometry? inner) =>
        inner is null ? outer! : new CombinedGeometry(GeometryCombineMode.Exclude, outer!, inner);

    /// <summary>BoxShadows → 断面の層（外向き / 内向き）と色（Neon は 1 色。最初の層の色を使う）。</summary>
    private static (List<NeonLayer> Layers, Color Color) Layers(BoxShadows shadows, bool inset)
    {
        var layers = new List<NeonLayer>();
        Color color = default;
        foreach (var s in shadows)
        {
            if (s.IsInset != inset || s.Color.A == 0) continue;
            if (layers.Count == 0) color = s.Color;
            layers.Add(new NeonLayer(s.Blur, s.Color.A / 255.0));
        }
        return (layers, color);
    }

    private static IBrush Brush(Color c, double alpha) => new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B), alpha);

    /// <summary>八角形のジオメトリ（形は Core Octagon）。</summary>
    public static Geometry Octagon(Rect r, double chamfer)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open()) AddOctagon(ctx, r, chamfer);
        return g;
    }

    private static void AddOctagon(StreamGeometryContext ctx, Rect r, double chamfer)
    {
        var p = Core.Theming.Octagon.Points(r.X, r.Y, r.Width, r.Height, chamfer);
        ctx.BeginFigure(new Point(p[0].X, p[0].Y), true);
        for (int i = 1; i < p.Length; i++) ctx.LineTo(new Point(p[i].X, p[i].Y));
        ctx.EndFigure(true);
    }
}
