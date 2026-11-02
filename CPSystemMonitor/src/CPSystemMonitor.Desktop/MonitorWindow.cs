using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CPSystemMonitor.Core.Hud;
using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Core.Monitoring;
namespace CPSystemMonitor.App;
internal sealed class MonitorWindow : ChromeWindow
{
    private readonly SettingsHub _hub;
    private readonly StackPanel _cards = new() { Spacing = 6, Margin = new Thickness(10, 6) };
    private readonly List<MetricCard> _metrics = [];
    private readonly TextBlock _clock = MetricCard.Text("", 11, "FlAccent");
    private readonly TextBlock _historyText = MetricCard.Text("", 10, "FlTextMuted");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Border _footer;
    private readonly ScrollViewer _scroll;
    private readonly SampleHistory _history;
    private MonitorSample? _last;
    private readonly Avalonia.Controls.Primitives.ToggleButton _pin = new() { Content = "PIN", FontSize = 10, Classes = { "chrome-close" } };
    private readonly Canvas _transitionLayer = new() { IsHitTestVisible = false, IsVisible = false };
    private readonly GlitchPlayer _transitionPlayer = new();
    private CancellationTokenSource? _transition;
    public Func<bool> ReducedMotion { get; set; } = () => false;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action? HideRequested;
    public bool AllowClose { get; set; }
    public bool HasOpenMenu => ContextMenu?.IsOpen == true || _metrics.Any(c => c.ContextMenu?.IsOpen == true);
    public MonitorWindow(SettingsHub hub, SampleHistory history)
    {
        _hub = hub; _history = history;
        Title = AppInfo.ProductName; TitleFontSize = 18; ShowInTaskbar = false; TagCode = "MON";
        ToolTip.SetTip(_pin, MonitorText.T("Always on top", "最前面に表示"));
        _pin.Click += (_, _) => _hub.Update(s => s.Monitor.AlwaysOnTop = _pin.IsChecked == true, SettingsChange.BoardLayout);
        var settingsButton = new Button { Content = "⚙", FontSize = 16, Classes = { "chrome-close" } };
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        ToolTip.SetTip(settingsButton, Strings.Common_Settings);
        TitleActions.Children.Add(_pin); TitleActions.Children.Add(settingsButton);
        Chrome.Overlays.Children.Add(_transitionLayer);
        Opened += (_, _) => Dispatcher.UIThread.Post(() => _ = AnimateAsync(true), DispatcherPriority.Loaded);
        _scroll = new ScrollViewer { Content = _cards, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var footerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _clock, _historyText } };
        Grid.SetColumn(_historyText, 1);
        _footer = new Border { Padding = new Thickness(12, 7), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerGrid };
        _footer[!Border.BorderBrushProperty] = Themed.Res("FlHeaderSeparator");
        var root = new DockPanel(); DockPanel.SetDock(_footer, Dock.Bottom); root.Children.Add(_footer); root.Children.Add(_scroll); Body = root;
        _timer.Tick += (_, _) => UpdateClock();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty) { if (IsVisible) { UpdateClock(); _timer.Start(); } else _timer.Stop(); }
        };
        PositionChanged += (_, _) =>
        {
            if (!IsVisible) return;
            _hub.Update(s => { s.Monitor.X = Position.X; s.Monitor.Y = Position.Y; s.Popup.RememberLastPosition(Position.X, Position.Y); }, SettingsChange.None);
        };
        Closing += (_, e) =>
        {
            if (!AllowClose && e.CloseReason is not (WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)) { e.Cancel = true; HideRequested?.Invoke(); }
        };
        Closed += (_, _) => { _transition?.Cancel(); _timer.Stop(); _hub.Changed -= OnSettings; };
        _hub.Changed += OnSettings;
        Rebuild();
    }
    protected override void OnCloseRequested() { if (AllowClose) base.OnCloseRequested(); else HideRequested?.Invoke(); }
    private void OnSettings(SettingsChange change)
    {
        if ((change & (SettingsChange.BoardLayout | SettingsChange.Hud | SettingsChange.Colors | SettingsChange.Opacity)) != 0) Rebuild();
    }
    private void Rebuild()
    {
        var settings = _hub.Current; var monitor = settings.Monitor;
        bool compact = monitor.Mode == MonitorMode.Compact;
        BodyWidth = compact ? 440 : 820;
        _pin.IsChecked = monitor.AlwaysOnTop;
        Topmost = monitor.AlwaysOnTop; Opacity = settings.Appearance.Opacity / 100d;
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        double scale = OperatingSystem.IsMacOS() ? 1 : screen?.Scaling ?? 1;
        _scroll.MaxHeight = Math.Max(160, (screen?.WorkingArea.Height ?? 1000) / scale - 170);
        _cards.Children.Clear(); _metrics.Clear();
        foreach (var section in Enum.GetValues<MonitorSection>())
        {
            if (!monitor.For(section).Visible) continue;
            var card = new MetricCard(section, compact, monitor.For(section), monitor.PerCore);
            card.ContextMenu = SectionMenu(section);
            _metrics.Add(card); _cards.Children.Add(card);
        }
        ContextMenu = MainMenu();
        UpdateClock();
        if (_last is { } sample) Update(sample);
    }
    public void Update(MonitorSample sample)
    {
        _last = sample;
        foreach (var card in _metrics) card.Update(sample, _history.Samples, _hub.Current.Monitor.HistorySeconds);
        UpdateClock();
    }
    private void UpdateClock()
    {
        var s = _hub.Current;
        _footer.IsVisible = s.Appearance.Hud.StatusBar;
        _clock.Text = StatusLine.Format(0, null, 0, DateTime.Now, s.Appearance.Hud.Clock, s.Appearance.Hud.Date).Clock ?? "";
        _historyText.Text = "HISTORY " + s.Monitor.HistorySeconds + "s" + (s.Monitor.AlwaysOnTop ? "  / PIN" : "");
    }
    public void CancelTransition()
    {
        _transition?.Cancel();
        GlitchPlayer.Finish(Chrome.Frame, _transitionLayer);
        EffectRunner.Reset(Chrome, true);
    }
    public async Task<bool> AnimateAsync(bool appearing)
    {
        CancelTransition();
        var cts = _transition = new CancellationTokenSource();
        var appearance = _hub.Current.Appearance;
        var spec = ReducedMotion() ? Core.Effects.EffectSpec.None : Core.Effects.EffectCatalog.Resolve(appearance,
            appearing ? Core.Effects.EffectCatalog.BoardShow : Core.Effects.EffectCatalog.BoardHide);
        try
        {
            if (spec.Kind == Core.Effects.EffectKind.Glitch && spec.IsActive)
            {
                IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Cyan;
                var task = _transitionPlayer.Start(Chrome, Chrome.Frame, _transitionLayer, spec, Core.Effects.GlitchLook.Scaled(appearance.GlitchIntensity), Brush("FlAccent"), Brush("FlAccent2"), cts.Token, appearing);
                if (task is not null) await task;
            }
            else if (appearing) await EffectRunner.ShowAsync(Chrome, spec, cts.Token);
            else await EffectRunner.HideAsync(Chrome, spec, cts.Token);
        }
        catch (OperationCanceledException) { }
        return !cts.IsCancellationRequested;
    }
    private MenuItem Check(string title, bool value, Action toggle)
    {
        var item = new MenuItem { Header = title, ToggleType = MenuItemToggleType.CheckBox, IsChecked = value };
        item.Click += (_, _) => toggle(); return item;
    }
    private MenuItem ActionItem(string title, Action action)
    {
        var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); return item;
    }
    private ContextMenu MainMenu()
    {
        var s = _hub.Current.Monitor;
        var mode = new MenuItem { Header = MonitorText.T("View mode", "表示モード") };
        foreach (var m in Enum.GetValues<MonitorMode>())
        {
            var item = new MenuItem { Header = m == MonitorMode.Standard ? MonitorText.T("Standard", "標準") : MonitorText.T("Compact", "コンパクト"), ToggleType = MenuItemToggleType.Radio, IsChecked = s.Mode == m };
            item.Click += (_, _) => _hub.Update(a => a.Monitor.Mode = m, SettingsChange.BoardLayout); mode.Items.Add(item);
        }
        var menu = new ContextMenu(); menu.Items.Add(mode); menu.Items.Add(new Separator());
        foreach (var section in Enum.GetValues<MonitorSection>())
        {
            var item = new MenuItem { Header = MonitorText.Section(section), ItemsSource = SectionItems(section) };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Check(MonitorText.T("Always on top", "最前面に表示"), s.AlwaysOnTop, () => _hub.Update(a => a.Monitor.AlwaysOnTop = !a.Monitor.AlwaysOnTop, SettingsChange.BoardLayout)));
        menu.Items.Add(ActionItem(Strings.Common_Settings, () => SettingsRequested?.Invoke()));
        menu.Items.Add(ActionItem(MonitorText.T("Hide", "格納"), () => HideRequested?.Invoke()));
        menu.Items.Add(ActionItem(Loc.Os(Strings.Tray_Quit_Win, Strings.Tray_Quit_Mac), () => ExitRequested?.Invoke()));
        return menu;
    }
    private ContextMenu SectionMenu(MonitorSection section)
    {
        var menu = new ContextMenu(); foreach (var item in SectionItems(section)) menu.Items.Add(item);
        menu.Items.Add(new Separator()); menu.Items.Add(new MenuItem { Header = MonitorText.T("All settings", "全体メニュー"), ItemsSource = GlobalItems() });
        return menu;
    }
    private object[] GlobalItems()
    {
        var menu = MainMenu(); var items = menu.Items.Cast<object>().ToArray(); menu.Items.Clear(); return items;
    }
    private object[] SectionItems(MonitorSection section)
    {
        var s = _hub.Current.Monitor.For(section);
        var items = new List<object>
        {
            Check(MonitorText.T("Show ", "表示：") + MonitorText.Section(section), s.Visible, () => _hub.Update(a => a.Monitor.For(section).Visible = !s.Visible, SettingsChange.BoardLayout)),
            Check(MonitorText.T("Top app", "最大負荷アプリ"), s.TopApp, () => _hub.Update(a => a.Monitor.For(section).TopApp = !s.TopApp, SettingsChange.BoardLayout)),
            Check(MonitorText.T("History graph", "履歴グラフ"), s.History, () => _hub.Update(a => a.Monitor.For(section).History = !s.History, SettingsChange.BoardLayout)),
        };
        if (section == MonitorSection.Cpu) items.Add(Check(MonitorText.T("Per-core meters", "コア別使用率"), _hub.Current.Monitor.PerCore, () => _hub.Update(a => a.Monitor.PerCore = !a.Monitor.PerCore, SettingsChange.BoardLayout)));
        return items.ToArray();
    }
}
