#if CPTOOLS_CLIPBOARD
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CPClipboard.Core.Effects;
using CPClipboard.Core.Theming;
using CPClipboard.Platform;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CPSystemMonitor.Core.Effects;
using CPSystemMonitor.Core.Theming;
using CPSystemMonitor.Platform;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FileLauncher.Core.Effects;
using FileLauncher.Core.Theming;
using FileLauncher.Platform;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 常時の演出を OS の層（macOS = Core Animation）で描くときの橋渡し（issue/CA_AMBIENT.md §4）。
/// BoardWindow の状態（大きさ・拡大率・配色・設定・不透明度・動く / 止まる）をホストの呼び出しに写す。
/// 動く条件の判定は BoardWindow.UpdateAmbient の 1 か所のままで、ここは描き手だけを受け持つ。
/// </summary>
internal sealed class NativeAmbient : IDisposable
{
    private readonly Window _window;
    private readonly FrameChrome _chrome;
    private readonly IAmbientLayerService _service;
    private readonly PulseLayer _pulse;
    private IAmbientLayerHost? _host;
    private EffectSpec _orb = EffectSpec.None, _pulseSpec = EffectSpec.None, _beam = EffectSpec.None;
    private AmbientGeometry? _geometry;
    private object? _sentPulseImage; // 最後に PNG にして渡した焼き込み（同じなら渡し直さない）

#if !CPTOOLS_MONITOR
    public NativeAmbient(BoardWindow window, IAmbientLayerService service, PulseLayer pulse)
        : this(window, window.Chrome, service, pulse) { }
#endif

    public NativeAmbient(Window window, FrameChrome chrome, IAmbientLayerService service, PulseLayer pulse)
    {
        _chrome = chrome;
        _window = window;
        _service = service;
        _pulse = pulse;
    }

    public bool IsAttached => _host is not null;

    /// <summary>テスト用: 最後に渡した値。</summary>
    internal AmbientGeometry? Geometry => _geometry;

    /// <summary>取り付ける（1 回だけ）。非対応・失敗なら false（呼び出し側は Avalonia の層で描く）。</summary>
    public bool TryAttach()
    {
        if (_host is not null) return true;
        if (!_service.IsSupported) return false;
        _host = _service.Attach(_window.TryGetPlatformHandle()?.Handle ?? 0); // ハンドルが無ければ実装が null を返す
        if (_host is null)
        {
            AppLog.Info("常時の演出: OS の層に取り付けられない → Avalonia の層で描く");
            return false;
        }
        var frame = _chrome.Frame;
        frame.SizeChanged += (_, _) => UpdateGeometry();
        _window.SizeChanged += (_, _) => UpdateGeometry();
        _window.ScalingChanged += (_, _) => UpdateGeometry();
        // 配色の切替はテーマの入れ直しとして来る（PulseLayer の焼き直しと同じ契機）
        _window.ActualThemeVariantChanged += (_, _) => Dispatcher.UIThread.Post(() => Apply(_orb, _pulseSpec, _beam), DispatcherPriority.Loaded);
        _window.Closed += (_, _) => Dispose();
        AppLog.Info("常時の演出: Core Animation");
        return true;
    }

    /// <summary>ApplyEffects から。解決済みの 3 つの spec と配色を写す。</summary>
    public void Apply(EffectSpec orb, EffectSpec pulse, EffectSpec beam)
    {
        if (_host is null) return;
        _orb = orb;
        _pulseSpec = pulse;
        _beam = beam;
        var accent = Accent("FlAccent", Colors.Cyan);
        var accent2 = Accent("FlAccent2", Colors.Magenta);
        _host.SetOrbs(orb.IsActive ? new AmbientOrbSpec(orb.Kind == EffectKind.OrbTwin ? 2 : 1, orb.DurationMs, accent, accent2) : null);
        _host.SetBeam(beam.IsActive ? new AmbientBeamSpec(beam.DurationMs, accent) : null);
        _sentPulseImage = null; // 配色が変わっていれば焼き直した画像を渡す
        PushPulse();
    }

    public void SetRunning(bool running)
    {
        if (_host is null) return;
        if (running && _geometry is null && !UpdateGeometry())
            Dispatcher.UIThread.Post(() => UpdateGeometry(), DispatcherPriority.Loaded); // Show 直後はまだ大きさが決まっていない
        _host.SetRunning(running);
    }

    public void SetOpacity(double opacity) => _host?.SetOpacity(opacity);

    /// <summary>今の枠の位置・大きさを渡す。レイアウト前（辺が 1 未満）なら false。</summary>
    private bool UpdateGeometry()
    {
        if (_host is null) return false;
        var chrome = _chrome;
        var size = chrome.Frame.Bounds.Size;
        if (size.Width < 1 || size.Height < 1) return false;
        var origin = chrome.Frame.TranslatePoint(default, _window) ?? default;
        var geometry = new AmbientGeometry(_window.ClientSize.Width, _window.ClientSize.Height, origin.X, origin.Y, size.Width, size.Height,
            chrome.Chamfer, Token<Thickness>("FlBoardBorderThickness").Left, Token<double>("FlGlowMargin"), _window.RenderScaling);
        if (geometry == _geometry) return true;
        _geometry = geometry;
        _host.SetGeometry(geometry);
        PushPulse();
        return true;
    }

    /// <summary>明滅の画像を焼いて（古ければ）PNG で渡す。焼き直しは大きさ・拡大率・配色の変化のときだけ。</summary>
    private void PushPulse()
    {
        if (_host is null) return;
        if (!_pulseSpec.IsActive) { _host.SetPulse(null); _sentPulseImage = null; return; }
        var size = _chrome.Frame.Bounds.Size;
        if (size.Width < 1 || size.Height < 1) return; // 大きさが決まったら UpdateGeometry から来る
        _pulse.Refresh(size);
        if (_pulse.BakedImage is not { } image) return;
        if (ReferenceEquals(image, _sentPulseImage)) return;
        var sw = Stopwatch.StartNew();
        using var stream = new MemoryStream();
        image.Save(stream);
        _sentPulseImage = image;
        _host.SetPulse(new AmbientPulseSpec(_pulseSpec.DurationMs, AmbientLook.PulsePeak(_pulseSpec.Kind),
            new EncodedImage(stream.ToArray(), image.PixelSize.Width, image.PixelSize.Height)));
        AppLog.Info($"明滅の画像を渡す: {image.PixelSize.Width}×{image.PixelSize.Height} px, {sw.ElapsedMilliseconds} ms");
    }

    private RgbColor Accent(string key, Color fallback)
    {
        var c = _window.TryFindResource(key, _window.ActualThemeVariant, out var v)
            ? v switch { ISolidColorBrush b => b.Color, Color color => color, _ => fallback }
            : fallback;
        return new RgbColor(c.R, c.G, c.B);
    }

    private T Token<T>(string key) =>
        _window.TryFindResource(key, _window.ActualThemeVariant, out var v) && v is T t ? t : default!;

    public void Dispose()
    {
        _host?.Dispose();
        _host = null;
    }
}
