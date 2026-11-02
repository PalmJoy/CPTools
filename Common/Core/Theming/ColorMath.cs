#if CPTOOLS_CLIPBOARD
namespace CPClipboard.Core.Theming;
#elif CPTOOLS_MONITOR
namespace CPSystemMonitor.Core.Theming;
#elif CPTOOLS_LAUNCHER
namespace FileLauncher.Core.Theming;
#else
#error A CPTools product must be selected.
#endif
/// <summary>色の計算（HSL・混色・WCAG 2 のコントラスト比）。</summary>
public static class ColorMath
{
    public static (double H, double S, double L) ToHsl(RgbColor c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d == 0) return (0, 0, l);
        double s = d / (1 - Math.Abs(2 * l - 1));
        double h = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, s, l);
    }

    public static RgbColor FromHsl(double h, double s, double l)
    {
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double hp = ((h % 360) + 360) % 360 / 60;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        (double r, double g, double b) = hp switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        double m = l - c / 2;
        static byte B(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        return new RgbColor(B(r + m), B(g + m), B(b + m));
    }

    /// <summary>a と b を t（0 = a、1 = b）で混ぜる。</summary>
    public static RgbColor Mix(RgbColor a, RgbColor b, double t)
    {
        static byte L(byte x, byte y, double t) => (byte)Math.Clamp(Math.Round(x + (y - x) * t), 0, 255);
        return new RgbColor(L(a.R, b.R, t), L(a.G, b.G, t), L(a.B, b.B, t));
    }

    public static double RelativeLuminance(RgbColor c)
    {
        static double Ch(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    /// <summary>WCAG 2 のコントラスト比（1〜21）。</summary>
    public static double Contrast(RgbColor a, RgbColor b)
    {
        double la = RelativeLuminance(a), lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
