namespace CPSystemMonitor.Core.Theming;

/// <summary>サイバーパンクの見た目の素データ 8 色（主色・副色と、主色から導いた背景・文字。SPEC §3.6「配色」）。残りのトークンは App の AppTheme が作る。</summary>
public sealed record CyberPalette(
    RgbColor Accent, RgbColor Accent2,
    RgbColor WindowBackground, RgbColor BoardBackground, RgbColor Surface,
    RgbColor Text, RgbColor TextMuted, RgbColor RowText);

/// <summary>主色・副色から背景・文字を導く（SPEC §3.6）。色相は主色に揃え、明度・彩度は固定。主色がほぼ無彩色なら背景・文字も無彩色。</summary>
public static class CyberDerivation
{
    public static CyberPalette Derive(RgbColor primary, RgbColor secondary)
    {
        var (h, s, _) = ColorMath.ToHsl(primary);
        double k = s < 0.15 ? 0 : 1; // 彩度の掛け率（無彩色なら 0）
        RgbColor C(double sat, double l) => ColorMath.FromHsl(h, sat * k, l);
        return new CyberPalette(
            primary, secondary,
            WindowBackground: C(0.38, 0.065),
            BoardBackground: C(0.40, 0.083),
            Surface: C(0.38, 0.12),
            Text: C(1.0, 0.95),
            TextMuted: C(0.28, 0.61),
            RowText: C(1.0, 0.88));
    }
}
