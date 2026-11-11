using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileLauncher.App;
using FileLauncher.Core.Model;
using FileLauncher.Core.Storage;
using FileLauncher.Platform;

namespace FileLauncher.Desktop.Tests;

/// <summary>テスト用の盤面コントローラ（ダイアログはそのまま実行、起動は記録）。</summary>
internal sealed class FakeController : IBoardController
{
    public List<LauncherItem> Launched { get; } = new();
    public bool ConfirmResult { get; set; } = true;
    public void Activate() { }
    public Task DeactivateAsync() => Task.CompletedTask;
    public bool IsShown => true;
    public void Toggle(string source) { }
    public void ShowFromExternal(string source) { }
    public void Launch(LauncherItem item, IReadOnlyList<string>? droppedPaths, bool newWindow = false) { Launched.Add(item); LastNewWindow = newWindow; }
    public bool? LastNewWindow { get; private set; }
    // 確認ダイアログは開かずに答えを返す（Headless で ShowDialog を待たないため）
    public Task<T> RunModalAsync<T>(Func<Task<T>> dialog) =>
        typeof(T) == typeof(bool) ? Task.FromResult((T)(object)ConfirmResult) : dialog();
}

public sealed class BoardEditingUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileLauncherUiTests", Guid.NewGuid().ToString("N"));
    private readonly AppDataStore _store;
    private readonly SettingsHub _hub;
    private readonly Board _board;
    private readonly BoardWindow _window = new();
    private readonly FakeController _controller = new();
    private readonly FakePlatform _platform = new();
    private readonly BoardEditor _editor;
    private readonly LauncherItem _a;
    private readonly LauncherItem _b;
    private int _invoked; // クリックで起動を求められた回数（起動そのものは盤面のコントローラの仕事）
    private bool? _lastNewWindow;

    public BoardEditingUiTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new AppDataStore(new DataPaths(_dir, IsPortable: false));
        var (settings, board) = _store.LoadAll();
        _hub = new SettingsHub(_store, settings.Value);
        _hub.Update(s => s.Appearance.Animation = false, SettingsChange.None); // 演出の待ちを無くす
        _board = board.Value;
        var page = _board.Pages[0];
        _a = new LauncherItem { Row = 0, Col = 0, Name = "a", Target = "/a" };
        _b = new LauncherItem { Row = 0, Col = 1, Name = "b", Target = "/b" };
        page.Items.Add(_a);
        page.Items.Add(_b);

        _window.ApplyEffects(_hub.Current.Appearance);
        _window.Render(_board, _hub.Current.Appearance, 0);
        _window.Show();
        _editor = new BoardEditor(_window, () => _controller, _platform, _store, _hub, _board);
        _editor.Attach();
        _window.ItemInvoked += (_, newWindow) => { _invoked++; _lastNewWindow = newWindow; };
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        _window.AllowClose = true;
        _window.Close();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private Button Slot(int row, int col) =>
        _window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "SlotGrid")
            .Children.OfType<Button>().Single(b => Grid.GetRow(b) == row && Grid.GetColumn(b) == col);

    private Point Center(int row, int col)
    {
        var slot = Slot(row, col);
        return slot.TranslatePoint(new Point(slot.Bounds.Width / 2, slot.Bounds.Height / 2), _window)!.Value;
    }

    private void Drag(Point from, Point to, RawInputModifiers mods = RawInputModifiers.None)
    {
        _window.MouseDown(from, MouseButton.Left);
        _window.MouseMove(new Point(from.X + 6, from.Y + 6), RawInputModifiers.LeftMouseButton | mods);
        _window.MouseMove(to, RawInputModifiers.LeftMouseButton | mods);
        _window.MouseUp(to, MouseButton.Left, mods);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void 空きスロットへドラッグすると移動し_アイテムは起動しない()
    {
        Drag(Center(0, 0), Center(1, 2));

        Assert.Equal((1, 2), (_a.Row, _a.Col));
        Assert.Equal(0, _invoked);
        Assert.False(_window.IsDragging);
    }

    [AvaloniaFact]
    public void 別のアイテムの上へドラッグすると入れ替わる()
    {
        Drag(Center(0, 0), Center(0, 1));
        Assert.Equal((0, 1, 0, 0), (_a.Row, _a.Col, _b.Row, _b.Col));
    }

    [AvaloniaFact]
    public void 複製の修飾キーを押したまま離すと複製される()
    {
        var copyKey = OperatingSystem.IsMacOS() ? RawInputModifiers.Alt : RawInputModifiers.Control;
        Drag(Center(0, 0), Center(1, 0), copyKey);

        Assert.Equal(3, _board.Pages[0].Items.Count);
        Assert.Equal((0, 0), (_a.Row, _a.Col));
        Assert.Contains(_board.Pages[0].Items, i => i.Row == 1 && i.Col == 0 && i.Name == "a" && i.Id != _a.Id);
    }

    [AvaloniaFact]
    public void 盤面の外で離すと確認なしで削除される()
    {
        _controller.ConfirmResult = false; // 確認を出したら「いいえ」になる状態でも消える
        Drag(Center(0, 0), new Point(-50, -50));
        Assert.DoesNotContain(_a, _board.Pages[0].Items);
    }

    [AvaloniaFact]
    public void 普通にクリックすると起動する()
    {
        var p = Center(0, 0);
        _window.MouseDown(p, MouseButton.Left);
        _window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, _invoked);
    }

    [AvaloniaFact]
    public void 右クリックメニューの色で色帯が付き_手を止めると保存される()
    {
        SlotContext? ctx = null;
        _window.ContextMenuRequested += c => ctx ??= c;
        _window.MouseDown(Center(0, 0), MouseButton.Right);
        _window.MouseUp(Center(0, 0), MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SlotContextKind.Item, ctx!.Kind);

        var menu = _editor.LastMenu;
        var color = menu!.Items.OfType<MenuItem>().Single(m => (string?)m.Header == Strings.Menu_Color);
        var purple = color.Items.OfType<MenuItem>().Single(m => (string?)m.Header == EnumNames.Color(ItemColor.Purple));
        purple.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ItemColor.Purple, _a.Color);
        _editor.Flush();
        var saved = new AppDataStore(new DataPaths(_dir, IsPortable: false)).LoadAll().Board.Value;
        Assert.Equal(ItemColor.Purple, saved.Pages[0].Items.Single(i => i.Name == "a").Color);
    }

    // ---------------- フォルダを開く先（SPEC §5.2 / §6.2 / §6.3 / §6.6） ----------------

    /// <summary>「新しいウィンドウで開く」の修飾キー（Win Ctrl / Mac ⌘）。</summary>
    private static RawInputModifiers NewWindowModifier => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    [AvaloniaFact]
    public void 動かさずにCtrlクリック_Macはコマンドクリックすると新しいウィンドウで開く指示になり_修飾キーなしでは付かない()
    {
        var p = Center(0, 0);
        _window.MouseDown(p, MouseButton.Left, NewWindowModifier);
        _window.MouseUp(p, MouseButton.Left, NewWindowModifier);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, _invoked);
        Assert.True(_lastNewWindow);

        // ポインタの離上を伴わない Click に、前回の修飾キーが残らない（控えは 1 回で消える）
        Slot(0, 0).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(true, _lastNewWindow);

        int before = _invoked;
        _window.MouseDown(p, MouseButton.Left);
        _window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before + 1, _invoked);
        Assert.False(_lastNewWindow);
    }

    [AvaloniaFact]
    public void 選択セルでCtrlEnter_MacはコマンドEnterを押すと新しいウィンドウで開く指示になる()
    {
        _window.Focus();
        _window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // (0,0) = a
        _window.KeyPressQwerty(PhysicalKey.Enter, NewWindowModifier);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, _invoked);
        Assert.True(_lastNewWindow);
    }

    private void OpenItemMenu(int row, int col)
    {
        _window.MouseDown(Center(row, col), MouseButton.Right);
        _window.MouseUp(Center(row, col), MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void 新しいウィンドウで開くはフォルダアイテムのメニューにだけあり_選ぶと新しいウィンドウで起動する()
    {
        _a.Kind = ItemKind.Folder;
        OpenItemMenu(0, 0);
        var item = _editor.LastMenu!.Items.OfType<MenuItem>().SingleOrDefault(m => (string?)m.Header == Strings.Menu_OpenNewWindow);
        Assert.NotNull(item);
        item!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(_a, _controller.Launched.Single());
        Assert.True(_controller.LastNewWindow);

        OpenItemMenu(0, 1); // b はファイル
        Assert.DoesNotContain(_editor.LastMenu!.Items.OfType<MenuItem>(), m => (string?)m.Header == Strings.Menu_OpenNewWindow);
    }

    [AvaloniaFact]
    public void 格納フォルダを開くは設定の開き先で開く()
    {
        _hub.Update(s => s.General.FolderOpenTarget = FolderOpenTarget.NewWindow, SettingsChange.None);
        OpenItemMenu(0, 0);
        MenuItemOf(OperatingSystem.IsMacOS() ? Strings.Menu_Reveal_Mac : Strings.Menu_Reveal_Win).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("/a", FolderOpenTarget.NewWindow), _platform.Revealed.Single());
    }

    [AvaloniaFact]
    public void ターミナルで開くはパスを取得の直後にありフォルダのパスで開く()
    {
        _a.Kind = ItemKind.Folder;
        OpenItemMenu(0, 0);
        var headers = _editor.LastMenu!.Items.OfType<MenuItem>().Select(m => (string?)m.Header).ToList();
        int reveal = headers.IndexOf(OperatingSystem.IsMacOS() ? Strings.Menu_Reveal_Mac : Strings.Menu_Reveal_Win);
        Assert.Equal(reveal + 1, headers.IndexOf(Strings.Menu_CopyPath));
        Assert.Equal(reveal + 2, headers.IndexOf(Strings.Menu_OpenTerminal));

        MenuItemOf(Strings.Menu_OpenTerminal).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("/a", _platform.OpenedTerminals.Single());
    }

    [AvaloniaFact]
    public void URLのアイテムにはターミナルで開くを出さない()
    {
        _b.Kind = ItemKind.Url;
        _b.Target = "https://example.com";
        OpenItemMenu(0, 1);
        Assert.DoesNotContain(_editor.LastMenu!.Items.OfType<MenuItem>(), m => (string?)m.Header == Strings.Menu_OpenTerminal);
    }

    [AvaloniaFact]
    public void ターミナルを開けなくても落ちず盤面は変わらない()
    {
        _a.Kind = ItemKind.Folder;
        _platform.OpenTerminalResult = LaunchResult.Fail(LaunchFailure.NotFound, "/a");
        int before = _board.Pages[0].Items.Count;
        OpenItemMenu(0, 0);
        MenuItemOf(Strings.Menu_OpenTerminal).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(_platform.OpenedTerminals);
        Assert.Equal(before, _board.Pages[0].Items.Count);
    }

    // ---------------- ページ（ステップ 2） ----------------

    private ToggleButton Tab(int index) =>
        _window.GetVisualDescendants().OfType<StackPanel>().Single(sp => sp.Name == "TabsPanel").Children.OfType<ToggleButton>().ElementAt(index);

    private MenuItem MenuItemOf(string header) => _editor.LastMenu!.Items.OfType<MenuItem>().Single(m => (string?)m.Header == header);

    [AvaloniaFact]
    public void タブの列の空き部分を右クリックすると今のページのメニューが出る()
    {
        var header = _window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Header");
        var p = header.TranslatePoint(new Point(header.Bounds.Width - 4, header.Bounds.Height / 2), _window)!.Value; // タブの右の空き
        _window.MouseDown(p, MouseButton.Right);
        _window.MouseUp(p, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        var headers = _editor.LastMenu!.Items.OfType<MenuItem>().Select(m => (string?)m.Header).ToList();
        Assert.Contains(Strings.Menu_PageSettings, headers);
        Assert.Contains(Strings.Menu_NewPage, headers);
        Assert.DoesNotContain(Strings.Menu_NewItem, headers); // 盤面の背景のメニューではない
    }

    private void OpenTabMenu(int index)
    {
        var tab = Tab(index);
        var p = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), _window)!.Value;
        _window.MouseDown(p, MouseButton.Right);
        _window.MouseUp(p, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void タブのメニューで新規ページを作るとそのページが表示され_左右に並べ替えられる()
    {
        OpenTabMenu(0);
        MenuItemOf(Strings.Menu_NewPage).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, _board.Pages.Count);
        Assert.Equal("Page 2", _board.Pages[1].Name);
        Assert.Equal(1, _window.PageIndex);

        OpenTabMenu(1);
        Assert.False(MenuItemOf(Strings.Menu_MoveRight).IsEnabled); // 端
        MenuItemOf(Strings.Menu_MoveLeft).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Page 2", _board.Pages[0].Name);
    }

    [AvaloniaFact]
    public void CtrlTabとホイールでページを切り替え_設定でオフならホイールは効かない()
    {
        BoardEditingPage2();
        _window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, _window.PageIndex);
        _window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, _window.PageIndex);

        var center = Center(0, 0);
        _window.MouseWheel(center, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, _window.PageIndex);

        _hub.Update(s => s.Board.WheelSwitchesPage = false, SettingsChange.None);
        _window.MouseWheel(center, new Vector(0, 1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, _window.PageIndex);
    }

    [AvaloniaFact]
    public void アイテムをタブへドラッグするとそのページの最初の空きへ移る()
    {
        BoardEditingPage2();
        var tab = Tab(1);
        var to = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), _window)!.Value;

        Drag(Center(0, 0), to);

        Assert.DoesNotContain(_a, _board.Pages[0].Items);
        Assert.Contains(_a, _board.Pages[1].Items);
        Assert.Equal((0, 0), (_a.Row, _a.Col));
    }

    [AvaloniaFact]
    public void タブの上でマウスを止めて待つとページが切り替わり_そのまま別ページのマスに置ける()
    {
        BoardEditingPage2();
        var tab = Tab(1);
        var onTab = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), _window)!.Value;
        var from = Center(0, 0);

        _window.MouseDown(from, MouseButton.Left);
        _window.MouseMove(new Point(from.X + 6, from.Y + 6), RawInputModifiers.LeftMouseButton);
        _window.MouseMove(onTab, RawInputModifiers.LeftMouseButton);
        // マウスを動かさずに待つ
        var until = DateTime.UtcNow.AddMilliseconds(900);
        while (DateTime.UtcNow < until && _window.PageIndex == 0) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Assert.Equal(1, _window.PageIndex);

        var target = Center(1, 2); // 2 ページ目のマス
        _window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        _window.MouseUp(target, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(_a, _board.Pages[1].Items);
        Assert.DoesNotContain(_a, _board.Pages[0].Items);
        Assert.Equal((1, 2), (_a.Row, _a.Col));
    }

    /// <summary>2 ページ目を足して 1 ページ目を表示した状態にする。</summary>
    private void BoardEditingPage2()
    {
        _board.Pages.Add(new Page { Name = "Page 2", Rows = 2, Cols = 3 });
        _window.Render(_board, _hub.Current.Appearance, 0);
        Dispatcher.UIThread.RunJobs();
    }

    // ---------------- キーボード（ステップ 4） ----------------

    [AvaloniaFact]
    public void 矢印で選択しEnterで起動_F2で編集_Deleteで削除を求める()
    {
        LauncherItem? edit = null, delete = null;
        _window.EditRequested += i => edit = i;
        _window.DeleteRequested += i => delete = i;
        _window.Focus();

        _window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // 選択なし → (0,0)
        Assert.Equal((0, 0), _window.SelectedCell);
        _window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Equal((0, 1), _window.SelectedCell);
        Assert.Contains("selected", Slot(0, 1).Classes);

        _window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        _window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        _window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None); // Mac の Delete
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, _invoked);
        Assert.Same(_b, edit);
        Assert.Same(_b, delete);

        _window.ClearSelection();
        Assert.Null(_window.SelectedCell);
    }

    [AvaloniaFact]
    public void ショートカットキーで起動し_スロットの右上にキーが表示される()
    {
        _b.Hotkey = "K";
        _window.Render(_board, _hub.Current.Appearance, 0);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(Slot(0, 1).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "K" && t.FontSize == 9);
        _window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, _invoked);
        _window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.None); // 割り当てなし
        Assert.Equal(1, _invoked);
    }

    // ---------------- 元に戻す（ステップ 5） ----------------

    private void PressUndo()
    {
        _window.Focus();
        _window.KeyPressQwerty(PhysicalKey.Z, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
    }

    private void OpenSlotMenu(int row, int col)
    {
        var p = Center(row, col);
        _window.MouseDown(p, MouseButton.Right);
        _window.MouseUp(p, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    private LauncherItem ItemNamed(int page, string name) => _board.Pages[page].Items.Single(i => i.Name == name);

    [AvaloniaFact]
    public void 移動をCtrlZで戻せて_もう一度押しても戻るのは1段階だけ()
    {
        Assert.False(_editor.CanUndo);
        Drag(Center(0, 0), Center(1, 2));
        Drag(Center(0, 1), Center(1, 1));
        Assert.True(_editor.CanUndo);

        PressUndo();
        Assert.Equal((1, 2), (ItemNamed(0, "a").Row, ItemNamed(0, "a").Col)); // 1 回目の移動は残る
        Assert.Equal((0, 1), (ItemNamed(0, "b").Row, ItemNamed(0, "b").Col));
        Assert.False(_editor.CanUndo);

        PressUndo();
        Assert.Equal((1, 2), (ItemNamed(0, "a").Row, ItemNamed(0, "a").Col));
    }

    [AvaloniaFact]
    public void 削除と色の変更も戻せる()
    {
        Drag(Center(0, 0), new Point(-50, -50));
        Assert.DoesNotContain(_board.Pages[0].Items, i => i.Name == "a");
        PressUndo();
        Assert.Equal((0, 0), (ItemNamed(0, "a").Row, ItemNamed(0, "a").Col));
    }

    [AvaloniaFact]
    public void アイテム入りのページを削除してもページごと戻り_表示ページも戻る()
    {
        BoardEditingPage2();
        _board.Pages[1].Items.Add(new LauncherItem { Row = 0, Col = 0, Name = "c", Target = "/c" });
        _window.Render(_board, _hub.Current.Appearance, 1);
        _editor.ResetUndoBaseline();
        Dispatcher.UIThread.RunJobs();

        OpenTabMenu(1);
        MenuItemOf(Strings.Menu_DeletePage).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(_board.Pages);
        Assert.Equal(0, _window.PageIndex);

        OpenSlotMenu(1, 2);
        Assert.True(MenuItemOf(Strings.Menu_Undo).IsEnabled);
        _editor.LastMenu!.Close();
        PressUndo();

        Assert.Equal(2, _board.Pages.Count);
        Assert.Equal("c", ItemNamed(1, "c").Name);
        Assert.Equal(1, _window.PageIndex);

        OpenSlotMenu(1, 2);
        Assert.False(MenuItemOf(Strings.Menu_Undo).IsEnabled);
        _editor.LastMenu!.Close();
    }

    // ---------------- ウィンドウの移動（アイテム以外の所をドラッグ） ----------------

    private void DragWindow(Point from, Point to)
    {
        _window.MouseDown(from, MouseButton.Left);
        _window.MouseMove(new Point(from.X + 2, from.Y + 1), RawInputModifiers.LeftMouseButton); // しきい値未満では動かない
        Assert.False(_window.IsMovingWindow);
        _window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        Assert.True(_window.IsMovingWindow);
        _window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void 空きスロットやタブをドラッグするとウィンドウが動き_タブは切り替わらない()
    {
        BoardEditingPage2();
        _window.Position = new PixelPoint(100, 100);
        var empty = Center(1, 2);
        DragWindow(empty, new Point(empty.X + 30, empty.Y + 20));
        Assert.Equal(new PixelPoint(130, 120), _window.Position);
        Assert.False(_window.IsMovingWindow);

        var tab = Tab(1);
        var t = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), _window)!.Value;
        DragWindow(t, new Point(t.X - 10, t.Y + 15));
        Assert.Equal(new PixelPoint(120, 135), _window.Position);
        Assert.Equal(0, _window.PageIndex); // 動かしただけではページは切り替わらない
        Assert.Equal(0, _invoked);
    }

    [AvaloniaFact]
    public void アイテムのドラッグではウィンドウは動かず_位置ロック中は動かない()
    {
        _window.Position = new PixelPoint(100, 100);
        Drag(Center(0, 0), Center(1, 2));
        Assert.Equal(new PixelPoint(100, 100), _window.Position);
        Assert.Equal((1, 2), (_a.Row, _a.Col));

        _window.AllowMove = false;
        var empty = Center(1, 1);
        _window.MouseDown(empty, MouseButton.Left);
        _window.MouseMove(new Point(empty.X + 40, empty.Y), RawInputModifiers.LeftMouseButton);
        _window.MouseUp(new Point(empty.X + 40, empty.Y), MouseButton.Left);
        Assert.Equal(new PixelPoint(100, 100), _window.Position);
    }

    [AvaloniaFact]
    public void ドラッグを始めた最初の移動からゴーストがカーソルの中心に来る()
    {
        var from = Center(0, 0);
        var to = new Point(from.X + 10, from.Y + 8);
        _window.MouseDown(from, MouseButton.Left);
        _window.MouseMove(to, RawInputModifiers.LeftMouseButton); // しきい値を越えた最初の 1 回
        var layer = _window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "DragLayer");
        var ghost = Assert.IsAssignableFrom<Control>(Assert.Single(layer.Children));
        var p = _window.TranslatePoint(to, layer)!.Value;

        Assert.True(ghost.DesiredSize.Width > 0);
        Assert.Equal(p.X - ghost.DesiredSize.Width / 2, Canvas.GetLeft(ghost), 3);
        Assert.Equal(p.Y - ghost.DesiredSize.Height / 2, Canvas.GetTop(ghost), 3);
        _window.MouseUp(to, MouseButton.Left);
    }

    [AvaloniaFact]
    public void ページが多くてタブがはみ出しても_切り替えたページのタブが見える位置までスクロールする()
    {
        for (int i = 2; i <= 12; i++) _board.Pages.Add(new Page { Name = $"とても長いページの名前 {i}", Rows = 4, Cols = 6 });
        _window.Render(_board, _hub.Current.Appearance, 0);
        Dispatcher.UIThread.RunJobs();
        var viewer = _window.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Content is StackPanel { Name: "TabsPanel" });
        Assert.True(viewer.Extent.Width > viewer.Viewport.Width); // はみ出している
        Assert.Equal(0, viewer.Offset.X);

        _window.SwitchPageTo(11);
        Dispatcher.UIThread.RunJobs();
        var tab = Tab(11);
        Assert.True(tab.Bounds.Right <= viewer.Offset.X + viewer.Viewport.Width + 0.5, $"right={tab.Bounds.Right} offset={viewer.Offset.X} viewport={viewer.Viewport.Width}");
        Assert.True(tab.Bounds.X >= viewer.Offset.X);

        _window.SwitchPageTo(0);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, viewer.Offset.X);
    }
}
