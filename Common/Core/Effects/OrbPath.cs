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
/// 枠の周上の経路（光の玉 frameOrb。spec/EFFECTS.md「常時の演出の詳細」）。角丸矩形と八角形（面取り）の 2 形。
/// 始点は左上の角の終わり（上辺の始まり）、時計回り。距離は周長で巻く（負も可）。
/// </summary>
public sealed class OrbPath
{
    private readonly (double Length, Func<double, (double X, double Y)> At)[] _segments;

    private OrbPath((double, Func<double, (double, double)>)[] segments)
    {
        _segments = segments;
        Length = segments.Sum(s => s.Item1);
    }

    /// <summary>角丸矩形（従来の形）。</summary>
    public OrbPath(double width, double height, double cornerRadius) : this(RoundedSegments(width, height, cornerRadius)) { }

    public static OrbPath RoundedRect(double width, double height, double cornerRadius) => new(width, height, cornerRadius);

    /// <summary>八角形（面取り chamfer）。形は Core <see cref="Theming.Octagon"/> と同じ。</summary>
    public static OrbPath Octagon(double width, double height, double chamfer)
    {
        var p = Theming.Octagon.Points(0, 0, width, height, chamfer);
        var segments = new (double, Func<double, (double, double)>)[8];
        for (int i = 0; i < 8; i++)
        {
            var (ax, ay) = p[i];
            var (bx, by) = p[(i + 1) % 8];
            segments[i] = (Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)), t => (ax + (bx - ax) * t, ay + (by - ay) * t));
        }
        return new OrbPath(segments);
    }

    private static (double, Func<double, (double, double)>)[] RoundedSegments(double width, double height, double cornerRadius)
    {
        double w = Math.Max(0, width), h = Math.Max(0, height);
        double r = Math.Clamp(cornerRadius, 0, Math.Min(w, h) / 2);
        double arc = Math.PI * r / 2, hx = w - 2 * r, vy = h - 2 * r;
        (double, double) Arc(double cx, double cy, double degrees)
        {
            double a = degrees * Math.PI / 180;
            return (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        }
        return
        [
            (hx, t => (r + t * hx, 0)),
            (arc, t => Arc(w - r, r, -90 + 90 * t)),
            (vy, t => (w, r + t * vy)),
            (arc, t => Arc(w - r, h - r, 0 + 90 * t)),
            (hx, t => (w - r - t * hx, h)),
            (arc, t => Arc(r, h - r, 90 + 90 * t)),
            (vy, t => (0, h - r - t * vy)),
            (arc, t => Arc(r, r, 180 + 90 * t)),
        ];
    }

    public double Length { get; }

    public (double X, double Y) PointAt(double distance)
    {
        if (Length <= 0) return _segments[0].At(0);
        double d = distance % Length;
        if (d < 0) d += Length;
        for (int i = 0; i < _segments.Length; i++)
        {
            double len = _segments[i].Length;
            if (d > len && i < _segments.Length - 1) { d -= len; continue; }
            return _segments[i].At(len <= 0 ? 0 : Math.Clamp(d / len, 0, 1));
        }
        return _segments[0].At(0);
    }
}
