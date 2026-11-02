#if CPTOOLS_CLIPBOARD
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CPClipboard.Platform;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CPSystemMonitor.Platform;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using FileLauncher.Platform;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 設定ウィンドウ・権限ガイドなどを開くときの前面化と、閉じたときの返却（SPEC §3.5）。
/// 常駐アプリは Dock / タスクバーから戻れないので、開いたら明示的に前面化してキーフォーカスを取る。
/// Mac は閉じたときに自アプリの可視ウィンドウが残っていなければ、開く前に前面だったアプリへフォーカスを返す
/// （返せなければ自アプリを隠す）。返さないと、どのアプリにもキーが入らない状態になる。
/// </summary>
internal static class WindowActivation
{
    public static void ShowAndActivate(Window window, IPlatformServices platform)
    {
        if (!window.IsVisible)
        {
            if (OperatingSystem.IsMacOS())
            {
                nint previous = platform.Window.CaptureForeground(); // 自アプリが前面なら 0
                window.Closed += (_, _) => ReturnFocus(window, previous, platform);
            }
            window.Show();
        }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        if (window.TryGetPlatformHandle()?.Handle is { } handle) platform.Window.BringToForeground(handle);
        window.Activate();
    }

    private static void ReturnFocus(Window closing, nint previous, IPlatformServices platform)
    {
        var others = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .Where(w => !ReferenceEquals(w, closing) && w.IsVisible && w.Opacity > 0) ?? [];
        if (others.Any()) return; // 他の窓（常駐の盤面・別のダイアログ）が残っているならそのまま
        bool restored = previous != 0 && platform.Window.RestoreForeground(previous);
        if (!restored) platform.Window.HideApplication();
        AppLog.Info($"窓を閉じた後の返却: {(restored ? "returned" : "hide app")}");
    }
}
