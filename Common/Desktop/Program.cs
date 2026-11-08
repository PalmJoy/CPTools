#if CPTOOLS_CLIPBOARD
using Avalonia;
using CPClipboard.Core.Storage;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using CPSystemMonitor.Core.Storage;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using FileLauncher.Core.Storage;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if CPTOOLS_MONITOR
        if (args.Length > 0 && args[0] == CPSystemMonitor.Platform.MacOS.MacProcessAccess.HelperArgument)
        {
            if (OperatingSystem.IsMacOS() && args.Length == 3)
                CPSystemMonitor.Platform.MacOS.MacProcessAccess.RunHelper(args[1], args[2]);
            return;
        }
        int wait = Array.IndexOf(args, "--wait-for");
        if (wait >= 0 && wait + 1 < args.Length && int.TryParse(args[wait + 1], out int oldPid))
        {
            try { using var old = System.Diagnostics.Process.GetProcessById(oldPid); old.WaitForExit(15000); }
            catch (ArgumentException) { }
        }
#endif
        var paths = DataPaths.Resolve(DataPaths.ExecutableDirectory(Environment.ProcessPath, AppContext.BaseDirectory));
        // 2 重起動なら既存インスタンスに「盤面を表示」を送って終了（SPEC §10.3）。Avalonia を立ち上げる前に判定する:
        // OnFrameworkInitializationCompleted の中で Shutdown() してもメインループ開始前なので効かず、2 つ目が残り続けた（2026-10-05 Windows）
        var instance = new SingleInstance(paths.Root);
        if (!instance.TryAcquire())
        {
            instance.SignalExisting();
            instance.Dispose();
            return;
        }
        Instance = instance;
        string settingsFile = paths.SettingsFile;
        // 表示言語は起動時に決める（変更は再起動で反映。SPEC §7）。--lang ja|en は確認用で、設定より優先し保存しない
        int lang = Array.IndexOf(args, "--lang");
        string language = lang >= 0 && lang + 1 < args.Length && args[lang + 1] is "ja" or "en" ? args[lang + 1] : SettingsPeek.Language(settingsFile);
        Loc.ApplyStartupLanguage(language);
        // --ambient avalonia: 常時の演出を OS の層ではなく Avalonia の層で描く（計測・切り分け用。issue/CA_AMBIENT.md）
        int ambient = Array.IndexOf(args, "--ambient");
        AvaloniaAmbient = ambient >= 0 && ambient + 1 < args.Length && args[ambient + 1] == "avalonia";
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>起動引数 --ambient avalonia（常時の演出を Avalonia の層で描く）。</summary>
    public static bool AvaloniaAmbient { get; private set; }

    /// <summary>Main で取得済みの単一インスタンスのロック（App が引き継いで待ち受け・解放する）。</summary>
    public static SingleInstance? Instance { get; private set; }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // macOS: Dock に出さない（.app では Info.plist の LSUIElement も設定する。SPEC §8）
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .WithInterFont()
            // 描画方式は両 OS ともソフトウェア描画に固定（SPEC §10.7）。GPU より CPU もメモリも少なかった。
            // Windows（2026-10-07 実測）: 表示中 CPU 53% → 8.6%、隠している間の Private 103 → 40 MB。
            // macOS（2026-10-07 実測、拡大率 1 の画面、§13.3 C27）: 全部オンの表示中 CPU 57% → 51%、footprint 185 → 115 MB、隠した後 152 → 116 MB。
            // Windows の CompositionMode は既定のまま（DirectComposition は隠している間 CPU 97% になった）。
            // GPU のリソースキャッシュの上限（SkiaOptions.MaxGpuResourceSizeBytes）は macOS の GPU 描画で CPU・メモリとも悪化したので指定しない
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
            .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
            // 変わった部分だけを塗り直す（複数の矩形で切り抜く）。明滅を枠の周りの帯に分けたのと組み合わせて、
            // 既定の構成で 26.3 → 22.8%（2026-10-08、Mac・ソフトウェア描画）。GPU 描画では逆効果だった（2026-10-04）。Windows は SPEC §13.3 C28
            .With(new Avalonia.Rendering.Composition.CompositionOptions { UseRegionDirtyRectClipping = true })
            .LogToTrace();
}
