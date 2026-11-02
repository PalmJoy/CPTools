using System.Runtime.Versioning;
using CPSystemMonitor.Core.Input;
using CPSystemMonitor.Core.Model;
using static CPSystemMonitor.Platform.Windows.Native.User32;

namespace CPSystemMonitor.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsWindowService : IWindowService
{
    private bool IsShell(nint window)
    {
        var name = new System.Text.StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "#32768";
    }
    public bool IsTargetValid(nint target)
    {
        if (target == 0 || !IsWindow(target)) return false;
        GetWindowThreadProcessId(target, out uint pid);
        return pid != 0 && pid != Environment.ProcessId;
    }
    public bool IsForeground(nint target) => IsTargetValid(target) && GetForegroundWindow() == target;
    public nint PreviousForeground()
    {
        // トレイより後ろの可視トップレベルをその場で探す。前面履歴は保持しない。
        nint foreground = GetForegroundWindow();
        return FindPrevious(foreground, w => GetWindow(w, GW_HWNDNEXT),
            w => IsWindowVisible(w) && GetAncestor(w, GA_ROOT) == w && IsTargetValid(w) && !IsShell(w));
    }

    internal static nint FindPrevious(nint foreground, Func<nint, nint> next, Func<nint, bool> eligible)
    {
        var visited = new HashSet<nint> { foreground };
        for (nint window = next(foreground); window != 0 && visited.Add(window); window = next(window))
            if (eligible(window)) return window;
        return 0;
    }

    /// <summary>「アニメーション効果」（設定 → アクセシビリティ → 視覚効果）が OFF なら true。</summary>
    public bool PrefersReducedMotion =>
        SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out bool on, 0) && !on;

    public bool UsesLogicalScreenCoordinates => false;

    public void ConfigureBoardWindow(nint windowHandle)
    {
        // ツールウィンドウにすると Alt+Tab にも出ない（Avalonia の ShowInTaskbar=false だけだと Alt+Tab には出る）
        long ex = GetWindowLongPtr(windowHandle, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(windowHandle, GWL_EXSTYLE, new IntPtr(ex));
        SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    public void ConfigurePreviewWindow(nint windowHandle)
    {
        ConfigureBoardWindow(windowHandle);
        const long transparent = 0x20, noActivate = 0x08000000;
        long ex = GetWindowLongPtr(windowHandle, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(windowHandle, GWL_EXSTYLE, new IntPtr(ex | transparent | noActivate));
        SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    public nint CaptureForeground()
    {
        nint current = GetForegroundWindow();
        if (IsShell(current)) return PreviousForeground();
        if (!IsTargetValid(current)) return 0;
        return current;
    }

    /// <summary>
    /// フックで入力を抑止して呼び出すと、このプロセスは「最後に入力を受けたプロセス」ではないため
    /// SetForegroundWindow が拒否される（フォアグラウンドロック）。前面ウィンドウのスレッドに
    /// 入力キューを一時接続すると前面化が許可される（SPEC §3.3、PlatformSpike F で確認）。
    /// </summary>
    public bool BringToForeground(nint windowHandle) => ForceForeground(windowHandle);

    /// <summary><see cref="BringToForeground"/> の本体。Explorer の前面化（ExplorerTabs）でも使う。</summary>
    internal static bool ForceForeground(nint windowHandle)
    {
        if (windowHandle == 0) return false;
        if (GetForegroundWindow() == windowHandle) return true;
        if (SetForegroundWindow(windowHandle) && GetForegroundWindow() == windowHandle) return true;

        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        uint myThread = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != myThread && AttachThreadInput(myThread, fgThread, true);
        try
        {
            BringWindowToTop(windowHandle);
            SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (attached) AttachThreadInput(myThread, fgThread, false);
        }
        return GetForegroundWindow() == windowHandle;
    }

    public bool RestoreForeground(nint previous) =>
        IsTargetValid(previous) && ForceForeground(previous);

    public ScreenPoint GetCursorPosition() =>
        GetCursorPos(out var p) ? new ScreenPoint(p.X, p.Y) : default;
}
