#if CPTOOLS_CLIPBOARD
using Avalonia;
using Avalonia.Media;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia;
using Avalonia.Media;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia;
using Avalonia.Media;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>ページ接続・登録の短い走査。SignalPulse と同じ有限時間の時計を使う。</summary>
internal sealed class DataSweep : SignalPulse
{
    public bool Vertical { get; init; }
    public bool Reverse { get; set; }
    public override void Render(DrawingContext context)
    {
        if (Progress < 0 || Progress > 1 || Stroke is null) return;
        double extent = Vertical ? Bounds.Height : Bounds.Width;
        double cross = Vertical ? Bounds.Width : Bounds.Height;
        double head = (Reverse ? 1 - Progress : Progress) * (extent + 24) - 12;
        double sign = Reverse ? 1 : -1;
        if (Vertical && Progress > 0.65)
        {
            using (context.PushOpacity((1 - Progress) * 2))
                context.DrawRectangle(new Pen(Stroke, 1), new Rect(1, 1, Math.Max(0, Bounds.Width - 2), Math.Max(0, Bounds.Height - 2)));
        }
        for (int i = 0; i < 12; i++)
        {
            double at = head + sign * i * 2;
            using (context.PushOpacity(i == 0 ? 0.85 : (1 - i / 12.0) * 0.16))
                context.DrawRectangle(Stroke, null, Vertical ? new Rect(0, at, cross, 2) : new Rect(at, 0, 2, cross));
        }
    }
}
