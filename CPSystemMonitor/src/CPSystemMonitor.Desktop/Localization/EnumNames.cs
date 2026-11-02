using CPSystemMonitor.Core.Effects;
using CPSystemMonitor.Core.Model;

namespace CPSystemMonitor.App;

/// <summary>enum の表示名（リソースのキー Enum_&lt;型名&gt;_&lt;値&gt;、spec/TERMS.md §4）と演出名（Effect_&lt;ID&gt;）。</summary>
internal static class EnumNames
{
    /// <summary>表示名を持つ enum（StringsTests が全値のキーがあるか確かめる）。</summary>
    internal static readonly Type[] Localized =
    [
        typeof(BackgroundFit), typeof(AccentPreset), typeof(ClockMode), typeof(PopupPosition),
        typeof(MouseButtonKind), typeof(EffectKind), typeof(EasingKind), typeof(CursorAnchor),
    ];

    /// <summary>macOS では OS で名前が変わる値の <c>_Mac</c> キーを優先する（TERMS.md §4）。</summary>
    public static string Of<T>(T value) where T : struct, Enum
    {
        string key = $"Enum_{typeof(T).Name}_{value}";
        return (OperatingSystem.IsMacOS() ? Strings.ResourceManager.GetString(key + "_Mac", Strings.Culture) : null)
            ?? Strings.ResourceManager.GetString(key, Strings.Culture) ?? value.ToString();
    }

    /// <summary>演出名（spec/EFFECTS.md「演出名」列）。</summary>
    public static string Effect(string id) => Strings.ResourceManager.GetString("Effect_" + id, Strings.Culture) ?? id;

    /// <summary>コンボボックス用（表示名, 値）の一覧。</summary>
    public static (string Text, T Value)[] Options<T>(params T[] values) where T : struct, Enum =>
        values.Select(v => (Of(v), v)).ToArray();
}
