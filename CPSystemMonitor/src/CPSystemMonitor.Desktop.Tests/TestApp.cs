using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(CPSystemMonitor.Desktop.Tests.TestApp))]

namespace CPSystemMonitor.Desktop.Tests;

/// <summary>UI テストの表示言語は英語に固定する（結果が開発機の OS 言語に依存しないように。要素は Strings.* の文言で探す）。</summary>
internal static class TestCulture
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init()
    {
        var en = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = en;
        System.Globalization.CultureInfo.CurrentUICulture = en;
        CPSystemMonitor.App.Loc.StartupLanguage = "en";
    }
}

/// <summary>テスト用の最小アプリ（本物の App は起動時に入力フック等を始めるので使わない）。</summary>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        CPSystemMonitor.App.AppTheme.Install(this);
    }

    // 実際の描画エンジン（Skia）で動かす: 同梱フォントの読み込み・RenderTargetBitmap（グリッチ）も本物と同じ経路で確かめられる
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
