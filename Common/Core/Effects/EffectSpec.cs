#if CPTOOLS_CLIPBOARD
namespace CPClipboard.Core.Effects;
#elif CPTOOLS_MONITOR
namespace CPSystemMonitor.Core.Effects;
#elif CPTOOLS_LAUNCHER
namespace FileLauncher.Core.Effects;
#else
#error A CPTools product must be selected.
#endif
/// <summary>演出の種類（spec/EFFECTS.md「種類」）。</summary>
public enum EffectKind
{
    None,
    Fade,
    Zoom,
    Slide,
    Highlight,

    /// <summary>盤面の出現時に一瞬だけ横ずれ + 色にじみ（盤面の表示専用。spec/EFFECTS.md「glitch の詳細」）。</summary>
    Glitch,

    /// <summary>色の変化 + 発光の強まり（ホバー・タブ・ドロップ先。「glow の詳細」）。</summary>
    Glow,

    /// <summary>常時の演出専用: 枠線の上を光の玉が 1 個周回する（frameOrb。spec/EFFECTS.md「常時の演出の詳細」）。</summary>
    Orb,

    /// <summary>常時の演出専用: 光の玉 2 個（主色と副色が対角）。</summary>
    OrbTwin,

    /// <summary>常時の演出専用: 枠の発光の柔らかい明滅（弱）。</summary>
    Pulse,

    /// <summary>常時の演出専用: 枠の発光の明滅（強）。</summary>
    PulseStrong,

    /// <summary>常時の演出専用: 走査線の帯が面を上から下へ流れ続ける（scanBeam）。</summary>
    Beam,
}

/// <summary>イージング（spec/EFFECTS.md「イージング」）。</summary>
public enum EasingKind
{
    Linear,
    EaseOut,
    EaseIn,
    EaseInOut,
    BackOut,
}

/// <summary>1 つの演出のパラメータ。settings.json の appearance.effects.&lt;ID&gt; にこの形で入る。</summary>
public sealed record EffectSpec
{
    public const int MaxDurationMs = 1000;

    public static EffectSpec None { get; } = new() { Kind = EffectKind.None, DurationMs = 0, Easing = EasingKind.Linear };

    public EffectKind Kind { get; init; }
    public int DurationMs { get; init; }
    public EasingKind Easing { get; init; } = EasingKind.EaseOut;

    /// <summary>実際に何かが動くか（なし・0 ms は即座に最終状態）。保存しない。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsActive => Kind != EffectKind.None && DurationMs > 0;
}
