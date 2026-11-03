using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CPClipboard.App;
using CPClipboard.Core.Effects;
using CPClipboard.Core.Model;
using CPClipboard.Core.Storage;
using CoreModifiers = CPClipboard.Core.Model.KeyModifiers;

namespace CPClipboard.Desktop.Tests;

public sealed class SettingsUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CPClipboardUiTests", Guid.NewGuid().ToString("N"));
    private readonly FakePlatform _platform = new();
    private readonly SettingsHub _hub;
    private readonly DataPaths _paths;

    public SettingsUiTests()
    {
        Directory.CreateDirectory(_dir);
        _paths = new DataPaths(_dir, IsPortable: false);
        var store = new AppDataStore(_paths);
        _hub = new SettingsHub(store, store.LoadAll().Settings.Value);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private Avalonia.PixelPoint? _boardPosition;

    private SettingsWindow OpenSettings()
    {
        var w = new SettingsWindow(new SettingsContext(_hub, _platform, _paths, () => _boardPosition, _ => { }, () => { }));
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static T Find<T>(Window w) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First();

    private static void SelectTab(Window w, string header)
    {
        var tabs = Find<TabControl>(w);
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (t.Header as TextBlock)?.Text == header);
        Dispatcher.UIThread.RunJobs();
    }

    private static T RowControl<T>(Window window, string label) where T : Control =>
        window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.Trim() == label)
            .GetVisualAncestors().OfType<Grid>().First().GetVisualDescendants().OfType<T>().Single();

    [AvaloniaFact]
    public void 二回押しOFFでキーと間隔を無効にし戻すと有効になる()
    {
        var w = OpenSettings();
        try
        {
            SelectTab(w, Strings.Settings_Tab_Triggers);
            var enabled = RowControl<ToggleSwitch>(w, Strings.Settings_Triggers_DoubleTap);
            var key = RowControl<ComboBox>(w, Strings.Settings_Triggers_DoubleTapKey);
            var interval = RowControl<Slider>(w, Strings.Settings_Triggers_DoubleTapInterval);
            enabled.IsChecked = false;
            Assert.False(_hub.Current.Triggers.DoubleTap.Enabled);
            Assert.False(key.IsEffectivelyEnabled); Assert.False(interval.IsEffectivelyEnabled);
            enabled.IsChecked = true;
            Assert.True(key.IsEffectivelyEnabled); Assert.True(interval.IsEffectivelyEnabled);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void 貼り付けと復元の設定に応じて復元と遅延を無効にする()
    {
        var w = OpenSettings();
        try
        {
            SelectTab(w, Strings.Settings_Tab_Paste);
            var auto = RowControl<ToggleSwitch>(w, Strings.Settings_Paste_AutoPaste);
            var restore = RowControl<ToggleSwitch>(w, Strings.Settings_Paste_Restore);
            var delay = RowControl<Slider>(w, Strings.Settings_Paste_RestoreDelay);
            Assert.False(restore.IsChecked);
            Assert.False(delay.IsEffectivelyEnabled);
            restore.IsChecked = true;
            Assert.True(delay.IsEffectivelyEnabled);
            auto.IsChecked = false;
            Assert.False(_hub.Current.Paste.AutoPaste);
            Assert.False(restore.IsEffectivelyEnabled); Assert.False(delay.IsEffectivelyEnabled);
            auto.IsChecked = true;
            Assert.True(restore.IsEffectivelyEnabled); Assert.True(delay.IsEffectivelyEnabled);
            restore.IsChecked = false;
            Assert.False(delay.IsEffectivelyEnabled);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void マウス設定は右ボタンを選べて無修飾の左右は保存しない()
    {
        var w = OpenSettings();
        try
        {
            SelectTab(w, Strings.Settings_Tab_Triggers);
            var list = w.GetVisualDescendants().OfType<MouseTriggerList>().First();
            var combos = list.GetVisualDescendants().OfType<ComboBox>().ToArray();
            var button = Assert.Single(combos);
            Assert.Equal(CoreModifiers.Alt, Assert.Single(_hub.Current.Triggers.Mouse).Modifiers);
            button.SelectedIndex = button.Items.Cast<string>().ToList().IndexOf(EnumNames.Of(MouseButtonKind.Right));
            Assert.Equal(MouseButtonKind.Right, Assert.Single(_hub.Current.Triggers.Mouse).Button);
            var modifiers = list.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Content is string).ToArray();
            Assert.Equal(4, modifiers.Length);
            modifiers[1].IsChecked = false;
            Assert.Equal(CoreModifiers.Alt, Assert.Single(_hub.Current.Triggers.Mouse).Modifiers);
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Strings.Settings_Triggers_LeftWithoutModifier && t.IsVisible);
            modifiers[0].IsChecked = true;
            Assert.Equal(CoreModifiers.Ctrl, Assert.Single(_hub.Current.Triggers.Mouse).Modifiers);
            Assert.DoesNotContain(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Strings.Settings_Triggers_LeftWithoutModifier && t.IsVisible);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void 設定画面のすべてのタブを開いても落ちない()
    {
        var w = OpenSettings();
        var tabs = Find<TabControl>(w);
        foreach (var tab in tabs.Items.OfType<TabItem>())
        {
            tabs.SelectedItem = tab;
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(9, tabs.Items.Count);
        w.Close();
    }

    [AvaloniaFact]
    public void フックが止まっているときは記録欄へのキー入力でホットキーを記録する()
    {
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Triggers);
        var recorder = Find<HotkeyRecorder>(w);

        recorder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        w.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control | RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(("K", CoreModifiers.Ctrl | CoreModifiers.Alt), (_hub.Current.Triggers.Hotkey.Key, _hub.Current.Triggers.Hotkey.Modifiers));
    }

    [AvaloniaFact]
    public void 記録中にどの物理キーを押しても落ちない()
    {
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Triggers);
        var recorder = Find<HotkeyRecorder>(w);

        foreach (var key in Enum.GetValues<PhysicalKey>().Where(k => k is not PhysicalKey.None and not PhysicalKey.Escape))
        {
            recorder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            w.KeyPressQwerty(key, RawInputModifiers.Alt);
            // 記録できなかったキーのあとは中止して次へ（記録が終わっていれば Esc は設定画面を閉じてしまうので押さない）
            if (recorder.IsRecording) w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void フックが動いているときはフックで記録し_終わったらフックの記録を止める()
    {
        _platform.HookRunning = true;
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Triggers);
        var recorder = Find<HotkeyRecorder>(w);

        recorder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.NotNull(_platform.Capture);
        _platform.Capture!("Space", CoreModifiers.Ctrl);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(("Space", CoreModifiers.Ctrl), (_hub.Current.Triggers.Hotkey.Key, _hub.Current.Triggers.Hotkey.Modifiers));
        Assert.Null(_platform.Capture);
    }

    [AvaloniaFact]
    public void 記録を2回したあとEscで中止すると表示は最後に記録した値()
    {
        _platform.HookRunning = true;
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Triggers);
        var recorder = Find<HotkeyRecorder>(w);

        foreach (var key in new[] { "K", "J" })
        {
            recorder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            _platform.Capture!(key, CoreModifiers.Ctrl);
            Dispatcher.UIThread.RunJobs();
        }
        recorder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        _platform.Capture!("Escape", CoreModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(HotkeyRecorder.Format(new HotkeySetting { Modifiers = CoreModifiers.Ctrl, Key = "J" }), recorder.Content);
    }

    [AvaloniaFact]
    public async Task 設定画面を閉じると予約中の保存が書かれる()
    {
        var w = OpenSettings();
        _hub.Update(s => s.Appearance.Opacity = 55, SettingsChange.Opacity);
        var closed = new TaskCompletionSource();
        w.Closed += (_, _) => closed.SetResult();
        w.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var saved = new AppDataStore(_paths).LoadAll().Settings.Value;
        Assert.Equal(55, saved.Appearance.Opacity);
    }

    [AvaloniaFact]
    public void 盤面は不透明度と演出の設定を反映する()
    {
        var board = new BoardWindow();
        var appearance = new AppearanceSettings { Opacity = 70 };

        board.ApplyEffects(appearance);
        board.Render(TemplateBook.CreateEmpty(), appearance, 0);
        Assert.Equal(0.7, board.Opacity, 3);
        Assert.True(board.HasShowEffect);

        appearance.Effects[EffectCatalog.BoardShow] = EffectSpec.None;
        board.ApplyEffects(appearance);
        Assert.False(board.HasShowEffect);

        appearance.Effects.Clear();
        appearance.Animation = false;
        board.ApplyEffects(appearance);
        Assert.False(board.HasShowEffect);
    }

    [AvaloniaFact]
    public void 呼び出しタブでキーボードとマウスの表示位置を別々に設定できる()
    {
        _boardPosition = new Avalonia.PixelPoint(300, 400);
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Popup);
        var tab = Find<TabControl>(w);
        var page = (Control)tab.SelectedContent!;
        // 表示位置の 2 組（呼び出しタブには 2 回押しのキーやマウス操作の ComboBox も並ぶ）
        var combos = page.GetVisualDescendants().OfType<ComboBox>()
            .Where(c => c.ItemsSource is IEnumerable<string> items && items.Contains(EnumNames.Of(PopupPosition.Fixed))).ToList();
        var numbers = page.GetVisualDescendants().OfType<NumericUpDown>().ToList();
        var useButtons = page.GetVisualDescendants().OfType<Button>().Where(b => (b.Content as string) == Strings.Settings_Triggers_UseCurrent).ToList();
        Assert.Equal(4, numbers.Count);   // キーボード X/Y, マウス X/Y
        Assert.Equal(2, useButtons.Count);

        combos[0].SelectedIndex = 3; // キーボード = 固定座標
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(PopupPosition.Fixed, _hub.Current.Popup.Keyboard.Position);
        Assert.Equal(PopupPosition.Cursor, _hub.Current.Popup.Mouse.Position);
        Assert.True(numbers[0].IsEnabled && numbers[1].IsEnabled);
        Assert.False(numbers[2].IsEnabled || numbers[3].IsEnabled);

        useButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal((300, 400), (_hub.Current.Popup.Keyboard.X, _hub.Current.Popup.Keyboard.Y));
        Assert.Null(_hub.Current.Popup.Mouse.X);
    }

    private static Button ButtonOf(Window w, string content) =>
        w.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == content);

    [AvaloniaFact]
    public void 設定をリセットすると確認の後に既定値へ戻り_キャンセルなら戻らない()
    {
        _hub.Update(s => { s.Appearance.Opacity = 50; s.Advanced.Logging = true; }, SettingsChange.Opacity);
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Advanced);
        bool answer = false;
        w.Confirm = (_, _, _) => Task.FromResult(answer);

        ButtonOf(w, Strings.Settings_Advanced_Reset).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(50, _hub.Current.Appearance.Opacity);

        answer = true;
        ButtonOf(w, Strings.Settings_Advanced_Reset).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(80, _hub.Current.Appearance.Opacity);
        Assert.False(_hub.Current.Advanced.Logging);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<ToggleSwitch>(), t => t.IsChecked == true); // 画面も読み直す（詳細タブのログが OFF に）
        w.Close();
    }

    [AvaloniaFact]
    public void グリッチの強さは詳細設定のboardHideの直後にあり_保存され_表示も非表示もグリッチでなければ無効()
    {
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Effects);
        var rows = w.GetVisualDescendants().OfType<Expander>().Single(e => (string?)e.Header == Strings.Settings_Effects_Advanced).Content as StackPanel;
        Assert.NotNull(rows);
        bool Has(Control row, string text) => row.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(text) == true); // 詳細設定の行名は「名前（ID）」
        int hide = rows!.Children.ToList().FindIndex(r => Has(r, Strings.Effect_boardHide));
        Assert.True(hide >= 0);
        var row = rows.Children[hide + 1];
        Assert.True(Has(row, Strings.Settings_Effects_GlitchIntensity));
        var slider = row.GetLogicalDescendants().OfType<Slider>().Single();
        Assert.Equal((0, 200, 10, 100), (slider.Minimum, slider.Maximum, slider.TickFrequency, slider.Value));

        slider.Value = 150;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(150, _hub.Current.Appearance.GlitchIntensity);

        // 種類を両方 fade にすると無効、片方を glitch に戻すと有効（種類の ComboBox だけで追従する）
        ComboBox KindOf(string effect) => rows.Children.First(r => Has(r, effect)).GetLogicalDescendants().OfType<ComboBox>().First();
        string fade = EnumNames.Of(EffectKind.Fade);
        string glitch = EnumNames.Of(EffectKind.Glitch);
        void Select(ComboBox c, string name) => c.SelectedIndex = ((IEnumerable<string>)c.ItemsSource!).ToList().IndexOf(name);
        Select(KindOf(Strings.Effect_boardShow), fade);
        Assert.True(slider.IsEffectivelyEnabled); // boardHide はまだ glitch
        Select(KindOf(Strings.Effect_boardHide), fade);
        Assert.False(slider.IsEffectivelyEnabled);
        Select(KindOf(Strings.Effect_boardShow), glitch);
        Assert.True(slider.IsEffectivelyEnabled);

        // すべて既定に戻すで 100
        var resetAll = w.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == Strings.Settings_Effects_ResetAll);
        resetAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(100, _hub.Current.Appearance.GlitchIntensity);
        Assert.Equal(100, slider.Value);
        w.Close();
    }

    [AvaloniaFact]
    public void カーソルの位置の3x3はキーボードとマウス共通の1つで保存され_どちらもカーソル位置でなければ無効()
    {
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Popup);
        var picker = w.GetVisualDescendants().OfType<AnchorPicker>().Single();
        Assert.Equal(CursorAnchor.Top, picker.Value);
        Assert.True(picker.Cells[(int)CursorAnchor.Top].IsChecked);

        picker.Cells[(int)CursorAnchor.BottomRight].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CursorAnchor.BottomRight, _hub.Current.Popup.CursorAnchor);
        Assert.Equal(1, picker.Cells.Count(c => c.IsChecked == true));

        // 選択中をもう一度押しても外れない
        picker.Cells[(int)CursorAnchor.BottomRight].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(picker.Cells[(int)CursorAnchor.BottomRight].IsChecked);

        // 矢印キーで隣へ（端で止まる）
        void Press(CursorAnchor from, Key key) =>
            picker.Cells[(int)from].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = picker.Cells[(int)from] });
        Press(CursorAnchor.BottomRight, Key.Up);
        Assert.Equal(CursorAnchor.Right, _hub.Current.Popup.CursorAnchor);
        Press(CursorAnchor.Right, Key.Right);
        Assert.Equal(CursorAnchor.Right, _hub.Current.Popup.CursorAnchor);

        // キーボード・マウスの表示位置がどちらもカーソル位置以外なら無効、片方でもカーソル位置なら有効
        var combos = w.GetVisualDescendants().OfType<ComboBox>()
            .Where(c => c.ItemsSource is IEnumerable<string> items && items.Contains(EnumNames.Of(PopupPosition.ScreenCenter))).ToList();
        Assert.Equal(2, combos.Count);
        void Choose(ComboBox c, PopupPosition v) => c.SelectedIndex = ((IEnumerable<string>)c.ItemsSource!).ToList().IndexOf(EnumNames.Of(v));
        Choose(combos[0], PopupPosition.ScreenCenter);
        Assert.True(picker.IsEffectivelyEnabled); // マウスはまだカーソル位置
        Choose(combos[1], PopupPosition.Fixed);
        Assert.False(picker.IsEffectivelyEnabled);
        Choose(combos[1], PopupPosition.Cursor);
        Assert.True(picker.IsEffectivelyEnabled);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void OSの層で描くときは演出タブにVSyncとCPUの注記を出さない(bool native)
    {
        _platform.Ambient = new FakeAmbientLayers { IsSupported = native };
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Effects);
        bool vsync = w.GetVisualDescendants().OfType<ComboBox>()
            .Any(c => c.ItemsSource is IEnumerable<string> items && items.Contains(Strings.FormatSettings_Effects_Vsync_Item(2, 30)));
        bool note = w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Strings.Settings_Effects_AmbientNote);
        Assert.Equal(!native, vsync);
        Assert.Equal(!native, note);
        w.Close();
    }

    [AvaloniaFact]
    public void 走査線の行は演出タブにあり_オフで濃さと間隔が無効になり_間隔を選ぶと保存される()
    {
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Effects);
        var pitch = w.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => c.ItemsSource is IEnumerable<string> items && items.Contains(Strings.FormatCommon_PixelValue(4)));
        Assert.Equal(1, pitch.SelectedIndex); // 既定 3 px
        pitch.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, _hub.Current.Appearance.Hud.ScanlinePitch);
        _hub.Flush();
        Assert.Contains("\"scanlinePitch\": 4", File.ReadAllText(_paths.SettingsFile));

        var row = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Strings.Settings_Effects_Scanlines);
        var toggle = row.GetVisualAncestors().OfType<Grid>().First().GetVisualDescendants().OfType<ToggleSwitch>().Single();
        toggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(_hub.Current.Appearance.Hud.Scanlines);
        Assert.False(pitch.IsEnabled);
        w.Close();
    }

    [AvaloniaFact]
    public void 静止した走査線は演出タブの末尾にあり_アニメーションOFFでも有効で_すべて既定に戻すでは変わらない()
    {
        _hub.Update(s => { s.Appearance.Hud.ScanlineOpacity = 40; s.Appearance.Animation = false; }, SettingsChange.Hud);
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Appearance);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Strings.Settings_Effects_Scanlines && t.IsEffectivelyVisible);

        SelectTab(w, Strings.Settings_Tab_Effects);
        var texts = w.GetVisualDescendants().OfType<TextBlock>().ToList();
        var heading = texts.First(t => t.Text == Strings.Settings_Effects_ScanlinesHeading);
        var reset = ButtonOf(w, Strings.Settings_Effects_ResetAll);
        var panel = (StackPanel)heading.Parent!;
        Assert.True(panel.Children.IndexOf(heading) > panel.Children.IndexOf(reset)); // ボタンより後
        var row = texts.First(t => t.Text == Strings.Settings_Effects_Scanlines);
        Assert.True(row.GetVisualAncestors().OfType<Grid>().First().GetVisualDescendants().OfType<ToggleSwitch>().Single().IsEffectivelyEnabled); // アニメーション OFF でも有効

        _hub.Update(s => s.Appearance.Animation = true, SettingsChange.Effects);
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, _hub.Current.Appearance.Hud.ScanlineOpacity);
        w.Close();
    }

    [AvaloniaFact]
    public void データタブにエクスポートとインポートがある()
    {
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Data);
        Assert.NotNull(ButtonOf(w, Strings.Settings_Data_Export));
        Assert.NotNull(ButtonOf(w, Strings.Settings_Data_Import));
        w.Close();
    }

    [AvaloniaFact]
    public void 言語を今と違う値にすると今すぐ再起動が出て_保存される()
    {
        int restarts = 0;
        var w = new SettingsWindow(new SettingsContext(_hub, _platform, _paths, () => null, _ => { }, () => { }, Restart: () => restarts++));
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var language = w.GetVisualDescendants().OfType<ComboBox>().First(); // 一般タブの先頭
        Button Restart() => w.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == Strings.Common_RestartNow);
        Assert.False(Restart().IsVisible); // テストは英語で起動している（system → en）

        language.SelectedIndex = 1; // 日本語
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("ja", _hub.Current.General.Language);
        Assert.True(Restart().IsVisible);
        Restart().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, restarts);

        language.SelectedIndex = 2; // English = 今の表示言語
        Dispatcher.UIThread.RunJobs();
        Assert.False(Restart().IsVisible);

        _hub.Flush();
        Assert.Contains("\"language\": \"en\"", File.ReadAllText(_paths.SettingsFile));
        w.Close();
    }

    [AvaloniaFact]
    public void どのタブでも項目が設定画面の右端を突き抜けない_英語() => AssertNoOverflow("en");

    [AvaloniaFact]
    public void どのタブでも項目が設定画面の右端を突き抜けない_日本語() => AssertNoOverflow("ja");

    private void AssertNoOverflow(string culture)
    {
        // 長いファイル名の背景画像（2026-10-04 に背景画像の行が右へはみ出した）
        _hub.Update(s => s.Appearance.Background.Image = "background/ChatGPT Image 2026年10月4日 14_30_00 とても長いファイル名.png", SettingsChange.None);
        var saved = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(culture);
        SettingsWindow w;
        try { w = OpenSettings(); }
        finally { System.Globalization.CultureInfo.CurrentUICulture = saved; }
        var tabs = Find<TabControl>(w);
        var bad = new List<string>();
        foreach (var tab in tabs.Items.OfType<TabItem>())
        {
            tabs.SelectedItem = tab;
            Dispatcher.UIThread.RunJobs();
            foreach (var expander in w.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var scroll = w.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(v => v.IsEffectivelyVisible && v.Content is StackPanel);
            var content = tab.Content is TemplatesEditor editor ? (Control)editor : (Control)scroll!.Content!;
            double right = tab.Content is TemplatesEditor ? content.Bounds.Width + 0.5 : scroll!.Viewport.Width + 0.5;
            foreach (var c in content.GetVisualDescendants().OfType<Control>())
            {
                if (!c.IsEffectivelyVisible || c.Bounds.Width <= 0 || c is TextBlock { TextWrapping: TextWrapping.Wrap }) continue;
                if (c.TranslatePoint(new Avalonia.Point(c.Bounds.Width, 0), content) is { } p && p.X > right)
                    bad.Add($"{(tab.Header as TextBlock)?.Text}: {c.GetType().Name} {(c as ContentControl)?.Content ?? (c as TextBlock)?.Text} → {p.X:F0} > {right:F0}");
            }
        }
        w.Close();
        Assert.True(bad.Count == 0, string.Join("\n", bad.Distinct().Take(20)));
    }

    [AvaloniaFact]
    public void 配色のプリセットを選ぶと主色と副色が入り_入れ替えるとカスタムになる()
    {
        var w = OpenSettings();
        SelectTab(w, Strings.Settings_Tab_Appearance);
        var preset = w.GetVisualDescendants().OfType<ComboBox>().First(c => c.ItemsSource is List<int>);
        Assert.Equal(0, preset.SelectedIndex); // 既定はシアン

        preset.SelectedIndex = 1; // レッド
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("#FF2E4D", "#37EBF3"), (_hub.Current.Appearance.Colors.Primary, _hub.Current.Appearance.Colors.Secondary));

        var swap = w.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == "⇄");
        swap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("#37EBF3", "#FF2E4D"), (_hub.Current.Appearance.Colors.Primary, _hub.Current.Appearance.Colors.Secondary));
        Assert.Equal(CPClipboard.Core.Theming.ColorPresets.All.Count, preset.SelectedIndex); // カスタム
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("ja")]
    public void 設定画面のタブはアウトラインで_9つとも1行に収まる(string culture)
    {
        var saved = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(culture);
        SettingsWindow w;
        string[] expected;
        try
        {
            expected = new[]
            {
                Strings.Settings_Tab_General, Strings.Settings_Tab_Appearance,
                Strings.Settings_Tab_Effects, Strings.Settings_Tab_Popup,
                Strings.Settings_Tab_Triggers, Strings.Settings_Tab_Paste,
                Strings.Settings_Tab_History, Strings.Settings_Tab_Data, Strings.Settings_Tab_Advanced,
            };
            w = OpenSettings();
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = saved; }
        var tabs = w.GetVisualDescendants().OfType<TabItem>().ToList();
        Assert.Equal(9, tabs.Count);
        Assert.Equal(expected, tabs.Select(t => ((TextBlock)t.Header!).Text));
        Assert.All(tabs, t => Assert.Equal(new Avalonia.Thickness(1), t.BorderThickness));
        double top = tabs[0].Bounds.Y;
        Assert.All(tabs, t => Assert.Equal(top, t.Bounds.Y)); // 折り返していない
        var last = tabs[^1];
        var right = last.TranslatePoint(new Avalonia.Point(last.Bounds.Width, 0), w)!.Value.X;
        Assert.True(right <= w.Bounds.Width, $"{culture}: {right} > {w.Bounds.Width}");
        w.Close();
    }
}
