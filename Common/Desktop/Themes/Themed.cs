#if CPTOOLS_CLIPBOARD
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>コードで作る文字にテーマのトークンを結ぶ小さなヘルパー（SPEC §3.6）。窓の面・文字・フォントは ChromeWindow が結ぶ。</summary>
internal static class Themed
{
    public static DynamicResourceExtension Res(string key) => new(key);

    /// <summary>注記（小さく控えめな文字）。</summary>
    public static TextBlock Note(TextBlock text)
    {
        text[!TextBlock.ForegroundProperty] = Res("FlTextMuted");
        return text;
    }

    /// <summary>文字のネオン発光（サイバーパンクのみ。他のテーマは null = なし）。strong = 見出し・選択中、弱 = その他。</summary>
    public static T Glow<T>(T visual, bool strong = true) where T : Avalonia.Visual
    {
        visual[!Avalonia.Visual.EffectProperty] = Res(strong ? "FlTextGlow" : "FlTextGlowSoft");
        return visual;
    }

    public static T Foreground<T>(T text, string key) where T : TextBlock
    {
        text[!TextBlock.ForegroundProperty] = Res(key);
        return text;
    }
}
