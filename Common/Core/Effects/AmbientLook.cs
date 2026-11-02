#if CPTOOLS_CLIPBOARD
using CPClipboard.Core.Theming;

namespace CPClipboard.Core.Effects;
#elif CPTOOLS_MONITOR
using CPSystemMonitor.Core.Theming;

namespace CPSystemMonitor.Core.Effects;
#elif CPTOOLS_LAUNCHER
using FileLauncher.Core.Theming;

namespace FileLauncher.Core.Effects;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 常時の演出の見た目の定数（spec/EFFECTS.md「常時の演出の詳細」）。Avalonia の層（Windows）と Core Animation の層（macOS）が
/// 同じ値を参照する（見た目の一致をコードの共有で保証する。issue/CA_AMBIENT.md §2）。
/// </summary>
public static class AmbientLook
{
    public const double OrbCoreRadius = 2.5, OrbHaloRadius = 9;
    public const int OrbTailPoints = 10;
    public const double OrbTailLength = 48, OrbTailStartRadius = 7, OrbTailEndRadius = 3, OrbTailStartOpacity = 0.6;

    /// <summary>暈の中心の不透明度（放射グラデーションの中心 α230、外周 0）。</summary>
    public const double OrbHaloAlpha = 230 / 255.0;

    public const double PulseWeakPeak = 0.45, PulseStrongPeak = 0.9;

    /// <summary>明滅で発光が最も明るくなる倍率（静的な発光 + 明滅「強」の画像）。発光の広がりの終わり（窓の余白）の計算に使う。</summary>
    public const double MaxPulseGain = 1 + PulseStrongPeak;

    public const double BeamBandHeight = 60;

    /// <summary>帯の縦グラデーション（位置 0〜1, α 0〜255）。上端は透明、下へ行くほど明るい。</summary>
    public static readonly (double Offset, byte Alpha)[] BeamStops = [(0, 0), (0.85, 28), (1, 60)];

    /// <summary>常時の演出が再開するときのフェードイン。</summary>
    public const int AmbientFadeInMs = 250;

    /// <summary>玉の芯の色 = 主色を白と 60% 混ぜる。</summary>
    public static RgbColor OrbCoreColor(RgbColor accent)
    {
        static byte Mix(byte v) => (byte)(v + (255 - v) * 0.6);
        return new RgbColor(Mix(accent.R), Mix(accent.G), Mix(accent.B));
    }

    /// <summary>
    /// 尾の 10 個（半径・不透明度・頭からの遅れ px）。順は描く順 = 尾の先端（いちばん遠い i = 10）が先。
    /// </summary>
    public static (double Radius, double Opacity, double LagPx)[] OrbTailSteps()
    {
        var steps = new (double, double, double)[OrbTailPoints];
        for (int i = OrbTailPoints; i >= 1; i--)
        {
            double t = i / (double)OrbTailPoints;
            steps[OrbTailPoints - i] = (OrbTailStartRadius + (OrbTailEndRadius - OrbTailStartRadius) * t,
                OrbTailStartOpacity * (1 - t), OrbTailLength * i / OrbTailPoints);
        }
        return steps;
    }

    /// <summary>明滅の種類 → 最大の明るさ（弱 0.45 / 強 0.9、他 0）。</summary>
    public static double PulsePeak(EffectKind kind) => kind switch
    {
        EffectKind.Pulse => PulseWeakPeak,
        EffectKind.PulseStrong => PulseStrongPeak,
        _ => 0,
    };

    /// <summary>明滅の 1 周期を samples 点で標本化した明るさ（最初と最後が 0、中央が peak）。CA のキーフレーム用。</summary>
    public static double[] PulseKeyframes(double peak, int samples)
    {
        const int period = 1_000_000;
        var values = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            double t = samples <= 1 ? 0 : i / (double)(samples - 1);
            values[i] = AmbientMath.PulseLevel(t >= 1 ? 0 : t * period, period, peak);
        }
        return values;
    }
}
