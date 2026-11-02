using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CPSystemMonitor.Platform;

namespace CPSystemMonitor.App;

/// <summary>
/// macOS の権限ガイド（SPEC §4.3）。ユーザーがメニューの「権限の設定…」を選んだときだけ開く（自動では開かない）。
/// 開いている間は 1 秒ごとに状態を表示し直す。許可後のフック再開は App 側のポーリングが行う。
/// </summary>
internal sealed class PermissionGuideWindow : ChromeWindow
{
    private readonly IPlatformServices _platform;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public PermissionGuideWindow(IPlatformServices platform, Action retry, Action restart)
    {
        _platform = platform;
        Title = AppInfo.TitlePrefix + Strings.Permission_Title;
        TagCode = "AUTH";
        BodyWidth = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // 項目（アクセシビリティ / Finder のタブ / 有効にならないとき）ごとに見出しの線で区切り、
        // 手順の文のすぐ下にその操作のボタン、ボタンの右に説明を置く（2026-10-12 ユーザー要望）
        TextBlock Text(string text, string? brush = null)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            return brush is null ? t : Themed.Foreground(t, brush);
        }
        Control Heading(string text)
        {
            // 区切り線は主色を半分の濃さで（2026-10-12 ユーザー要望「少し薄く」）
            var line = new Border { Height = 1, Opacity = 0.5 };
            line[!Border.BackgroundProperty] = Themed.Res("FlAccent");
            return new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(0, 10, 0, 0),
                Children = { new TextBlock { Text = text, FontWeight = FontWeight.Bold, FontSize = 15 }, line },
            };
        }
        // ボタンの列の幅は窓全体でそろえる（SharedSizeGroup）
        Control Action(string text, string? note, Action onClick)
        {
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "Button" }, new ColumnDefinition(GridLength.Star) } };
            var b = Dialogs.ActionButton(text);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.VerticalAlignment = VerticalAlignment.Top;
            b.Click += (_, _) => onClick();
            row.Children.Add(b);
            if (note is not null)
            {
                var n = Text(note, "FlTextMuted");
                n.Margin = new Thickness(14, 6, 0, 0); // 上の余白で 1 行目をボタンの文字にそろえる
                Grid.SetColumn(n, 1);
                row.Children.Add(n);
            }
            return row;
        }

        var close = Dialogs.ActionButton(Strings.Common_Close);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += (_, _) => Close();

        var body = new StackPanel { Margin = new Thickness(18, 8, 18, 18), Spacing = 8 };
        Grid.SetIsSharedSizeScope(body, true);
        body.Children.AddRange(
        [
            Heading(Strings.Permission_Heading),
            _status,
            Text(Strings.Permission_Intro),
            Text(Strings.Permission_Step1),
            Action(Strings.Permission_Request, null, platform.Permissions.RequestAccessibility),
            Text(Strings.Permission_Step2),
            Action(Strings.Permission_OpenSettings, Strings.Permission_OpenSettings_Note, platform.Permissions.OpenSystemSettings),
            // .app ではなくターミナルから起動しているときは、権限が付くのはターミナル（CLAUDE.md）
            Text(Strings.Permission_Step3 + (IsInsideAppBundle() ? "" : "\n" + Strings.Permission_TerminalNote)),

            Heading(Strings.Permission_TroubleshootHeading),
            Action(Strings.Permission_Retry, Strings.Permission_Retry_Note, retry),
            Action(Strings.Common_Restart, Strings.Permission_Restart_Note, restart),
            close,
        ]);
        Body = body;

        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var p = _platform.Permissions;
        string ax = p.Accessibility == PermissionState.Granted ? Strings.Permission_Granted : Strings.Permission_NotGranted;
        string hook = _platform.InputHook.IsRunning ? Strings.Permission_HookOn : Strings.Permission_HookOff;
        _status.Text = Strings.FormatPermission_Status(ax, hook);
        Themed.Foreground(_status, _platform.InputHook.IsRunning ? "FlSuccess" : "FlError");

    }

    private static bool IsInsideAppBundle() =>
        Environment.ProcessPath?.Contains(".app/Contents/MacOS/", StringComparison.Ordinal) == true;
}
