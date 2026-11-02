using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CPSystemMonitor.Platform.MacOS.Native;

[SupportedOSPlatform("macos")]
internal static class Accessibility
{
    private const string Lib = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    [return: MarshalAs(UnmanagedType.I1)] public static extern bool IsSecureEventInputEnabled();
    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";

    public const int kAXErrorSuccess = 0;

    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool AXIsProcessTrusted();
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool AXIsProcessTrustedWithOptions(nint options);
    [DllImport(Lib)] public static extern nint AXUIElementCreateSystemWide();
    [DllImport(Lib)] public static extern int AXUIElementCopyElementAtPosition(nint application, float x, float y, out nint element);
    [DllImport(Lib)] public static extern int AXUIElementCopyAttributeValue(nint element, nint attribute, out nint value);
    [DllImport(Lib)] public static extern int AXUIElementGetPid(nint element, out int pid);
    [DllImport(Lib)] public static extern int AXUIElementSetMessagingTimeout(nint element, float timeoutInSeconds);
    [DllImport(Lib)] public static extern nint AXUIElementCreateApplication(int pid);
    [DllImport(Lib)] public static extern int AXUIElementPerformAction(nint element, nint action);

    /// <summary>kIOHIDRequestTypeListenEvent = 1。戻り値 kIOHIDAccessTypeGranted=0 / Denied=1 / Unknown=2。</summary>
    [DllImport(IOKit)] public static extern int IOHIDCheckAccess(int requestType);
}
