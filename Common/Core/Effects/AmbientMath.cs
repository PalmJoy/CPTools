#if CPTOOLS_CLIPBOARD
namespace CPClipboard.Core.Effects;
#elif CPTOOLS_MONITOR
namespace CPSystemMonitor.Core.Effects;
#elif CPTOOLS_LAUNCHER
namespace FileLauncher.Core.Effects;
#else
#error A CPTools product must be selected.
#endif
/// <summary>常時の演出の計算（spec/EFFECTS.md「常時の演出の詳細」）。</summary>
public static class AmbientMath
{
    /// <summary>経過時間 → 周期の中の位置（0 以上 1 未満）。周期 0 以下なら 0。</summary>
    public static double Phase(double elapsedMs, int periodMs)
    {
        if (periodMs <= 0) return 0;
        double p = elapsedMs % periodMs / periodMs;
        return p < 0 ? p + 1 : p;
    }

    /// <summary>明滅の強さ（0〜peak）。サイン波（0 から始まり半周期で peak）。</summary>
    public static double PulseLevel(double elapsedMs, int periodMs, double peak) =>
        peak * (1 - Math.Cos(2 * Math.PI * Phase(elapsedMs, periodMs))) / 2;

    /// <summary>
    /// VSync（1〜3）→ 更新の最小間隔 ms。60 Hz の N 枚ぶん − 約 3 ms のマージン（ちょうどにすると時計の揺れで N 枚目が通らず、
    /// 2 段の fps が混ざってカクつく）。時間で決めるので 120 Hz の画面でも 1 = 60 fps（spec/EFFECTS.md「時計」）。
    /// </summary>
    public static double FrameIntervalMs(int vsync) => vsync <= 1 ? 13 : vsync == 2 ? 30 : 47;

    /// <summary>走査線の帯の上端の y（帯が面の上の外から入り、下の外へ抜ける。phase 0〜1 で一定速度）。</summary>
    public static double SweepOffset(double phase, double faceHeight, double bandHeight) =>
        -bandHeight + phase * (faceHeight + bandHeight);
}
