#if CPTOOLS_CLIPBOARD
using CPClipboard.Core.Input;
using CPClipboard.Core.Model;

namespace CPClipboard.Core.Popup;
#elif CPTOOLS_MONITOR
using CPSystemMonitor.Core.Input;
using CPSystemMonitor.Core.Model;

namespace CPSystemMonitor.Core.Popup;
#elif CPTOOLS_LAUNCHER
using FileLauncher.Core.Input;
using FileLauncher.Core.Model;

namespace FileLauncher.Core.Popup;
#else
#error A CPTools product must be selected.
#endif
/// <summary>ポップアップ盤面の表示位置（SPEC §3.3）。すべて物理 px。</summary>
public static class PopupPlacement
{
    /// <summary>
    /// カーソル位置表示で、辺・角のアンカーのときカーソルがウィンドウ外形からどれだけ内側に来るか（拡大率 1 のときの値）。
    /// ウィンドウには発光の余白 40 があるので、見えている枠の 20 外側になる（SPEC §3.3）。
    /// </summary>
    public const int CursorInset = 20;

    /// <summary>拡大率を掛けた寄せ量（物理 px。Mac は pt なので 1）。</summary>
    public static int InsetFor(double scaling) => (int)Math.Round(CursorInset * scaling, MidpointRounding.AwayFromZero);

    /// <summary>
    /// アンカーの点がカーソルに来るときのウィンドウ左上（作業領域への補正前）。列・行ごとに 左 / 上 = inset、中 = 半分、右 / 下 = 大きさ − inset。
    /// </summary>
    public static ScreenPoint CursorOrigin(ScreenPoint cursor, int width, int height, CursorAnchor anchor, int inset)
    {
        static int Along(int index, int size, int inset) => index switch { 0 => inset, 1 => size / 2, _ => size - inset };
        return new ScreenPoint(cursor.X - Along((int)anchor % 3, width, inset), cursor.Y - Along((int)anchor / 3, height, inset));
    }

    /// <param name="cursor">カーソル（トリガー）位置。</param>
    /// <param name="workAreaAt">指定座標を含む（無ければ最寄りの）モニタの作業領域（タスクバー除く）。</param>
    /// <param name="primaryWorkArea">「画面中央」用のプライマリモニタ作業領域。</param>
    /// <param name="anchor">カーソル位置のとき盤面のどの点をカーソルに合わせるか（popup.cursorAnchor。キーボード・マウス共通）。</param>
    /// <param name="scaling">盤面の拡大率（Windows の RenderScaling。Mac は 1）。カーソルの寄せ量に掛ける。</param>
    public static ScreenPoint Compute(
        PopupPlacementSettings settings, ScreenPoint cursor, int width, int height,
        Func<ScreenPoint, ScreenRect> workAreaAt, ScreenRect primaryWorkArea, CursorAnchor anchor = CursorAnchor.Top, double scaling = 1.0)
    {
        switch (settings.Position)
        {
            case PopupPosition.LastPosition or PopupPosition.Fixed when settings.X.HasValue && settings.Y.HasValue:
                // 保存座標のモニタに収める（外したモニタの座標なら最寄りのモニタへ）
                var saved = new ScreenPoint(settings.X.Value, settings.Y.Value);
                return ClampInto(saved, width, height, workAreaAt(saved));
            case PopupPosition.ScreenCenter:
                var a = primaryWorkArea;
                return ClampInto(new ScreenPoint(a.X + (a.Width - width) / 2, a.Y + (a.Height - height) / 2), width, height, a);
            default: // Cursor（前回位置・固定座標が未設定の場合もここ）
                return ClampInto(CursorOrigin(cursor, width, height, anchor, InsetFor(scaling)), width, height, workAreaAt(cursor));
        }
    }

    /// <summary>はみ出さないよう作業領域内に収める。盤面の方が大きければ左上を合わせる。</summary>
    public static ScreenPoint ClampInto(ScreenPoint p, int width, int height, ScreenRect area) => new(
        Math.Clamp(p.X, area.X, Math.Max(area.X, area.Right - width)),
        Math.Clamp(p.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
}
