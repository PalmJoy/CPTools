using System.Globalization;
using CoreModifiers = CPSystemMonitor.Core.Model.KeyModifiers;

namespace CPSystemMonitor.App;

/// <summary>
/// UI 文字列の小さな道具（SPEC §7「言語と UI 文字列」、訳語は spec/TERMS.md）。文言そのものは Resources/Strings*.resx（生成クラス Strings）。
/// </summary>
internal static class Loc
{
    /// <summary>起動時に決めた表示言語（"ja" / "en"）。Program.Main が入れる。設定の変更は再起動で反映されるので、今の表示言語はこれ。</summary>
    public static string StartupLanguage { get; set; } = Effective("system");

    /// <summary>設定値（system / ja / en）→ 実際の表示言語。system は OS の UI 言語が日本語なら ja、それ以外は en。</summary>
    public static string Effective(string? setting) => setting switch
    {
        "ja" => "ja",
        "en" => "en",
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en",
    };

    /// <summary>起動時の UI 言語を決める（Avalonia より前に呼ぶ）。system なら .NET の初期値（OS の言語）のまま。</summary>
    public static void ApplyStartupLanguage(string setting)
    {
        if (setting is "ja" or "en")
        {
            var culture = new CultureInfo(setting);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        StartupLanguage = Effective(setting);
    }

    /// <summary>OS で語が違うもの（「格納フォルダを開く」/「Finder で表示」など）。</summary>
    public static string Os(string win, string mac) => OperatingSystem.IsMacOS() ? mac : win;

    private static readonly (CoreModifiers Flag, string Win, string Mac)[] ModifierNames =
    [
        (CoreModifiers.Ctrl, "Ctrl", "⌃"),
        (CoreModifiers.Alt, "Alt", "⌥"),
        (CoreModifiers.Shift, "Shift", "⇧"),
        (CoreModifiers.Meta, "Win", "⌘"),
    ];

    /// <summary>修飾キー 1 つの表記（Win「Ctrl」、Mac「⌃」）。</summary>
    public static string Modifier(CoreModifiers flag) =>
        ModifierNames.First(m => m.Flag == flag) is var n && OperatingSystem.IsMacOS() ? n.Mac : n.Win;

    public static IEnumerable<CoreModifiers> AllModifiers => ModifierNames.Select(m => m.Flag);

    /// <summary>2 回押しの修飾キーの表記（Win「Ctrl」、Mac「⌃」）。</summary>
    public static string Modifier(CPSystemMonitor.Core.Model.DoubleTapModifier key) => Modifier(key switch
    {
        CPSystemMonitor.Core.Model.DoubleTapModifier.Alt => CoreModifiers.Alt,
        CPSystemMonitor.Core.Model.DoubleTapModifier.Shift => CoreModifiers.Shift,
        CPSystemMonitor.Core.Model.DoubleTapModifier.Meta => CoreModifiers.Meta,
        _ => CoreModifiers.Ctrl,
    });

    /// <summary>文に埋めるキーの組み合わせ（Win「Ctrl+Alt+Space」、Mac「⌃⌥Space」）。</summary>
    public static string Shortcut(CoreModifiers mods, string key)
    {
        bool mac = OperatingSystem.IsMacOS();
        var parts = ModifierNames.Where(m => mods.HasFlag(m.Flag)).Select(m => mac ? m.Mac : m.Win).Append(key);
        return string.Join(mac ? "" : "+", parts);
    }

    /// <summary>件数つきの文（英語の単数・複数）。one / other は {0} に件数を取る書式。</summary>
    public static string Count(int n, string one, string other) =>
        string.Format(CultureInfo.CurrentCulture, n == 1 ? one : other, n);

    /// <summary>起動・フォルダを開くのに失敗した理由の文（Platform はコードだけ返す。SPEC §10.3）。</summary>
    public static string LaunchError(CPSystemMonitor.Platform.LaunchResult result) => result.Reason switch
    {
        CPSystemMonitor.Platform.LaunchFailure.NotFound => Strings.FormatLaunch_NotFound(result.Detail),
        CPSystemMonitor.Platform.LaunchFailure.Cancelled => Strings.Launch_Cancelled,
        CPSystemMonitor.Platform.LaunchFailure.LauncherUnavailable => Strings.FormatLaunch_LauncherUnavailable(result.Detail),
        _ => Strings.FormatLaunch_OsError(result.Detail ?? result.Reason.ToString()),
    };

    /// <summary>文の断片をつなぐ（日本語「、」、英語「, 」）。</summary>
    public static string Join(IEnumerable<string> parts) => string.Join(Strings.Common_ListSeparator, parts);
}
