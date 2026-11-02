#if CPTOOLS_CLIPBOARD
using System.Diagnostics;
using Avalonia.Controls;
using CPClipboard.Core.Effects;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using System.Diagnostics;
using Avalonia.Controls;
using CPSystemMonitor.Core.Effects;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using System.Diagnostics;
using Avalonia.Controls;
using FileLauncher.Core.Effects;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 常時の演出（光の玉・発光の明滅・走査線の帯）を毎フレーム進める（spec/EFFECTS.md「常時の演出の詳細」）。
/// TopLevel.RequestAnimationFrame で動き、止まっている間はフレームを要求しない（CPU 0）。更新の間隔は設定「VSync」（既定 約 30 fps）。
/// </summary>
internal sealed class AmbientAnimator(TopLevel topLevel, OrbLayer orbs, PulseLayer pulse, ScanBeamLayer beam)
{
    /// <summary>設定「VSync」（1 = 60 fps / 2 = 30 fps / 3 = 20 fps、既定 2）。BoardWindow.ApplyEffects が入れる。</summary>
    public int Vsync { get; set; } = 2;

    /// <summary>
    /// 更新の最小間隔。macOS の Avalonia は一部が動くだけで窓全体を描き直すので、60 fps では CPU を 1.5 倍使った（2026-10-07 計測）。
    /// 値は Core AmbientMath.FrameIntervalMs（60 Hz の N 枚ぶんより少し短い）。
    /// </summary>
    internal double MinFrameMs => AmbientMath.FrameIntervalMs(Vsync);
    private const double PulseStep = 0.02;
    private readonly Stopwatch _clock = new();
    private double _lastMs = double.NegativeInfinity;
    private double _lastFrameMs = double.NegativeInfinity;

    // Start のたびに進める。Stop → すぐ Start で、前のフレーム要求が未発火のまま残ると要求の連鎖が 2 本になるので、
    // 古い世代のコールバックは捨てる
    private int _generation;

    public bool IsRunning { get; private set; }

    /// <summary>明滅（glowPulse）。none なら Opacity は 0 のまま。</summary>
    public EffectSpec PulseSpec { get; set; } = EffectSpec.None;

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _clock.Start();
        _lastMs = double.NegativeInfinity;
        _lastFrameMs = double.NegativeInfinity;
        Tick(_clock.Elapsed.TotalMilliseconds);
        RequestFrame(++_generation);
    }

    private void RequestFrame(int generation) => topLevel.RequestAnimationFrame(t => OnFrame(generation, t.TotalMilliseconds));

    /// <summary>このフレームで更新するか（フレーム時刻で間引く。最初のフレームは必ず）。</summary>
    internal static bool ShouldTick(double lastFrameMs, double frameMs, double minFrameMs) =>
        double.IsNegativeInfinity(lastFrameMs) || frameMs - lastFrameMs >= minFrameMs;

    public void Stop()
    {
        IsRunning = false;
        _clock.Stop(); // 再開したら続きから（玉が止まった位置から動き出す）
    }

    private void OnFrame(int generation, double frameMs)
    {
        if (!IsRunning || generation != _generation) return; // Stop の後・再開前の世代から遅れて来た 1 回
        double now = _clock.Elapsed.TotalMilliseconds; // 位置・明るさは Stopwatch（Stop で止まり、再開は続きから）
        if (ShouldTick(_lastFrameMs, frameMs, MinFrameMs)) { _lastFrameMs = frameMs; Tick(now); }
        RequestFrame(generation);
    }

    /// <summary>1 フレームぶん適用する（UI テストは時計を使わずこれで進める）。</summary>
    internal void Tick(double elapsedMs)
    {
        _lastMs = elapsedMs;
        orbs.Advance(elapsedMs);
        beam.Advance(elapsedMs);
        double peak = AmbientLook.PulsePeak(PulseSpec.Kind);
        double level = peak <= 0 ? 0 : AmbientMath.PulseLevel(elapsedMs, PulseSpec.DurationMs, peak);
        // 明滅の層は盤面全体に掛かるので、変えるたびに盤面全体が描き直しになる。目に見える差（2%）が出たときだけ変える
        if (Math.Abs(level - pulse.Level) >= PulseStep || (level == 0) != (pulse.Level == 0)) pulse.Level = level;
    }
}
