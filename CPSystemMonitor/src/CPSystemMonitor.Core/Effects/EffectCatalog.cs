using CPSystemMonitor.Core.Model;

namespace CPSystemMonitor.Core.Effects;

/// <summary>
/// 演出 1 つの定義。既定値と選べる種類は spec/EFFECTS.md の表と一致させる。表示名は App のリソース（Effect_&lt;Id&gt;）。
/// </summary>
/// IsAmbient = 盤面を出している間ずっと動く演出（時間は 1 周 / 1 周期）。Min/Max/DurationStepMs = 時間の範囲と刻み。UsesEasing = イージングを使うか。
public sealed record EffectDefinition(string Id, EffectSpec Default, IReadOnlyList<EffectKind> AllowedKinds,
    bool IsAmbient = false, int MinDurationMs = 0, int MaxDurationMs = EffectSpec.MaxDurationMs,
    int DurationStepMs = 10, bool UsesEasing = true)
{
}

/// <summary>
/// 演出の一覧と解決（spec/EFFECTS.md）。settings.json には既定と違う演出だけを appearance.effects に保存する。
/// </summary>
public static class EffectCatalog
{
    public const string BoardShow = "boardShow";
    public const string BoardHide = "boardHide";
    public const string GroupSwitch = "groupSwitch";
    public const string TabHighlight = "tabHighlight";
    public const string RowHover = "rowHover";
    public const string RowPress = "rowPress";
    public const string PasteFlash = "pasteFlash";
    public const string PreviewShow = "previewShow";
    public const string ToastShow = "toastShow";
    public const string ToastHide = "toastHide";
    public const string FrameOrb = "frameOrb";
    public const string GlowPulse = "glowPulse";
    public const string ScanBeam = "scanBeam";

    private static EffectSpec Spec(EffectKind kind, int ms, EasingKind easing) => new() { Kind = kind, DurationMs = ms, Easing = easing };

    private static readonly EffectKind[] ShowKinds = [EffectKind.None, EffectKind.Fade, EffectKind.Zoom, EffectKind.Slide, EffectKind.Glitch];
    private static readonly EffectKind[] HideKinds = [EffectKind.None, EffectKind.Fade, EffectKind.Zoom, EffectKind.Slide, EffectKind.Glitch];
    private static readonly EffectKind[] HighlightKinds = [EffectKind.None, EffectKind.Highlight, EffectKind.Glow];
    private static readonly EffectKind[] FadeKinds = [EffectKind.None, EffectKind.Fade];

    public static IReadOnlyList<EffectDefinition> All { get; } =
    [
        new(BoardShow, Spec(EffectKind.Glitch, 250, EasingKind.Linear), ShowKinds), // 2026-10-03 ユーザー要望で 1.5 倍、2026-10-10 に 250
        new(BoardHide, Spec(EffectKind.Glitch, 250, EasingKind.Linear), HideKinds), // 2026-10-03 ユーザー要望: Out も In と同じグリッチ。2026-10-10 に 250
        new(ToastShow, Spec(EffectKind.Slide, 150, EasingKind.EaseOut), [EffectKind.None, EffectKind.Fade, EffectKind.Slide]),
        new(ToastHide, Spec(EffectKind.Fade, 150, EasingKind.EaseIn), FadeKinds),
        // 常時の演出（2026-10-04 ユーザー要望）。3 つともこのアプリの肝なので既定でオン（2026-10-07 ユーザー判断。それまでの
        // 「CPU が重いので既定なし」は撤回。30 fps 化と背景の作り置きで軽くなった。spec/EFFECTS.md「既定値の経緯」）
        new(FrameOrb, Spec(EffectKind.Orb, 8000, EasingKind.Linear), [EffectKind.None, EffectKind.Orb, EffectKind.OrbTwin],
            IsAmbient: true, MinDurationMs: 2000, MaxDurationMs: 20000, DurationStepMs: 500, UsesEasing: false),
        new(GlowPulse, Spec(EffectKind.Pulse, 4000, EasingKind.Linear), [EffectKind.None, EffectKind.Pulse, EffectKind.PulseStrong],
            IsAmbient: true, MinDurationMs: 1000, MaxDurationMs: 10000, DurationStepMs: 250, UsesEasing: false),
        // 走査線の帯（SPEC §3.9 H14）
        new(ScanBeam, Spec(EffectKind.Beam, 4000, EasingKind.Linear), [EffectKind.None, EffectKind.Beam],
            IsAmbient: true, MinDurationMs: 2000, MaxDurationMs: 12000, DurationStepMs: 500, UsesEasing: false),
    ];

    private static readonly Dictionary<string, EffectDefinition> ById = All.ToDictionary(d => d.Id);

    public static EffectDefinition? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>既定値。</summary>
    public static EffectSpec DefaultFor(string id) =>
        ById.TryGetValue(id, out var def) ? def.Default : EffectSpec.None;

    /// <summary>実際に使う値。アニメーション OFF なら None、上書きがあればそれ、無ければ既定。</summary>
    public static EffectSpec Resolve(AppearanceSettings appearance, string id)
    {
        if (!appearance.Animation) return EffectSpec.None;
        if (appearance.Effects is { } overrides && overrides.TryGetValue(id, out var spec)) return spec;
        return DefaultFor(id);
    }

    /// <summary>
    /// 保存値を整える: 時間を演出ごとの範囲（ふつうは 0〜1000）に丸める、未知の ID・その演出で選べない種類は捨てる、そのテーマの既定と同じものは捨てる
    /// （「既定と違うものだけ保存」を保つ）。
    /// </summary>
    public static Dictionary<string, EffectSpec> Normalize(Dictionary<string, EffectSpec>? effects)
    {
        var result = new Dictionary<string, EffectSpec>(StringComparer.Ordinal);
        if (effects is null) return result;
        foreach (var (id, raw) in effects)
        {
            if (raw is null || !ById.TryGetValue(id, out var def) || !def.AllowedKinds.Contains(raw.Kind)) continue;
            int min = raw.Kind == EffectKind.None ? 0 : def.MinDurationMs;
            var spec = raw with { DurationMs = Math.Clamp(raw.DurationMs, min, def.MaxDurationMs) };
            // glitch はステップ関数、常時の演出は一定速度（イージングを使わない）
            if (spec.Kind == EffectKind.Glitch || !def.UsesEasing) spec = spec with { Easing = EasingKind.Linear };
            if (spec != def.Default) result[id] = spec;
        }
        return result;
    }
}
