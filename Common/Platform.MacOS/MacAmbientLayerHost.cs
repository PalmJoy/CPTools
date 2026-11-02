#if CPTOOLS_CLIPBOARD
using System.Diagnostics;
using System.Runtime.Versioning;
using CPClipboard.Core.Effects;
using CPClipboard.Core.Theming;
using CPClipboard.Platform.MacOS.Native;
using static CPClipboard.Platform.MacOS.Native.CoreAnimation;
using static CPClipboard.Platform.MacOS.Native.ObjC;

namespace CPClipboard.Platform.MacOS;
#elif CPTOOLS_MONITOR
using System.Diagnostics;
using System.Runtime.Versioning;
using CPSystemMonitor.Core.Effects;
using CPSystemMonitor.Core.Theming;
using CPSystemMonitor.Platform.MacOS.Native;
using static CPSystemMonitor.Platform.MacOS.Native.CoreAnimation;
using static CPSystemMonitor.Platform.MacOS.Native.ObjC;

namespace CPSystemMonitor.Platform.MacOS;
#elif CPTOOLS_LAUNCHER
using System.Diagnostics;
using System.Runtime.Versioning;
using FileLauncher.Core.Effects;
using FileLauncher.Core.Theming;
using FileLauncher.Platform.MacOS.Native;
using static FileLauncher.Platform.MacOS.Native.CoreAnimation;
using static FileLauncher.Platform.MacOS.Native.ObjC;

namespace FileLauncher.Platform.MacOS;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 盤面の窓 1 つぶんの Core Animation の層（issue/CA_AMBIENT.md §3）。contentView の層の上にコンテナ _root を重ね、
/// 明滅 → 帯 → 玉の順に置く。動きはすべて OS（render server）が進め、アプリは毎フレーム何もしない。
/// 時間はコンテナの speed / timeOffset で止める・進める（Apple QA1673）。アニメーションの beginTime は共通の _startTime なので、
/// 作り直しても位相が続く（ページ切替で玉が飛ばない）。
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacAmbientLayerHost : IAmbientLayerHost
{
    private const int PulseSamples = 33;

    private readonly nint _view;
    private readonly nint _root, _pulse, _beamClip, _beamBand, _beamMask, _orbs; // retain して持つ
    private AmbientGeometry? _geometry;
    private AmbientOrbSpec? _orbSpec;
    private AmbientPulseSpec? _pulseSpec;
    private AmbientBeamSpec? _beamSpec;
    private EncodedImage? _pulseImage; // _pulse の contents に入っている画像
    private double _opacity = 1;
    private bool _running, _dirty = true, _disposed;
    private double _startTime = double.NaN;

    public MacAmbientLayerHost(nint view)
    {
        _view = view;
        using var t = Transaction.Begin();
        _root = Retain(Layer("CALayer"));
        _pulse = Retain(Layer("CALayer"));
        _beamClip = Retain(Layer("CALayer"));
        _beamBand = Retain(Layer("CAGradientLayer"));
        _beamMask = Retain(Layer("CAShapeLayer"));
        _orbs = Retain(Layer("CALayer"));
        SetHidden(_root, true);
        // Avalonia の描画（contentView の層の子）より必ず上に（後から足される子の下に潜らないように）
        SetDouble(_root, "setZPosition:", 1000);
        // 止めた状態で始める（最初の SetRunning(true) で時間を進め、_startTime を決める）
        SendVoidFloat(_root, Sel("setSpeed:"), 0f); // speed は float
        CoreAnimation.SetOpacity(_pulse, 0);
        SendVoid(_beamClip, Sel("setMask:"), _beamMask);
        AddSublayer(_beamClip, _beamBand);
        AddSublayer(_root, _pulse);
        AddSublayer(_root, _beamClip);
        AddSublayer(_root, _orbs);
        AddSublayer(Send(view, Sel("layer")), _root);
        Trace.WriteLine("[ambient] attached");
    }

    public void SetGeometry(AmbientGeometry geometry) => Change(() => _geometry = geometry);
    public void SetOrbs(AmbientOrbSpec? orbs) => Change(() => _orbSpec = orbs);
    public void SetPulse(AmbientPulseSpec? pulse) => Change(() => _pulseSpec = pulse);
    public void SetBeam(AmbientBeamSpec? beam) => Change(() => _beamSpec = beam);

    public void SetOpacity(double opacity)
    {
        Guard();
        _opacity = Math.Clamp(opacity, 0, 1);
        using var pool = AutoreleasePool.Push();
        using var t = Transaction.Begin();
        CoreAnimation.SetOpacity(_root, _opacity);
    }

    public void SetRunning(bool running)
    {
        Guard();
        if (running == _running) return;
        _running = running;
        using var pool = AutoreleasePool.Push();
        using var t = Transaction.Begin();
        if (running) Resume(); else Pause();
    }

    /// <summary>値を覚え、動いていればその場で組み直す。止まっている間は次の再開でまとめて（WindowServer に仕事をさせない）。</summary>
    private void Change(Action set)
    {
        Guard();
        set();
        _dirty = true;
        if (!_running) return;
        using var pool = AutoreleasePool.Push();
        using var t = Transaction.Begin();
        Rebuild();
    }

    private void Pause()
    {
        double now = LocalTime();
        SendVoidFloat(_root, Sel("setSpeed:"), 0f); // speed は float
        SetDouble(_root, "setTimeOffset:", now);
        SetHidden(_root, true);
        Send(_root, Sel("removeAnimationForKey:"), NSString("fadeIn"));
    }

    private void Resume()
    {
        EnsureAttached();
        double paused = SendRetDouble(_root, Sel("timeOffset"));
        SendVoidFloat(_root, Sel("setSpeed:"), 1f);
        SetDouble(_root, "setTimeOffset:", 0);
        SetDouble(_root, "setBeginTime:", 0);
        SetDouble(_root, "setBeginTime:", LocalTime() - paused);
        if (double.IsNaN(_startTime)) _startTime = LocalTime();
        if (_dirty) Rebuild();
        CoreAnimation.SetOpacity(_root, _opacity);
        nint fade = BasicAnimation("opacity");
        Send(fade, Sel("setFromValue:"), NSNumber(0));
        Send(fade, Sel("setToValue:"), NSNumber(_opacity));
        SendVoidDouble(fade, Sel("setDuration:"), AmbientLook.AmbientFadeInMs / 1000.0);
        AddAnimation(_root, fade, "fadeIn");
        SetHidden(_root, false);
    }

    /// <summary>_root のローカル時刻（今）。</summary>
    private double LocalTime() => SendRetDouble(_root, Sel("convertTime:fromLayer:"), CACurrentMediaTime(), 0);

    /// <summary>Avalonia が contentView の層を作り直したときの保険。</summary>
    private void EnsureAttached()
    {
        nint layer = Send(_view, Sel("layer"));
        if (layer == 0 || Send(_root, Sel("superlayer")) == layer) return;
        Send(_root, Sel("removeFromSuperlayer"));
        AddSublayer(layer, _root);
        Trace.WriteLine("[ambient] re-attached to the content layer");
    }

    private void Rebuild()
    {
        _dirty = false;
        if (_geometry is not { } g || g.FrameWidth < 1 || g.FrameHeight < 1)
        {
            SetHidden(_pulse, true); SetHidden(_beamClip, true); SetHidden(_orbs, true);
            return;
        }
        SetFrame(_root, 0, 0, g.WindowWidth, g.WindowHeight);
        // 子は左上原点で置く（contentView が flipped でなければコンテナで反転する）
        SendVoidBool(_root, Sel("setGeometryFlipped:"), !SendRetBool(_view, Sel("isFlipped")));
        BuildPulse(g);
        BuildBeam(g);
        BuildOrbs(g);
    }

    private void BuildPulse(AmbientGeometry g)
    {
        Send(_pulse, Sel("removeAllAnimations"));
        if (_pulseSpec is not { } spec || spec.Peak <= 0 || spec.PeriodMs <= 0)
        {
            SetHidden(_pulse, true);
            return;
        }
        if (!ReferenceEquals(spec.Image, _pulseImage))
        {
            if (!SetContents(_pulse, spec.Image.Png)) { SetHidden(_pulse, true); return; }
            _pulseImage = spec.Image;
        }
        var (x, y, w, h) = AmbientLayout.PulseLayerRect(g.FrameX, g.FrameY, g.GlowMargin, spec.Image.PixelWidth, spec.Image.PixelHeight, g.Scale);
        SetFrame(_pulse, x, y, w, h);
        SetDouble(_pulse, "setContentsScale:", g.Scale);
        nint nearest = NSString("nearest");
        Send(_pulse, Sel("setMagnificationFilter:"), nearest);
        Send(_pulse, Sel("setMinificationFilter:"), nearest);
        CoreAnimation.SetOpacity(_pulse, 0);
        var keys = AmbientLook.PulseKeyframes(spec.Peak, PulseSamples);
        nint anim = KeyframeAnimation("opacity");
        Send(anim, Sel("setValues:"), NSArray(keys.Select(NSNumber).ToArray()));
        AddAnimation(_pulse, Repeating(anim, spec.PeriodMs / 1000.0, _startTime, 0), "pulse");
        SetHidden(_pulse, false);
    }

    /// <summary>PNG → CGImage → contents（層が retain するので作った側はすぐ解放）。</summary>
    private static bool SetContents(nint layer, byte[] png)
    {
        nint data = CoreFoundation.CFDataCreate(0, png, png.Length);
        if (data == 0) return false;
        nint source = ImageIO.CGImageSourceCreateWithData(data, 0);
        nint image = source == 0 ? 0 : ImageIO.CGImageSourceCreateImageAtIndex(source, 0, 0);
        if (image != 0) SendVoid(layer, Sel("setContents:"), image);
        if (image != 0) CoreGraphics.CGImageRelease(image);
        if (source != 0) CoreFoundation.CFRelease(source);
        CoreFoundation.CFRelease(data);
        if (image == 0) Trace.WriteLine("[ambient] pulse image decode failed");
        return image != 0;
    }

    private void BuildBeam(AmbientGeometry g)
    {
        Send(_beamBand, Sel("removeAllAnimations"));
        if (_beamSpec is not { } spec || spec.PeriodMs <= 0)
        {
            SetHidden(_beamClip, true);
            return;
        }
        var (fx, fy, fw, fh) = AmbientLayout.FaceRect(g.FrameX, g.FrameY, g.FrameWidth, g.FrameHeight, g.BorderThickness);
        SetFrame(_beamClip, fx, fy, fw, fh);
        SetFrame(_beamMask, 0, 0, fw, fh);
        nint path = CoreGraphics.ClosedPath(Octagon.Points(0, 0, fw, fh, g.Chamfer));
        SendVoid(_beamMask, Sel("setPath:"), path);
        CoreGraphics.CGPathRelease(path);

        var c = spec.Color;
        var colors = AmbientLook.BeamStops.Select(s => CoreGraphics.CGColorCreateSRGB(c.R / 255.0, c.G / 255.0, c.B / 255.0, s.Alpha / 255.0)).ToArray();
        Send(_beamBand, Sel("setColors:"), NSArray(colors));
        foreach (nint color in colors) CoreGraphics.CGColorRelease(color);
        Send(_beamBand, Sel("setLocations:"), NSArray(AmbientLook.BeamStops.Select(s => NSNumber(s.Offset)).ToArray()));
        // 上端が透明・下端が明るい（_root が左上原点なので y は下向き）
        SendVoidPoint(_beamBand, Sel("setStartPoint:"), new CGPoint { X = 0.5, Y = 0 });
        SendVoidPoint(_beamBand, Sel("setEndPoint:"), new CGPoint { X = 0.5, Y = 1 });
        SetBounds(_beamBand, fw, AmbientLook.BeamBandHeight);
        SetPosition(_beamBand, fw / 2, AmbientLayout.BeamCenterY(0, fh));
        SetDouble(_beamBand, "setContentsScale:", g.Scale);

        nint anim = BasicAnimation("position.y");
        Send(anim, Sel("setFromValue:"), NSNumber(AmbientLayout.BeamCenterY(0, fh)));
        Send(anim, Sel("setToValue:"), NSNumber(AmbientLayout.BeamCenterY(1, fh)));
        AddAnimation(_beamBand, Repeating(anim, spec.PeriodMs / 1000.0, _startTime, 0), "beam");
        SetHidden(_beamClip, false);
    }

    private void BuildOrbs(AmbientGeometry g)
    {
        Send(_orbs, Sel("setSublayers:"), 0);
        if (_orbSpec is not { } spec || spec.PeriodMs <= 0 || spec.Count <= 0)
        {
            SetHidden(_orbs, true);
            return;
        }
        SetFrame(_orbs, 0, 0, g.WindowWidth, g.WindowHeight);
        var points = AmbientLayout.OrbCenterline(g.FrameWidth, g.FrameHeight, g.Chamfer, g.BorderThickness)
            .Select(p => (p.X + g.FrameX, p.Y + g.FrameY)).ToArray();
        double perimeter = AmbientLayout.Perimeter(points);
        nint path = CoreGraphics.ClosedPath(points);
        try
        {
            AddOrb(path, perimeter, spec, spec.Primary, 0, g.Scale);
            if (spec.Count >= 2) AddOrb(path, perimeter, spec, spec.Secondary, 0.5, g.Scale);
        }
        finally { CoreGraphics.CGPathRelease(path); }
        SetHidden(_orbs, false);
    }

    /// <summary>玉 1 個 = 尾 10（先端から）→ 暈 → 芯。どれも同じ経路を timeOffset をずらして追う。</summary>
    private void AddOrb(nint path, double perimeter, AmbientOrbSpec spec, RgbColor color, double phaseOffset, double scale)
    {
        nint halo0 = CoreGraphics.CGColorCreateSRGB(color.R / 255.0, color.G / 255.0, color.B / 255.0, AmbientLook.OrbHaloAlpha);
        nint halo1 = CoreGraphics.CGColorCreateSRGB(color.R / 255.0, color.G / 255.0, color.B / 255.0, 0);
        nint haloColors = NSArray([halo0, halo1]);
        CoreGraphics.CGColorRelease(halo0);
        CoreGraphics.CGColorRelease(halo1);

        void Follow(nint layer, double lagPx)
        {
            SetDouble(layer, "setContentsScale:", scale);
            nint anim = KeyframeAnimation("position");
            SendVoid(anim, Sel("setPath:"), path);
            Send(anim, Sel("setCalculationMode:"), NSString("paced"));
            double offset = AmbientLayout.OrbTimeOffsetMs(spec.PeriodMs, phaseOffset, lagPx, perimeter) / 1000.0;
            AddAnimation(layer, Repeating(anim, spec.PeriodMs / 1000.0, _startTime, offset), "orb");
            AddSublayer(_orbs, layer);
        }

        nint Halo(double radius, double opacity)
        {
            nint l = Layer("CAGradientLayer");
            Send(l, Sel("setType:"), NSString("radial"));
            Send(l, Sel("setColors:"), haloColors);
            SendVoidPoint(l, Sel("setStartPoint:"), new CGPoint { X = 0.5, Y = 0.5 });
            SendVoidPoint(l, Sel("setEndPoint:"), new CGPoint { X = 1, Y = 1 });
            SetBounds(l, radius * 2, radius * 2);
            CoreAnimation.SetOpacity(l, opacity);
            return l;
        }

        foreach (var (radius, opacity, lag) in AmbientLook.OrbTailSteps()) Follow(Halo(radius, opacity), lag);
        Follow(Halo(AmbientLook.OrbHaloRadius, 1), 0);

        var core = AmbientLook.OrbCoreColor(color);
        nint coreColor = CoreGraphics.CGColorCreateSRGB(core.R / 255.0, core.G / 255.0, core.B / 255.0, 1);
        nint dot = Layer("CALayer");
        SetBounds(dot, AmbientLook.OrbCoreRadius * 2, AmbientLook.OrbCoreRadius * 2);
        SetDouble(dot, "setCornerRadius:", AmbientLook.OrbCoreRadius);
        SendVoid(dot, Sel("setBackgroundColor:"), coreColor);
        CoreGraphics.CGColorRelease(coreColor);
        Follow(dot, 0);
    }

    private void Guard()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SendRetBool(Class("NSThread"), Sel("isMainThread")))
            throw new InvalidOperationException("IAmbientLayerHost must be called on the UI thread");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        using var pool = AutoreleasePool.Push();
        using (Transaction.Begin()) Send(_root, Sel("removeFromSuperlayer"));
        foreach (nint l in new[] { _orbs, _beamMask, _beamBand, _beamClip, _pulse, _root }) Release(l);
        Trace.WriteLine("[ambient] detached");
    }
}
