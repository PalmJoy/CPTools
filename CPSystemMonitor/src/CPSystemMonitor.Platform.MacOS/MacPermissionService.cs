using System.Diagnostics;
using System.Runtime.Versioning;
using CPSystemMonitor.Platform.MacOS.Native;

namespace CPSystemMonitor.Platform.MacOS;

/// <summary>
/// 権限（SPEC §4.3）。SharpHook のフックはアクセシビリティだけで動く（PlatformSpike A: 未許可だと ErrorAxApiDisabled）。
/// 入力監視は参考表示用に IOHIDCheckAccess で状態だけ読む。
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacPermissionService : IPermissionService
{
    public PermissionState InputMonitoring => Native.Accessibility.IOHIDCheckAccess(1) switch
    {
        0 => PermissionState.Granted,
        1 => PermissionState.Denied,
        _ => PermissionState.Unknown,
    };

    public PermissionState Accessibility =>
        Native.Accessibility.AXIsProcessTrusted() ? PermissionState.Granted : PermissionState.Denied;

    /// <summary>OS の「アクセシビリティを許可しますか」ダイアログを出す（未許可のときだけ出る）。</summary>
    public void RequestAccessibility()
    {
        nint key = CoreFoundation.CreateString("AXTrustedCheckOptionPrompt"); // kAXTrustedCheckOptionPrompt の値
        nint options = CoreFoundation.CFDictionaryCreate(0, [key], [CoreFoundation.BooleanTrue], 1,
            CoreFoundation.TypeDictionaryKeyCallBacks, CoreFoundation.TypeDictionaryValueCallBacks);
        try { Native.Accessibility.AXIsProcessTrustedWithOptions(options); }
        finally
        {
            CoreFoundation.CFRelease(options);
            CoreFoundation.CFRelease(key);
        }
    }

    /// <summary>システム設定の「プライバシーとセキュリティ → アクセシビリティ」を開く。</summary>
    public void OpenSystemSettings() => OpenPrivacyPane("Privacy_Accessibility");

    private static void OpenPrivacyPane(string anchor)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("/usr/bin/open",
                $"\"x-apple.systempreferences:com.apple.preference.security?{anchor}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OpenSystemSettings({anchor}) failed: {ex.Message}");
        }
    }
}
