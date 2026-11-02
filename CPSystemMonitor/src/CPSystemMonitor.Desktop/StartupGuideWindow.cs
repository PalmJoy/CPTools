using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Core.Storage;
using CPSystemMonitor.Platform;

namespace CPSystemMonitor.App;

internal sealed class StartupGuideWindow : ChromeWindow
{
    public StartupGuideWindow(SettingsHub hub, DataPaths? paths = null, IPlatformServices? platform = null)
    {
        Title = AppInfo.TitlePrefix + Strings.StartupGuide_Title;
        TagCode = "WELCOME";
        BodyWidth = 440;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var hide = new CheckBox
        {
            Content = Strings.StartupGuide_DoNotShowAgain,
            IsChecked = hub.Current.General.HideStartupGuide,
            IsEnabled = !hub.IsReadOnly,
        };
        hide.IsCheckedChanged += (_, _) =>
        {
            hub.Update(s => s.General.HideStartupGuide = hide.IsChecked == true, SettingsChange.None);
            hub.Flush();
        };
        var close = Dialogs.ActionButton(Strings.Common_Close, isDefault: true, isCancel: true);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += (_, _) => Close();
        Body = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = Strings.StartupGuide_Description, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = InvocationText(hub.Current.Triggers), TextWrapping = TextWrapping.Wrap },
                hide,
                close,
            },
        };
        _ = new BoardWindowAppearance(this, hub.Current.Appearance, paths, platform);
    }

    internal static string InvocationText(TriggerSettings triggers)
    {
        var methods = new List<string>();
        if (triggers.DoubleTap.Enabled)
            methods.Add(Strings.FormatStartupGuide_DoubleTap(Loc.Modifier(triggers.DoubleTap.Modifier)));
        if (triggers.Hotkey.Enabled)
            methods.Add(Loc.Shortcut(triggers.Hotkey.Modifiers, triggers.Hotkey.Key));
        foreach (var mouse in triggers.Mouse.Where(m => m.Enabled && m.IsValid))
        {
            string gesture = Strings.FormatStartupGuide_MouseButton(EnumNames.Of(mouse.Button));
            methods.Add(Loc.Shortcut(mouse.Modifiers, gesture));
        }
        return methods.Count > 0
            ? Strings.FormatStartupGuide_OpenBoard(string.Concat(methods.Select(Strings.FormatStartupGuide_Shortcut)))
            : Strings.StartupGuide_ShortcutDisabled;
    }
}
