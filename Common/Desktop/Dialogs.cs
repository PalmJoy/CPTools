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
internal static class Dialogs
{
    /// <summary>
    /// ダイアログのボタン（OK / キャンセルなど）。高さを固定し、文字を縦横とも中央に置く
    /// （日本語の文字は欧文フォントの代替で行の高さが変わり、「OK」と「キャンセル」で高さがずれていた。2026-10-04）。
    /// </summary>
    public static Button ActionButton(string text, bool isDefault = false, bool isCancel = false) => new()
    {
        Content = text,
        Height = 32,
        MinWidth = 90,
        Padding = new Thickness(16, 0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        IsDefault = isDefault,
        IsCancel = isCancel,
    };

    /// <summary>はい / いいえ の確認。盤面（最前面）より上に出す。呼ぶ側は PopupController.RunModalAsync で包むこと。</summary>
    public static Task<bool> ConfirmAsync(Window owner, string title, string message, string? ok = null, string? cancel = null)
    {
        var okButton = ActionButton(ok ?? Strings.Common_OK, isDefault: true);
        var cancelButton = ActionButton(cancel ?? Strings.Common_Cancel, isCancel: true);
        var dialog = new ChromeWindow
        {
            Title = AppInfo.TitlePrefix + title,
            TagCode = "CONFIRM",
            BodyWidth = 420,
            CancelResult = () => false, // × / Esc = キャンセル
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Body = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { okButton, cancelButton },
                    },
                },
            },
        };
        okButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);
        return dialog.ShowDialog<bool>(owner);
    }
}
