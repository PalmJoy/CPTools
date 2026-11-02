#if CPTOOLS_CLIPBOARD
using System.Diagnostics;
using System.Runtime.Versioning;
using CPClipboard.Platform.MacOS.Native;
using static CPClipboard.Platform.MacOS.Native.ObjC;

namespace CPClipboard.Platform.MacOS;
#elif CPTOOLS_MONITOR
using System.Diagnostics;
using System.Runtime.Versioning;
using CPSystemMonitor.Platform.MacOS.Native;
using static CPSystemMonitor.Platform.MacOS.Native.ObjC;

namespace CPSystemMonitor.Platform.MacOS;
#elif CPTOOLS_LAUNCHER
using System.Diagnostics;
using System.Runtime.Versioning;
using FileLauncher.Platform.MacOS.Native;
using static FileLauncher.Platform.MacOS.Native.ObjC;

namespace FileLauncher.Platform.MacOS;
#else
#error A CPTools product must be selected.
#endif
/// <summary>常時の演出を Core Animation の層で描く（SPEC §10.3 / §10.7、issue/CA_AMBIENT.md §3）。</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacAmbientLayerService : IAmbientLayerService
{
    public bool IsSupported => true;

    public IAmbientLayerHost? Attach(nint windowHandle)
    {
        try
        {
            using var _ = AutoreleasePool.Push();
            nint window = MacWindowService.NSWindowOf(windowHandle);
            nint view = window == 0 ? 0 : Send(window, Sel("contentView"));
            if (view == 0) { Trace.WriteLine("[ambient] attach failed: no contentView"); return null; }
            if (Send(view, Sel("layer")) == 0) SendVoidBool(view, Sel("setWantsLayer:"), true);
            if (Send(view, Sel("layer")) == 0) { Trace.WriteLine("[ambient] attach failed: no layer"); return null; }
            return new MacAmbientLayerHost(view);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ambient] attach failed: {ex.Message}");
            return null;
        }
    }
}
