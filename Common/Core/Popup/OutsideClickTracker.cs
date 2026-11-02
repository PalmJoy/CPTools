#if CPTOOLS_CLIPBOARD
using CPClipboard.Core.Input;

namespace CPClipboard.Core.Popup;
#elif CPTOOLS_MONITOR
using CPSystemMonitor.Core.Input;

namespace CPSystemMonitor.Core.Popup;
#elif CPTOOLS_LAUNCHER
using FileLauncher.Core.Input;

namespace FileLauncher.Core.Popup;
#else
#error A CPTools product must be selected.
#endif
public enum OutsideClickResult
{
    /// <summary>何もしない（盤面内の操作、または盤面が非表示）。</summary>
    None,

    /// <summary>盤面外をクリックした → 閉じる。</summary>
    CloseClick,

    /// <summary>盤面外でドラッグを始めて盤面外で離した → 閉じる。</summary>
    CloseDragEndedOutside,

    /// <summary>盤面外から盤面へドラッグして離した（D&D 登録）→ 開いたまま。</summary>
    KeepOpenDroppedOnBoard,
}

/// <summary>
/// 盤面外クリックで閉じる判定（SPEC §3.3）。押下時ではなく離上時に判定する
/// （Explorer / Finder からつかんで盤面へ D&D 登録できるように）。
/// 押下・離上はグローバルフックから渡す（ウィンドウの非アクティブ化イベントは外クリックで来ないことがあるため）。
/// </summary>
public sealed class OutsideClickTracker
{
    public const int DragThresholdPx = 4;

    private ScreenPoint? _pressAt;

    /// <summary>盤面外でボタンが押されている間 true。この間は非アクティブ化でも閉じない。</summary>
    public bool IsPressedOutside => _pressAt is not null;

    public void OnDown(ScreenPoint point, bool boardVisible, ScreenRect boardBounds)
    {
        _pressAt = boardVisible && !boardBounds.Contains(point) ? point : null;
    }

    public OutsideClickResult OnUp(ScreenPoint point, bool boardVisible, ScreenRect boardBounds)
    {
        if (_pressAt is not { } start) return OutsideClickResult.None;
        _pressAt = null;
        if (!boardVisible) return OutsideClickResult.None;

        bool dragged = Math.Abs(point.X - start.X) > DragThresholdPx || Math.Abs(point.Y - start.Y) > DragThresholdPx;
        if (!dragged) return OutsideClickResult.CloseClick;
        return boardBounds.Contains(point) ? OutsideClickResult.KeepOpenDroppedOnBoard : OutsideClickResult.CloseDragEndedOutside;
    }

    public void Reset() => _pressAt = null;
}
