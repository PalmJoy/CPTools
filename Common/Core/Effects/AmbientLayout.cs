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
/// 常時の演出の配置の数式（issue/CA_AMBIENT.md §2）。座標は窓の論理座標（左上原点）。上下の反転は Platform が受け持つ。
/// </summary>
public static class AmbientLayout
{
    /// <summary>枠の外形から枠線の中心線までの距離（面取りの枠線は枠の太さ t の内側のさらに t/2 内側）。</summary>
    public static double OrbInset(double chamfer, double borderThickness) =>
        chamfer > 0 ? borderThickness * 1.5 : borderThickness / 2;

    /// <summary>玉の経路（枠線の中心線の八角形。枠の左上を原点とする 8 点、上辺の始まりから時計回り）。</summary>
    public static (double X, double Y)[] OrbCenterline(double frameWidth, double frameHeight, double chamfer, double borderThickness)
    {
        double inset = OrbInset(chamfer, borderThickness);
        return Octagon.Points(inset, inset, frameWidth - 2 * inset, frameHeight - 2 * inset, chamfer);
    }

    /// <summary>閉じた折れ線の長さ。</summary>
    public static double Perimeter((double X, double Y)[] points)
    {
        double length = 0;
        for (int i = 0; i < points.Length; i++)
        {
            var (ax, ay) = points[i];
            var (bx, by) = points[(i + 1) % points.Length];
            length += Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        }
        return length;
    }

    /// <summary>
    /// CA の timeOffset（ms、[0, 周期)）。phaseOffset = 周の中の位置のずれ（2 個目の玉は 0.5）、lagPx = 頭からの遅れ（尾）。
    /// </summary>
    public static double OrbTimeOffsetMs(int periodMs, double phaseOffset, double lagPx, double perimeter)
    {
        if (periodMs <= 0) return 0;
        double t = phaseOffset * periodMs - (perimeter > 0 ? periodMs * lagPx / perimeter : 0);
        double r = t % periodMs;
        if (r < 0) r += periodMs;
        return r >= periodMs ? 0 : r;
    }

    /// <summary>明滅の画像の置き場所（枠の外へ余白ぶん広げた位置。大きさは画像のピクセル数 / 拡大率を正とする）。</summary>
    public static (double X, double Y, double Width, double Height) PulseLayerRect(
        double frameX, double frameY, double glowMargin, int pixelWidth, int pixelHeight, double scale)
    {
        double s = scale > 0 ? scale : 1;
        return (frameX - glowMargin, frameY - glowMargin, pixelWidth / s, pixelHeight / s);
    }

    /// <summary>面（枠線の内側。FrameChrome.FaceClip と同じ）。走査線の帯はこの中だけを流れる。</summary>
    public static (double X, double Y, double Width, double Height) FaceRect(
        double frameX, double frameY, double frameWidth, double frameHeight, double borderThickness) =>
        (frameX + borderThickness, frameY + borderThickness,
         Math.Max(0, frameWidth - 2 * borderThickness), Math.Max(0, frameHeight - 2 * borderThickness));

    /// <summary>帯の中心の y（面の座標）。phase 0 で面の上の外、1 で下の外。</summary>
    public static double BeamCenterY(double phase, double faceHeight) =>
        AmbientMath.SweepOffset(phase, faceHeight, AmbientLook.BeamBandHeight) + AmbientLook.BeamBandHeight / 2;
}
