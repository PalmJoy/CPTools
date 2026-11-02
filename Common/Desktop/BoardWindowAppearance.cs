#if CPTOOLS_CLIPBOARD
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CPClipboard.Core.Effects;
using CPClipboard.Core.Model;
using CPClipboard.Core.Storage;
using CPClipboard.Platform;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CPSystemMonitor.Core.Effects;
using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Core.Storage;
using CPSystemMonitor.Platform;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FileLauncher.Core.Effects;
using FileLauncher.Core.Model;
using FileLauncher.Core.Storage;
using FileLauncher.Platform;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>Uses the launcher's background and effect layers for auxiliary windows.</summary>
internal sealed class BoardWindowAppearance
{
    private readonly BoardBackgroundLoader _loader = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly BackdropLayer _backdrop = new();
    private readonly Border _overlay = new() { IsHitTestVisible = false };
    private readonly ChromeWindow _window;
    private readonly AppearanceSettings _appearance;
    private readonly AmbientAnimator _animator;
    private NativeAmbient? _native;
    private readonly GridLayer _grid;
    private readonly ScanlineLayer _scanlines;
    private bool _initialized;
    private readonly Action _refreshRunning;

    public BoardWindowAppearance(ChromeWindow window, AppearanceSettings appearance, DataPaths? paths, IPlatformServices? platform)
    {
        _window = window;
        _appearance = appearance;
        window.Opacity = appearance.Opacity / 100.0;
        var chrome = window.Chrome;
        window.WindowShowEffect = EffectCatalog.Resolve(appearance, EffectCatalog.BoardShow);
        window.WindowHideEffect = EffectCatalog.Resolve(appearance, EffectCatalog.BoardHide);
        chrome.Opacity = window.WindowShowEffect.IsActive ? 0 : 1;
        chrome.BackgroundKey = "FlBoardBackground";
        chrome.MarginKey = "FlGlowMarginThickness";
        chrome.ShadowKey = "FlBoardGlow";
        chrome.BakeGlow = true;
        // Reapply the width after changing the frame margin.
        window.BodyWidth = window.BodyWidth;
        var layout = chrome.Body;
        chrome.Body = null;
        var grid = _grid = new GridLayer { IsVisible = appearance.Hud.Grid, Opacity = appearance.Hud.GridOpacity / 100.0 };
        grid[!GridLayer.StrokeProperty] = Themed.Res("FlHudGrid");
        var scanlines = _scanlines = new ScanlineLayer
        {
            IsVisible = appearance.Hud.Scanlines,
            Opacity = appearance.Hud.ScanlineOpacity / 100.0,
            Pitch = appearance.Hud.ScanlinePitch,
        };
        scanlines[!ScanlineLayer.StrokeProperty] = Themed.Res("FlHudScanline");
        _overlay[!Border.BackgroundProperty] = Themed.Res("FlBoardBackground");
        _overlay.IsVisible = false;
        var body = new Grid { Children = { _backdrop, _overlay, grid, scanlines } };
        if (layout is not null) body.Children.Add(layout);
        chrome.Body = body;

        var orb = new OrbLayer { Chamfer = chrome.Chamfer, Spec = EffectCatalog.Resolve(appearance, EffectCatalog.FrameOrb) };
        var pulseSpec = EffectCatalog.Resolve(appearance, EffectCatalog.GlowPulse);
        var pulse = new PulseLayer { Chamfer = chrome.Chamfer, IsVisible = pulseSpec.IsActive };
        var beam = new ScanBeamLayer { Spec = EffectCatalog.Resolve(appearance, EffectCatalog.ScanBeam) };
        _animator = new AmbientAnimator(window, orb, pulse, beam) { Vsync = appearance.Vsync, PulseSpec = pulseSpec };
        var ambient = new Grid { IsHitTestVisible = false, Children = { pulse, beam, orb } };
        chrome.Overlays.Children.Insert(0, ambient);
        beam.Clip = chrome.FaceClip;
        chrome.FaceClipChanged += (_, _) => beam.Clip = chrome.FaceClip;
        pulse.SizeChanged += (_, _) => { if (_animator.IsRunning) pulse.Refresh(); };
        _refreshRunning = () =>
        {
            bool run = window.IsVisible && window.WindowState != WindowState.Minimized && platform?.Window.PrefersReducedMotion != true;
            if (_native?.IsAttached == true) _native.SetRunning(run);
            else if (run && (orb.Spec.IsActive || beam.Spec.IsActive || pulseSpec.IsActive)) _animator.Start();
            else _animator.Stop();
        };
        window.PropertyChanged += (_, change) =>
        {
            if (_initialized && (change.Property == Avalonia.Visual.IsVisibleProperty || change.Property == Window.WindowStateProperty)) _refreshRunning();
        };
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_cancellation.IsCancellationRequested) return;
            if (_initialized) { _refreshRunning(); return; }
            _initialized = true;
            if (platform?.Window.PrefersReducedMotion == true) return;
            if (platform is not null && !Program.AvaloniaAmbient)
            {
                _native = new NativeAmbient(window, chrome, platform.Ambient, pulse);
                if (_native.TryAttach())
                {
                    _native.Apply(orb.Spec, pulseSpec, beam.Spec);
                    _native.SetOpacity(window.Opacity);
                    ambient.IsVisible = false;
                    _native.SetRunning(true);
                    return;
                }
            }
            if (orb.Spec.IsActive || beam.Spec.IsActive || pulseSpec.IsActive)
            {
                pulse.Refresh();
                _animator.Start();
            }
        }, DispatcherPriority.Loaded);
        window.Closed += (_, _) =>
        {
            _animator.Stop();
            _native?.Dispose();
            _cancellation.Cancel();
            _backdrop.Source = null;
            _loader.Dispose();
        };
        if (paths is not null) _ = LoadBackgroundAsync(paths);
    }

    public void ApplyLiveSettings(AppearanceSettings appearance)
    {
        _window.Opacity = appearance.Opacity / 100d;
        _native?.SetOpacity(_window.Opacity);
        _grid.IsVisible = appearance.Hud.Grid;
        _grid.Opacity = appearance.Hud.GridOpacity / 100d;
        _scanlines.IsVisible = appearance.Hud.Scanlines;
        _scanlines.Opacity = appearance.Hud.ScanlineOpacity / 100d;
        _scanlines.Pitch = appearance.Hud.ScanlinePitch;
        _refreshRunning();
    }

    private async Task LoadBackgroundAsync(DataPaths paths)
    {
        try
        {
            Bitmap? image = await _loader.LoadAsync(BackgroundImageStore.Resolve(paths, _appearance.Background.Image), _cancellation.Token);
            if (_cancellation.IsCancellationRequested || image is null) return;
            _window.Chrome.SetSurfaceVisible(false);
            _backdrop.Fit = _appearance.Background.Fit;
            _backdrop.ImageOpacity = _appearance.Background.ImageOpacity / 100.0;
            _backdrop.Source = image;
            _overlay.Opacity = _appearance.Background.Overlay / 100.0;
            _overlay.IsVisible = _appearance.Background.Overlay > 0;
        }
        catch (OperationCanceledException) { }
    }
}
