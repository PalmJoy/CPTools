using Avalonia.Media;
using CPSystemMonitor.Core.Theming;

namespace CPSystemMonitor.App;

/// <summary>
/// サイバーパンクの素データ 8 色を Avalonia の Color にしたもの（色そのものは Core の CyberDerivation が主色・副色から導く。SPEC §3.6）。
/// 残りのトークンは AppTheme.Cyber が導出規則で作る。
/// </summary>
internal sealed record CyberPalette(
    Color Accent, Color Accent2,
    Color WindowBackground, Color BoardBackground, Color Surface,
    Color Text, Color TextMuted, Color RowText)
{
    private static Color C(RgbColor c) => Color.FromRgb(c.R, c.G, c.B);

    public static CyberPalette From(Core.Theming.CyberPalette p) =>
        new(C(p.Accent), C(p.Accent2), C(p.WindowBackground), C(p.BoardBackground), C(p.Surface), C(p.Text), C(p.TextMuted), C(p.RowText));
}
