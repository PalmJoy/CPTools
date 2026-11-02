#if CPTOOLS_CLIPBOARD
using System.Runtime;
using Avalonia.Threading;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using System.Runtime;
using Avalonia.Threading;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using System.Runtime;
using Avalonia.Threading;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 盤面を隠した・設定画面を閉じた後、しばらく操作が無ければ 1 回だけコンパクションありのフル GC をして
/// マネージドヒープの空きを OS に返す。隠している間はほぼ割り当てが無く GC が起きないので、放っておくと
/// 表示中のピークのまま残る。ワーキングセットの強制切り詰め（EmptyWorkingSet）は次の表示が遅れるのでしない。
/// </summary>
internal static class MemoryTrim
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);
    private static DispatcherTimer? _timer;
    private static Func<bool>? _canTrim;

    /// <summary>UI スレッドから呼ぶ。続けて呼ばれたら最後の呼び出しから数え直す。canTrim が false なら（再表示された等）何もしない。</summary>
    public static void Schedule(Func<bool> canTrim)
    {
        _canTrim = canTrim;
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Delay };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                if (_canTrim?.Invoke() == true) Run();
            };
        }
        _timer.Stop();
        _timer.Start();
    }

    private static void Run()
    {
        long before = GC.GetTotalMemory(false);
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        AppLog.Info($"メモリ整理: マネージドヒープ {before / 1024 / 1024} MB → {GC.GetTotalMemory(false) / 1024 / 1024} MB");
    }
}
