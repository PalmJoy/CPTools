using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CPSystemMonitor.Platform.MacOS.Native;

[SupportedOSPlatform("macos")]
internal static class CoreFoundation
{
    private const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    public const uint kCFStringEncodingUTF8 = 0x08000100;
    public const int kCFNumberSInt64Type = 4;

    [DllImport(Lib)] public static extern void CFRelease(nint obj);
    [DllImport(Lib)] public static extern nint CFStringCreateWithCString(nint alloc, string s, uint encoding);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool CFStringGetCString(nint s, byte[] buffer, long size, uint encoding);
    [DllImport(Lib)] public static extern nint CFGetTypeID(nint obj);
    [DllImport(Lib)] public static extern nint CFStringGetTypeID();
    [DllImport(Lib)] public static extern nint CFURLCreateWithFileSystemPath(nint alloc, nint path, int style, [MarshalAs(UnmanagedType.I1)] bool isDirectory);
    [DllImport(Lib)] public static extern nint CFNumberCreate(nint alloc, int type, ref long value);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool CFNumberGetValue(nint number, int type, out long value);
    [DllImport(Lib)] public static extern nint CFNumberGetTypeID();
    [DllImport(Lib)] public static extern long CFArrayGetCount(nint array);
    [DllImport(Lib)] public static extern nint CFDataCreate(nint alloc, byte[] bytes, nint length);
    [DllImport(Lib)] public static extern nint CFArrayGetValueAtIndex(nint array, long index);
    [DllImport(Lib)] public static extern nint CFDictionaryCreate(nint alloc, nint[] keys, nint[] values, nint count, nint keyCallbacks, nint valueCallbacks);


    private static readonly nint LibHandle = NativeLibrary.Load(Lib);
    public static nint BooleanTrue { get; } = Marshal.ReadIntPtr(NativeLibrary.GetExport(LibHandle, "kCFBooleanTrue"));
    public static nint TypeDictionaryKeyCallBacks { get; } = NativeLibrary.GetExport(LibHandle, "kCFTypeDictionaryKeyCallBacks");
    public static nint TypeDictionaryValueCallBacks { get; } = NativeLibrary.GetExport(LibHandle, "kCFTypeDictionaryValueCallBacks");

    /// <summary>呼び出し側が CFRelease すること。</summary>
    public static nint CreateString(string s) => CFStringCreateWithCString(0, s, kCFStringEncodingUTF8);

    public static string? ToManagedString(nint cfString)
    {
        if (cfString == 0 || CFGetTypeID(cfString) != CFStringGetTypeID()) return null;
        var buf = new byte[1024];
        if (!CFStringGetCString(cfString, buf, buf.Length, kCFStringEncodingUTF8)) return null;
        int len = Array.IndexOf(buf, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buf, 0, len < 0 ? buf.Length : len);
    }
}
