#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>ユーザーへの通知（データ復旧・フック失敗など）。トースト通知は M3 で検討、それまでは小さなウィンドウ。</summary>
internal static class Notice
{
    public static void Show(string title, string message)
    {
        var ok = Dialogs.ActionButton(Strings.Common_OK, isDefault: true);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        var window = new ChromeWindow
        {
            Title = AppInfo.TitlePrefix + title,
            TagCode = "SYS",
            BodyWidth = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            Body = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 15 },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => window.Close();
        window.Show();
    }
}
