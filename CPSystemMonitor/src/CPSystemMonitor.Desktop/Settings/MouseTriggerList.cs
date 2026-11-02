using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CPSystemMonitor.Core.Model;
using CoreModifiers = CPSystemMonitor.Core.Model.KeyModifiers;

namespace CPSystemMonitor.App;

/// <summary>
/// マウス操作トリガーの一覧（設定画面のトリガータブ、SPEC §4.2）。行ごとに 有効 / ボタン / 修飾キー / 削除。
/// 修飾キーなしの左右ボタンは通常のクリックを奪うので使えない（行を赤枠にして、その間は保存しない）。
/// </summary>
internal sealed class MouseTriggerList : StackPanel
{
    private static readonly (string Text, MouseButtonKind Value)[] Buttons =
    [
        (EnumNames.Of(MouseButtonKind.Left), MouseButtonKind.Left),
        (EnumNames.Of(MouseButtonKind.Right), MouseButtonKind.Right),
        (EnumNames.Of(MouseButtonKind.Middle), MouseButtonKind.Middle),
        (EnumNames.Of(MouseButtonKind.X1), MouseButtonKind.X1),
        (EnumNames.Of(MouseButtonKind.X2), MouseButtonKind.X2),
    ];

    private static readonly (CoreModifiers Flag, string Win, string Mac)[] Modifiers =
    [
        (CoreModifiers.Ctrl, "Ctrl", "⌃"),
        (CoreModifiers.Alt, "Alt", "⌥"),
        (CoreModifiers.Shift, "Shift", "⇧"),
        (CoreModifiers.Meta, "Win", "⌘"),
    ];

    private readonly SettingsHub _hub;
    private readonly Func<AppSettings, TriggerSettings> _get;
    private readonly StackPanel _rows = new() { Spacing = 6 };
    private readonly TextBlock _error = Themed.Foreground(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false }, "FlError");
    private List<MouseTriggerSetting> _items = new();

    public MouseTriggerList(SettingsHub hub, List<Action> refreshers, Func<AppSettings, TriggerSettings>? get = null)
    {
        _hub = hub;
        _get = get ?? (s => s.Triggers);
        Spacing = 6;
        var add = new Button { Content = Strings.Common_Add };
        add.Click += (_, _) =>
        {
            _items.Add(new MouseTriggerSetting { Button = MouseButtonKind.Middle, Modifiers = CoreModifiers.Alt });
            Rebuild();
            Commit();
        };
        Children.Add(_rows);
        Children.Add(_error);
        Children.Add(add);
        if (OperatingSystem.IsMacOS())
            Children.Add(Themed.Note(new TextBlock { Text = Strings.Settings_Triggers_SideButtonNote, FontSize = 12, TextWrapping = TextWrapping.Wrap }));
        refreshers.Add(() =>
        {
            _items = _get(_hub.Current).Mouse.Select(Clone).ToList();
            Rebuild();
        });
    }

    private static MouseTriggerSetting Clone(MouseTriggerSetting m) =>
        new() { Enabled = m.Enabled, Button = m.Button, Modifiers = m.Modifiers };

    private static bool IsInvalid(MouseTriggerSetting m) =>
        !m.IsValid;

    /// <summary>全行が有効な組み合わせのときだけ保存する（途中の不正な状態は保存しない）。</summary>
    private void Commit()
    {
        bool invalid = _items.Any(IsInvalid);
        _error.Text = Strings.Settings_Triggers_LeftWithoutModifier;
        _error.IsVisible = invalid;
        if (invalid) return;
        var copy = _items.Select(Clone).ToList();
        _hub.Update(s => _get(s).Mouse = copy, SettingsChange.Triggers);
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        foreach (var item in _items) _rows.Children.Add(RowFor(item));
        _error.IsVisible = _items.Any(IsInvalid);
    }

    private Control RowFor(MouseTriggerSetting item)
    {
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 4) };
        void UpdateBorder()
        {
            if (IsInvalid(item)) border[!Border.BorderBrushProperty] = Themed.Res("FlError");
            else border.BorderBrush = Brushes.Transparent;
        }

        var enabled = new CheckBox { IsChecked = item.Enabled, VerticalAlignment = VerticalAlignment.Center };
        enabled.IsCheckedChanged += (_, _) => { item.Enabled = enabled.IsChecked == true; Commit(); };

        var button = new ComboBox { ItemsSource = Buttons.Select(b => b.Text).ToList(), SelectedIndex = Array.FindIndex(Buttons, b => b.Value == item.Button), MinWidth = 90 };
        button.SelectionChanged += (_, _) =>
        {
            if (button.SelectedIndex < 0) return;
            item.Button = Buttons[button.SelectedIndex].Value;
            UpdateBorder();
            Commit();
        };

        // 記号は自分の四角のすぐ右に寄せ、隣の四角とは大きく離す（記号が右隣の四角の見出しに見えないように）
        var mods = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        foreach (var (flag, win, mac) in Modifiers)
        {
            var cb = new CheckBox
            {
                Content = OperatingSystem.IsMacOS() ? mac : win,
                IsChecked = item.Modifiers.HasFlag(flag),
                MinWidth = 0,                        // Fluent の既定 120 を外して、幅を中身に合わせる
                Padding = new Thickness(4, 0, 0, 0), // 四角と記号の間（Fluent の既定 8）
            };
            cb.IsCheckedChanged += (_, _) =>
            {
                item.Modifiers = cb.IsChecked == true ? item.Modifiers | flag : item.Modifiers & ~flag;
                UpdateBorder();
                Commit();
            };
            mods.Children.Add(cb);
        }

        var remove = new Button { Content = Strings.Common_Remove, FontSize = 12, Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center };
        remove.Click += (_, _) =>
        {
            _items.Remove(item);
            Rebuild();
            Commit();
        };

        border.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { enabled, button, remove } },
                mods,
            },
        };
        UpdateBorder();
        return border;
    }
}
