#if CPTOOLS_CLIPBOARD
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CPClipboard.Core.Effects;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CPSystemMonitor.Core.Effects;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FileLauncher.Core.Effects;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 盤面の出現時のグリッチ（spec/EFFECTS.md「glitch の詳細」）。盤面（Frame）の静止画を撮り、
/// シアン / マゼンタの影 + 本体 + 横にずれた帯 3 本を GlitchLayer に重ねて、時間 ÷ 3 ごとに帯を引き直す。ずれ・影の濃さは
/// <see cref="GlitchLook"/>（設定「グリッチの強さ」）。
/// 同時に盤面全体をフェードイン（表示）/ フェードアウト（非表示）する。時間が来たら静止画層を消して本物の盤面に切り替える。常時動く演出ではない。
/// 静止画が撮れないとき（描画バックエンド無し・大きさ未確定）は呼び出し側で fade に切り替える。
/// </summary>
internal sealed class GlitchPlayer
{
    private const int Steps = 3;

    private readonly Random _random;

    public GlitchPlayer(Random? random = null) => _random = random ?? Random.Shared;

    /// <summary>直前の再生で静止画を撮るのにかかった時間（Debug ログ・計測用）。</summary>
    public double LastSnapshotMs { get; private set; }

    /// <summary>
    /// 静止画を撮って最初の段を出すところまでを同期的に行い（最初のフレームからグリッチが見えるように）、残りを非同期で進める。
    /// 撮れなければ null を返す（何も変えていない）。
    /// </summary>
    /// <param name="appearing">true = 表示（透明 → 不透明にフェードイン）、false = 非表示（不透明 → 透明にフェードアウト）。</param>
    /// <param name="fade">false ならフェードを重ねない（常駐の前面化・端へ隠れるとき。見えている盤面を消さない）。</param>
    public Task? Start(Visual root, Visual frame, Canvas layer, EffectSpec spec, GlitchLook look, IBrush cyan, IBrush magenta, CancellationToken ct,
        bool appearing = true, bool fade = true)
    {
        var bounds = frame.Bounds;
        if (bounds.Width < 1 || bounds.Height < 1) return null;

        var sw = Stopwatch.StartNew();
        RenderTargetBitmap snapshot;
        try
        {
            double scale = (TopLevel.GetTopLevel(frame)?.RenderScaling) ?? 1.0;
            var px = new PixelSize(Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(bounds.Height * scale)));
            snapshot = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
            snapshot.Render(frame);
        }
        catch (Exception)
        {
            return null;
        }
        LastSnapshotMs = sw.Elapsed.TotalMilliseconds;

        layer.Children.Clear();
        layer.Width = bounds.Width;
        layer.Height = bounds.Height;
        Canvas.SetLeft(layer, 0);
        Canvas.SetTop(layer, 0);

        Control Shadow(IBrush color, double dx) => new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Background = color,
            OpacityMask = new ImageBrush(snapshot) { Stretch = Stretch.Fill },
            Opacity = look.ShadowOpacity,
            RenderTransform = new TranslateTransform(dx, 0),
        };
        // 強さ 0% はずれも色にじみも無い = 本体の静止画にフェードだけ（影・帯を作らず乱数も使わない）
        if (!look.IsStill)
        {
            layer.Children.Add(Shadow(cyan, -look.ShadowOffset));
            layer.Children.Add(Shadow(magenta, look.ShadowOffset));
        }
        layer.Children.Add(new Image { Source = snapshot, Width = bounds.Width, Height = bounds.Height, Stretch = Stretch.Fill });
        var bands = new List<Image>();
        for (int i = 0; i < (look.IsStill ? 0 : 3); i++)
        {
            var band = new Image { Source = snapshot, Width = bounds.Width, Height = bounds.Height, Stretch = Stretch.Fill };
            bands.Add(band);
            layer.Children.Add(band);
        }
        Shuffle(bands, bounds.Size, look, final: false);

        frame.Opacity = 0;
        layer.IsVisible = true;
        // ずれと同時にフェード（2026-10-03 ユーザー要望）。フェードはグリッチの半分の時間で、一定の速さ:
        // 表示は前半でフェードイン（後半は不透明のままブレる）、非表示は後半でフェードアウト（前半はくっきりブレる）
        var half = TimeSpan.FromMilliseconds(spec.DurationMs / 2.0);
        root.Transitions = null;
        if (fade)
        {
            root.Opacity = appearing ? 0 : 1;
            root.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = half, Delay = appearing ? TimeSpan.Zero : half }];
            root.Opacity = appearing ? 1 : 0;
        }
        else root.Opacity = 1;
        return RunAsync(root, frame, layer, bands, bounds.Size, snapshot, spec, look, ct);
    }

    private async Task RunAsync(Visual root, Visual frame, Canvas layer, List<Image> bands, Size size, RenderTargetBitmap snapshot, EffectSpec spec, GlitchLook look, CancellationToken ct)
    {
        var step = TimeSpan.FromMilliseconds(spec.DurationMs / (double)Steps);
        try
        {
            for (int i = 1; i < Steps; i++)
            {
                await Task.Delay(step, ct);
                Shuffle(bands, size, look, final: i == Steps - 1);
            }
            await Task.Delay(step, ct);
        }
        catch (OperationCanceledException)
        {
            // 打ち切った側（ResetFrame）が層を片付ける
            return;
        }
        root.Transitions = null;
        Finish(frame, layer);
        snapshot.Dispose();
    }

    /// <summary>静止画層を消して本物の盤面に戻す（完了・打ち切りの共通処理）。</summary>
    public static void Finish(Visual frame, Canvas layer)
    {
        layer.IsVisible = false;
        foreach (var child in layer.Children)
        {
            if (child is Image { Source: IDisposable d }) d.Dispose();
        }
        layer.Children.Clear();
        frame.Opacity = 1;
    }

    /// <summary>帯の位置とずれ量を引き直す。最後の段は帯 1 本・±FinalShift に収束させる。乱数の引き方は強さに依らない。</summary>
    private void Shuffle(List<Image> bands, Size size, GlitchLook look, bool final)
    {
        for (int i = 0; i < bands.Count; i++)
        {
            var band = bands[i];
            if (final && i > 0)
            {
                band.IsVisible = false;
                continue;
            }
            band.IsVisible = true;
            double h = size.Height * (0.06 + _random.NextDouble() * 0.12);
            double y = _random.NextDouble() * Math.Max(0, size.Height - h);
            double shift = final ? look.FinalShift : look.BandShiftMin + _random.NextDouble() * (look.BandShiftMax - look.BandShiftMin);
            if (_random.Next(2) == 0) shift = -shift;
            band.Clip = new RectangleGeometry(new Rect(0, y, size.Width, h));
            band.RenderTransform = new TranslateTransform(shift, 0);
        }
    }
}
