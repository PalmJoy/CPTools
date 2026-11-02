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
/// <summary>
/// 「マウスが離れたら閉じる」（SPEC §3.3、spec/SETTINGS.md popup.closeOnMouseLeave）の判定。
/// 盤面の矩形の外側までの最短距離が閾値を超えたら閉じる。ただし一度カーソルが盤面に入ってから判定を始める
/// （画面中央・固定座標に出してカーソルが最初から遠いとき、すぐ閉じないように）。
/// </summary>
public sealed class MouseLeaveTracker
{
    private bool _entered;

    /// <summary>表示するたびに呼ぶ。</summary>
    public void Reset() => _entered = false;

    /// <returns>閉じるべきなら true。</returns>
    public bool Update(ScreenPoint cursor, ScreenRect board, int distance)
    {
        if (board.Contains(cursor))
        {
            _entered = true;
            return false;
        }
        return _entered && DistanceOutside(cursor, board) > distance;
    }

    /// <summary>矩形の外側までの最短距離（中にあれば 0）。</summary>
    public static double DistanceOutside(ScreenPoint p, ScreenRect r)
    {
        int dx = Math.Max(Math.Max(r.X - p.X, 0), p.X - (r.Right - 1));
        int dy = Math.Max(Math.Max(r.Y - p.Y, 0), p.Y - (r.Bottom - 1));
        return Math.Sqrt((double)dx * dx + (double)dy * dy);
    }
}
