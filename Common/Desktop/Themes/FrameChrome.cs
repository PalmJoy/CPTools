#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Styling;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Styling;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Styling;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 盤面と窓（設定画面・ダイアログ）で共有する枠（SPEC §3.8）。
/// 自分の Margin = 発光・影が窓の縁で切れないための透明な余白。子は Frame（面・枠線・発光）→ 角ブラケット ×4 → Overlays（最上段の層）。
/// 盤面: Margin FlGlowMarginThickness / 面 FlBoardBackground / 影 FlBoardGlow。窓: FlChromeMarginThickness / FlWindowBackground / FlChromeShadow。
/// </summary>
internal sealed class FrameChrome : Grid
{
    private string _marginKey = "FlGlowMarginThickness";
    private string _backgroundKey = "FlBoardBackground";
    private string _shadowKey = "FlBoardGlow";

    public Border Frame { get; } = new() { Name = "Frame", ClipToBounds = false };

    /// <summary>Frame の上に重ねる層（盤面のグリッチの静止画・ドラッグのゴースト）。</summary>
    public Grid Overlays { get; } = new() { IsHitTestVisible = false, ClipToBounds = false };

    /// <summary>
    /// 面取り（四隅を斜めに切る量 px）。HUD 段階 D（SPEC §3.9 H8）。2026-10-04 に実物を見てユーザーが正式採用。0 なら従来の角丸矩形。
    /// </summary>
    public static double DefaultChamfer { get; set; } = 10;

    private readonly Grid _layers = new() { ClipToBounds = false }; // Frame の中身: 外側の発光 → _content
    private readonly Grid _content = new();       // 面取りの面（八角形）→ 内側の発光 → Body
    private readonly Path _face = new() { IsHitTestVisible = false };
    private readonly NeonGlowLayer _outerGlow = new(NeonGlowLayer.GlowPart.Outer);
    private readonly NeonGlowLayer _innerGlow = new(NeonGlowLayer.GlowPart.Inner);
    private const int BodyIndex = 2;

    /// <summary>
    /// 面（中身の切り抜きと同じ形）を Frame / Overlays の座標で表したもの。Overlays に置く層（走査線の帯）を面の中だけに切り抜くのに使う。
    /// </summary>
    public Geometry? FaceClip { get; private set; }

    public event EventHandler? FaceClipChanged;

    /// <summary>面取りのときの外側 / 内側の発光（八角形に沿う。テスト用）。</summary>
    internal NeonGlowLayer OuterGlow => _outerGlow;
    internal NeonGlowLayer InnerGlow => _innerGlow;

    /// <summary>面取りのときに面と枠線を描く八角形（テスト用）。</summary>
    internal Path Face => _face;

    /// <summary>今の面の色（面取りなら八角形の Fill、そうでなければ Border の Background）。</summary>
    internal IBrush? SurfaceBrush => _chamfer > 0 ? _face.Fill : Frame.Background;

    /// <summary>今の枠線の色。</summary>
    internal IBrush? OutlineBrush => _chamfer > 0 ? _face.Stroke : Frame.BorderBrush;
    private readonly Path[] _brackets = new Path[4];
    private bool _surfaceVisible = true;
    private double _chamfer;

    public FrameChrome()
    {
        Frame[!Border.CornerRadiusProperty] = Themed.Res("FlBoardCornerRadius");
        Frame[!Border.BorderThicknessProperty] = Themed.Res("FlBoardBorderThickness");
        _face[!Shape.StrokeProperty] = Themed.Res("FlBoardBorder");
        // 面の八角形は Canvas に入れて大きさの計算に加えない（Path は図形の大きさを「必要な大きさ」として返すので、
        // 一度大きく描いた後は中身に合わせて縮む窓（SizeToContent）が縮まなくなった。2026-10-08 ページの設定が縦に伸びた）
        _content.Children.Add(new Canvas { IsHitTestVisible = false, Children = { _face } });
        _content.Children.Add(_innerGlow);
        _content.SizeChanged += (_, _) => UpdateChamferGeometry();
        _layers.Children.Add(_outerGlow);
        _layers.Children.Add(_content);
        Frame.Child = _layers;
        Children.Add(Frame);
        // 4 隅の角ブラケット（枠線から 2 px 外側。左上・右下 = 主色、右上・左下 = 副色。サイバーパンクのみ）
        _brackets[0] = Bracket("FlAccent", new Thickness(-3, -3, 0, 0), HorizontalAlignment.Left, VerticalAlignment.Top);
        _brackets[1] = Bracket("FlAccent2", new Thickness(0, -3, -3, 0), HorizontalAlignment.Right, VerticalAlignment.Top);
        _brackets[2] = Bracket("FlAccent2", new Thickness(-3, 0, 0, -3), HorizontalAlignment.Left, VerticalAlignment.Bottom);
        _brackets[3] = Bracket("FlAccent", new Thickness(0, 0, -3, -3), HorizontalAlignment.Right, VerticalAlignment.Bottom);
        foreach (var b in _brackets) Children.Add(b);
        Children.Add(Overlays);
        Chamfer = DefaultChamfer;
        Bind();
    }

    /// <summary>面取りの量（px）。0 なら角丸矩形（Border の面と枠線）、それ以外は八角形の Path で面と枠線を描き、中身もその形で切り抜く。</summary>
    public double Chamfer
    {
        get => _chamfer;
        set
        {
            _chamfer = Math.Max(0, value);
            UpdateBracketShapes();
            Bind();
            UpdateChamferGeometry();
        }
    }

    public string MarginKey { get => _marginKey; set { _marginKey = value; Bind(); } }

    /// <summary>枠の発光を作り置きの画像で描くか（盤面だけ true。SPEC §3.6「発光の作り方」の「作り置き」）。</summary>
    public bool BakeGlow
    {
        get => _outerGlow.Bake;
        set => _outerGlow.Bake = _innerGlow.Bake = value;
    }
    public string BackgroundKey { get => _backgroundKey; set { _backgroundKey = value; Bind(); } }
    public string ShadowKey { get => _shadowKey; set { _shadowKey = value; Bind(); } }

    /// <summary>枠の中身。</summary>
    [Content]
    public Control? Body
    {
        get => _content.Children.Count > BodyIndex ? _content.Children[BodyIndex] : null;
        set
        {
            while (_content.Children.Count > BodyIndex) _content.Children.RemoveAt(BodyIndex);
            if (value is not null) _content.Children.Add(value);
        }
    }

    /// <summary>面（地の色）を描くか。盤面の背景画像があるときは描かない（画像の部分からデスクトップが透けるように。SPEC §3.7）。</summary>
    public void SetSurfaceVisible(bool visible)
    {
        _surfaceVisible = visible;
        Bind();
    }

    /// <summary>今のテーマでの余白（片側）。窓の大きさの計算に使う。</summary>
    public double GetMargin()
    {
        var variant = (TopLevel.GetTopLevel(this) as IThemeVariantHost)?.ActualThemeVariant ?? Application.Current?.ActualThemeVariant ?? ThemeVariant.Default;
        return this.TryFindResource(_marginKey, variant, out var v) && v is Thickness t ? t.Left
            : Application.Current?.TryFindResource(_marginKey, variant, out var a) == true && a is Thickness at ? at.Left : 0;
    }

    private void Bind()
    {
        this[!MarginProperty] = Themed.Res(_marginKey);
        _outerGlow.IsVisible = _innerGlow.IsVisible = _chamfer > 0;
        if (_chamfer > 0)
        {
            // 面と枠線は八角形の Path で描く（Border は角を斜めに切れない）。発光も BoxShadow（矩形）ではなく八角形に沿うリング描画
            Frame.ClearValue(Border.BoxShadowProperty);
            _outerGlow[!NeonGlowLayer.ShadowsProperty] = Themed.Res(_shadowKey);
            _innerGlow[!NeonGlowLayer.ShadowsProperty] = Themed.Res(_shadowKey);
            BindSurface(null);
            Frame.ClearValue(Border.BorderBrushProperty);
            Frame.BorderBrush = Brushes.Transparent;
            _face.IsVisible = true;
            BindSurface(_face);
        }
        else
        {
            _face.IsVisible = false;
            _content.Clip = null;
            Frame[!Border.BoxShadowProperty] = Themed.Res(_shadowKey);
            Frame[!Border.BorderBrushProperty] = Themed.Res("FlBoardBorder");
            BindSurface(Frame);
        }
    }

    private IDisposable? _surfaceBinding;

    /// <summary>
    /// 面の色を target（八角形の Path か Frame）に結ぶ。地の色を描かないときと、もう片方は透明。前の結び付けは必ず外す
    /// （DynamicResource を残したまま値を上書きすると、表示時のリソースの解決や配色の変更で地の色に戻る）。
    /// </summary>
    private void BindSurface(AvaloniaObject? target)
    {
        _surfaceBinding?.Dispose();
        _surfaceBinding = null;
        _face.Fill = Brushes.Transparent;
        Frame.Background = Brushes.Transparent;
        if (!_surfaceVisible || target is null) return;
        var property = target == _face ? Shape.FillProperty : Border.BackgroundProperty;
        _surfaceBinding = target.Bind(property, this.GetResourceObservable(_backgroundKey));
    }

    /// <summary>八角形（面取り c。形は Core Octagon）。</summary>
    private static Geometry Octagon(double x, double y, double w, double h, double c) => NeonGlowPlan.Octagon(new Rect(x, y, w, h), c);

    /// <summary>面（線の太さの半分だけ内側）と中身の切り抜きを今の大きさで作り直す。</summary>
    private void UpdateChamferGeometry()
    {
        var size = _content.Bounds.Size;
        if (size.Width < 1 || size.Height < 1) return;
        double t = this.TryFindResource("FlBoardBorderThickness", out var v) && v is Thickness th ? th.Left : 1;
        // 中身は Frame の枠の太さ t の内側にある
        var face = new Rect(_layers.Bounds.Position, size);
        FaceClip = _chamfer > 0
            ? Octagon(face.X, face.Y, face.Width, face.Height, _chamfer)
            : new RectangleGeometry(face, Frame.CornerRadius.TopLeft, Frame.CornerRadius.TopLeft);
        FaceClipChanged?.Invoke(this, EventArgs.Empty);
        if (_chamfer <= 0) return;
        _face.StrokeThickness = t;
        _face.Data = Octagon(t / 2, t / 2, size.Width - t, size.Height - t, _chamfer);
        _content.Clip = Octagon(0, 0, size.Width, size.Height, _chamfer);
        _outerGlow.Chamfer = _innerGlow.Chamfer = _chamfer;
    }

    /// <summary>角ブラケット: 角丸のときは L 字（腕 10 px）、面取りのときは斜辺に沿って折れた線（腕 6 px + 斜辺）。</summary>
    private void UpdateBracketShapes()
    {
        double c = _chamfer, a = 6;
        string[] data = c > 0
            ?
            [
                $"M 0,{c + a} L 0,{c} L {c},0 L {c + a},0",
                $"M 0,0 L {a},0 L {c + a},{c} L {c + a},{c + a}",
                $"M 0,0 L 0,{a} L {c},{c + a} L {c + a},{c + a}",
                $"M {c + a},0 L {c + a},{a} L {a},{c + a} L 0,{c + a}",
            ]
            : ["M 0,10 L 0,0 L 10,0", "M 0,0 L 10,0 L 10,10", "M 0,0 L 0,10 L 10,10", "M 10,0 L 10,10 L 0,10"];
        for (int i = 0; i < 4; i++) _brackets[i].Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", data[i]));
    }

    private static Path Bracket(string strokeKey, Thickness margin, HorizontalAlignment h, VerticalAlignment v)
    {
        var path = new Path { Margin = margin, HorizontalAlignment = h, VerticalAlignment = v, Classes = { "bracket" } };
        path[!Shape.StrokeProperty] = Themed.Res(strokeKey);
        return path;
    }
}
