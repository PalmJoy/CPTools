using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Core.Theming;

namespace CPSystemMonitor.App;

/// <summary>
/// テーマ（SPEC §3.6 / §10.5）。テーマ = ThemeVariant + アプリ独自の色トークン（Fl*）。
/// トークンはライト / ダーク / サイバーパンクの 3 辞書に同じキーで持ち、UI は {DynamicResource Fl*} で参照する。
/// ライト / ダークのトークンは Fluent の標準ブラシ（起動時に引いた値）なので、見た目は従来と同じ。
/// サイバーパンクは Dark を継承した独自 ThemeVariant で、Fluent のコントロールはダーク基調 + アクセント色だけシアン。
/// トークンは XAML ではなくここで組み立てる（ライト / ダークの値を Fluent から確実に引くため）。
/// </summary>
internal static class AppTheme
{
    public static readonly ThemeVariant Cyberpunk = new("Cyberpunk", ThemeVariant.Dark);

    private static ResourceDictionary? _tokens;

    /// <summary>
    /// Fluent のリソースのうち、アクセント色（とその濃淡）をそのまま使っているブラシのキーと濃淡の段。
    /// Fluent のブラシは色を固定で持っていて SystemAccentColor を差し替えても追従しないので、サイバーパンクでは
    /// これらのブラシ自体を主色で作り直して上書きする（ToggleSwitch の ON、Slider、選択中の項目、タブの下線など）。
    /// </summary>
    private static List<(string Key, int Shade, double Opacity)> _accentBrushKeys = new();

    /// <summary>トークンのキー一覧（3 テーマすべてに定義する。UI テストで網羅を確かめる）。</summary>
    public static IReadOnlyCollection<string> TokenKeys => Cyber(DefaultPalette).Keys.OfType<string>().Where(k => k.StartsWith("Fl", StringComparison.Ordinal)).ToList();

    private static CyberPalette DefaultPalette
    {
        get
        {
            var (p, s) = ColorPresets.For(AccentPreset.Cyan);
            return CyberPalette.From(CyberDerivation.Derive(p, s));
        }
    }

    /// <summary>トークン辞書をアプリに入れる。FluentTheme を Styles に入れた後に呼ぶ（本物の App と TestApp の両方）。</summary>
    public static void Install(Application app)
    {
        _accentBrushKeys = FindAccentBrushes(app);
        var tokens = new ResourceDictionary();
        tokens.ThemeDictionaries[Cyberpunk] = Cyber(DefaultPalette);
        app.Resources.MergedDictionaries.Add(tokens);
        _tokens = tokens;
        app.Styles.Add(ChromeStyles());
        // 配色の ColorPicker（Avalonia.Controls.ColorPicker）の見た目
        app.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://CPSystemMonitor/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });
        // 見た目はサイバーパンク専用（テーマの選択は 2026-10-04 に廃止）。OS がライトでも Fluent はダーク基調のまま
        app.RequestedThemeVariant = Cyberpunk;
    }

    /// <summary>
    /// 配色（主色・副色）を切り替える（SPEC §3.6「配色」。背景・文字は主色から導く）。辞書を差し替え、
    /// 念のため ThemeVariant を入れ直して確実に描き直させる（DynamicResource は辞書の変更通知で再解決される）。
    /// </summary>
    public static void SetColors(Application app, RgbColor primary, RgbColor secondary)
    {
        if (_tokens is null) return;
        _tokens.ThemeDictionaries[Cyberpunk] = Cyber(CyberPalette.From(CyberDerivation.Derive(primary, secondary)));
        app.RequestedThemeVariant = ThemeVariant.Dark;
        app.RequestedThemeVariant = Cyberpunk;
    }

    /// <summary>盤面と窓で共有する枠の部品のスタイル（角ブラケット・× ボタン。SPEC §3.8）。</summary>
    private static Styles ChromeStyles()
    {
        static Style Make(Func<Selector?, Selector> selector, params (AvaloniaProperty Property, object? Value)[] setters)
        {
            var style = new Style(selector);
            foreach (var (prop, value) in setters) style.Setters.Add(new Setter(prop, value!));
            return style;
        }
        return new Styles
        {
            // 設定画面のタブのアウトライン化（HUD 段階 C。SPEC §3.9 H7）: 1 px の枠で囲み、選択中は主色の枠 + 薄い面。Fluent の選択下線は消す
            Make(x => x.OfType<TabItem>(),
                (TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                (TemplatedControl.BorderBrushProperty, new DynamicResourceExtension("FlTabOutline")),
                (TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
                (TemplatedControl.PaddingProperty, new Thickness(9, 2)),
                (TemplatedControl.FontSizeProperty, 14.0), // Fluent の既定 20 だと英語 UI で 7 つが 1 行に収まらない
                (Layoutable.MarginProperty, new Thickness(0, 0, 4, 0)),
                (Layoutable.MinHeightProperty, 30.0)),
            Make(x => x.OfType<TabItem>().Class(":selected"),
                (TemplatedControl.BorderBrushProperty, new DynamicResourceExtension("FlAccent")),
                (TemplatedControl.BackgroundProperty, new DynamicResourceExtension("FlAccentSoft"))),
            Make(x => x.OfType<TabItem>().Template().OfType<Border>().Name("PART_SelectedPipe"),
                (Visual.IsVisibleProperty, false)),
            Make(x => x.OfType<Avalonia.Controls.Shapes.Path>().Class("bracket"),
                (Avalonia.Controls.Shapes.Shape.StrokeThicknessProperty, 1.5),
                (Visual.IsVisibleProperty, new DynamicResourceExtension("FlCornerBrackets")),
                (InputElement.IsHitTestVisibleProperty, false)),
            Make(x => x.OfType<Button>().Class("chrome-close"),
                (TemplatedControl.BackgroundProperty, Brushes.Transparent),
                (TemplatedControl.PaddingProperty, new Thickness(7)),
                (TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                (TemplatedControl.CornerRadiusProperty, new DynamicResourceExtension("FlRowCornerRadius")),
                (Layoutable.VerticalAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center)),
            Make(x => x.OfType<Button>().Class("chrome-close").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                (ContentPresenter.BackgroundProperty, new DynamicResourceExtension("FlAccentSoft"))),
            Make(x => x.OfType<Button>().Class("chrome-close").Class(":pointerover").Descendant().OfType<Avalonia.Controls.Shapes.Path>(),
                (Avalonia.Controls.Shapes.Shape.StrokeProperty, new DynamicResourceExtension("FlText"))),
            // カーソルの位置の 3 × 3（Settings/AnchorPicker）: タブのアウトラインと同じ流儀。選択中は主色の枠 + 薄い面 + 主色の点
            Make(x => x.OfType<ToggleButton>().Class("anchor-cell"),
                (TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                (TemplatedControl.BorderBrushProperty, new DynamicResourceExtension("FlTabOutline")),
                (TemplatedControl.BackgroundProperty, Brushes.Transparent),
                (TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
                (TemplatedControl.PaddingProperty, new Thickness(3)),
                (Layoutable.MinWidthProperty, 0.0),
                (Layoutable.MinHeightProperty, 0.0)),
            Make(x => x.OfType<ToggleButton>().Class("anchor-cell").Class(":checked").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                (ContentPresenter.BackgroundProperty, new DynamicResourceExtension("FlAccentSoft")),
                (ContentPresenter.BorderBrushProperty, new DynamicResourceExtension("FlAccent"))),
            Make(x => x.OfType<ToggleButton>().Class("anchor-cell").Class(":checked").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                (ContentPresenter.BackgroundProperty, new DynamicResourceExtension("FlAccentSoft")),
                (ContentPresenter.BorderBrushProperty, new DynamicResourceExtension("FlAccent"))),
            Make(x => x.OfType<ToggleButton>().Class("anchor-cell").Descendant().OfType<Border>().Name("Dot"),
                (Border.BackgroundProperty, new DynamicResourceExtension("FlTabOutline"))),
            Make(x => x.OfType<ToggleButton>().Class("anchor-cell").Class(":checked").Descendant().OfType<Border>().Name("Dot"),
                (Border.BackgroundProperty, new DynamicResourceExtension("FlAccent"))),
        };
    }

    /// <summary>テーマの数値トークン（ウィンドウの大きさの計算など、コードで値が要るもの）。</summary>
    public static double GlowMargin(IResourceHost host, string key = "FlGlowMargin") =>
        host.TryFindResource(key, (host as IThemeVariantHost)?.ActualThemeVariant ?? ThemeVariant.Default, out var v) && v is double d ? d : 0;

    // ---------------- ライト / ダーク（Fluent の値への写像） ----------------

    private static readonly FontFamily ChakraPetch = new("avares://CPSystemMonitor/Resources/Fonts#Chakra Petch");

    /// <summary>HUD の等幅フォント（Share Tech Mono、SIL OFL 1.1。SPEC §3.9）。</summary>
    private static readonly FontFamily ShareTechMono = new("avares://CPSystemMonitor/Resources/Fonts#Share Tech Mono");

    /// <summary>アルファを付けた色（a = 0〜255）。</summary>
    private static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>c を to と t（0〜1）の割合で混ぜる（Fluent のアクセント Dark/Light 1〜3 用）。</summary>
    private static Color Mix(Color c, Color to, double t) => Color.FromRgb(
        (byte)Math.Round(c.R + (to.R - c.R) * t), (byte)Math.Round(c.G + (to.G - c.G) * t), (byte)Math.Round(c.B + (to.B - c.B) * t));

    private static BoxShadows Glow(double blur, Color c, byte a) =>
        new(new BoxShadow { Blur = blur, Color = A(c, a) });

    /// <summary>
    /// ネオン管のような発光（2026-10-03 ユーザー要望「フレームの発光感を CP2077 のように」）: 線のすぐ外の強い光 +
    /// 中くらいの光 + 外へ広がる淡い光の重ね。inner を付けると内側にもうっすら染みる。
    /// </summary>
    private static BoxShadows Neon(Color c, double near, double mid, double far, byte inner = 0)
    {
        var shadows = new List<BoxShadow>
        {
            new() { Blur = near, Color = A(c, 0xE6) },
            new() { Blur = mid, Color = A(c, 0x8C) },
            new() { Blur = far, Color = A(c, 0x4D) },
        };
        if (inner > 0) shadows.Add(new BoxShadow { Blur = 18, Color = A(c, inner), IsInset = true });
        return new BoxShadows(shadows[0], shadows.Skip(1).ToArray());
    }

    /// <summary>サイバーパンクのトークン（SPEC §3.6 の導出規則。cyan に当てはめると色トークン表の値になる）。</summary>
    internal static ResourceDictionary Cyber(CyberPalette p)
    {
        IBrush B(Color c) => new SolidColorBrush(c);
        var d = new ResourceDictionary
        {
            ["FlWindowBackground"] = B(p.WindowBackground),
            ["FlBoardBackground"] = B(p.BoardBackground),
            ["FlBoardBorder"] = B(A(p.Accent, 0xCC)),
            ["FlSurface"] = B(p.Surface),
            ["FlText"] = B(p.Text),
            ["FlTextMuted"] = B(p.TextMuted),
            ["FlRowText"] = B(p.RowText),
            ["FlAccent"] = B(p.Accent),
            ["FlAccent2"] = B(p.Accent2),
            ["FlAccentSoft"] = B(A(p.Accent, 0x1F)),
            ["FlAccent2Soft"] = B(A(p.Accent2, 0x24)),
            ["FlWarning"] = Brush("#FFC53D"),
            ["FlOnWarning"] = B(p.WindowBackground),
            ["FlError"] = Brush("#FF3B5C"),
            ["FlSuccess"] = Brush("#2EE6A6"),
            ["FlRowHoverBackground"] = B(A(p.Accent, 0x1F)),
            ["FlRowHoverBorder"] = B(p.Accent),
            ["FlEmptyGroupText"] = B(A(p.TextMuted, 0x80)),
            ["FlTabText"] = B(p.TextMuted),
            ["FlTabSelectedText"] = B(p.Accent),
            ["FlTabBackground"] = Brushes.Transparent,
            ["FlTabHoverBackground"] = B(A(p.Accent, 0x1F)),
            ["FlTabCheckedBackground"] = Brushes.Transparent,
            ["FlTabCheckedHoverBackground"] = B(A(p.Accent, 0x1F)),
            ["FlTabIndicator"] = B(p.Accent),
            ["FlTabIndicatorThickness"] = new Thickness(0, 0, 0, 2),
            ["FlTabCornerRadius"] = new CornerRadius(0),
            ["FlHeaderSeparator"] = B(A(p.Accent, 0x33)),
            ["FlToastBackground"] = B(A(p.WindowBackground, 0xF5)),
            ["FlToastErrorBackground"] = Brush("#F5160B14"),
            ["FlToastBorder"] = B(A(p.Accent, 0x99)),
            ["FlToastText"] = B(p.Text),
            ["FlToastStripeWidth"] = 3.0,
            ["FlBoardCornerRadius"] = new CornerRadius(2),
            ["FlRowCornerRadius"] = new CornerRadius(2),
            ["FlToastCornerRadius"] = new CornerRadius(2),
            ["FlCornerBrackets"] = true,
            ["FlBoardGlow"] = Neon(p.Accent, near: 4, mid: 14, far: 32, inner: 0x26),
            ["FlRowHoverGlow"] = Neon(p.Accent, near: 3, mid: 10, far: 20, inner: 0x1A),
            ["FlTabSelectedGlow"] = Neon(p.Accent, near: 3, mid: 8, far: 16),
            ["FlPasteGlow"] = Neon(p.Accent, near: 4, mid: 14, far: 28, inner: 0x33),
            ["FlToastGlow"] = Neon(p.Accent, near: 3, mid: 12, far: 26),
            ["FlBoardBorderThickness"] = new Thickness(1.5),
            ["FlRowSelectedBorder"] = B(p.Accent),
            ["FlRowSelectedGlow"] = Neon(p.Accent, near: 3, mid: 10, far: 20),
            // コンテキストメニュー（Fluent のキーを直接上書き。SPEC §3.6 導出規則）
            ["MenuFlyoutPresenterBackground"] = B(A(p.WindowBackground, 0xF5)),
            ["MenuFlyoutPresenterBorderBrush"] = B(A(p.Accent, 0x99)),
            ["MenuFlyoutItemBackgroundPointerOver"] = B(A(p.Accent, 0x1F)),
            ["MenuFlyoutItemBackgroundPressed"] = B(A(p.Accent, 0x33)),
            ["MenuFlyoutItemForeground"] = B(p.Text),
            ["MenuFlyoutItemForegroundPointerOver"] = B(p.Text),
            ["MenuFlyoutItemForegroundPressed"] = B(p.Text),
            ["MenuFlyoutSubItemForeground"] = B(p.Text),
            ["MenuFlyoutSubItemBackgroundPointerOver"] = B(A(p.Accent, 0x1F)),
            ["MenuFlyoutSeparatorBackground"] = B(A(p.Accent, 0x33)),
            ["OverlayCornerRadius"] = new CornerRadius(2),
            ["FlToastErrorGlow"] = Neon(Color.Parse("#FF3B5C"), near: 3, mid: 12, far: 26),
            // 枠の発光が消えきる距離 NeonProfile.Extent(FlBoardGlow, 明滅「強」1.9 倍)（約 27）+ 2 以上。8 px の余裕を含む（SPEC §3.6）
            ["FlGlowMargin"] = 40.0,
            ["FlGlowMarginThickness"] = new Thickness(40),
            // トーストの発光は矩形の BoxShadow のまま（ぼかし 26）。位置を変えないため従来の 34 に据え置く
            ["FlToastMargin"] = 34.0,
            ["FlToastMarginThickness"] = new Thickness(34),
            ["FlChromeShadow"] = Neon(p.Accent, near: 4, mid: 14, far: 32, inner: 0x26),
            ["FlChromeMargin"] = 40.0,
            ["FlChromeMarginThickness"] = new Thickness(40),
            ["FlFontFamily"] = ChakraPetch,
            // HUD（SPEC §3.9）
            ["FlMonoFontFamily"] = ShareTechMono,
            ["FlHudText"] = B(A(p.Accent, 0xE6)), // 9 px・70% は Windows の等倍・ソフトウェア描画で読みづらかった（2026-10-07）
            ["FlHudGrid"] = B(p.Accent), // 濃さは設定 appearance.hud.gridOpacity（GridLayer.Opacity。既定 3%）
            ["FlHudScanline"] = B(p.Accent), // 濃さは設定 appearance.hud.scanlineOpacity（ScanlineLayer.Opacity。既定 25%）
            ["FlTabOutline"] = B(A(p.TextMuted, 0x80)),
            ["FlHudHeight"] = 18.0,
            // 文字のネオン発光（見出し・タブ・トースト。全ラベルには付けない: Effect は描画が重いため）
            ["FlTextGlow"] = new DropShadowEffect { OffsetX = 0, OffsetY = 0, BlurRadius = 10, Color = A(p.Accent, 0xE6), Opacity = 1 },
            ["FlTextGlowSoft"] = new DropShadowEffect { OffsetX = 0, OffsetY = 0, BlurRadius = 6, Color = A(p.Accent, 0x80), Opacity = 1 },
            // Fluent のアクセント色（ToggleSwitch の ON、Slider、ComboBox の選択、フォーカス枠）を主色に
            ["SystemAccentColor"] = p.Accent,
            ["SystemAccentColorDark1"] = Mix(p.Accent, Colors.Black, 0.2),
            ["SystemAccentColorDark2"] = Mix(p.Accent, Colors.Black, 0.4),
            ["SystemAccentColorDark3"] = Mix(p.Accent, Colors.Black, 0.6),
            ["SystemAccentColorLight1"] = Mix(p.Accent, Colors.White, 0.3),
            ["SystemAccentColorLight2"] = Mix(p.Accent, Colors.White, 0.5),
            ["SystemAccentColorLight3"] = Mix(p.Accent, Colors.White, 0.7),
        };
        var shades = new[]
        {
            Mix(p.Accent, Colors.Black, 0.6), Mix(p.Accent, Colors.Black, 0.4), Mix(p.Accent, Colors.Black, 0.2), p.Accent,
            Mix(p.Accent, Colors.White, 0.3), Mix(p.Accent, Colors.White, 0.5), Mix(p.Accent, Colors.White, 0.7),
        };
        foreach (var (key, shade, opacity) in _accentBrushKeys)
        {
            if (!d.ContainsKey(key)) d[key] = new SolidColorBrush(shades[shade], opacity);
        }
        return d;
    }

    /// <summary>Fluent（ダーク）のリソースを全部見て、アクセント色の濃淡と同じ色の単色ブラシを探す。</summary>
    private static List<(string, int, double)> FindAccentBrushes(Application app)
    {
        string[] shadeKeys =
        [
            "SystemAccentColorDark3", "SystemAccentColorDark2", "SystemAccentColorDark1", "SystemAccentColor",
            "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        ];
        var shadeOf = new Dictionary<Color, int>();
        for (int i = 0; i < shadeKeys.Length; i++)
            if (app.TryGetResource(shadeKeys[i], ThemeVariant.Dark, out var v) && v is Color c) shadeOf.TryAdd(c, i);

        var keys = new HashSet<string>();
        foreach (var style in app.Styles) if (style is IResourceProvider rp) CollectKeys(rp, keys);

        var result = new List<(string, int, double)>();
        foreach (var key in keys)
        {
            if (!app.TryGetResource(key, ThemeVariant.Dark, out var v) || v is not ISolidColorBrush brush) continue;
            var opaque = Color.FromRgb(brush.Color.R, brush.Color.G, brush.Color.B);
            if (shadeOf.TryGetValue(opaque, out int shade)) result.Add((key, shade, brush.Opacity * brush.Color.A / 255.0));
        }
        return result;
    }

    private static void CollectKeys(IResourceProvider provider, HashSet<string> keys)
    {
        switch (provider)
        {
            case ResourceDictionary rd:
                foreach (var k in rd.Keys) if (k is string s) keys.Add(s);
                foreach (var m in rd.MergedDictionaries) CollectKeys(m, keys);
                foreach (var t in rd.ThemeDictionaries.Values) if (t is IResourceProvider tp) CollectKeys(tp, keys);
                break;
            case Avalonia.Markup.Xaml.Styling.ResourceInclude ri when ri.Loaded is IResourceProvider loaded:
                CollectKeys(loaded, keys);
                break;
            case IResourceHost:
                break;
        }
        if (provider is Styles styles)
        {
            CollectKeys(styles.Resources, keys);
            foreach (var st in styles) if (st is IResourceProvider sp) CollectKeys(sp, keys);
        }
        else if (provider is StyleBase sb)
        {
            CollectKeys(sb.Resources, keys);
        }
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));
}
