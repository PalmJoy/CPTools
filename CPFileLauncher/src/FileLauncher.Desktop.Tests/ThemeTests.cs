using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using FileLauncher.App;
using FileLauncher.Core.Model;
using FileLauncher.Core.Theming;

namespace FileLauncher.Desktop.Tests;

/// <summary>見た目はサイバーパンク専用（2026-10-04 にテーマの選択を廃止。SPEC §3.6）。</summary>
public sealed class ThemeTests
{
    [AvaloniaFact]
    public void すべてのトークンがサイバーパンクの辞書にあり_アプリはサイバーパンクで動く()
    {
        var app = Application.Current!;
        var missing = AppTheme.TokenKeys.Where(key => !app.TryGetResource(key, AppTheme.Cyberpunk, out _)).ToList();
        Assert.Empty(missing);
        Assert.True(AppTheme.TokenKeys.Count > 40);
        Assert.Equal(AppTheme.Cyberpunk, app.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void 盤面には発光の余白40が付く()
    {
        var board = new BoardWindow();
        var appearance = new AppearanceSettings();
        board.ApplyEffects(appearance);
        board.Render(Board.CreateDefault(), appearance, 0);
        board.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(board).OfType<FrameChrome>().Single().GetMargin());
        board.AllowClose = true;
        board.Close();
    }

    [AvaloniaFact]
    public void 配色を何度切り替えても盤面とトーストが落ちない()
    {
        var app = Application.Current!;
        var board = new BoardWindow();
        var appearance = new AppearanceSettings();
        board.Show();
        try
        {
            foreach (var (_, p, s) in ColorPresets.All.Append((AccentPreset.Cyan, new RgbColor(0x80, 0x80, 0x80), new RgbColor(0xFF, 0xFF, 0x00))))
            {
                AppTheme.SetColors(app, p, s);
                board.ApplyEffects(appearance);
                board.Render(Board.CreateDefault(), appearance, 0);
                Toast.Show("test", error: false, seconds: 1); // i18n:ignore テストのデータ
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            var (p, s) = ColorPresets.For(AccentPreset.Cyan);
            AppTheme.SetColors(app, p, s);
            board.AllowClose = true;
            board.Close();
        }
    }
}

public sealed class ThemedWindowTests
{
    [AvaloniaFact]
    public void 設定画面の面と文字とFluentのアクセントが配色から導いた色になる()
    {
        var app = Application.Current!;
        var dir = Path.Combine(Path.GetTempPath(), "FileLauncherUiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new FileLauncher.Core.Storage.DataPaths(dir, IsPortable: false);
            var store = new FileLauncher.Core.Storage.AppDataStore(paths);
            var hub = new SettingsHub(store, store.LoadAll().Settings.Value);
            var w = new SettingsWindow(new SettingsContext(hub, new FakePlatform(), paths, () => null, _ => { }, () => { }));
            w.Show();
            Dispatcher.UIThread.RunJobs();

            var (p, s) = ColorPresets.For(AccentPreset.Cyan);
            var derived = CyberDerivation.Derive(p, s);
            static Color C(RgbColor c) => Color.FromRgb(c.R, c.G, c.B);
            Assert.Equal(C(derived.BoardBackground), ((ISolidColorBrush)w.Chrome.SurfaceBrush!).Color); // 窓は透明、面は枠（SPEC §3.8）
            Assert.Equal(C(derived.Text), ((ISolidColorBrush)w.Foreground!).Color);
            Assert.True(app.TryGetResource("ToggleSwitchFillOn", AppTheme.Cyberpunk, out var on));
            Assert.Equal(Color.Parse("#00E5FF"), ((ISolidColorBrush)on!).Color);
            w.Close();
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
