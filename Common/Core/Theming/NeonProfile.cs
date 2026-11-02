#if CPTOOLS_CLIPBOARD
namespace CPClipboard.Core.Theming;
#elif CPTOOLS_MONITOR
namespace CPSystemMonitor.Core.Theming;
#elif CPTOOLS_LAUNCHER
namespace FileLauncher.Core.Theming;
#else
#error A CPTools product must be selected.
#endif
/// <summary>発光の層 1 枚（ぼかしの大きさ px と不透明度 0〜1）。</summary>
public readonly record struct NeonLayer(double Blur, double Alpha);

/// <summary>
/// ネオンの発光の断面（SPEC §3.6「発光の作り方」、§3.9「面取りの仕様」）。辺から距離 d の点の不透明度を、
/// Avalonia 11.3 の BoxShadow と同じガウスぼかしで計算する。面取りした枠の発光は、この断面を入れ子の塗り（リング）で描く。
/// </summary>
public static class NeonProfile
{
    /// <summary>リングの刻み（px）。</summary>
    public const double Step = 0.5;

    /// <summary>「見えない」とみなす不透明度（8 bit の半分）。</summary>
    public const double Invisible = 0.5 / 255;

    /// <summary>ぼかしの大きさ → ガウスの σ（Avalonia 11.3 DrawingContextImpl.SkBlurRadiusToSigma と同じ換算）。</summary>
    public static double Sigma(double blur) => blur <= 0 ? 0 : 0.288675 * blur + 0.5;

    /// <summary>相補誤差関数（Abramowitz–Stegun 7.1.26、誤差 1.5e−7）。</summary>
    public static double Erfc(double x)
    {
        if (x < 0) return 2 - Erfc(-x);
        double t = 1 / (1 + 0.3275911 * x);
        double poly = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
        return poly * Math.Exp(-x * x);
    }

    /// <summary>1 層の辺から距離 d（0 以上）の不透明度。ぼかしなしなら辺の外は 0。</summary>
    public static double Layer(NeonLayer layer, double distance)
    {
        double sigma = Sigma(layer.Blur);
        if (sigma <= 0) return distance <= 0 ? layer.Alpha : 0;
        return layer.Alpha * 0.5 * Erfc(distance / (sigma * Math.Sqrt(2)));
    }

    /// <summary>層を重ねた合成の不透明度 1 − Π(1 − g)。</summary>
    public static double Composite(IReadOnlyList<NeonLayer> layers, double distance)
    {
        double clear = 1;
        foreach (var l in layers) clear *= 1 - Layer(l, Math.Max(0, distance));
        return 1 - clear;
    }

    /// <summary>合成 × gain が見えなくなる最小の距離（Step の倍数）。gain は明滅で明るくする最大倍率。</summary>
    public static double Extent(IReadOnlyList<NeonLayer> layers, double gain = 1)
    {
        if (layers.Count == 0) return 0;
        for (double d = 0; d < 1000; d += Step)
            if (Composite(layers, d) * gain < Invisible) return d;
        return 1000;
    }

    /// <summary>
    /// 外側から内側へ並んだ帯の列。帯 i は辺からの距離 (Inner_i, Outer_i] を、帯の中央の合成の不透明度 Alpha_i で 1 回だけ塗る。
    /// 帯は重ならない（入れ子の塗りを重ねると、外側の薄い部分で 1 枚ごとの増分が 8 bit の刻みより小さくなって消える）。
    /// </summary>
    public static IReadOnlyList<(double Outer, double Inner, double Alpha)> Bands(IReadOnlyList<NeonLayer> layers, double gain = 1)
    {
        var bands = new List<(double, double, double)>();
        for (double outer = Extent(layers, gain); outer > 0; outer -= Step)
        {
            double inner = Math.Max(0, outer - Step);
            bands.Add((outer, inner, Composite(layers, (outer + inner) / 2)));
        }
        return bands;
    }
}
