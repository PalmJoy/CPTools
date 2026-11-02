using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using CPSystemMonitor.Core.Effects;

namespace CPSystemMonitor.App;

/// <summary>
/// EffectSpec を Avalonia の Transitions で再生する（spec/EFFECTS.md）。
/// fade = 透明度、zoom = 透明度 + 0.96⇔1.0 の拡大縮小、slide = 透明度 + 12 px の平行移動。
/// 開始状態を即座に入れてから Transitions を付けて最終値を入れる。値そのものが最終値なので、
/// 終わった瞬間に開始値へ戻るちらつきが無い（KeyFrame アニメーションは終了時に 1 フレーム戻ることがあった）。
/// </summary>
internal static class EffectRunner
{
    private static readonly TransformOperations Identity = TransformOperations.Parse("none");
    private static readonly TransformOperations ZoomOut = TransformOperations.Parse("scale(0.96)");
    private static readonly TransformOperations SlideDown = TransformOperations.Parse("translateY(12px)");

    private static readonly TransformOperations SlideFromRight = TransformOperations.Parse("translateX(12px)");
    private static readonly TransformOperations SlideFromLeft = TransformOperations.Parse("translateX(-12px)");
    private static readonly TransformOperations Pulse = TransformOperations.Parse("scale(1.12)");
    private static readonly TransformOperations Pressed = TransformOperations.Parse("scale(0.92)");
    // 行は横長なので控えめに縮める（spec/EFFECTS.md rowPress）
    private static readonly TransformOperations RowPressed = TransformOperations.Parse("scale(0.97)");

    /// <summary>表示: 透明 → 不透明。</summary>
    public static Task ShowAsync(Visual target, EffectSpec spec, CancellationToken ct = default) =>
        RunAsync(target, spec, appearing: true, ct);

    /// <summary>ページ切替: 新しいページを出す。slide は進む向き（+1: 右から、-1: 左から）。</summary>
    public static Task PageInAsync(Visual target, EffectSpec spec, int direction, CancellationToken ct = default) =>
        RunAsync(target, spec, appearing: true, ct, slideOverride: direction >= 0 ? SlideFromRight : SlideFromLeft);

    /// <summary>起動のフィードバック: zoom は 1.0→1.12→1.0、fade は一瞬薄くして戻す。</summary>
    public static async Task PulseAsync(Visual target, EffectSpec spec)
    {
        if (!spec.IsActive) return;
        var half = TimeSpan.FromMilliseconds(spec.DurationMs / 2.0);
        var easing = ToEasing(spec.Easing);
        target.RenderTransformOrigin = RelativePoint.Center;
        target.Transitions =
        [
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = half, Easing = easing },
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = half, Easing = easing },
        ];
        target.RenderTransform = Identity;
        if (spec.Kind == EffectKind.Zoom) target.RenderTransform = Pulse;
        else target.Opacity = 0.4;
        await Task.Delay(half);
        target.RenderTransform = Identity;
        target.Opacity = 1;
        await Task.Delay(half);
        target.Transitions = null;
    }

    /// <summary>
    /// 状態の変化（押下・ホバーで出るラベルなど）に時間を付ける Transitions。演出なしなら null（即座に変わる）。
    /// </summary>
    public static Transitions? StateTransitions(EffectSpec spec, params AvaloniaProperty[] properties)
    {
        if (!spec.IsActive) return null;
        var duration = TimeSpan.FromMilliseconds(spec.DurationMs);
        var easing = ToEasing(spec.Easing);
        var t = new Transitions();
        foreach (var p in properties)
        {
            if (p == Visual.RenderTransformProperty) t.Add(new TransformOperationsTransition { Property = p, Duration = duration, Easing = easing });
            else if (p.PropertyType == typeof(Avalonia.Media.BoxShadows)) t.Add(new BoxShadowsTransition { Property = p, Duration = duration, Easing = easing });
            else if (p.PropertyType == typeof(double)) t.Add(new DoubleTransition { Property = p, Duration = duration, Easing = easing });
            else t.Add(new BrushTransition { Property = p, Duration = duration, Easing = easing });
        }
        return t;
    }

    public static TransformOperations PressedTransform => Pressed;
    public static TransformOperations RowPressedTransform => RowPressed;
    public static TransformOperations NoTransform => Identity;

    /// <summary>非表示: 不透明 → 透明。終わったら Opacity = 0（次の表示で戻す）。</summary>
    public static Task HideAsync(Visual target, EffectSpec spec, CancellationToken ct = default) =>
        RunAsync(target, spec, appearing: false, ct);

    /// <summary>演出なしの最終状態にする（打ち切り・OFF のとき）。</summary>
    public static void Reset(Visual target, bool visible)
    {
        target.Transitions = null;
        target.Opacity = visible ? 1 : 0;
        target.RenderTransform = null;
    }

    private static async Task RunAsync(Visual target, EffectSpec spec, bool appearing, CancellationToken ct, TransformOperations? slideOverride = null)
    {
        if (!spec.IsActive || spec.Kind == EffectKind.Highlight)
        {
            Reset(target, appearing);
            return;
        }

        var offset = spec.Kind switch
        {
            EffectKind.Zoom => ZoomOut,
            EffectKind.Slide => slideOverride ?? SlideDown,
            _ => null,
        };

        // 開始状態（Transitions を外した状態で入れるので即座に効く）
        target.Transitions = null;
        target.RenderTransformOrigin = RelativePoint.Center;
        target.Opacity = appearing ? 0 : 1;
        target.RenderTransform = offset is null ? null : appearing ? offset : Identity;

        var duration = TimeSpan.FromMilliseconds(spec.DurationMs);
        var easing = ToEasing(spec.Easing);
        target.Transitions =
        [
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = easing },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = easing },
        ];

        // 最終値（ここから Transitions が補間する）
        target.Opacity = appearing ? 1 : 0;
        if (offset is not null) target.RenderTransform = appearing ? Identity : offset;

        try { await Task.Delay(duration, ct); }
        catch (OperationCanceledException) { return; } // 打ち切った側が状態を決める
        target.Transitions = null;
        if (appearing) target.RenderTransform = null;
    }

    public static Easing ToEasing(EasingKind kind) => kind switch
    {
        EasingKind.Linear => new LinearEasing(),
        EasingKind.EaseIn => new CubicEaseIn(),
        EasingKind.EaseInOut => new CubicEaseInOut(),
        EasingKind.BackOut => new BackEaseOut(),
        _ => new CubicEaseOut(),
    };
}
