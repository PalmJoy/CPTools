using CoreModifiers = CPClipboard.Core.Model.KeyModifiers;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CPClipboard.Core.Effects;
using CPClipboard.Core.Model;
using CPClipboard.Core.Storage;
using CPClipboard.Core.Theming;
using CPClipboard.Platform;

namespace CPClipboard.App;

/// <summary>
/// 設定画面（SPEC §7、項目は spec/SETTINGS.md）。変更はその場で SettingsHub.Update → 盤面などへ即時反映、保存はまとめて。
/// ユーザーが選んだときだけ開く。閉じたら破棄し、次に開くときに作り直す（状態はすべて SettingsHub にある）。
/// M5 ステップ 2: 一般 / 表示（演出の調整を含む）/ ポップアップ / トリガー / データ / 詳細。常駐タブはステップ 3。
/// </summary>
internal sealed class SettingsWindow : ChromeWindow
{
    private const double LabelColumnWidth = 200;
    private readonly SettingsHub _hub;
    private readonly BoardWindowAppearance _boardAppearance;
    private readonly List<HotkeyRecorder> _recorders = [];
    private readonly SettingsContext _ctx;
    private readonly Func<PixelPoint?> _boardPosition;
    private readonly Action<string> _openFolder;
    private readonly List<Action> _refreshers = new();
    private bool _refreshing; // コントロールへ値を書き戻している最中は Update しない

    /// <summary>確認ダイアログ（title, message, ok ボタン名）。UI テストで差し替える。</summary>
    internal Func<string, string, string, Task<bool>> Confirm { get; set; }

    public SettingsWindow(SettingsContext ctx)
    {
        _ctx = ctx;
        _hub = ctx.Hub;
        _boardPosition = ctx.BoardPosition;
        _openFolder = ctx.OpenFolder;
        Confirm = (title, message, ok) => Dialogs.ConfirmAsync(this, title, message, ok);
        var hub = _hub;

        Title = AppInfo.TitlePrefix + Strings.Settings_Title;
        TagCode = "CFG";
        BodyWidth = 700;
        BodyHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = OperatingSystem.IsWindows(); // Win は通常のウィンドウとして Alt+Tab / タスクバーから戻れるように（SPEC §7）

        var tabs = new TabControl
        {
            Items =
            {
                Tab(Strings.Settings_Tab_General, GeneralTab()),
                Tab(Strings.Settings_Tab_Appearance, AppearanceTab()),
                Tab(Strings.Settings_Tab_Effects, EffectsTab()),
                Tab(Strings.Settings_Tab_Popup, PopupTab()),
                Tab(Strings.Settings_Tab_Triggers, TriggersTab()),
                Tab(Strings.Settings_Tab_Paste, PasteTab()),
                Tab(Strings.Settings_Tab_History, HistoryTab()),
                Tab(Strings.Settings_Tab_Data, DataTab()),
                Tab(Strings.Settings_Tab_Advanced, AdvancedTab()),
            },
        };

        // 閉じるのはタイトル行の × / Esc / Ctrl+W（⌘W）。下部の「閉じる」ボタンは廃止（SPEC §3.8）
        var root = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (hub.IsReadOnly)
        {
            var banner = new Border
            {
                [!Border.BackgroundProperty] = Themed.Res("FlWarning"),
                Padding = new Thickness(12, 6),
                Child = new TextBlock
                {
                    Text = Strings.Settings_ReadOnlyBanner,
                    [!TextBlock.ForegroundProperty] = Themed.Res("FlOnWarning"),
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            DockPanel.SetDock(banner, Dock.Top);
            root.Children.Add(banner);
        }
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs)) foreach (var r in _recorders) r.Cancel(); };
        Deactivated += (_, _) => { foreach (var r in _recorders) r.Cancel(); };
        root.Children.Add(tabs);
        Body = root;
        _boardAppearance = new BoardWindowAppearance(this, hub.Current.Appearance, ctx.Paths, ctx.Platform, fixedOpacity: 1);

        _hub.Changed += OnHubChanged;
        Closed += (_, _) =>
        {
            foreach (var r in _recorders) r.Cancel();
            _hub.Changed -= OnHubChanged;
            _hub.Flush();
        };
        RefreshAll();
    }

    private void OnHubChanged(SettingsChange change)
    {
        _boardAppearance.ApplyLiveSettings(_hub.Current.Appearance);
        // リセット・インポート後、テーマ変更後（演出の既定値が変わるので「演出の調整…」を読み直す）
        if (change == SettingsChange.All || change.HasFlag(SettingsChange.Colors)) RefreshAll();
    }

    private void RefreshAll()
    {
        _refreshing = true;
        try { foreach (var r in _refreshers) r(); }
        finally { _refreshing = false; }
    }

    private void Set(Action<AppSettings> mutate, SettingsChange change)
    {
        if (_refreshing) return;
        _hub.Update(mutate, change);
    }

    // ---------------- タブ ----------------

    private Control AppearanceTab()
    {
        var p = Panel();
        ColorRows(p);
        p.Children.Add(Row(Strings.Settings_Appearance_Opacity, PercentSlider(30, 100, 5,
            s => s.Appearance.Opacity, (s, v) => s.Appearance.Opacity = v, SettingsChange.Opacity)));
        BackgroundRows(p);
        // 盤面の大きさと行（SPEC §3.1）
        p.Children.Add(Row(Strings.Settings_Appearance_BoardWidth, ValueSlider(280, 800, 20, "px",
            s => s.Appearance.BoardWidth, (s, v) => s.Appearance.BoardWidth = v, SettingsChange.BoardLayout)));
        p.Children.Add(Row(Strings.Settings_Appearance_MaxRows, Number(4, 30,
            s => s.Appearance.MaxRows, (s, v) => s.Appearance.MaxRows = v, SettingsChange.BoardLayout)));
        p.Children.Add(Row(Strings.Settings_Appearance_FontSize, Number(11, 20,
            s => s.Appearance.FontSize, (s, v) => s.Appearance.FontSize = v, SettingsChange.BoardLayout)));
        p.Children.Add(Row(Strings.Settings_Appearance_Preview, Toggle(
            s => s.Board.Preview, (s, v) => s.Board.Preview = v, SettingsChange.Board), Strings.Settings_Appearance_Preview_Note));
        p.Children.Add(Row(Strings.Settings_Appearance_WheelGroups, Toggle(
            s => s.Board.WheelSwitchesGroup, (s, v) => s.Board.WheelSwitchesGroup = v, SettingsChange.Board),
            Strings.Settings_Appearance_WheelGroups_Note));
        // HUD（SPEC §3.9）: ステータス行と時刻
        var statusBar = Toggle(s => s.Appearance.Hud.StatusBar, (s, v) => s.Appearance.Hud.StatusBar = v, SettingsChange.Hud);
        p.Children.Add(Row(Strings.Settings_Appearance_Hud_StatusBar, statusBar, Strings.Settings_Appearance_Hud_StatusBar_Note));
        var clock = Combo(EnumNames.Options(ClockMode.Off, ClockMode.Minutes, ClockMode.Seconds),
            s => s.Appearance.Hud.Clock, (s, v) => s.Appearance.Hud.Clock = v, SettingsChange.Hud);
        p.Children.Add(Row(Sub(Strings.Settings_Appearance_Hud_Clock), clock, Strings.Settings_Appearance_Hud_Clock_Note));
        var date = Toggle(s => s.Appearance.Hud.Date, (s, v) => s.Appearance.Hud.Date = v, SettingsChange.Hud);
        p.Children.Add(Row(Sub(Strings.Settings_Appearance_Hud_Date), date));
        var grid = Toggle(s => s.Appearance.Hud.Grid, (s, v) => s.Appearance.Hud.Grid = v, SettingsChange.Hud);
        p.Children.Add(Row(Strings.Settings_Appearance_Hud_Grid, grid, Strings.Settings_Appearance_Hud_Grid_Note));
        var gridOpacity = ValueSlider(0, 10, 1, "%", s => s.Appearance.Hud.GridOpacity, (s, v) => s.Appearance.Hud.GridOpacity = v, SettingsChange.Hud);
        p.Children.Add(Row(Sub(Strings.Settings_Appearance_Hud_GridOpacity), gridOpacity));
        void UpdateGridEnabled() => gridOpacity.IsEnabled = _hub.Current.Appearance.Hud.Grid;
        grid.IsCheckedChanged += (_, _) => UpdateGridEnabled();
        _refreshers.Add(UpdateGridEnabled);
        void UpdateClockEnabled() => clock.IsEnabled = date.IsEnabled = _hub.Current.Appearance.Hud.StatusBar;
        statusBar.IsCheckedChanged += (_, _) => UpdateClockEnabled();
        _refreshers.Add(UpdateClockEnabled);
        return Scroll(p);
    }

    // ---------------- 背景画像（SPEC §3.7） ----------------

    private static readonly FilePickerFileType ImageType = new(Strings.Common_ImageFiles)
    {
        Patterns = BackgroundImageStore.Extensions.Select(e => "*" + e).ToArray(),
        AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "com.microsoft.bmp", "com.compuserve.gif", "org.webmproject.webp"],
        MimeTypes = ["image/png", "image/jpeg", "image/bmp", "image/gif", "image/webp"],
    };

    private void BackgroundRows(StackPanel p)
    {
        var previewImage = new Image { Width = 120, Height = 68, Stretch = Stretch.Uniform };
        var none = new TextBlock { Text = Strings.Common_None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var hint = new TextBlock { Text = Strings.Settings_Appearance_Background_DropHint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        var preview = new Border
        {
            Width = 120, Height = 68, BorderThickness = new Thickness(1),
            // 透明でも塗っておく（null だと「なし」の文字以外に当たり判定が無く、ドロップが素通りする）
            Background = Brushes.Transparent,
            [!Border.BorderBrushProperty] = Themed.Res("FlEmptySlotBorder"),
            Child = new Grid { Children = { Themed.Note(none), Themed.Note(hint), previewImage } },
        };

        // ドロップで設定（SPEC §3.7「ドロップで設定」）。受け口はサムネイルだけ
        bool highlighted = false;
        _setDropHighlight = on =>
        {
            if (on == highlighted) return;
            highlighted = on;
            preview[!Border.BorderBrushProperty] = Themed.Res(on ? "FlDropBorder" : "FlEmptySlotBorder");
            if (on) preview[!Border.BackgroundProperty] = Themed.Res("FlDropBackground");
            else preview.Background = Brushes.Transparent;
            bool noImage = _hub.Current.Appearance.Background.Image is null;
            hint.IsVisible = on && noImage;
            none.IsVisible = !on && previewImage.Source is null;
        };
        DragDrop.SetAllowDrop(preview, true);
        preview.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            if (!e.DataTransfer.Contains(DataFormat.File)) { e.DragEffects = DragDropEffects.None; SetDropHighlight(false); return; }
            var paths = DroppedLocalPaths(e); // 読めなければ null（ドロップ時に判定する）
            bool ok = paths is null || BackgroundImageStore.FirstSupported(paths) is not null;
            e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            SetDropHighlight(ok);
        });
        preview.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropHighlight(false));
        preview.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            SetDropHighlight(false);
            e.Handled = true;
            _ = ApplyDroppedPathsAsync(DroppedLocalPaths(e) ?? []);
        });
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180 };
        var choose = new Button { Content = Strings.Settings_Appearance_Background_Choose };
        choose.Click += (_, _) => _ = ChooseBackgroundAsync();
        var clear = new Button { Content = Strings.Settings_Appearance_Background_Clear };
        clear.Click += (_, _) =>
        {
            BackgroundImageStore.Remove(_ctx.Paths, _hub.Current.Appearance.Background.Image);
            _hub.Update(s => s.Appearance.Background.Image = null, SettingsChange.Background);
            RefreshAll();
        };
        var error = Themed.Foreground(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(LabelColumnWidth, 0, 0, 0), IsVisible = false }, "FlError");

        var fit = Combo(
            EnumNames.Options(BackgroundFit.Fill, BackgroundFit.Fit, BackgroundFit.Stretch, BackgroundFit.Tile, BackgroundFit.Center),
            s => s.Appearance.Background.Fit, (s, v) => s.Appearance.Background.Fit = v, SettingsChange.Background);
        var overlay = PercentSlider(0, 90, 5,
            s => s.Appearance.Background.Overlay, (s, v) => s.Appearance.Background.Overlay = v, SettingsChange.Background);
        var imageOpacity = PercentSlider(10, 100, 5,
            s => s.Appearance.Background.ImageOpacity, (s, v) => s.Appearance.Background.ImageOpacity = v, SettingsChange.Background);

        void RefreshBackground()
        {
            string? image = _hub.Current.Appearance.Background.Image;
            var bitmap = image is null ? null : _ctx.BackgroundBitmap?.Invoke();
            previewImage.Source = bitmap;
            none.IsVisible = bitmap is null;
            name.Text = image is null ? "" : Path.GetFileName(image.Replace('\\', '/'));
            ToolTip.SetTip(name, image);
            clear.IsEnabled = image is not null;
            fit.IsEnabled = image is not null;
            overlay.IsEnabled = image is not null;
            imageOpacity.IsEnabled = image is not null;
            var status = image is null ? BackgroundLoadStatus.None : _ctx.BackgroundStatus?.Invoke() ?? BackgroundLoadStatus.None;
            error.Text = status switch
            {
                BackgroundLoadStatus.NotFound => Strings.FormatSettings_Appearance_BackgroundMissing(image),
                BackgroundLoadStatus.Failed => Strings.FormatSettings_Appearance_BackgroundUnreadable(image),
                _ => "",
            };
            error.IsVisible = error.Text.Length > 0;
        }
        _refreshers.Add(RefreshBackground);
        _hub.BackgroundLoaded += RefreshBackground;
        Closed += (_, _) => _hub.BackgroundLoaded -= RefreshBackground;

        // プレビューの右に「ファイル名」と「選択… / クリア」を 2 段で（1 行に並べると右へはみ出した。2026-10-04）
        p.Children.Add(Row(Strings.Settings_Appearance_Background, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                preview,
                new StackPanel
                {
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { name, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { choose, clear } } },
                },
            },
        }, Strings.Settings_Appearance_Background_Note));
        p.Children.Add(error);
        p.Children.Add(Row(Sub(Strings.Settings_Appearance_BackgroundFit), fit));
        p.Children.Add(Row(Sub(Strings.Settings_Appearance_BackgroundOverlay), overlay, Strings.Settings_Appearance_BackgroundOverlay_Note));
        p.Children.Add(Row(Sub(Strings.Settings_Appearance_BackgroundOpacity), imageOpacity, Strings.Settings_Appearance_BackgroundOpacity_Note));
    }

    private async Task ChooseBackgroundAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Settings_Appearance_BackgroundPicker_Title,
            AllowMultiple = false,
            FileTypeFilter = [ImageType],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await ApplyBackgroundFileAsync(path);
    }

    /// <summary>選んだ画像を background/ にコピーして設定する（UI テストはここから呼ぶ）。</summary>
    internal Task ApplyBackgroundFileAsync(string path)
    {
        try
        {
            string value = BackgroundImageStore.Import(_ctx.Paths, path);
            _hub.Update(s => s.Appearance.Background.Image = value, SettingsChange.Background);
            RefreshAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toast.Show(Strings.FormatSettings_Appearance_BackgroundCopyFailed(ex.Message));
        }
        return Task.CompletedTask;
    }

    private Action<bool>? _setDropHighlight;

    /// <summary>サムネイルのドロップ先の強調（ドラッグ中）。UI テストからも呼ぶ。</summary>
    internal void SetDropHighlight(bool on) => _setDropHighlight?.Invoke(on);

    /// <summary>サムネイルに落とされたパスから背景画像を設定する（SPEC §3.7「ドロップで設定」）。対応ファイルが無ければトースト。</summary>
    internal Task ApplyDroppedPathsAsync(IReadOnlyList<string> paths)
    {
        if (BackgroundImageStore.FirstSupported(paths) is { } path) return ApplyBackgroundFileAsync(path);
        Toast.Show(Strings.Toast_BackgroundDropUnsupported);
        return Task.CompletedTask;
    }

    /// <summary>ドロップされたファイルのローカルパス。ファイル一覧が読めなければ null。</summary>
    private static List<string>? DroppedLocalPaths(DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { } files) return null;
        var list = new List<string>();
        foreach (var f in files)
            if (f.TryGetLocalPath() is { } p) list.Add(p);
        return list;
    }

    /// <summary>表示位置と閉じる条件。</summary>
    private Control PopupTab()
    {
        var p = Panel();
        PlacementGroup(p, Strings.Settings_Triggers_KeyboardPlacement, s => s.Popup.Keyboard);
        PlacementGroup(p, Strings.Settings_Triggers_MousePlacement, s => s.Popup.Mouse);

        // 盤面のどの点をカーソルに合わせるか（3 × 3。キーボード・マウス共通。どちらかがカーソル位置のときだけ有効。SPEC §3.3）
        var anchor = new AnchorPicker();
        anchor.ValueChanged += v => Set(s => s.Popup.CursorAnchor = v, SettingsChange.Popup);
        void UpdateAnchorEnabled() => anchor.IsEnabled = _hub.Current.Popup.Keyboard.Position == PopupPosition.Cursor
            || _hub.Current.Popup.Mouse.Position == PopupPosition.Cursor;
        _refreshers.Add(() => { anchor.Value = _hub.Current.Popup.CursorAnchor; UpdateAnchorEnabled(); });
        AnchorPositionChanged += UpdateAnchorEnabled;
        p.Children.Add(Row(Strings.Settings_Triggers_CursorAnchor, anchor, Strings.Settings_Triggers_CursorAnchor_Note));
        p.Children.Add(Themed.Note(new TextBlock
        {
            Text = Strings.FormatSettings_Triggers_PositionNote(Loc.Os(Strings.Common_TrayIcon_Win, Strings.Common_TrayIcon_Mac)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        }));

        p.Children.Add(Row(Strings.Settings_Triggers_CloseOnOutside, Toggle(
            s => s.Popup.CloseOnOutsideClick, (s, v) => s.Popup.CloseOnOutsideClick = v, SettingsChange.Popup)));
        p.Children.Add(Row(Strings.Settings_Triggers_CloseOnRetrigger, Toggle(
            s => s.Popup.ToggleOnTrigger, (s, v) => s.Popup.ToggleOnTrigger = v, SettingsChange.Popup)));
        var leave = Toggle(s => s.Popup.CloseOnMouseLeave, (s, v) => s.Popup.CloseOnMouseLeave = v, SettingsChange.Popup);
        p.Children.Add(Row(Strings.Settings_Triggers_CloseOnLeave, leave, Strings.Settings_Triggers_CloseOnLeave_Note));
        var distance = ValueSlider(50, 1000, 10, "px", s => s.Popup.MouseLeaveDistance, (s, v) => s.Popup.MouseLeaveDistance = v, SettingsChange.Popup);
        p.Children.Add(Row(Strings.Settings_Triggers_LeaveDistance, distance));
        void UpdateLeave() => distance.IsEnabled = _hub.Current.Popup.CloseOnMouseLeave;
        leave.IsCheckedChanged += (_, _) => UpdateLeave();
        _refreshers.Add(UpdateLeave);
        return Scroll(p);
    }

    // ---------------- 配色（主色・副色。SPEC §3.6「配色」） ----------------

    private void ColorRows(StackPanel p)
    {
        // 「配色」: プリセット 5 組 + カスタム（2 色がどのプリセットとも一致しないとき。選べない項目）
        var presets = ColorPresets.All;
        Control Item(int index)
        {
            Border Swatch(RgbColor c) => new() { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) };
            if (index >= presets.Count) return new TextBlock { Text = Strings.Enum_AccentPreset_Custom, VerticalAlignment = VerticalAlignment.Center };
            var (id, primary, secondary) = presets[index];
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { Swatch(primary), Swatch(secondary), new TextBlock { Text = EnumNames.Of(id), VerticalAlignment = VerticalAlignment.Center } },
            };
        }
        // 項目はテンプレートで作る（コントロールを直接 ItemsSource に入れると選択欄と一覧で取り合いになる）
        var preset = new ComboBox
        {
            MinWidth = 200,
            ItemsSource = Enumerable.Range(0, presets.Count + 1).ToList(),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((i, _) => Item(i)),
        };
        preset.ContainerPrepared += (_, e) => { if (e.Index == presets.Count) e.Container.IsEnabled = false; };

        var primaryPicker = Picker();
        var secondaryPicker = Picker();
        var primaryHex = HexText();
        var secondaryHex = HexText();
        var swap = new Button { Content = "⇄", Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center }; // i18n:ignore 記号
        ToolTip.SetTip(swap, Strings.Settings_Appearance_Swap_Tooltip);

        bool updating = false;
        void Refresh()
        {
            updating = true;
            try
            {
                var c = _hub.Current.Appearance.Colors;
                var p1 = RgbColor.TryParse(c.Primary) ?? default;
                var p2 = RgbColor.TryParse(c.Secondary) ?? default;
                primaryPicker.Color = Color.FromRgb(p1.R, p1.G, p1.B);
                secondaryPicker.Color = Color.FromRgb(p2.R, p2.G, p2.B);
                primaryHex.Text = c.Primary;
                secondaryHex.Text = c.Secondary;
                var match = ColorPresets.Match(p1, p2);
                preset.SelectedIndex = match is { } m ? presets.Select(x => x.Id).ToList().IndexOf(m) : presets.Count;
            }
            finally { updating = false; }
        }
        _refreshers.Add(Refresh);

        void SetColors(string primary, string secondary)
        {
            if (updating || _refreshing) return;
            _hub.Update(s => { s.Appearance.Colors.Primary = primary; s.Appearance.Colors.Secondary = secondary; }, SettingsChange.Colors);
            // Normalize が暗すぎる色を明るく書き戻すことがあるので読み直す
            Refresh();
        }
        preset.SelectionChanged += (_, _) =>
        {
            if (updating || preset.SelectedIndex < 0 || preset.SelectedIndex >= presets.Count) return;
            var (_, a, b) = presets[preset.SelectedIndex];
            SetColors(a.ToHex(), b.ToHex());
        };
        // スペクトルをドラッグしている間は毎フレーム来るので、反映は 1 フレームに 1 回にまとめる（辞書の作り直しが重いため）
        bool pending = false;
        void Schedule()
        {
            if (updating || pending) return;
            pending = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                pending = false;
                SetColors(Hex(primaryPicker.Color), Hex(secondaryPicker.Color));
            }, Avalonia.Threading.DispatcherPriority.Render);
        }
        primaryPicker.ColorChanged += (_, _) => Schedule();
        secondaryPicker.ColorChanged += (_, _) => Schedule();
        swap.Click += (_, _) =>
        {
            var c = _hub.Current.Appearance.Colors;
            SetColors(c.Secondary, c.Primary);
        };

        p.Children.Add(Row(Strings.Settings_Appearance_Colors, preset));
        p.Children.Add(Row(Sub(Strings.Settings_Appearance_Primary), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { primaryPicker, primaryHex } }));
        p.Children.Add(Row(Sub(Strings.Settings_Appearance_Secondary), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { secondaryPicker, secondaryHex, swap } }));

        static ColorPicker Picker() => new() { IsAlphaEnabled = false, IsAlphaVisible = false, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock HexText()
        {
            var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 70 };
            t[!TextBlock.FontFamilyProperty] = Themed.Res("FlMonoFontFamily");
            return t;
        }
        static string Hex(Color c) => new RgbColor(c.R, c.G, c.B).ToHex();
    }

    /// <summary>表示位置 1 組（表示位置 + 固定座標 + 「今の盤面の位置を使う」）。キーボード用とマウス用で 2 回使う（SPEC §3.3）。</summary>
    /// <summary>ポップアップタブの「表示位置」が変わった（共通の 3 × 3 の有効・無効を追従させる）。</summary>
    private event Action? AnchorPositionChanged;

    private void PlacementGroup(StackPanel p, string title, Func<AppSettings, PopupPlacementSettings> get)
    {
        NumericUpDown? x = null, y = null;
        Button? useCurrent = null;
        void UpdateEnabled()
        {
            bool isFixed = get(_hub.Current).Position == PopupPosition.Fixed;
            if (x is not null) x.IsEnabled = isFixed;
            if (y is not null) y.IsEnabled = isFixed;
            if (useCurrent is not null) useCurrent.IsEnabled = isFixed && _boardPosition() is not null;
        }

        var position = Combo(
            EnumNames.Options(PopupPosition.Cursor, PopupPosition.LastPosition, PopupPosition.ScreenCenter, PopupPosition.Fixed),
            s => get(s).Position, (s, v) => get(s).Position = v, SettingsChange.Popup);
        position.SelectionChanged += (_, _) => { UpdateEnabled(); AnchorPositionChanged?.Invoke(); };
        p.Children.Add(Row(title, position));

        x = Number(-100000, 100000, s => get(s).X ?? 0, (s, v) => get(s).X = v, SettingsChange.Popup);
        y = Number(-100000, 100000, s => get(s).Y ?? 0, (s, v) => get(s).Y = v, SettingsChange.Popup);
        useCurrent = new Button { Content = Strings.Settings_Triggers_UseCurrent };
        useCurrent.Click += (_, _) =>
        {
            if (_boardPosition() is not { } pos) return;
            _hub.Update(s => { get(s).X = pos.X; get(s).Y = pos.Y; }, SettingsChange.Popup);
            RefreshAll();
        };
        p.Children.Add(Row(Sub(Strings.Settings_Triggers_Fixed), new WrapPanel
        {
            Children =
            {
                new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }, x,
                new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) }, y,
            },
        }, Strings.Settings_Triggers_Fixed_Note));
        p.Children.Add(Row("", useCurrent, Strings.Settings_Triggers_UseCurrent_Note));
        _refreshers.Add(UpdateEnabled);
        Activated += (_, _) => UpdateEnabled(); // 盤面を出してから戻ってきたとき
    }

    private Control GeneralTab()
    {
        var p = Panel();
        // 言語の名前は各言語の自称で固定（どちらの UI でも「日本語」「English」。spec/TERMS.md）
        var language = Combo([(Strings.Settings_General_Language_System, "system"), ("日本語", "ja"), ("English", "en")], // i18n:ignore
            s => s.General.Language, (s, v) => s.General.Language = v, SettingsChange.None);
        p.Children.Add(Row(Strings.Settings_General_Language, WithRestart(language, () => Loc.Effective(_hub.Current.General.Language) != Loc.StartupLanguage),
            Strings.Settings_General_Language_Note));

        // 自動起動は OS 側の登録が正（settings.json の値ではなく、開くたびに OS に問い合わせる）
        var autoStart = new ToggleSwitch { OnContent = Strings.Common_On, OffContent = Strings.Common_Off };
        bool canAutoStart = _ctx.Platform.AutoStart.IsAvailable; // Mac は .app から起動したときだけ（SMAppService）
        autoStart.IsEnabled = canAutoStart;
        _refreshers.Add(() => autoStart.IsChecked = canAutoStart && _ctx.Platform.AutoStart.IsEnabled);
        autoStart.IsCheckedChanged += (_, _) =>
        {
            if (_refreshing || !canAutoStart) return;
            bool want = autoStart.IsChecked == true;
            try
            {
                _ctx.Platform.AutoStart.SetEnabled(want, Environment.ProcessPath ?? "");
                _hub.Update(s => s.General.AutoStart = want, SettingsChange.General);
            }
            catch (Exception ex)
            {
                AppLog.Info($"自動起動の設定に失敗: {ex.Message}");
                Toast.Show(Strings.FormatSettings_General_AutoStartFailed(ex.Message));
                _refreshing = true;
                autoStart.IsChecked = !want;
                _refreshing = false;
            }
        };
        // 使えない起動のしかた（Mac の dotnet run）では項目を出さない（2026-10-03 ユーザー判断。SPEC §1.2）
        if (canAutoStart) p.Children.Add(Row(Strings.Settings_General_AutoStart, autoStart));
        p.Children.Add(Row(Strings.Settings_General_HideStartupGuide, Toggle(
            s => s.General.HideStartupGuide, (s, v) => s.General.HideStartupGuide = v, SettingsChange.None),
            Strings.Settings_General_HideStartupGuide_Note));

        if (OperatingSystem.IsMacOS())
        {
            var perm = new Button { Content = Strings.Settings_General_Permissions };
            perm.Click += (_, _) => _ctx.ShowPermissionGuide();
            p.Children.Add(Row(Strings.Settings_General_Accessibility, perm, Strings.Settings_General_Accessibility_Note));
        }
        return Scroll(p);
    }


    private Control TriggersTab()
    {
        var p = Panel();
        p.Children.Add(Themed.Glow(new TextBlock { Text = Strings.Settings_Triggers_EditorHeading, FontWeight = FontWeight.SemiBold }));
        TriggerRows(p, s => s.Triggers);
        p.Children.Add(Themed.Glow(new TextBlock { Text = Strings.Settings_Triggers_HistoryHeading, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 16, 0, 0) }));
        TriggerRows(p, s => s.HistoryTriggers);
        p.Children.Add(Themed.Note(new TextBlock { Text = Strings.History_TriggerNote, FontSize = 12, TextWrapping = TextWrapping.Wrap }));
        return Scroll(p);
    }

    private void TriggerRows(StackPanel p, Func<AppSettings, TriggerSettings> get)
    {
        // 修飾キーの 2 回押し（SPEC §4.1）
        var doubleTap = Toggle(s => get(s).DoubleTap.Enabled, (s, v) => get(s).DoubleTap.Enabled = v, SettingsChange.Triggers);
        p.Children.Add(Row(Strings.Settings_Triggers_DoubleTap, doubleTap, Strings.Settings_Triggers_DoubleTap_Note));
        var tapKey = Combo(Enum.GetValues<DoubleTapModifier>().Select(m => (Loc.Modifier(m), m)).ToArray(),
            s => get(s).DoubleTap.Modifier, (s, v) => get(s).DoubleTap.Modifier = v, SettingsChange.Triggers);
        p.Children.Add(Row(Sub(Strings.Settings_Triggers_DoubleTapKey), tapKey));
        var tapInterval = ValueSlider(200, 500, 10, "ms",
            s => get(s).DoubleTap.IntervalMs, (s, v) => get(s).DoubleTap.IntervalMs = v, SettingsChange.Triggers);
        p.Children.Add(Row(Sub(Strings.Settings_Triggers_DoubleTapInterval), tapInterval));
        void UpdateTapEnabled() => tapKey.IsEnabled = tapInterval.IsEnabled = get(_hub.Current).DoubleTap.Enabled;
        doubleTap.IsCheckedChanged += (_, _) => UpdateTapEnabled();
        _refreshers.Add(UpdateTapEnabled);

        p.Children.Add(Row(Strings.Settings_Triggers_UseHotkey, Toggle(
            s => get(s).Hotkey.Enabled, (s, v) => get(s).Hotkey.Enabled = v, SettingsChange.Triggers)));

        var recorder = new HotkeyRecorder(_ctx.Platform);
        _recorders.Add(recorder);
        recorder.RecordingStarted += () => { foreach (var other in _recorders) if (!ReferenceEquals(other, recorder)) other.Cancel(); };
        _refreshers.Add(() => recorder.SetValue(get(_hub.Current).Hotkey));
        recorder.RecordingStarted += () => _ctx.Platform.Hotkey.Unregister();
        recorder.RecordingEnded += h =>
        {
            if (h is not null) _hub.Update(s => get(s).Hotkey = h, SettingsChange.Triggers);
            else _ctx.Platform.Hotkey.Register(_hub.Current.Triggers.Hotkey, out _); // 中止: 元に戻す
        };
        p.Children.Add(Row(Strings.Settings_Triggers_Hotkey, recorder,
            Strings.Settings_Triggers_Hotkey_Note));

        p.Children.Add(Themed.Glow(new TextBlock { Text = Strings.Settings_Triggers_Mouse, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) }));
        p.Children.Add(new MouseTriggerList(_hub, _refreshers, get));
        p.Children.Add(Themed.Note(new TextBlock { Text = Strings.Settings_Triggers_Mouse_Note, FontSize = 12, TextWrapping = TextWrapping.Wrap }));
    }

    private Control HistoryTab()
    {
        var p = Panel();
        var clear = new Button { Content = Strings.History_Clear };
        clear.Click += async (_, _) =>
        {
            if (await Confirm(Strings.History_Clear, Strings.History_ClearConfirm, Strings.History_Clear)) _ctx.ClearHistory?.Invoke();
        };
        p.Children.Add(Row(Strings.History_Clear, clear));
        var auto = Toggle(s => s.History.AutoClear, (s, v) => s.History.AutoClear = v, SettingsChange.History);
        p.Children.Add(Row(Strings.History_AutoClear, auto, Strings.History_AutoClear_Note));
        var minutes = Number(1, int.MaxValue, s => s.History.AutoClearMinutes, (s, v) => s.History.AutoClearMinutes = v, SettingsChange.History);
        p.Children.Add(Row(Strings.History_Minutes, minutes));
        void UpdateMinutes() => minutes.IsEnabled = _hub.Current.History.AutoClear;
        auto.IsCheckedChanged += (_, _) => UpdateMinutes();
        _refreshers.Add(UpdateMinutes);
        p.Children.Add(Row(Strings.History_MaxItems, Number(1, 1000, s => s.History.MaxItems, (s, v) => s.History.MaxItems = v, SettingsChange.History), Strings.History_MaxItems_Note));
        return Scroll(p);
    }

    /// <summary>貼り付けタブ（SPEC §5、SETTINGS.md 貼り付けタブ）。</summary>
    private Control PasteTab()
    {
        var p = Panel();
        var auto = Toggle(s => s.Paste.AutoPaste, (s, v) => s.Paste.AutoPaste = v, SettingsChange.Paste);
        p.Children.Add(Row(Strings.Settings_Paste_AutoPaste, auto, Strings.Settings_Paste_AutoPaste_Note));
        var restore = Toggle(s => s.Paste.RestoreClipboard, (s, v) => s.Paste.RestoreClipboard = v, SettingsChange.Paste);
        p.Children.Add(Row(Strings.Settings_Paste_Restore, restore, Strings.Settings_Paste_Restore_Note));
        var delay = ValueSlider(200, 3000, 100, "ms", s => s.Paste.RestoreDelayMs, (s, v) => s.Paste.RestoreDelayMs = v, SettingsChange.Paste);
        p.Children.Add(Row(Sub(Strings.Settings_Paste_RestoreDelay), delay));
        void UpdateEnabled()
        {
            restore.IsEnabled = _hub.Current.Paste.AutoPaste;
            delay.IsEnabled = _hub.Current.Paste.AutoPaste && _hub.Current.Paste.RestoreClipboard;
        }
        auto.IsCheckedChanged += (_, _) => UpdateEnabled();
        restore.IsCheckedChanged += (_, _) => UpdateEnabled();
        _refreshers.Add(UpdateEnabled);
        p.Children.Add(Themed.Note(new TextBlock
        {
            Text = Loc.Os(Strings.Settings_Paste_Limit_Note_Win, Strings.Settings_Paste_Limit_Note_Mac),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        }));
        return Scroll(p);
    }

    private Control DataTab()
    {
        var p = Panel();
        p.Children.Add(Row(Strings.Settings_Data_Backups, Number(0, 100,
            s => s.Data.BackupGenerations, (s, v) => s.Data.BackupGenerations = v, SettingsChange.Data),
            Strings.Settings_Data_Backups_Note));
        var open = new Button { Content = Strings.Settings_Data_OpenFolder };
        open.Click += (_, _) => _openFolder(_ctx.Paths.Root);
        p.Children.Add(Row("", open));

        var export = new Button { Content = Strings.Settings_Data_Export };
        export.Click += (_, _) => _ = ExportAsync();
        var import = new Button { Content = Strings.Settings_Data_Import };
        import.Click += (_, _) => _ = ImportAsync();
        p.Children.Add(Row(Strings.Settings_Data_Backup, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { export, import } },
            Strings.Settings_Data_Backup_Note));
        // ポータブルモードは機能だけ残し、画面には出さない（分かりづらいため。2026-10-10 ユーザー判断。SPEC §9.1）
        return Scroll(p);
    }

    private static readonly FilePickerFileType ZipType = new("zip") { Patterns = ["*.zip"], AppleUniformTypeIdentifiers = ["public.zip-archive"], MimeTypes = ["application/zip"] };

    private async Task ExportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Settings_Data_Export_Title,
            SuggestedFileName = DataArchive.DefaultFileName(DateTime.Now),
            DefaultExtension = "zip",
            FileTypeChoices = [ZipType],
        });
        if (file is null) return;
        try
        {
            _ctx.FlushAll?.Invoke();
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0); // 既存のファイルへ上書きするとき
            DataArchive.Export(_ctx.Paths, stream);
            AppLog.Info($"エクスポート: {file.Name}");
            Toast.Show(Strings.FormatSettings_Data_Exported(file.Name), error: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toast.Show(Strings.FormatSettings_Data_ExportFailed(ex.Message));
        }
    }

    private async Task ImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Settings_Data_Import_Title,
            AllowMultiple = false,
            FileTypeFilter = [ZipType],
        });
        if (files.Count == 0) return;
        ArchiveContents contents;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            contents = await Task.Run(() => DataArchive.Read(stream));
        }
        catch (Exception ex) when (ex is ArchiveException or IOException or UnauthorizedAccessException)
        {
            Toast.Show(Strings.FormatToast_ImportFailed(ex is ArchiveException ae ? ArchiveMessage(ae) : ex.Message));
            return;
        }
        string what = (contents.Settings, contents.Templates) switch
        {
            (not null, not null) => Strings.Settings_Data_ImportWhat_Both,
            (not null, null) => Strings.Settings_Data_ImportWhat_Settings,
            _ => Strings.Settings_Data_ImportWhat_Templates,
        };
        if (!await Confirm(Strings.Settings_Data_ImportConfirm_Title, Strings.FormatSettings_Data_ImportConfirm(what, files[0].Name), Strings.Settings_Data_ImportConfirm_Ok)) return;
        _ctx.ImportAndRestart?.Invoke(contents);
    }

    /// <summary>
    /// 演出タブ（SPEC §7「演出タブの構成」）。このアプリの肝である常時の演出 3 つ（光の玉・発光の明滅・走査線の帯）は普通の行で、
    /// 残りの演出は折りたたみの「詳細設定」に入れる。
    /// </summary>
    private Control EffectsTab()
    {
        var p = Panel();
        p.Children.Add(Row(Strings.Settings_Effects_Animation, Toggle(
            s => s.Appearance.Animation, (s, v) => s.Appearance.Animation = v, SettingsChange.Effects),
            Strings.Settings_Effects_Animation_Note));

        // アニメーション OFF の間は以下を無効表示（注記は読めるように外に置く）
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = Strings.Settings_Effects_AmbientHeading, FontWeight = FontWeight.SemiBold, FontSize = 13, Margin = new Thickness(0, 6, 0, 0) });
        foreach (var def in EffectCatalog.All.Where(d => d.IsAmbient))
            body.Children.Add(EffectRow(def, showId: false, showEasing: false));
        // OS の層で描く（macOS = Core Animation）ときは OS が画面に合わせて進めるので VSync も CPU の注記も出さない（2026-10-09 ユーザー判断）
        bool native = _ctx.Platform.Ambient.IsSupported;
        if (!native)
            body.Children.Add(Row(Strings.Settings_Effects_Vsync, Combo(
                AppearanceSettings.VsyncValues.Select(v => (Strings.FormatSettings_Effects_Vsync_Item(v, 60 / v), v)).ToArray(),
                s => s.Appearance.Vsync, (s, v) => s.Appearance.Vsync = v, SettingsChange.Effects),
                Strings.Settings_Effects_Vsync_Note));
        p.Children.Add(body);

        if (!native)
            p.Children.Add(Themed.Note(new TextBlock { Text = Strings.Settings_Effects_AmbientNote, FontSize = 12, TextWrapping = TextWrapping.Wrap }));
        if (_ctx.Platform.Window.PrefersReducedMotion)
            p.Children.Add(Themed.Note(new TextBlock { Text = Strings.Settings_Effects_ReducedMotionNote, FontSize = 12, TextWrapping = TextWrapping.Wrap }));

        var rows = new StackPanel { Spacing = 12 };
        // グリッチの強さ（表示・非表示で共通）は boardHide の直下。どちらかの種類がグリッチのときだけ有効
        Control? glitchIntensity = null;
        bool UsesGlitch() => new[] { EffectCatalog.BoardShow, EffectCatalog.BoardHide }
            .Any(id => EffectCatalog.Resolve(_hub.Current.Appearance, id).Kind == EffectKind.Glitch);
        void UpdateGlitchEnabled() { if (glitchIntensity is not null) glitchIntensity.IsEnabled = UsesGlitch(); }
        foreach (var def in EffectCatalog.All.Where(d => !d.IsAmbient))
        {
            bool board = def.Id is EffectCatalog.BoardShow or EffectCatalog.BoardHide;
            rows.Children.Add(EffectRow(def, onKindChanged: board ? UpdateGlitchEnabled : null));
            if (def.Id != EffectCatalog.BoardHide) continue;
            glitchIntensity = PercentSlider(GlitchLook.MinIntensity, GlitchLook.MaxIntensity, GlitchLook.IntensityStep,
                s => s.Appearance.GlitchIntensity, (s, v) => s.Appearance.GlitchIntensity = v, SettingsChange.Effects);
            rows.Children.Add(Row(Strings.Settings_Effects_GlitchIntensity, glitchIntensity, Strings.Settings_Effects_GlitchIntensity_Note));
        }
        _refreshers.Add(UpdateGlitchEnabled);
        var advanced = new Expander { Header = Strings.Settings_Effects_Advanced, Content = rows, HorizontalAlignment = HorizontalAlignment.Stretch };
        p.Children.Add(advanced);

        var resetAll = new Button { Content = Strings.Settings_Effects_ResetAll };
        resetAll.Click += (_, _) =>
        {
            _hub.Update(s => { s.Appearance.Effects.Clear(); s.Appearance.Vsync = 2; s.Appearance.GlitchIntensity = GlitchLook.DefaultIntensity; }, SettingsChange.Effects);
            RefreshAll();
        };
        p.Children.Add(resetAll);
        _refreshers.Add(() => body.IsEnabled = advanced.IsEnabled = resetAll.IsEnabled = _hub.Current.Appearance.Animation);

        // 静止した走査線（SPEC §3.9 H14）。演出ではなく見た目なので、アニメーション OFF でも有効・「すべて既定に戻す」の対象外。
        // 上の無効化の並びを途切れさせないよう末尾に置く
        p.Children.Add(new TextBlock { Text = Strings.Settings_Effects_ScanlinesHeading, FontWeight = FontWeight.SemiBold, FontSize = 13, Margin = new Thickness(0, 6, 0, 0) });
        var scanlines = Toggle(s => s.Appearance.Hud.Scanlines, (s, v) => s.Appearance.Hud.Scanlines = v, SettingsChange.Hud);
        p.Children.Add(Row(Strings.Settings_Effects_Scanlines, scanlines, Strings.Settings_Effects_Scanlines_Note));
        var scanlineOpacity = ValueSlider(0, 50, 5, "%", s => s.Appearance.Hud.ScanlineOpacity, (s, v) => s.Appearance.Hud.ScanlineOpacity = v, SettingsChange.Hud);
        p.Children.Add(Row(Sub(Strings.Settings_Effects_ScanlineOpacity), scanlineOpacity));
        var scanlinePitch = Combo(HudSettings.ScanlinePitches.Select(px => (Strings.FormatCommon_PixelValue(px), px)).ToArray(),
            s => s.Appearance.Hud.ScanlinePitch, (s, v) => s.Appearance.Hud.ScanlinePitch = v, SettingsChange.Hud);
        p.Children.Add(Row(Sub(Strings.Settings_Effects_ScanlinePitch), scanlinePitch));
        void UpdateScanlinesEnabled() => scanlineOpacity.IsEnabled = scanlinePitch.IsEnabled = _hub.Current.Appearance.Hud.Scanlines;
        scanlines.IsCheckedChanged += (_, _) => UpdateScanlinesEnabled();
        _refreshers.Add(UpdateScanlinesEnabled);
        return Scroll(p);
    }

    /// <param name="showId">項目名に演出 ID を添えるか（詳細設定の行）。</param>
    /// <param name="showEasing">イージングの欄を出すか（常時の演出はイージングを使わないので出さない）。</param>
    /// <param name="onKindChanged">種類を変えたときに呼ぶ（種類の変更では RefreshAll が走らないので、他の行の有効・無効を追従させる用）。</param>
    private Control EffectRow(EffectDefinition def, bool showId = true, bool showEasing = true, Action? onKindChanged = null)
    {
        EffectSpec Current() => _hub.Current.Appearance.Effects.GetValueOrDefault(def.Id) ?? def.Default;
        void Apply(Func<EffectSpec, EffectSpec> change) =>
            Set(s => s.Appearance.Effects[def.Id] = change(s.Appearance.Effects.GetValueOrDefault(def.Id) ?? def.Default), SettingsChange.Effects);

        var kinds = def.AllowedKinds.ToArray();
        var kind = new ComboBox { MinWidth = 110, ItemsSource = kinds.Select(KindName).ToList() };
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex < 0) return;
            Apply(e => e with { Kind = kinds[kind.SelectedIndex] });
            if (!_refreshing) onKindChanged?.Invoke();
        };

        var easings = Enum.GetValues<EasingKind>();
        var easing = new ComboBox { MinWidth = 120, ItemsSource = easings.Select(EasingName).ToList() };
        easing.SelectionChanged += (_, _) => { if (easing.SelectedIndex >= 0) Apply(e => e with { Easing = easings[easing.SelectedIndex] }); };

        var slider = new Slider { Minimum = def.MinDurationMs, Maximum = def.MaxDurationMs, TickFrequency = def.DurationStepMs, IsSnapToTickEnabled = true, Width = 150 };
        // 常時の演出は 1 周 / 1 周期の長さなので秒で見せる
        string Duration(int ms) => def.IsAmbient ? $"{ms / 1000.0:0.0} s" : $"{ms} ms";
        var ms = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 56 };
        slider.ValueChanged += (_, e) =>
        {
            int v = (int)Math.Round(e.NewValue);
            ms.Text = Duration(v);
            Apply(x => x with { DurationMs = v });
        };

        var reset = new Button { Content = Strings.Common_ResetToDefault, FontSize = 12, Padding = new Thickness(8, 2) };
        reset.Click += (_, _) =>
        {
            _hub.Update(s => s.Appearance.Effects.Remove(def.Id), SettingsChange.Effects);
            RefreshAll();
        };

        _refreshers.Add(() =>
        {
            var c = Current();
            kind.SelectedIndex = Array.IndexOf(kinds, c.Kind);
            easing.SelectedIndex = Array.IndexOf(easings, c.Easing);
            slider.Value = c.DurationMs;
            ms.Text = Duration(c.DurationMs);
            bool active = c.Kind != EffectKind.None;
            slider.IsEnabled = active;
            easing.IsEnabled = active && def.UsesEasing && c.Kind != EffectKind.Glitch; // glitch・常時の演出はイージングを使わない
        });

        var header = new DockPanel();
        DockPanel.SetDock(reset, Dock.Right);
        header.Children.Add(reset);
        header.Children.Add(new TextBlock
        {
            Text = showId ? Strings.FormatSettings_Effects_Row(EnumNames.Effect(def.Id), def.Id) : EnumNames.Effect(def.Id),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                header,
                showEasing ? new WrapPanel { Children = { kind, Spacer(), slider, ms, Spacer(), easing } } : new WrapPanel { Children = { kind, Spacer(), slider, ms } },
            },
        };
    }

    private static Control Spacer() => new Border { Width = 8 };

    private static string KindName(EffectKind k) => EnumNames.Of(k);

    private static string EasingName(EasingKind e) => EnumNames.Of(e);

    private Control AdvancedTab()
    {
        var p = Panel();
        p.Children.Add(Row(Strings.Settings_Advanced_Logging, Toggle(
            s => s.Advanced.Logging, (s, v) => s.Advanced.Logging = v, SettingsChange.Logging),
#if DEBUG
            Strings.Settings_Advanced_Logging_Note
#else
            null
#endif
            ));
        var open = new Button { Content = Strings.Settings_Advanced_OpenLogFolder };
        open.Click += (_, _) => { if (AppLog.LogsDirectory is { } dir) { Directory.CreateDirectory(dir); _openFolder(dir); } };
        p.Children.Add(Row("", open));

        var reset = new Button { Content = Strings.Settings_Advanced_Reset };
        reset.Click += (_, _) => _ = ResetAsync();
        p.Children.Add(Row("", reset, Strings.Settings_Advanced_Reset_Note));
        return Scroll(p);
    }

    private async Task ResetAsync()
    {
        if (!await Confirm(Strings.Settings_Advanced_ResetConfirm_Title, Strings.Settings_Advanced_Reset_Note, Strings.Settings_Advanced_ResetConfirm_Ok)) return;
        AppLog.Info("設定をリセット");
        _hub.Replace(new AppSettings());
    }

    // ---------------- 部品 ----------------

    private static TabItem Tab(string header, Control content) =>
        new() { Header = Themed.Glow(new TextBlock { Text = header }), Content = content };

    private static StackPanel Panel() => new() { Spacing = 10, Margin = new Thickness(16) };

    /// <summary>
    /// 再起動で反映される設定の右に「今すぐ再起動」を添える。今の起動と違う値を選んでいる間だけ見せる（SPEC §7）。
    /// </summary>
    private Control WithRestart(Control control, Func<bool> needsRestart)
    {
        var restart = new Button { Content = Strings.Common_RestartNow, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        restart.Click += (_, _) => _ctx.Restart?.Invoke();
        void Update() => restart.IsVisible = _ctx.Restart is not null && needsRestart();
        _refreshers.Add(Update);
        switch (control)
        {
            case ComboBox c: c.SelectionChanged += (_, _) => Update(); break;
            case ToggleSwitch t: t.IsCheckedChanged += (_, _) => Update(); break;
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { control, restart } };
    }

    /// <summary>上の行に属する項目（字下げして見せる）。</summary>
    private static string Sub(string label) => "\u3000" + label;

    /// <summary>インポートできない zip の理由（Core はコードだけ返す）。</summary>
    private static string ArchiveMessage(ArchiveException ex) => ex.Kind switch
    {
        ArchiveError.NotZip => Strings.Archive_NotZip,
        ArchiveError.TooLarge => Strings.Archive_TooLarge,
        ArchiveError.NoKnownEntries => Strings.Archive_NoKnownEntries,
        ArchiveError.EntryNotObject => Strings.FormatArchive_EntryNotObject(ex.Entry),
        _ => Strings.FormatArchive_EntryCorrupt(ex.Entry, ex.Detail),
    };

    private static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

    private static Control Row(string label, Control control, string? note = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelColumnWidth},*") };
        // 項目名はほんのり光らせる（サイバーパンクのみ。2026-10-03 ユーザー要望）
        var text = Themed.Glow(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) }, strong: false);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        if (note is null) return grid;
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                grid,
                Themed.Note(new TextBlock { Text = note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(LabelColumnWidth, 0, 0, 0) }),
            },
        };
    }

    private ToggleSwitch Toggle(Func<AppSettings, bool> get, Action<AppSettings, bool> set, SettingsChange change)
    {
        var t = new ToggleSwitch { OnContent = Strings.Common_On, OffContent = Strings.Common_Off };
        _refreshers.Add(() => t.IsChecked = get(_hub.Current));
        t.IsCheckedChanged += (_, _) => Set(s => set(s, t.IsChecked == true), change);
        return t;
    }

    private ComboBox Combo<T>((string Text, T Value)[] options, Func<AppSettings, T> get, Action<AppSettings, T> set, SettingsChange change)
    {
        var c = new ComboBox { MinWidth = 200, ItemsSource = options.Select(o => o.Text).ToList() };
        _refreshers.Add(() => c.SelectedIndex = Array.FindIndex(options, o => EqualityComparer<T>.Default.Equals(o.Value, get(_hub.Current))));
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedIndex >= 0) Set(s => set(s, options[c.SelectedIndex].Value), change);
        };
        return c;
    }

    private NumericUpDown Number(int min, int max, Func<AppSettings, int> get, Action<AppSettings, int> set, SettingsChange change)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", MinWidth = 120 };
        _refreshers.Add(() => n.Value = get(_hub.Current));
        n.ValueChanged += (_, _) =>
        {
            if (n.Value is { } v) Set(s => set(s, (int)v), change);
        };
        return n;
    }

    private Control PercentSlider(int min, int max, int step, Func<AppSettings, int> get, Action<AppSettings, int> set, SettingsChange change) =>
        ValueSlider(min, max, step, "%", get, set, change);

    private Control ValueSlider(int min, int max, int step, string unit, Func<AppSettings, int> get, Action<AppSettings, int> set, SettingsChange change)
    {
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, Width = 220 };
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 64 };
        _refreshers.Add(() =>
        {
            slider.Value = get(_hub.Current);
            value.Text = $"{get(_hub.Current)} {unit}";
        });
        slider.ValueChanged += (_, e) =>
        {
            int v = (int)Math.Round(e.NewValue);
            value.Text = $"{v} {unit}";
            Set(s => set(s, v), change);
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { slider, value } };
    }
}
