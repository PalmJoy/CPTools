#if CPTOOLS_CLIPBOARD
using System.Globalization;

namespace CPClipboard.Core.Theming;
#elif CPTOOLS_MONITOR
using System.Globalization;

namespace CPSystemMonitor.Core.Theming;
#elif CPTOOLS_LAUNCHER
using System.Globalization;

namespace FileLauncher.Core.Theming;
#else
#error A CPTools product must be selected.
#endif
/// <summary>不透明な色（配色の主色・副色と導出色。SPEC §3.6「配色」）。</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>#RRGGBB / #AARRGGBB（アルファは捨てる）。大文字小文字は問わない。読めなければ null。</summary>
    public static RgbColor? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 8) s = s[2..];
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return null;
        return new RgbColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();
}
