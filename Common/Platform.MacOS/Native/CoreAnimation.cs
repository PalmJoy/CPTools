#if CPTOOLS_CLIPBOARD
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static CPClipboard.Platform.MacOS.Native.ObjC;

namespace CPClipboard.Platform.MacOS.Native;
#elif CPTOOLS_MONITOR
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static CPSystemMonitor.Platform.MacOS.Native.ObjC;

namespace CPSystemMonitor.Platform.MacOS.Native;
#elif CPTOOLS_LAUNCHER
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static FileLauncher.Platform.MacOS.Native.ObjC;

namespace FileLauncher.Platform.MacOS.Native;
#else
#error A CPTools product must be selected.
#endif
/// <summary>Core Animation（QuartzCore）の最小限のバインディング（常時の演出。issue/CA_AMBIENT.md §3）。</summary>
[SupportedOSPlatform("macos")]
internal static class CoreAnimation
{
    private const string Lib = "/System/Library/Frameworks/QuartzCore.framework/QuartzCore";

    static CoreAnimation() => NativeLibrary.Load(Lib);

    [DllImport(Lib)] public static extern double CACurrentMediaTime();

    /// <summary>autorelease された層（CALayer / CAGradientLayer / CAShapeLayer）。</summary>
    public static nint Layer(string className) => Send(Class(className), Sel("layer"));

    public static nint Retain(nint obj) { if (obj != 0) Send(obj, Sel("retain")); return obj; }
    public static void Release(nint obj) { if (obj != 0) Send(obj, Sel("release")); }

    public static void SetFrame(nint layer, double x, double y, double w, double h) =>
        SendVoidRect(layer, Sel("setFrame:"), new CGRect { X = x, Y = y, Width = w, Height = h });

    public static void SetBounds(nint layer, double w, double h) =>
        SendVoidRect(layer, Sel("setBounds:"), new CGRect { X = 0, Y = 0, Width = w, Height = h });

    public static void SetPosition(nint layer, double x, double y) =>
        SendVoidPoint(layer, Sel("setPosition:"), new CGPoint { X = x, Y = y });

    public static void SetOpacity(nint layer, double opacity) => SendVoidFloat(layer, Sel("setOpacity:"), (float)opacity);
    public static void SetHidden(nint layer, bool hidden) => SendVoidBool(layer, Sel("setHidden:"), hidden);
    public static void SetDouble(nint obj, string selector, double value) => SendVoidDouble(obj, Sel(selector), value);
    public static void AddSublayer(nint parent, nint child) => SendVoid(parent, Sel("addSublayer:"), child);

    /// <summary>暗黙アニメーションを切った CATransaction（using で囲む）。</summary>
    public readonly struct Transaction : IDisposable
    {
        private Transaction(bool _) { }
        public static Transaction Begin()
        {
            Send(Class("CATransaction"), Sel("begin"));
            SendVoidBool(Class("CATransaction"), Sel("setDisableActions:"), true);
            return new Transaction(true);
        }
        public void Dispose() => Send(Class("CATransaction"), Sel("commit"));
    }

    /// <summary>autorelease された NSArray（要素は配列が retain する）。</summary>
    public static nint NSArray(nint[] items) => SendArray(Class("NSArray"), Sel("arrayWithObjects:count:"), items, items.Length);

    public static nint NSNumber(double value) => SendDouble(Class("NSNumber"), Sel("numberWithDouble:"), value);

    /// <summary>repeatCount ∞ の繰り返しアニメーション（beginTime は層の親の時間空間）。</summary>
    public static nint Repeating(nint animation, double durationSec, double beginTime, double timeOffsetSec)
    {
        SendVoidDouble(animation, Sel("setDuration:"), durationSec);
        SendVoidFloat(animation, Sel("setRepeatCount:"), float.PositiveInfinity);
        SendVoidDouble(animation, Sel("setBeginTime:"), beginTime);
        SendVoidDouble(animation, Sel("setTimeOffset:"), timeOffsetSec);
        Send(animation, Sel("setTimingFunction:"), Send(Class("CAMediaTimingFunction"), Sel("functionWithName:"), NSString("linear")));
        LimitFrameRate(animation);
        return animation;
    }

    /// <summary>
    /// 合成の頻度の希望（fps）。既定は表示のリフレッシュ（60 Hz 以上）で、WindowServer が毎フレーム合成し直す。
    /// macOS 12 以降（preferredFrameRateRange）。
    /// </summary>
    /// 2026-10-09 計測（既定構成、アプリ + WindowServer）: 上限なし 約 33% / 30 fps 約 24% / 20 fps 約 19%（従来の Avalonia の層 約 39%）。
    /// 動きの滑らかさを従来の既定（VSync 2 = 30 fps）にそろえて 30。
    public const float FrameRate = 30;

    private static void LimitFrameRate(nint animation)
    {
        if (!RespondsTo(animation, "setPreferredFrameRateRange:")) return;
        SendVoidFrameRate(animation, Sel("setPreferredFrameRateRange:"), new CAFrameRateRange { Minimum = FrameRate / 2, Maximum = FrameRate, Preferred = FrameRate });
    }

    public static nint KeyframeAnimation(string keyPath) => Send(Class("CAKeyframeAnimation"), Sel("animationWithKeyPath:"), NSString(keyPath));
    public static nint BasicAnimation(string keyPath) => Send(Class("CABasicAnimation"), Sel("animationWithKeyPath:"), NSString(keyPath));

    public static void AddAnimation(nint layer, nint animation, string key) => SendVoid(layer, Sel("addAnimation:forKey:"), animation, NSString(key));
}
