#if CPTOOLS_CLIPBOARD
using CPClipboard.Core.Theming;

namespace CPClipboard.Platform;
#elif CPTOOLS_MONITOR
using CPSystemMonitor.Core.Theming;

namespace CPSystemMonitor.Platform;
#elif CPTOOLS_LAUNCHER
using FileLauncher.Core.Theming;

namespace FileLauncher.Platform;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 常時の演出（光の玉・発光の明滅・走査線の帯）を OS のアニメーション層で描く（SPEC §10.3 / §10.7、spec/EFFECTS.md「描き方（OS 別）」）。
/// macOS = Core Animation。他 OS は IsSupported = false で、App は Avalonia の層で描く。すべて UI スレッドから呼ぶ。
/// </summary>
public interface IAmbientLayerService
{
    bool IsSupported { get; }

    /// <summary>盤面ウィンドウに取り付ける。非対応・失敗なら null（App は Avalonia の層に戻る）。</summary>
    IAmbientLayerHost? Attach(nint windowHandle);
}

/// <summary>
/// 取り付けた窓 1 つぶんの層。Dispose で層を外す。どの順で呼ばれても最後の値が効く。
/// 止まっている間（SetRunning(false)）に変えた値は次の SetRunning(true) でまとめて反映する。
/// </summary>
public interface IAmbientLayerHost : IDisposable
{
    /// <summary>枠の位置・大きさ・面取り・余白・拡大率。変わるたびに呼ぶ（経路・マスク・画像の枠を作り直す。位相は保つ）。</summary>
    void SetGeometry(AmbientGeometry geometry);

    /// <summary>光の玉。null = なし。</summary>
    void SetOrbs(AmbientOrbSpec? orbs);

    /// <summary>発光の明滅。null = なし。画像は枠 + 余白の大きさ（Geometry.GlowMargin）。</summary>
    void SetPulse(AmbientPulseSpec? pulse);

    /// <summary>走査線の帯。null = なし。</summary>
    void SetBeam(AmbientBeamSpec? beam);

    /// <summary>盤面の不透明度（appearance.opacity、0〜1）。Window.Opacity は層に掛からないので別に渡す。</summary>
    void SetOpacity(double opacity);

    /// <summary>true: 表示してフェードインし、止めた位置から再開。false: 即座に非表示にして時間を止める。</summary>
    void SetRunning(bool running);
}

/// <summary>窓の論理座標（左上原点、pt）。Frame = 枠の外形（FrameChrome.Frame の Bounds を窓座標へ移したもの）。</summary>
public sealed record AmbientGeometry(
    double WindowWidth, double WindowHeight,
    double FrameX, double FrameY, double FrameWidth, double FrameHeight,
    double Chamfer, double BorderThickness, double GlowMargin, double Scale);

/// <summary>Count = 1（主色）/ 2（主色 + 副色が対角）。PeriodMs = 1 周。</summary>
public sealed record AmbientOrbSpec(int Count, int PeriodMs, RgbColor Primary, RgbColor Secondary);

/// <summary>Peak = 最大の明るさ（弱 0.45 / 強 0.9）。Image = 焼いた発光（PNG、枠 + 余白、デバイス px）。</summary>
public sealed record AmbientPulseSpec(int PeriodMs, double Peak, EncodedImage Image);

/// <summary>帯の高さ・グラデーションは Core AmbientLook の定数（App と同じ値）。</summary>
public sealed record AmbientBeamSpec(int PeriodMs, RgbColor Color);

/// <summary>PNG にエンコードした画像。論理サイズ = PixelWidth / Scale（Geometry.Scale）。</summary>
public sealed record EncodedImage(byte[] Png, int PixelWidth, int PixelHeight);

/// <summary>非対応（Windows・テスト）の既定。</summary>
public static class AmbientLayers
{
    public static IAmbientLayerService Unsupported { get; } = new UnsupportedService();

    private sealed class UnsupportedService : IAmbientLayerService
    {
        public bool IsSupported => false;
        public IAmbientLayerHost? Attach(nint windowHandle) => null;
    }
}
