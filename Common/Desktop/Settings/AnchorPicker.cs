#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using CPClipboard.Core.Model;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using CPSystemMonitor.Core.Model;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using FileLauncher.Core.Model;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// カーソル位置表示で盤面のどの点をカーソルに合わせるかを選ぶ 3 × 3 のボタン（SPEC §3.3、SETTINGS.md ポップアップタブ）。
/// 各ボタンの中の点をその位置に寄せ、グリッド全体が盤面の縮図に見えるようにする。ラジオボタンの振る舞い（選択中を押しても外れない）。
/// キーボードは Tab で 1 回だけ止まり、矢印キーで隣へ（端で止まる）。見た目は AppTheme の ToggleButton.anchor-cell。
/// </summary>
internal sealed class AnchorPicker : UserControl
{
    private const double CellSize = 26;
    private readonly ToggleButton[] _cells = new ToggleButton[9];
    private CursorAnchor _value = CursorAnchor.Top;

    public AnchorPicker()
    {
        var grid = new UniformGrid { Rows = 3, Columns = 3, HorizontalAlignment = HorizontalAlignment.Left };
        KeyboardNavigation.SetTabNavigation(grid, KeyboardNavigationMode.Once);
        for (int i = 0; i < 9; i++)
        {
            var anchor = (CursorAnchor)i;
            var cell = new ToggleButton
            {
                Classes = { "anchor-cell" },
                Width = CellSize,
                Height = CellSize,
                Margin = new Thickness(1),
                Content = new Border
                {
                    Name = "Dot",
                    Width = 6,
                    Height = 6,
                    HorizontalAlignment = (i % 3) switch { 0 => HorizontalAlignment.Left, 1 => HorizontalAlignment.Center, _ => HorizontalAlignment.Right },
                    VerticalAlignment = (i / 3) switch { 0 => VerticalAlignment.Top, 1 => VerticalAlignment.Center, _ => VerticalAlignment.Bottom },
                },
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            string name = EnumNames.Of(anchor);
            ToolTip.SetTip(cell, name);
            AutomationProperties.SetName(cell, name);
            cell.Click += (_, _) => Select(anchor, focus: false);
            _cells[i] = cell;
            grid.Children.Add(cell);
        }
        grid.KeyDown += OnKeyDown;
        Content = grid;
        Update();
    }

    public CursorAnchor Value
    {
        get => _value;
        set { _value = value; Update(); }
    }

    /// <summary>ユーザーが選んだとき（Value を外から入れたときは出さない）。</summary>
    public event Action<CursorAnchor>? ValueChanged;

    /// <summary>ボタン（テスト用）。並びは CursorAnchor の値の順。</summary>
    internal IReadOnlyList<ToggleButton> Cells => _cells;

    private void Select(CursorAnchor anchor, bool focus)
    {
        bool changed = anchor != _value;
        _value = anchor;
        Update(); // 選択中を押して外れた IsChecked も戻す
        if (focus) _cells[(int)anchor].Focus(NavigationMethod.Directional);
        if (changed) ValueChanged?.Invoke(anchor);
    }

    private void Update()
    {
        for (int i = 0; i < 9; i++) _cells[i].IsChecked = i == (int)_value;
        if (Content is Control grid) KeyboardNavigation.SetTabOnceActiveElement(grid, _cells[(int)_value]);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        int index = Array.IndexOf(_cells, e.Source as ToggleButton ?? (e.Source as Visual)?.FindAncestorOfType<ToggleButton>());
        if (index < 0) index = (int)_value;
        int col = index % 3, row = index / 3;
        switch (e.Key)
        {
            case Key.Left: col = Math.Max(0, col - 1); break;
            case Key.Right: col = Math.Min(2, col + 1); break;
            case Key.Up: row = Math.Max(0, row - 1); break;
            case Key.Down: row = Math.Min(2, row + 1); break;
            default: return;
        }
        e.Handled = true;
        Select((CursorAnchor)(row * 3 + col), focus: true);
    }
}
