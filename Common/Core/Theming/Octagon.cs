#if CPTOOLS_CLIPBOARD
namespace CPClipboard.Core.Theming;
#elif CPTOOLS_MONITOR
namespace CPSystemMonitor.Core.Theming;
#elif CPTOOLS_LAUNCHER
namespace FileLauncher.Core.Theming;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 面取りした枠（八角形）の形の唯一の定義（SPEC §3.9「面取りの仕様」）。枠の面・枠線・切り抜き・発光・光の玉の経路がこれを使う。
/// </summary>
public static class Octagon
{
    /// <summary>
    /// 8 頂点。左上の斜辺の終わり（上辺の始まり (x + c, y)）から時計回り。面取り c は min(幅, 高さ) / 2 に丸める。
    /// </summary>
    public static (double X, double Y)[] Points(double x, double y, double width, double height, double chamfer)
    {
        double w = Math.Max(0, width), h = Math.Max(0, height);
        double c = Math.Clamp(chamfer, 0, Math.Min(w, h) / 2);
        return
        [
            (x + c, y), (x + w - c, y),
            (x + w, y + c), (x + w, y + h - c),
            (x + w - c, y + h), (x + c, y + h),
            (x, y + h - c), (x, y + c),
        ];
    }

    /// <summary>
    /// 八角形を外側へ distance だけ平行移動（オフセット）したときの面取りの量。辺の向きは変わらず、斜辺も法線方向に distance 動く。
    /// 内側（負の distance）も同じ式。
    /// </summary>
    public static double OffsetChamfer(double chamfer, double distance) =>
        Math.Max(0, chamfer + distance * (2 - Math.Sqrt(2)));
}
