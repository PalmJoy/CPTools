using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Platform.Input;
using CPSystemMonitor.Platform.MacOS.Native;

namespace CPSystemMonitor.Platform.MacOS;

[SupportedOSPlatform("macos")]
public sealed class MacPlatformServices : IPlatformServices
{
    private readonly SharpHookInputService _input;

    public MacPlatformServices()
    {
        _input = new SharpHookInputService(currentModifiers: CurrentModifiers);
        // Dock に出さない（.app なら LSUIElement で既に accessory。dotnet run でも同じにする）
        MacWindowService.UseAccessoryActivationPolicy();
    }

    /// <summary>今押されている修飾キーを OS から読む（SharpHookInputService の注記参照）。</summary>
    private static KeyModifiers CurrentModifiers()
    {
        ulong f = CoreGraphics.CGEventSourceFlagsState(0);
        var m = KeyModifiers.None;
        if ((f & CoreGraphics.kCGEventFlagMaskControl) != 0) m |= KeyModifiers.Ctrl;
        if ((f & CoreGraphics.kCGEventFlagMaskAlternate) != 0) m |= KeyModifiers.Alt;
        if ((f & CoreGraphics.kCGEventFlagMaskShift) != 0) m |= KeyModifiers.Shift;
        if ((f & CoreGraphics.kCGEventFlagMaskCommand) != 0) m |= KeyModifiers.Meta;
        return m;
    }

    public IInputHookService InputHook => _input;
    public IHotkeyService Hotkey => _input;
    public IMouseTriggerService MouseTrigger => _input;
    public IShellService Shell { get; } = new MacShellService();
    public IWindowService Window { get; } = new MacWindowService();
    public IAutoStartService AutoStart { get; } = new MacAutoStartService();
    public IPermissionService Permissions { get; } = new MacPermissionService();
    public IAmbientLayerService Ambient { get; } = new MacAmbientLayerService();

    public void Dispose()
    {
        _input.Dispose();
        (Window as IDisposable)?.Dispose();
    }
}

/// <summary>
/// ログイン時の自動起動（SPEC §8）。macOS 13 以降の SMAppService.mainAppService で .app 自身をログイン項目に登録する。
/// dotnet run（.app でない）ではバンドルが無いので使えない（IsAvailable = false。設定画面は行を出さない）。
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacAutoStartService : IAutoStartService
{
    // SMAppServiceStatus
    private const long NotRegistered = 0, Enabled = 1, RequiresApproval = 2;

    private static readonly Lazy<nint> Service = new(() =>
    {
        using var pool = AutoreleasePool.Push();
        if (!NativeLibrary.TryLoad("/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement", out _)) return 0;
        nint cls = ObjC.Class("SMAppService");
        return cls == 0 ? 0 : ObjC.Send(cls, ObjC.Sel("mainAppService"));
    });

    public bool IsAvailable
    {
        get
        {
            using var pool = AutoreleasePool.Push();
            nint bundle = ObjC.Send(ObjC.Class("NSBundle"), ObjC.Sel("mainBundle"));
            return ObjC.ToManagedString(ObjC.Send(bundle, ObjC.Sel("bundleIdentifier"))) is { Length: > 0 }
                && Environment.ProcessPath?.Contains(".app/Contents/MacOS/", StringComparison.Ordinal) == true
                && Service.Value != 0;
        }
    }

    /// <summary>登録済み（ユーザーの承認待ちも含む。承認はシステム設定 → 一般 → ログイン項目）。</summary>
    public bool IsEnabled
    {
        get
        {
            if (!IsAvailable) return false;
            using var pool = AutoreleasePool.Push();
            long status = ObjC.SendRetLong(Service.Value, ObjC.Sel("status"));
            return status is Enabled or RequiresApproval;
        }
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        if (!IsAvailable) throw new InvalidOperationException("auto start needs the .app bundle");
        using var pool = AutoreleasePool.Push();
        bool ok = ObjC.SendRetBoolError(Service.Value, ObjC.Sel(enabled ? "registerAndReturnError:" : "unregisterAndReturnError:"), out nint error);
        if (ok) return;
        string? message = error == 0 ? null : ObjC.ToManagedString(ObjC.Send(error, ObjC.Sel("localizedDescription")));
        throw new InvalidOperationException(message ?? (enabled ? "register failed" : "unregister failed"));
    }
}
