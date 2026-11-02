#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 設定画面・ダイアログの基底（SPEC §3.8）。OS のタイトルバーを消し、盤面と同じ枠（FrameChrome）で囲む。
/// 上段のタイトル行をドラッグして移動、× / Esc / Ctrl+W（⌘W）で閉じる。最小化・最大化・サイズ変更は無し。
/// 大きさは中身（Body）の大きさで指定する（BodyWidth / BodyHeight。窓の大きさ = 中身 + タイトル行 + 余白×2）。
/// </summary>
internal class ChromeWindow : Window
{
    public const double TitleBarHeight = 34;
    private const string TitlePrefix = AppInfo.TitlePrefix;

    private readonly FrameChrome _chrome = new() { MarginKey = "FlChromeMarginThickness", BackgroundKey = "FlWindowBackground", ShadowKey = "FlChromeShadow" };
    private readonly Border _bodyHost = new();
    private readonly TextBlock _titleText = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center };
    private double? _bodyWidth;
    private double? _bodyHeight;
    private bool _closed;

    // ---------------- グリッチ（盤面と同じ。spec/EFFECTS.md「glitch の詳細」） ----------------

    /// <summary>窓の表示・非表示の演出（App が設定の boardShow / boardHide を入れる。既定はなし = テストでは動かない）。</summary>
    public static Core.Effects.EffectSpec ShowEffect { get; set; } = Core.Effects.EffectSpec.None;
    public static Core.Effects.EffectSpec HideEffect { get; set; } = Core.Effects.EffectSpec.None;
    public static Core.Effects.GlitchLook GlitchLook { get; set; } = Core.Effects.GlitchLook.Base;
    internal Core.Effects.EffectSpec? WindowShowEffect { get; set; }
    internal Core.Effects.EffectSpec? WindowHideEffect { get; set; }
    private Core.Effects.EffectSpec EffectiveShowEffect => WindowShowEffect ?? ShowEffect;
    private Core.Effects.EffectSpec EffectiveHideEffect => WindowHideEffect ?? HideEffect;

    private readonly Canvas _glitchLayer;
    private readonly GlitchPlayer _glitch = new();
    private bool _hideGlitchStarted, _hideGlitchDone;

    private static bool UsesGlitch(Core.Effects.EffectSpec spec) => spec.Kind == Core.Effects.EffectKind.Glitch && spec.IsActive;

    private IBrush Token(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Cyan;

    private void PlayShowGlitch()
    {
        if (!UsesGlitch(EffectiveShowEffect))
        {
            if (WindowShowEffect is { } spec) _ = EffectRunner.ShowAsync(_chrome, spec);
            else _chrome.Opacity = 1;
            return;
        }
        var task = _glitch.Start(_chrome, _chrome.Frame, _glitchLayer, EffectiveShowEffect, GlitchLook, Token("FlAccent"), Token("FlAccent2"), CancellationToken.None);
        if (task is null) _chrome.Opacity = 1; // 静止画が撮れなければそのまま出す
    }

    /// <summary>閉じるときは一度止めてグリッチで消し、終わってから同じ結果で閉じ直す。</summary>
    private async void OnClosingGlitch(object? sender, WindowClosingEventArgs e)
    {
        if (_hideGlitchDone || !(UsesGlitch(EffectiveHideEffect) || WindowHideEffect?.IsActive == true) || !IsVisible
            || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;
        e.Cancel = true;
        if (_hideGlitchStarted) return;
        _hideGlitchStarted = true;
        // Close(result) の結果を覚えておく（Avalonia の Window は閉じるのを取り消しても結果を保持するが、念のため自分で持ち直す）
        object? result = typeof(Window).GetField("_dialogResult", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(this);
        IsHitTestVisible = false;
        GlitchPlayer.Finish(_chrome.Frame, _glitchLayer);
        var task = UsesGlitch(EffectiveHideEffect)
            ? _glitch.Start(_chrome, _chrome.Frame, _glitchLayer, EffectiveHideEffect, GlitchLook, Token("FlAccent"), Token("FlAccent2"), CancellationToken.None, appearing: false)
            : EffectRunner.HideAsync(_chrome, EffectiveHideEffect);
        try { if (task is not null) await task; }
        catch (OperationCanceledException) { }
        _hideGlitchDone = true;
        Close(result);
    }

    /// <summary>× / Esc で閉じたときの結果（ShowDialog の戻り値）。null ならそのまま Close()。</summary>
    public Func<object?>? CancelResult { get; set; }

    /// <summary>テスト用: × ボタン。</summary>
    internal Button CloseButton { get; }
    public StackPanel TitleActions { get; } = new() { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };

    internal FrameChrome Chrome => _chrome;

    public double TitleFontSize { get => _titleText.FontSize; set => _titleText.FontSize = value; }

    public ChromeWindow()
    {
        WindowDecorations = WindowDecorations.None;
        try { Icon = AppIcon.Window; } catch (Exception ex) when (ex is IOException or InvalidOperationException or FileNotFoundException) { } // テストなど資産が無い環境
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        this[!TemplatedControl.FontFamilyProperty] = Themed.Res("FlFontFamily");
        this[!TemplatedControl.ForegroundProperty] = Themed.Res("FlText");

        var cross = new Path { Data = Geometry.Parse("M 0,0 L 10,10 M 10,0 L 0,10"), StrokeThickness = 1.5, Width = 10, Height = 10 };
        cross[!Avalonia.Controls.Shapes.Shape.StrokeProperty] = Themed.Res("FlTextMuted");
        CloseButton = new Button { Content = cross, Focusable = false, Classes = { "chrome-close" }, Margin = new Thickness(0, 0, 6, 0) };
        ToolTip.SetTip(CloseButton, Strings.Chrome_Close_Tooltip);
        CloseButton.Click += (_, _) => RequestClose();

        var titleRow = new DockPanel { Background = Brushes.Transparent, Height = TitleBarHeight };
        DockPanel.SetDock(CloseButton, Dock.Right);
        titleRow.Children.Add(CloseButton);
        DockPanel.SetDock(TitleActions, Dock.Right);
        titleRow.Children.Add(TitleActions);
        // タグ（HUD 段階 B。SPEC §3.9 H4）: 主色の縦棒 2×10 + 等幅のコード（CFG など。翻訳しない装飾）
        _tagBar = new Border { Width = 2, Height = 10, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        _tagBar[!Border.BackgroundProperty] = Themed.Res("FlAccent");
        _tagText = new TextBlock { FontSize = 9, LetterSpacing = 1, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        _tagText[!TextBlock.FontFamilyProperty] = Themed.Res("FlMonoFontFamily");
        _tagText[!TextBlock.ForegroundProperty] = Themed.Res("FlHudText");
        DockPanel.SetDock(_tagBar, Dock.Left);
        DockPanel.SetDock(_tagText, Dock.Left);
        titleRow.Children.Add(_tagBar);
        titleRow.Children.Add(_tagText);
        titleRow.Children.Add(Themed.Glow(_titleText));
        titleRow.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button) BeginMoveDrag(e);
        };
        var separator = new Border { Height = 1 };
        separator[!Border.BackgroundProperty] = Themed.Res("FlHeaderSeparator");

        var layout = new DockPanel();
        DockPanel.SetDock(titleRow, Dock.Top);
        DockPanel.SetDock(separator, Dock.Top);
        layout.Children.Add(titleRow);
        layout.Children.Add(separator);
        layout.Children.Add(_bodyHost);
        _chrome.Body = layout;
        Content = _chrome;

        // Esc / Ctrl+W（⌘W）。中のコントロール（ドロップダウン・ホットキー記録欄など）が使ったキーは除く
        KeyDown += (_, e) =>
        {
            if (e.Handled) return;
            var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if ((e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) || (e.Key == Key.W && e.KeyModifiers == command))
            {
                e.Handled = true;
                RequestClose();
            }
        };
        ActualThemeVariantChanged += (_, _) => UpdateSize(); // テーマで余白が変わる
        Closed += (_, _) => _closed = true;

        // 盤面と同じグリッチで出し入れする（2026-10-04 ユーザー要望。演出の設定 boardShow / boardHide に従う）
        _glitchLayer = new Canvas { IsVisible = false, IsHitTestVisible = false, ClipToBounds = false };
        _chrome.Overlays.Children.Add(_glitchLayer);
        if (UsesGlitch(ShowEffect)) _chrome.Opacity = 0; // 最初のフレームから見えないように（出たらグリッチで現れる）
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(PlayShowGlitch, Avalonia.Threading.DispatcherPriority.Loaded);
        Closing += OnClosingGlitch;
        UpdateTitle();
    }

    private readonly Border _tagBar;
    private readonly TextBlock _tagText;

    /// <summary>タイトル行のタグのコード（CFG / ITEM / PAGE / AUTH / SYS / CONFIRM / INFO。null なら出さない）。</summary>
    public string? TagCode
    {
        get => _tagText.Text;
        set
        {
            _tagText.Text = value;
            _tagBar.IsVisible = _tagText.IsVisible = !string.IsNullOrEmpty(value);
            _titleText.Margin = new Thickness(_tagBar.IsVisible ? 10 : 12, 0);
        }
    }

    /// <summary>枠の中身（タイトル行の下）。</summary>
    public Control? Body
    {
        get => _bodyHost.Child;
        set => _bodyHost.Child = value;
    }

    public double? BodyWidth
    {
        get => _bodyWidth;
        set { _bodyWidth = value; UpdateSize(); }
    }

    /// <summary>null なら高さは中身に合わせる（SizeToContent.Height）。</summary>
    public double? BodyHeight
    {
        get => _bodyHeight;
        set { _bodyHeight = value; UpdateSize(); }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty) UpdateTitle();
    }

    /// <summary>× / Esc / Ctrl+W。既定は CancelResult があればそれで、無ければそのまま閉じる。</summary>
    protected virtual void OnCloseRequested()
    {
        if (CancelResult is { } result) Close(result());
        else Close();
    }

    private void RequestClose()
    {
        if (!_closed) OnCloseRequested();
    }

    private void UpdateTitle()
    {
        string title = Title ?? "";
        _titleText.Text = title.StartsWith(TitlePrefix, StringComparison.Ordinal) ? title[TitlePrefix.Length..] : title;
    }

    private void UpdateSize()
    {
        double margin = _chrome.GetMargin();
        if (_bodyWidth is { } w) Width = w + margin * 2;
        if (_bodyHeight is { } h)
        {
            SizeToContent = _bodyWidth is null ? SizeToContent.Width : SizeToContent.Manual;
            Height = h + TitleBarHeight + 1 + margin * 2;
        }
        else
        {
            SizeToContent = _bodyWidth is null ? SizeToContent.WidthAndHeight : SizeToContent.Height;
        }
    }
}
