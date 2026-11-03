using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CPClipboard.App;
using CPClipboard.Core.Storage;

namespace CPClipboard.Desktop.Tests;

public sealed class ChromeWindowTests
{
    [AvaloniaFact]
    public void タイトルバーなしで枠に囲まれ_大きさは中身と余白から決まる()
    {
        var w = new ChromeWindow { Title = AppInfo.TitlePrefix + "テスト" /* i18n:ignore テストのデータ */, BodyWidth = 300, BodyHeight = 200, Body = new Border() };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(WindowDecorations.None, w.WindowDecorations);
        double m = w.Chrome.GetMargin();
        Assert.Equal(300 + m * 2, w.Width);
        Assert.Equal(200 + ChromeWindow.TitleBarHeight + 1 + m * 2, w.Height);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "テスト"); // 接頭辞は省く
        w.Close();
    }

    [AvaloniaFact]
    public void バツとEscで閉じ_ダイアログはキャンセルの結果を返す()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new ChromeWindow { BodyWidth = 200, Body = new TextBlock { Text = "x" }, CancelResult = () => false };
        var result = dialog.ShowDialog<bool?>(owner);
        Dispatcher.UIThread.RunJobs();
        dialog.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(result.IsCompleted);
        Assert.False(result.Result);

        var plain = new ChromeWindow { BodyWidth = 200, Body = new TextBlock { Text = "y" } };
        bool closed = false;
        plain.Closed += (_, _) => closed = true;
        plain.Show();
        Dispatcher.UIThread.RunJobs();
        plain.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task 設定画面に閉じるボタンは無く_Escで閉じると保存待ちが書かれる()
    {
        string dir = Path.Combine(Path.GetTempPath(), "CPClipboardUiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new DataPaths(dir, IsPortable: false);
            var store = new AppDataStore(paths);
            var hub = new SettingsHub(store, store.LoadAll().Settings.Value);
            var w = new SettingsWindow(new SettingsContext(hub, new FakePlatform(), paths, () => null, _ => { }, () => { }));
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == Strings.Common_Close);

            hub.Update(s => s.Appearance.Opacity = 65, SettingsChange.None);
            var closed = new TaskCompletionSource();
            w.Closed += (_, _) => closed.SetResult();
            w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(65, new AppDataStore(paths).LoadAll().Settings.Value.Appearance.Opacity);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void 権限ガイドと通知もフレームレスで開ける()
    {
        var guide = new PermissionGuideWindow(new FakePlatform(), () => { }, () => { });
        guide.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(WindowDecorations.None, guide.WindowDecorations);
        Assert.Contains(guide.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == Strings.Permission_Retry);
        guide.Close();
    }

    [AvaloniaFact]
    public void タグのコードを付けるとタイトル行の左に縦棒とコードが出て_無ければ出ない()
    {
        var w = new ChromeWindow { BodyWidth = 200, Body = new Border() };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "CFG");

        w.TagCode = "CFG";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "CFG" && t.FontSize == 9);
        w.Close();
    }

    [AvaloniaFact]
    public void OKとキャンセルのボタンは同じ高さで文字が中央()
    {
        var ok = Dialogs.ActionButton(Strings.Common_OK, isDefault: true);
        var cancel = Dialogs.ActionButton(Strings.Common_Cancel, isCancel: true);
        var w = new ChromeWindow { BodyWidth = 300, Body = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { ok, cancel } } };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ok.Bounds.Height, cancel.Bounds.Height);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, cancel.HorizontalContentAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, cancel.VerticalContentAlignment);
        w.Close();
    }

    [AvaloniaFact]
    public void グリッチで閉じても_ダイアログの結果はそのまま返る()
    {
        var glitch = new CPClipboard.Core.Effects.EffectSpec { Kind = CPClipboard.Core.Effects.EffectKind.Glitch, DurationMs = 150, Easing = CPClipboard.Core.Effects.EasingKind.Linear };
        var (savedShow, savedHide) = (ChromeWindow.ShowEffect, ChromeWindow.HideEffect);
        ChromeWindow.ShowEffect = glitch;
        ChromeWindow.HideEffect = glitch;
        try
        {
            var owner = new Window();
            owner.Show();
            var dialog = new ChromeWindow { BodyWidth = 200, Body = new TextBlock { Text = "x" }, CancelResult = () => false };
            var result = dialog.ShowDialog<bool?>(owner);
            Pump(TimeSpan.FromMilliseconds(400));
            dialog.Close(true);
            Assert.False(result.IsCompleted); // 消える演出の間はまだ閉じない
            var until = DateTime.UtcNow.AddSeconds(3);
            while (!result.IsCompleted && DateTime.UtcNow < until) Pump(TimeSpan.FromMilliseconds(20));
            Assert.True(result.IsCompleted);
            Assert.True(result.Result);
            owner.Close();
        }
        finally { (ChromeWindow.ShowEffect, ChromeWindow.HideEffect) = (savedShow, savedHide); }
    }

    private static void Pump(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } while (DateTime.UtcNow < until);
    }

    [AvaloniaFact]
    public void 面取りの枠は八角形の面と線で描き_中身も同じ形で切り抜く()
    {
        var w = new ChromeWindow { BodyWidth = 200, BodyHeight = 100, Body = new Border() };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(FrameChrome.DefaultChamfer, w.Chrome.Chamfer);
        Assert.True(w.Chrome.Face.IsVisible);
        Assert.NotNull(w.Chrome.Face.Data);
        Assert.Equal(Avalonia.Media.Colors.Transparent, ((Avalonia.Media.ISolidColorBrush)w.Chrome.Frame.BorderBrush!).Color);

        w.Chrome.Chamfer = 0; // 角丸に戻すと Border の面と線
        Assert.False(w.Chrome.Face.IsVisible);
        Assert.NotEqual(Avalonia.Media.Colors.Transparent, ((Avalonia.Media.ISolidColorBrush)w.Chrome.OutlineBrush!).Color);
        w.Close();
    }

    [AvaloniaFact]
    public void 高さを中身に合わせる窓は面取りの枠の図形に引き伸ばされない()
    {
        // 面の八角形（Path）が一度大きく描かれた後も、窓は中身の高さまで縮む（2026-10-08 ページの設定が縦に伸びた）
        var w = new ChromeWindow { BodyWidth = 300, Body = new Border { Height = 100 } };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        double margin = w.GetVisualDescendants().OfType<FrameChrome>().First().GetMargin();
        Assert.InRange(w.Height, 0, 100 + ChromeWindow.TitleBarHeight + 1 + margin * 2 + 4);
        w.Close();
    }
}
