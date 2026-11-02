#if CPTOOLS_CLIPBOARD
using CPClipboard.Core.Model;

namespace CPClipboard.Core.Theming;
#elif CPTOOLS_MONITOR
using CPSystemMonitor.Core.Model;

namespace CPSystemMonitor.Core.Theming;
#elif CPTOOLS_LAUNCHER
using FileLauncher.Core.Model;

namespace FileLauncher.Core.Theming;
#else
#error A CPTools product must be selected.
#endif
/// <summary>背景画像の置き方（元画像のどこを、面のどこへ描くか。単位は論理 px）。</summary>
public readonly record struct BackgroundPlacement(
    (double X, double Y, double Width, double Height) Source,
    (double X, double Y, double Width, double Height) Dest,
    int TileColumns,
    int TileRows)
{
    public static readonly BackgroundPlacement Empty = new((0, 0, 0, 0), (0, 0, 0, 0), 0, 0);

    public bool IsEmpty => TileColumns == 0 || TileRows == 0;
}

/// <summary>背景画像の表示方法 5 種の意味（SPEC §3.7「表示方法の意味」）。作り置き・失敗時の直描き・テストで共有する。</summary>
public static class BackgroundLayout
{
    public static BackgroundPlacement Compute(double faceWidth, double faceHeight, double imageWidth, double imageHeight, BackgroundFit fit)
    {
        double W = faceWidth, H = faceHeight, w = imageWidth, h = imageHeight;
        if (W <= 0 || H <= 0 || w <= 0 || h <= 0) return BackgroundPlacement.Empty;
        switch (fit)
        {
            case BackgroundFit.Fit:
            {
                double s = Math.Min(W / w, H / h);
                return new((0, 0, w, h), ((W - w * s) / 2, (H - h * s) / 2, w * s, h * s), 1, 1);
            }
            case BackgroundFit.Stretch:
                return new((0, 0, w, h), (0, 0, W, H), 1, 1);
            case BackgroundFit.Center:
            {
                // 原寸を中央に。面より大きい辺は元画像を中央で切り、その辺いっぱいに描く
                double sw = Math.Min(w, W), sh = Math.Min(h, H);
                return new(((w - sw) / 2, (h - sh) / 2, sw, sh), ((W - sw) / 2, (H - sh) / 2, sw, sh), 1, 1);
            }
            case BackgroundFit.Tile:
                return new((0, 0, w, h), (0, 0, w, h), (int)Math.Ceiling(W / w), (int)Math.Ceiling(H / h));
            default: // Fill: 面を覆う最小の倍率で、はみ出す部分は中央で切る
            {
                double s = Math.Max(W / w, H / h);
                double sw = W / s, sh = H / s;
                return new(((w - sw) / 2, (h - sh) / 2, sw, sh), (0, 0, W, H), 1, 1);
            }
        }
    }
}
