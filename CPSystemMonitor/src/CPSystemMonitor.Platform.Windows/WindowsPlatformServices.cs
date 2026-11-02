using System.Runtime.Versioning;
using CPSystemMonitor.Core.Input;
using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Platform.Input;
using CPSystemMonitor.Platform.Windows.Native;
using Microsoft.Win32;

namespace CPSystemMonitor.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformServices : IPlatformServices
{
    private readonly SharpHookInputService _input;

    public WindowsPlatformServices()
    {
        _input = new SharpHookInputService(
            isKeyPressed: code => IsPressed(code),
            isButtonPressed: button => button switch
            {
                InputButton.Left => IsPressed(0x01),
                InputButton.Right => IsPressed(0x02),
                InputButton.Middle => IsPressed(0x04),
                InputButton.X1 => IsPressed(0x05),
                InputButton.X2 => IsPressed(0x06),
                _ => true, // OS 状態を照会できないボタンは推測で解除しない。
            });
    }

    private static bool IsPressed(int virtualKey) => (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public IInputHookService InputHook => _input;
    public IHotkeyService Hotkey => _input;
    public IMouseTriggerService MouseTrigger => _input;
    public IShellService Shell { get; } = new WindowsShellService();
    public IWindowService Window { get; } = new WindowsWindowService();
    public IAutoStartService AutoStart { get; } = new WindowsAutoStartService();
    public IPermissionService Permissions { get; } = new WindowsPermissionService();

    public void Dispose()
    {
        _input.Dispose();
        (Window as IDisposable)?.Dispose();
    }
}

/// <summary>Windows には入力監視の権限が無いので常に許可済み。</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPermissionService : IPermissionService
{
    public PermissionState InputMonitoring => PermissionState.Granted;
    public PermissionState Accessibility => PermissionState.Granted;
    public void RequestAccessibility() { }
    public void OpenSystemSettings() { }
}

/// <summary>ログイン時自動起動: HKCU\Software\Microsoft\Windows\CurrentVersion\Run（SPEC §10.3）。</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsAutoStartService : IAutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CPSystemMonitor";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{executablePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
