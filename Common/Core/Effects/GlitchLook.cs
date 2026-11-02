#if CPTOOLS_CLIPBOARD
namespace CPClipboard.Core.Effects;
#elif CPTOOLS_MONITOR
namespace CPSystemMonitor.Core.Effects;
#elif CPTOOLS_LAUNCHER
namespace FileLauncher.Core.Effects;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// グリッチの見た目の強さ（spec/EFFECTS.md「glitch の詳細」「強さの倍率」）。グリッチの見た目の数値はここ以外に書かない。
/// 設定「グリッチの強さ」<c>appearance.glitchIntensity</c>（%）を <see cref="Scaled"/> で倍率にして掛ける。
/// </summary>
/// <param name="ShadowOffset">色にじみ（シアン / マゼンタの影）の左右のずれ px。</param>
/// <param name="ShadowOpacity">影の不透明度。</param>
/// <param name="BandShiftMin">横にずれる帯のずれの最小 px（符号はランダム）。</param>
/// <param name="BandShiftMax">同じく最大 px。</param>
/// <param name="FinalShift">最後の段（帯 1 本）のずれ px。</param>
public readonly record struct GlitchLook(double ShadowOffset, double ShadowOpacity, double BandShiftMin, double BandShiftMax, double FinalShift)
{
    public const int MinIntensity = 0, MaxIntensity = 200, IntensityStep = 10, DefaultIntensity = 100;

    /// <summary>100% の値（2026-10-03 ユーザー要望で強めにした値）。</summary>
    public static readonly GlitchLook Base = new(5, 0.55, 6, 16, 6);

    /// <summary>強さ %（0〜200）→ 見た目。ずれは比例、影の不透明度も比例して 1 で止まる。</summary>
    public static GlitchLook Scaled(int intensityPercent)
    {
        double k = Math.Clamp(intensityPercent, MinIntensity, MaxIntensity) / 100.0;
        return new(Base.ShadowOffset * k, Math.Min(1, Base.ShadowOpacity * k), Base.BandShiftMin * k, Base.BandShiftMax * k, Base.FinalShift * k);
    }

    /// <summary>ずれも色にじみも無い（0%）。静止画にフェードだけを掛ける。</summary>
    public bool IsStill => ShadowOffset == 0 && BandShiftMax == 0;

    /// <summary>読み込み時の丸め（0〜200、10 刻み。不透明度と同じ丸め方）。</summary>
    public static int NormalizeIntensity(int value) =>
        (int)Math.Round(Math.Clamp(value, MinIntensity, MaxIntensity) / (double)IntensityStep, MidpointRounding.AwayFromZero) * IntensityStep;
}
