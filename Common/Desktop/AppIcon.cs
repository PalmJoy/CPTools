#if CPTOOLS_CLIPBOARD
using Avalonia.Controls;
using Avalonia.Platform;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia.Controls;
using Avalonia.Platform;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia.Controls;
using Avalonia.Platform;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// アプリとトレイ / メニューバーのアイコン（SPEC §3.9 H13。画像は src/build/make-icons.sh が Assets/*.svg から作る）。
/// warning=true は権限未許可・フック失敗などの警告状態（SPEC §8。橙の線画）。
/// </summary>
internal static class AppIcon
{
    private static WindowIcon Load(string name) =>
        new(AssetLoader.Open(new Uri($"avares://{AppInfo.ProductName}/Assets/{name}")));

    /// <summary>トレイ / メニューバー。macOS の通常時はテンプレート画像（黒の単色。OS がメニューバーの明暗に合わせて塗る）。</summary>
    public static WindowIcon Create(bool warning = false) =>
        Load(warning ? "tray-warning.png" : OperatingSystem.IsMacOS() ? "tray-template.png" : "tray-color.png");

    /// <summary>macOS でテンプレート画像として扱うか（警告の橙は色を見せたいのでテンプレートにしない）。</summary>
    public static bool IsTemplate(bool warning) => OperatingSystem.IsMacOS() && !warning;

    private static WindowIcon? _window;

    /// <summary>窓のアイコン（Windows の Alt+Tab・タスクバー）。</summary>
    public static WindowIcon Window => _window ??= Load("app-256.png");
}
