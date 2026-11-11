using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using FileLauncher.Core.Items;
using FileLauncher.Core.Model;
using FileLauncher.Core.Storage;
using FileLauncher.Platform;

namespace FileLauncher.App;

/// <summary>
/// 盤面の編集の司令塔（M3: D&D 登録・Drop-to-Open、M4: 盤面内の移動・入替・複製・削除・色・右クリックメニュー）。
/// 変更したら盤面を描き直し、board.json は 500 ms まとめて書く（世代バックアップは 10 分おき。SPEC §6.7 / §9.3）。
/// </summary>
internal sealed class BoardEditor
{
    private readonly BoardWindow _window;
    private readonly Func<IBoardController> _controller; // 今の表示モードのコントローラ（モード切替で変わる）
    private readonly IPlatformServices _platform;
    private readonly AppDataStore _store;
    private readonly SettingsHub _hub;
    private readonly Board _board;
    private readonly ItemFactory _factory;
    private readonly Avalonia.Threading.DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _dirty;
    private bool _readOnlyWarned;
    private string? _committed;                       // 直近に確定した盤面の写し（次の Commit で _undo になる）
    private (string Board, int PageIndex)? _undo;     // 元に戻す先（1 段階。SPEC §6.7）

    /// <summary>メニューの共通部（表示モード・設定・終了）を App が渡す。</summary>
    public Action<DisplayMode>? SetDisplayMode { get; set; }
    public Action? OpenSettings { get; set; }
    public Action? Quit { get; set; }
    public Action? CheckForUpdates { get; set; }
    public Action? ShowAbout { get; set; }

    /// <summary>元に戻せる編集があるか（メニューの「元に戻す」の有効・無効）。</summary>
    internal bool CanUndo => _undo is not null;

    /// <summary>最後に開いた右クリックメニュー（UI テスト用）。</summary>
    internal ContextMenu? LastMenu { get; private set; }

    public BoardEditor(BoardWindow window, Func<IBoardController> controller, IPlatformServices platform, AppDataStore store, SettingsHub hub, Board board)
    {
        _window = window;
        _controller = controller;
        _platform = platform;
        _store = store;
        _hub = hub;
        _board = board;
        _factory = new ItemFactory(platform.Shell.ReadShortcut);
    }

    private AppSettings _settings => _hub.Current;

    public void Attach()
    {
        _window.Dropped += r => _ = OnDroppedAsync(r);
        _window.ItemDropped += d => _ = OnItemDroppedAsync(d);
        _window.ContextMenuRequested += c => _ = ShowMenuAsync(c);
        _window.PageSettingsRequested += index => _ = EditPageAsync(index);
        _window.EmptySlotActivated += cell => _ = NewItemAsync(cell);
        _window.EditRequested += item => _ = EditItemAsync(item);
        _window.DeleteRequested += item => Delete(CurrentPage, item);
        _window.UndoRequested += Undo;
        _window.BoardOptions = () => _settings.Board;
        _committed = BoardSnapshot.Capture(_board);
        _saveTimer.Tick += (_, _) => Flush();
    }

    // ---------------- 変更と保存（SPEC §6.7） ----------------

    /// <summary>盤面を変えたあとに呼ぶ: 描き直し、保存を予約する。</summary>
    private void Commit(string reason, int? pageIndex = null)
    {
        // 変更前の写しと、そのとき表示していたページを「元に戻す」先にする
        if (_committed is not null) _undo = (_committed, _window.PageIndex);
        _committed = BoardSnapshot.Capture(_board);
        _window.Render(_board, _settings.Appearance, pageIndex ?? _window.PageIndex);
        AppLog.Info($"盤面の編集: {reason}");
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>盤面を BoardEditor の外で変えたあと、今の状態を「元に戻す」の基準にする（UI テスト用）。</summary>
    internal void ResetUndoBaseline()
    {
        _committed = BoardSnapshot.Capture(_board);
        _undo = null;
    }

    /// <summary>直前の編集を 1 段階だけ戻す（Ctrl+Z / ⌘Z・メニュー）。戻したあとはもう戻せない。</summary>
    internal void Undo()
    {
        if (_undo is not { } undo) return;
        _undo = null;
        BoardSnapshot.Restore(_board, undo.Board);
        _committed = undo.Board;
        _window.Render(_board, _settings.Appearance, Math.Min(undo.PageIndex, _board.Pages.Count - 1));
        AppLog.Info("盤面の編集: 元に戻す");
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>予約中の保存を今すぐ書く（モード切替・終了・設定画面を開く前に App が呼ぶ）。</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        _dirty = false;
        if (_store.BoardStore.IsReadOnly)
        {
            if (!_readOnlyWarned) Toast.Show(Strings.Toast_ReadOnlyBoard);
            _readOnlyWarned = true;
            return;
        }
        _store.SaveBoard(_board);
        AppLog.Info("盤面を保存");
    }

    private Page CurrentPage => _board.Pages[_window.PageIndex];

    // ---------------- 盤面内のドラッグ（SPEC §6.2） ----------------

    private async Task OnItemDroppedAsync(SlotDrop d)
    {
        var from = _board.Pages[d.FromPage];
        var to = _board.Pages[d.ToPage ?? d.FromPage]; // タブで別ページへ切り替えてから落としたときは移動先のページ
        bool sameCell = d.Target == DragTargetKind.Slot && d.Cell == (d.Item.Row, d.Item.Col) && d.ToPage == d.FromPage;
        bool occupied = d.Target == DragTargetKind.Slot && d.Cell is { } c
            && BoardEditing.ItemAt(_board.Pages[d.ToPage ?? d.FromPage], c.Row, c.Col) is { } other && !ReferenceEquals(other, d.Item);
        var outcome = BoardDragRules.Classify(d.Target, occupied, sameCell, d.Copy);
        switch (outcome)
        {
            case DragOutcome.Move:
            case DragOutcome.Swap:
                BoardEditing.MoveOrSwap(from, d.Item, to, d.Cell!.Value.Row, d.Cell.Value.Col);
                Commit($"{(outcome == DragOutcome.Swap ? "入替" : "移動")} '{d.Item.Name}' → {d.Cell}");
                break;
            case DragOutcome.Duplicate:
                BoardEditing.Duplicate(d.Item, to, d.Cell!.Value.Row, d.Cell.Value.Col);
                Commit($"複製 '{d.Item.Name}' → {d.Cell}");
                break;
            case DragOutcome.MoveToPage:
            case DragOutcome.DuplicateToPage:
                {
                    if (ReferenceEquals(to, from)) break;
                    if (BoardEditing.FirstEmpty(to) is not { } cell)
                    {
                        Toast.Show(Strings.FormatToast_PageFull(to.Name));
                        break;
                    }
                    if (outcome == DragOutcome.MoveToPage) BoardEditing.MoveOrSwap(from, d.Item, to, cell.Row, cell.Col);
                    else BoardEditing.Duplicate(d.Item, to, cell.Row, cell.Col);
                    Commit($"{(outcome == DragOutcome.MoveToPage ? "ページへ移動" : "ページへ複製")} '{d.Item.Name}' → '{to.Name}'");
                    break;
                }
            case DragOutcome.Delete:
                Delete(from, d.Item);
                break;
        }
    }

    // 削除はどの操作でも確認しない（2026-10-03 ユーザー判断。取り消しは「元に戻す」）
    private void Delete(Page page, LauncherItem item)
    {
        if (BoardEditing.Remove(page, item)) Commit($"削除 '{item.Name}'");
    }

    // ---------------- 登録・編集ダイアログ（SPEC §6.4） ----------------

    /// <summary>空きスロット（cell。埋まっていれば・null なら最初の空き）へ新しいアイテムを登録する。</summary>
    private async Task NewItemAsync((int Row, int Col)? cell)
    {
        var page = CurrentPage;
        var draft = new LauncherItem { Kind = ItemKind.File };
        var result = await _controller().RunModalAsync(() => ItemEditorWindow.ShowAsync(_window, draft, isNew: true, _window.Icons, page, _settings.Appearance));
        if (result is null) return;
        var target = cell is { } c && BoardEditing.ItemAt(page, c.Row, c.Col) is null ? c : BoardEditing.FirstEmpty(page);
        if (target is not { } t)
        {
            Toast.Show(Strings.Toast_NoEmptySlot);
            return;
        }
        result.Row = t.Row;
        result.Col = t.Col;
        page.Items.Add(result);
        Commit($"登録（ダイアログ） '{result.Name}' → {t}");
        _window.PlayRegistration([result]);
    }

    private async Task EditItemAsync(LauncherItem item)
    {
        var page = CurrentPage;
        var result = await _controller().RunModalAsync(() => ItemEditorWindow.ShowAsync(_window, item, isNew: false, _window.Icons, page, _settings.Appearance));
        if (result is null) return;
        CopyEditable(result, item);
        Commit($"編集 '{item.Name}'");
    }

    /// <summary>編集ダイアログの結果を元のアイテムへ写す（Id と位置は変えない）。</summary>
    internal static void CopyEditable(LauncherItem from, LauncherItem to)
    {
        to.Kind = from.Kind;
        to.Target = from.Target;
        to.Name = from.Name;
        to.Args = from.Args;
        to.WorkingDir = from.WorkingDir;
        to.IconOverride = from.IconOverride;
        to.LaunchMode = from.LaunchMode;
        to.RunAsAdmin = from.RunAsAdmin;
        to.Hotkey = from.Hotkey;
        to.Color = from.Color;
    }

    private async Task CopyPathAsync(LauncherItem item)
    {
        if (TopLevel.GetTopLevel(_window)?.Clipboard is not { } clipboard) return;
        string path = item.LinkPath ?? item.Target;
        await clipboard.SetTextAsync(item.Kind == ItemKind.Url ? path : LaunchArgs.ExpandPath(path));
    }

    /// <summary>クリップボードのファイル / テキスト（パス・URL）を登録する。</summary>
    private async Task PasteAsync((int Row, int Col)? cell)
    {
        if (TopLevel.GetTopLevel(_window)?.Clipboard is not { } clipboard) return;
        var items = new List<LauncherItem>();
        if (await clipboard.TryGetFilesAsync() is { } files)
        {
            foreach (var f in files)
                if (f.TryGetLocalPath() is { } p && _factory.FromPath(p) is { } item) items.Add(item);
        }
        if (items.Count == 0 && await clipboard.TryGetTextAsync() is { } text && _factory.FromText(text) is { } fromText) items.Add(fromText);
        if (items.Count == 0)
        {
            Toast.Show(Strings.Toast_NothingToPaste);
            return;
        }
        var page = CurrentPage;
        var (row, col) = cell ?? BoardEditing.FirstEmpty(page) ?? (0, 0);
        int placed = BoardEditing.Place(page, row, col, items);
        if (placed < items.Count) Toast.Show(Loc.Count(items.Count - placed, Strings.Toast_NotAdded_One, Strings.Toast_NotAdded_Other));
        if (placed > 0) Commit($"貼り付け {placed} 件");
    }

    // ---------------- ページ（SPEC §6.5） ----------------

    private async Task EditPageAsync(int index)
    {
        if (index < 0 || index >= _board.Pages.Count) return;
        var page = _board.Pages[index];
        var result = await _controller().RunModalAsync(() => PageSettingsWindow.ShowAsync(_window, page, _settings.Appearance));
        if (result is null) return;
        page.Name = result.Name;
        page.Rows = result.Rows;
        page.Cols = result.Cols;
        page.ButtonSize = result.ButtonSize;
        int moved = BoardEditing.ReflowOutOfRange(page, allowGrow: false);
        Commit($"ページの設定 '{page.Name}' {page.Rows}x{page.Cols}", index);
        if (moved > 0) Toast.Show(Loc.Count(moved, Strings.Toast_MovedToEmpty_One, Strings.Toast_MovedToEmpty_Other), error: false);
    }

    private void RemovePage(int index)
    {
        if (!PageEditing.CanRemove(_board)) return;
        var page = _board.Pages[index];
        int next = PageEditing.Remove(_board, index);
        if (next >= 0) Commit($"ページを削除 '{page.Name}'", next);
    }

    private void MovePage(int index, int delta)
    {
        int to = PageEditing.Move(_board, index, delta);
        if (to >= 0) Commit($"ページを移動 '{_board.Pages[to].Name}'", to);
    }

    // ---------------- 右クリックメニュー（SPEC §6.3） ----------------

    private async Task ShowMenuAsync(SlotContext ctx)
    {
        var page = CurrentPage;
        var menu = BoardMenus.Build(ctx, new MenuActions(
            Launch: item => _controller().Launch(item, null),
            LaunchAsAdmin: OperatingSystem.IsWindows() ? item =>
            {
                var admin = BoardEditing.Clone(item, keepHotkey: false);
                admin.RunAsAdmin = true;
                _controller().Launch(admin, null);
            } : null,
            Reveal: item =>
            {
                var r = _platform.Shell.RevealInFileManager(item.LinkPath ?? item.Target, _settings.General.FolderOpenTarget);
                if (!r.Success) Toast.Show(Strings.FormatToast_RevealFailed(item.Name, Loc.LaunchError(r)));
            },
            Edit: item => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = EditItemAsync(item)),
            SetColor: (item, color) =>
            {
                item.Color = color;
                Commit($"色 '{item.Name}' → {color?.ToString() ?? "なし"}");
            },
            Duplicate: item =>
            {
                // 右隣から読み順で空きを探し、無ければ先頭から
                var cell = BoardEditing.FindEmptySlots(page, item.Row, item.Col, 1) is [var after] ? after : BoardEditing.FirstEmpty(page);
                if (cell is not { } c) { Toast.Show(Strings.Toast_NoEmptySlot); return; }
                BoardEditing.Duplicate(item, page, c.Row, c.Col);
                Commit($"複製 '{item.Name}' → {c}");
            },
            Delete: item => Avalonia.Threading.Dispatcher.UIThread.Post(() => Delete(page, item)),
            NewItem: () => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = NewItemAsync(ctx.Cell)),
            Paste: () => _ = PasteAsync(ctx.Cell),
            Undo: _undo is not null ? Undo : null,
            SetDisplayMode: mode => SetDisplayMode?.Invoke(mode),
            CurrentMode: _settings.General.DisplayMode,
            OpenSettings: () => Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenSettings?.Invoke()),
            CheckForUpdates: CheckForUpdates is { } check ? () => Avalonia.Threading.Dispatcher.UIThread.Post(check) : null,
            About: ShowAbout is { } about ? () => Avalonia.Threading.Dispatcher.UIThread.Post(about) : null,
            Quit: () => Quit?.Invoke(),
            OpenNewWindow: item => _controller().Launch(item, null, newWindow: true),
            CopyPath: item => _ = CopyPathAsync(item),
            OpenTerminal: item =>
            {
                if (TerminalLocation.For(item) is not { } dir) return;
                var r = _platform.Shell.OpenTerminal(dir);
                if (!r.Success) Toast.Show(Strings.FormatToast_OpenTerminalFailed(dir, Loc.LaunchError(r)));
            },
            Pages: new PageActions(
                NewPage: index =>
                {
                    int i = PageEditing.Add(_board, index, _settings.Appearance.DefaultRows, _settings.Appearance.DefaultCols);
                    Commit($"新規ページ '{_board.Pages[i].Name}'", i);
                },
                Settings: index => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = EditPageAsync(index)),
                Duplicate: index =>
                {
                    int i = PageEditing.Duplicate(_board, index, Strings.Page_CopyName);
                    Commit($"ページを複製 '{_board.Pages[i].Name}'", i);
                },
                Remove: PageEditing.CanRemove(_board) ? index => Avalonia.Threading.Dispatcher.UIThread.Post(() => RemovePage(index)) : null,
                MoveLeft: ctx.PageIndex > 0 ? index => MovePage(index, -1) : null,
                MoveRight: ctx.PageIndex < _board.Pages.Count - 1 ? index => MovePage(index, +1) : null)));
        if (menu is null) return;
        LastMenu = menu;

        // メニューは別のトップレベルに出るので、出している間は盤面の閉じる条件を止める
        var closed = new TaskCompletionSource<bool>();
        menu.Closed += (_, _) => closed.TrySetResult(true);
        menu.Placement = Avalonia.Controls.PlacementMode.Pointer;
        await _controller().RunModalAsync(async () =>
        {
            menu.Open(ctx.Anchor);
            if (!menu.IsOpen) return true;
            return await closed.Task;
        });
    }

    private async Task OnDroppedAsync(DropRequest r)
    {
        try
        {
            if (r.TargetItem is { } target) await DropOntoItemAsync(target, r);
            else await RegisterAsync(r);
        }
        catch (Exception ex)
        {
            AppLog.Info($"drop 処理失敗: {ex}");
            Toast.Show(Strings.FormatToast_DropFailed(ex.Message));
        }
    }

    // ---------------- 登録（SPEC §6.1） ----------------

    private async Task RegisterAsync(DropRequest r)
    {
        var items = r.Paths.Select(_factory.FromPath).OfType<LauncherItem>().ToList();
        if (items.Count == 0 && r.Text is { } text && _factory.FromText(text) is { } fromText) items.Add(fromText);
        if (items.Count == 0)
        {
            Toast.Show(Strings.Toast_NothingToAdd);
            return;
        }

        var page = _board.Pages[_window.PageIndex];
        var (row, col) = r.Cell ?? (0, 0);

        int extra = BoardEditing.ExtraRowsNeeded(page, row, col, items.Count);
        int canAdd = Math.Min(extra, Page.MaxGrid - page.Rows);
        if (canAdd > 0)
        {
            bool add = await _controller().RunModalAsync(() => Dialogs.ConfirmAsync(_window, Strings.Dialog_AddRows_Title,
                Strings.FormatDialog_AddRows(items.Count, canAdd),
                ok: Strings.Dialog_AddRows_Ok, cancel: Strings.Dialog_AddRows_Cancel));
            if (add) page.Rows += canAdd;
        }

        int placed = BoardEditing.Place(page, row, col, items);
        AppLog.Info($"登録 {placed}/{items.Count} 件 → page '{page.Name}' ({row},{col})〜: {string.Join(", ", items.Take(placed).Select(i => $"{i.Kind}:{i.Name}"))}");
        if (placed < items.Count)
            Toast.Show(Loc.Count(items.Count - placed, Strings.Toast_NotAdded_One, Strings.Toast_NotAdded_Other));
        if (placed > 0)
        {
            Commit($"登録 {placed} 件");
            _window.PlayRegistration(items.Take(placed));
        }
    }

    // ---------------- Drop-to-Open（SPEC §5.3） ----------------

    /// <summary>フォルダアイテムへコピー / 移動し、完了したら件数をトーストで知らせる（SPEC §5.3）。</summary>
    private static async Task TransferIntoAsync(LauncherItem target, IReadOnlyList<string> paths, bool move)
    {
        string folder = LaunchArgs.ExpandPath(target.Target);
        if (!Directory.Exists(folder))
        {
            Toast.Show(string.Format(move ? Strings.Toast_Transfer_FolderMissing_Move : Strings.Toast_Transfer_FolderMissing_Copy, target.Name, folder));
            return;
        }
        var result = await FileTransfer.TransferAsync(folder, paths, move);
        AppLog.Info($"{(move ? "move" : "copy")} {paths.Count} 件 → '{target.Name}': {result}");

        int done = result.Added + result.Renamed;
        string message;
        if (done == 0 && result.Skipped > 0)
        {
            message = string.Format(move ? Strings.Toast_Transfer_AllSame_Move : Strings.Toast_Transfer_AllSame_Copy, target.Name, result.Skipped);
        }
        else
        {
            var details = new List<string>();
            if (result.Renamed > 0) details.Add(Strings.FormatToast_Transfer_Renamed(result.Renamed));
            if (result.Skipped > 0) details.Add(Strings.FormatToast_Transfer_Skipped(result.Skipped));
            message = string.Format(done == 1
                ? move ? Strings.Toast_Transfer_Moved_One : Strings.Toast_Transfer_Copied_One
                : move ? Strings.Toast_Transfer_Moved_Other : Strings.Toast_Transfer_Copied_Other, target.Name, done)
                + (details.Count > 0 ? Strings.FormatToast_Transfer_Details(Loc.Join(details)) : "");
        }
        if (result.Failed > 0) message += "\n" + Strings.FormatToast_Transfer_Failed(result.Failed);
        Toast.Show(message, error: result.Failed > 0);
    }

    private async Task DropOntoItemAsync(LauncherItem target, DropRequest r)
    {
        if (r.Paths.Count == 0)
        {
            Toast.Show(Strings.Toast_DropOnItemNeedsFiles);
            return;
        }

        switch (target.Kind)
        {
            case ItemKind.Folder:
                if (r.Move && _settings.Editing.ConfirmMoveOnDrop)
                {
                    bool ok = await _controller().RunModalAsync(() => Dialogs.ConfirmAsync(_window, Strings.Dialog_ConfirmMove_Title,
                        string.Format(r.Paths.Count == 1 ? Strings.Dialog_ConfirmMove_One : Strings.Dialog_ConfirmMove_Other, r.Paths.Count, target.Name),
                        ok: Strings.Dialog_ConfirmMove_Ok));
                    if (!ok) return;
                }
                await TransferIntoAsync(target, r.Paths, r.Move);
                break;

            case ItemKind.Url:
                Toast.Show(Strings.Toast_UrlCannotOpenFiles);
                break;

            default:
                _controller().Launch(target, r.Paths);
                break;
        }
    }

}
