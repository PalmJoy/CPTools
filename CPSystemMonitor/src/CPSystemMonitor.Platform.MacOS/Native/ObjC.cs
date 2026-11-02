using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CPSystemMonitor.Platform.MacOS.Native;

/// <summary>
/// Objective-C ランタイムを objc_msgSend で直接呼ぶ最小限のバインディング（PlatformSpike / MacIconSpike で検証済み）。
/// arm64 では objc_msgSend は可変長ではなく通常の関数呼び出し規約なので、引数の型ごとに DllImport を分ける。
/// </summary>
[SupportedOSPlatform("macos")]
internal static class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";

    static ObjC()
    {
        // NSWorkspace / NSImage などの AppKit クラスを objc_getClass で引けるようにする（Avalonia が読む前に呼ばれても良いように）
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        NativeLibrary.Load("/System/Library/Frameworks/UniformTypeIdentifiers.framework/UniformTypeIdentifiers");
    }

    [DllImport(Lib, EntryPoint = "objc_getClass")] private static extern nint GetClass(string name);
    [DllImport(Lib, EntryPoint = "sel_registerName")] private static extern nint RegisterSel(string name);
    [DllImport(Lib)] public static extern nint objc_autoreleasePoolPush();
    [DllImport(Lib)] public static extern void objc_autoreleasePoolPop(nint pool);

    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, nint a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, nint a, nint b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, nint a, nint b, nint c, nint d);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SendRetBool(nint receiver, nint sel, nint a, nint b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint SendInt(nint receiver, nint sel, int a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint SendLong(nint receiver, nint sel, long a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern int SendRetInt(nint receiver, nint sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SendRetBool(nint receiver, nint sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SendRetBool(nint receiver, nint sel, nint a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SendRetBoolLong(nint receiver, nint sel, long a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SendRetBool(nint receiver, nint sel, out nint value, nint key, out nint error);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint SendRect(nint receiver, nint sel, ref CGRect rect, nint context, nint hints);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoid(nint receiver, nint sel, nint a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoidLong(nint receiver, nint sel, long a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint SendResolveAlias(nint receiver, nint sel, nint url, long options, out nint error);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint SendWithError(nint receiver, nint sel, nint a, out nint error);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SendRetBoolError(nint receiver, nint sel, out nint error);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern long SendRetLong(nint receiver, nint sel);
    // 構造体・浮動小数を引数に取るセレクタ（構造体を返すセレクタは使わない。Core Animation 用）
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoidRect(nint receiver, nint sel, CGRect a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoidPoint(nint receiver, nint sel, CGPoint a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoidDouble(nint receiver, nint sel, double a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoidFloat(nint receiver, nint sel, float a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoidBool(nint receiver, nint sel, [MarshalAs(UnmanagedType.I1)] bool a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoid(nint receiver, nint sel, nint a, nint b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern double SendRetDouble(nint receiver, nint sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern double SendRetDouble(nint receiver, nint sel, double a, nint b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint SendDouble(nint receiver, nint sel, double a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoidFrameRate(nint receiver, nint sel, CAFrameRateRange a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint SendArray(nint receiver, nint sel, nint[] items, nint count);

    private static readonly Dictionary<string, nint> Selectors = new();
    private static readonly Dictionary<string, nint> Classes = new();

    public static nint Sel(string name)
    {
        lock (Selectors)
        {
            if (!Selectors.TryGetValue(name, out var s)) Selectors[name] = s = RegisterSel(name);
            return s;
        }
    }

    public static nint Class(string name)
    {
        lock (Classes)
        {
            if (!Classes.TryGetValue(name, out var c)) Classes[name] = c = GetClass(name);
            return c;
        }
    }

    public static bool RespondsTo(nint obj, string selector) =>
        obj != 0 && SendRetBool(obj, Sel("respondsToSelector:"), Sel(selector));

    /// <summary>autorelease される NSString を作る。呼び出し側は autorelease pool の内側で使うこと。</summary>
    public static nint NSString(string s)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(s);
        try { return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    public static string? ToManagedString(nint nsString) =>
        nsString == 0 ? null : Marshal.PtrToStringUTF8(Send(nsString, Sel("UTF8String")));

    public static nint FileUrl(string path) => Send(Class("NSURL"), Sel("fileURLWithPath:"), NSString(path));

    public static nint SharedApplication => Send(Class("NSApplication"), Sel("sharedApplication"));

    public static nint SharedWorkspace => Send(Class("NSWorkspace"), Sel("sharedWorkspace"));

}

/// <summary>autorelease pool の範囲（using で使う）。フック・ワーカースレッドなど AppKit のイベントループ外では必須。</summary>
[SupportedOSPlatform("macos")]
/// <summary>
/// AppKit の外のスレッド・ネイティブ呼び出しを囲む autorelease pool。必ず <see cref="Push"/> で作る
/// （<c>new AutoreleasePool()</c> は空の値になり、Pop(0) でプロセスが落ちる。2026-10-04 に .app で発生）。
/// </summary>
internal readonly struct AutoreleasePool : IDisposable
{
    private readonly nint _pool;
    private AutoreleasePool(nint pool) => _pool = pool;
    public static AutoreleasePool Push() => new(ObjC.objc_autoreleasePoolPush());
    public void Dispose()
    {
        if (_pool != 0) ObjC.objc_autoreleasePoolPop(_pool);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public double X, Y, Width, Height;
}

/// <summary>CAFrameRateRange（float 3 つ。arm64 では浮動小数レジスタで渡る）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CAFrameRateRange
{
    public float Minimum, Maximum, Preferred;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGPoint
{
    public double X, Y;
}
