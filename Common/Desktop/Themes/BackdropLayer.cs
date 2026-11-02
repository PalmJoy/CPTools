#if CPTOOLS_CLIPBOARD
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CPClipboard.Core.Model;
using CPClipboard.Core.Theming;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Core.Theming;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FileLauncher.Core.Model;
using FileLauncher.Core.Theming;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 盤面の背景画像（SPEC §3.7「作り置き」）。面の大きさ × 拡大率に一度だけ縮小した画像を作り置きし、毎フレームは等倍で貼るだけにする。
/// 常時の演出で毎フレーム描き直すとき、元画像の拡大縮小が 1 フレームの大半を占めていた（2026-10-07 計測。背景ありで約 15% 上乗せ）。
/// 作り直すのは面の大きさ・拡大率・表示方法・画像が変わったときだけ。画像の不透明度は描くときに掛ける（要素の Opacity は 1 のまま）。
/// </summary>
internal sealed class BackdropLayer : Control
{
    private RenderTargetBitmap? _baked;
    private (Size Size, double Scale, BackgroundFit Fit, Bitmap? Source) _bakedKey;
    private Bitmap? _source;
    private BackgroundFit _fit;
    private double _imageOpacity = 1;
    private TopLevel? _topLevel;

    public BackdropLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None); // 作り置きは等倍
        SizeChanged += (_, _) => Refresh();
    }

    public Bitmap? Source
    {
        get => _source;
        set { if (!ReferenceEquals(_source, value)) { _source = value; Refresh(); } }
    }

    public BackgroundFit Fit
    {
        get => _fit;
        set { if (_fit != value) { _fit = value; Refresh(); } }
    }

    /// <summary>画像の不透明度（0.1〜1）。作り直さず、描くときに掛ける。</summary>
    public double ImageOpacity
    {
        get => _imageOpacity;
        set { if (_imageOpacity != value) { _imageOpacity = value; InvalidateVisual(); } }
    }

    // テスト用
    internal Bitmap? BakedImage => _baked;
    internal int BakeCount { get; private set; }
    internal bool UsesFallback { get; private set; }

    /// <summary>作り置きが古ければ作り直す（大きさ 1 px 未満・画像なしなら捨てる）。</summary>
    public void Refresh()
    {
        var size = Bounds.Size;
        if (_source is null || size.Width < 1 || size.Height < 1)
        {
            Discard();
            InvalidateVisual();
            return;
        }
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (_baked is not null && size == _bakedKey.Size && scale == _bakedKey.Scale && _fit == _bakedKey.Fit && ReferenceEquals(_source, _bakedKey.Source))
            return;
        var sw = Stopwatch.StartNew();
        try
        {
            var px = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
            var bitmap = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
            using (var ctx = bitmap.CreateDrawingContext())
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                DrawSource(ctx, size);
            Discard();
            _baked = bitmap;
            _bakedKey = (size, scale, _fit, _source);
            BakeCount++;
            UsesFallback = false;
            AppLog.Info($"背景の作り置き: {px.Width}×{px.Height} px, {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            Discard();
            UsesFallback = true;
            AppLog.Info($"背景の作り置きに失敗（毎フレーム直接描く）: {ex.Message}");
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_source is null) return;
        if (_imageOpacity >= 1) { Draw(context); return; }
        using (context.PushOpacity(_imageOpacity)) Draw(context);
    }

    private void Draw(DrawingContext context)
    {
        if (_baked is not null)
        {
            context.DrawImage(_baked, new Rect(_baked.Size), new Rect(Bounds.Size));
            return;
        }
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            DrawSource(context, Bounds.Size);
    }

    /// <summary>元画像を表示方法どおりに描く（作り置きと失敗時の直描きで共有）。</summary>
    private void DrawSource(DrawingContext ctx, Size face)
    {
        var p = BackgroundLayout.Compute(face.Width, face.Height, _source!.Size.Width, _source.Size.Height, _fit);
        if (p.IsEmpty) return;
        var src = new Rect(p.Source.X, p.Source.Y, p.Source.Width, p.Source.Height);
        for (int r = 0; r < p.TileRows; r++)
            for (int c = 0; c < p.TileColumns; c++)
                ctx.DrawImage(_source, src, new Rect(p.Dest.X + c * p.Dest.Width, p.Dest.Y + r * p.Dest.Height, p.Dest.Width, p.Dest.Height));
    }

    private void Discard()
    {
        _baked?.Dispose();
        _baked = null;
        _bakedKey = default;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null) _topLevel.ScalingChanged += OnScalingChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_topLevel is not null) _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        Discard();
    }

    private void OnScalingChanged(object? sender, EventArgs e) => Refresh();
}
