#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using CPClipboard.Core.Effects;
using CPClipboard.Core.Model;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using CPSystemMonitor.Core.Effects;
using CPSystemMonitor.Core.Model;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using FileLauncher.Core.Effects;
using FileLauncher.Core.Model;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 画面右下に数秒出して消える通知（SPEC §5.2 起動失敗など）。フォーカスは奪わない。
/// OS のトースト通知（Windows App Notifications / NSUserNotification）への置き換えは M5 以降で検討。
/// </summary>
internal static class Toast
{
    private const int Margin = 12;

    /// <summary>演出（toastShow / toastHide）の設定の読み先。App が設定する。未設定なら演出なし。</summary>
    public static Func<AppearanceSettings>? Appearance { get; set; }

    private static EffectSpec Effect(string id) =>
        Appearance?.Invoke() is { } a ? EffectCatalog.Resolve(a, id) : EffectSpec.None;

    public static void Show(string message, bool error = true, int seconds = 5)
    {
        AppLog.Info($"toast: {message}");
        // 見た目はテーマのトークン（SPEC §3.6 トースト）。外側の余白は発光がウィンドウの縁で切れないため（FlToastMargin）
        static DynamicResourceExtension Res(string key) => new(key);
        var stripe = new Border { [!Border.BackgroundProperty] = Res(error ? "FlError" : "FlAccent"), [!Layoutable.WidthProperty] = Res("FlToastStripeWidth") };
        DockPanel.SetDock(stripe, Dock.Left);
        var card = new Border
        {
            Opacity = 0, // 表示演出（toastShow）で出す
            BorderThickness = new Thickness(1),
            ClipToBounds = false,
            [!Border.CornerRadiusProperty] = Res("FlToastCornerRadius"),
            [!Border.BackgroundProperty] = Res(error ? "FlToastErrorBackground" : "FlToastBackground"),
            [!Border.BorderBrushProperty] = Res("FlToastBorder"),
            [!Border.BoxShadowProperty] = Res(error ? "FlToastErrorGlow" : "FlToastGlow"),
            [!Layoutable.MarginProperty] = Res("FlToastMarginThickness"),
            Child = new DockPanel
            {
                Children =
                {
                    stripe,
                    // コンソール風の状態コード（HUD 段階 B。SPEC §3.9 H5）: 情報 ">"、エラー "!"。等幅・縦帯と同じ色
                    DockLeft(new TextBlock
                    {
                        Text = error ? "!" : ">",
                        FontSize = 12,
                        FontWeight = FontWeight.Bold,
                        Margin = new Thickness(10, 10, 0, 0),
                        VerticalAlignment = VerticalAlignment.Top,
                        [!TextBlock.FontFamilyProperty] = Res("FlMonoFontFamily"),
                        [!TextBlock.ForegroundProperty] = Res(error ? "FlError" : "FlAccent"),
                    }),
                    new TextBlock
                    {
                        [!Visual.EffectProperty] = Res("FlTextGlowSoft"),
                        Text = message,
                        Margin = new Thickness(8, 10, 14, 10),
                        TextWrapping = TextWrapping.Wrap,
                        [!TextBlock.ForegroundProperty] = Res("FlToastText"),
                    },
                },
            },
        };
        double glow = Application.Current is { } app ? AppTheme.GlowMargin(app, "FlToastMargin") : 0;
        var window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            Topmost = true,
            CanResize = false,
            Width = 380 + glow * 2,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-10000, -10000), // 大きさが決まってから右下へ移す
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            [!TemplatedControl.FontFamilyProperty] = Res("FlFontFamily"),
            Content = card,
        };
        window.Opened += (_, _) =>
        {
            if (window.Screens.Primary is not { } screen) return;
            var wa = screen.WorkingArea;
            var size = PixelSize.FromSize(window.ClientSize, screen.Scaling);
            window.Position = new PixelPoint(wa.Right - size.Width - Margin, wa.Bottom - size.Height - Margin);
            _ = EffectRunner.ShowAsync(card, Effect(EffectCatalog.ToastShow));
        };

        bool closing = false;
        async void CloseWithEffect()
        {
            if (closing) return;
            closing = true;
            await EffectRunner.HideAsync(card, Effect(EffectCatalog.ToastHide));
            window.Close();
        }
        window.PointerPressed += (_, _) => CloseWithEffect();
        window.Show();
        DispatcherTimer.RunOnce(CloseWithEffect, TimeSpan.FromSeconds(seconds));
    }

    private static Control DockLeft(Control c)
    {
        DockPanel.SetDock(c, Avalonia.Controls.Dock.Left);
        return c;
    }
}
