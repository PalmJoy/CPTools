using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using FileLauncher.Core.Items;
using FileLauncher.Core.Model;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace FileLauncher.App;

/// <summary>メニューから呼ぶ処理の束（BoardEditor が用意する）。null の項目は無効表示。</summary>
internal sealed record MenuActions(
    Action<LauncherItem> Launch,
    Action<LauncherItem>? LaunchAsAdmin,
    Action<LauncherItem> Reveal,
    Action<LauncherItem>? Edit,
    Action<LauncherItem, ItemColor?> SetColor,
    Action<LauncherItem> Duplicate,
    Action<LauncherItem> Delete,
    Action? NewItem,
    Action? Paste,
    Action? Undo,
    Action<DisplayMode> SetDisplayMode,
    DisplayMode CurrentMode,
    Action OpenSettings,
    Action? About,
    Action Quit,
    PageActions? Pages = null,
    Action<LauncherItem>? OpenNewWindow = null, // フォルダを新しいウィンドウで（SPEC §6.3）
    Action<LauncherItem>? OpenTerminal = null,
    Action? CheckForUpdates = null,
    Action<LauncherItem>? CopyPath = null); // ターミナルで開く（SPEC §6.3）

/// <summary>ページタブのメニューの処理（ページ番号を受ける）。</summary>
internal sealed record PageActions(
    Action<int> NewPage,
    Action<int> Settings,
    Action<int> Duplicate,
    Action<int>? Remove,
    Action<int>? MoveLeft,
    Action<int>? MoveRight);

/// <summary>
/// 盤面の右クリックメニュー（SPEC §6.3）。場所（アイテム / 空スロット / 背景）ごとに項目を組む。
/// 見た目はテーマ（サイバーパンクでは Fluent のメニューのキーを上書き済み）、フォントは FlFontFamily。
/// タブのメニューは M4 ステップ 2。
/// </summary>
internal static class BoardMenus
{

    public static ContextMenu? Build(SlotContext ctx, MenuActions a)
    {
        var items = new List<Control>();
        switch (ctx.Kind)
        {
            case SlotContextKind.Item when ctx.Item is { } item:
                items.Add(Item(Strings.Menu_Launch, () => a.Launch(item), new KeyGesture(Key.Enter)));
                if (item.Kind == ItemKind.Folder && a.OpenNewWindow is { } newWindow)
                    items.Add(Item(Strings.Menu_OpenNewWindow, () => newWindow(item),
                        new KeyGesture(Key.Enter, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)));
                if (OperatingSystem.IsWindows()) items.Add(Item(Strings.Menu_RunAsAdmin, a.LaunchAsAdmin is { } admin ? () => admin(item) : null));
                items.Add(Item(Loc.Os(Strings.Menu_Reveal_Win, Strings.Menu_Reveal_Mac), () => a.Reveal(item)));
                items.Add(Item(Strings.Menu_CopyPath, a.CopyPath is { } copyPath ? () => copyPath(item) : null));
                if (a.OpenTerminal is { } terminal && TerminalLocation.For(item) is not null)
                    items.Add(Item(Strings.Menu_OpenTerminal, () => terminal(item)));
                items.Add(new Separator());
                items.Add(Item(Strings.Menu_Edit, a.Edit is { } edit ? () => edit(item) : null, new KeyGesture(Key.F2)));
                items.Add(ColorMenu(item, a));
                items.Add(Item(Strings.Menu_Duplicate, () => a.Duplicate(item)));
                items.Add(Item(Strings.Menu_Delete, () => a.Delete(item), new KeyGesture(Key.Delete)));
                break;
            case SlotContextKind.EmptySlot:
            case SlotContextKind.Background:
                items.Add(Item(Strings.Menu_NewItem, a.NewItem));
                items.Add(Item(Strings.Menu_Paste, a.Paste));
                items.Add(new Separator());
                items.Add(Item(Strings.Menu_Undo, a.Undo, new KeyGesture(Key.Z, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)));
                items.Add(new Separator());
                AddCommon(items, a);
                break;
            case SlotContextKind.Tab when ctx.PageIndex is { } index && a.Pages is { } p:
                items.Add(Item(Strings.Menu_NewPage, () => p.NewPage(index)));
                items.Add(Item(Strings.Menu_PageSettings, () => p.Settings(index)));
                items.Add(Item(Strings.Menu_DuplicatePage, () => p.Duplicate(index)));
                items.Add(Item(Strings.Menu_DeletePage, p.Remove is { } remove ? () => remove(index) : null));
                items.Add(new Separator());
                items.Add(Item(Strings.Menu_MoveLeft, p.MoveLeft is { } left ? () => left(index) : null));
                items.Add(Item(Strings.Menu_MoveRight, p.MoveRight is { } right ? () => right(index) : null));
                break;
            default:
                return null;
        }

        var menu = new ContextMenu { ItemsSource = items };
        menu[!TemplatedControl.FontFamilyProperty] = new DynamicResourceExtension("FlFontFamily");
        return menu;
    }

    private static void AddCommon(List<Control> items, MenuActions a)
    {
        var popup = new MenuItem { Header = EnumNames.Of(DisplayMode.Popup), ToggleType = MenuItemToggleType.Radio, IsChecked = a.CurrentMode == DisplayMode.Popup };
        var resident = new MenuItem { Header = EnumNames.Of(DisplayMode.Resident), ToggleType = MenuItemToggleType.Radio, IsChecked = a.CurrentMode == DisplayMode.Resident };
        popup.Click += (_, _) => a.SetDisplayMode(DisplayMode.Popup);
        resident.Click += (_, _) => a.SetDisplayMode(DisplayMode.Resident);
        items.Add(new MenuItem { Header = Strings.Menu_DisplayMode, ItemsSource = new[] { popup, resident } });
        items.Add(Item(Strings.Common_Settings, a.OpenSettings, new KeyGesture(Key.OemComma, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)));
        items.Add(Item(Strings.Update_Check, a.CheckForUpdates));
        items.Add(Item(Strings.Menu_About, a.About));
        items.Add(new Separator());
        items.Add(Item(Loc.Os(Strings.Tray_Quit_Win, Strings.Tray_Quit_Mac), a.Quit));
    }

    /// <summary>「色 ▸」: なし + 8 色。各項目に色見本、今の色に ✓。</summary>
    private static MenuItem ColorMenu(LauncherItem item, MenuActions a)
    {
        var sub = new List<Control>();
        var none = new MenuItem
        {
            Header = EnumNames.Color(null),
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = item.Color is null,
            Icon = Swatch("FlEmptySlotBorder", outline: true),
        };
        none.Click += (_, _) => a.SetColor(item, null);
        sub.Add(none);
        sub.Add(new Separator());
        foreach (var id in Enum.GetValues<ItemColor>())
        {
            var mi = new MenuItem
            {
                Header = EnumNames.Color(id),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = item.Color == id,
                Icon = Swatch(AppTheme.ItemColorKey(id), outline: false),
            };
            mi.Click += (_, _) => a.SetColor(item, id);
            sub.Add(mi);
        }
        return new MenuItem { Header = Strings.Menu_Color, ItemsSource = sub };
    }

    private static Border Swatch(string key, bool outline)
    {
        var b = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2) };
        if (outline)
        {
            b.BorderThickness = new Thickness(1);
            b[!Border.BorderBrushProperty] = new DynamicResourceExtension(key);
        }
        else b[!Border.BackgroundProperty] = new DynamicResourceExtension(key);
        return b;
    }

    private static MenuItem Item(string header, Action? onClick, KeyGesture? gesture = null)
    {
        var mi = new MenuItem { Header = header, IsEnabled = onClick is not null, InputGesture = gesture };
        if (onClick is not null) mi.Click += (_, _) => onClick();
        return mi;
    }
}
