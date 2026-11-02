#if CPTOOLS_CLIPBOARD
using System.Collections.Concurrent;
using System.Runtime.Versioning;

namespace CPClipboard.Platform.Windows;
#elif CPTOOLS_MONITOR
using System.Collections.Concurrent;
using System.Runtime.Versioning;

namespace CPSystemMonitor.Platform.Windows;
#elif CPTOOLS_LAUNCHER
using System.Collections.Concurrent;
using System.Runtime.Versioning;

namespace FileLauncher.Platform.Windows;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// シェルの COM API 用の専用 STA スレッド。UI スレッドを塞がずにアイコン取得・.lnk 読み取りを行う
/// （IconSpike でメッセージポンプ無しでも固まらないことを確認済み）。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();

    public StaWorker(string name)
    {
        var thread = new Thread(() =>
        {
            foreach (var work in _queue.GetConsumingEnumerable()) work();
        })
        { IsBackground = true, Name = name };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public Task<T> Run<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            if (cancellationToken.IsCancellationRequested) { tcs.SetCanceled(cancellationToken); return; }
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    /// <summary>STA スレッドで同期実行（短い処理用。.lnk 読み取りなど）。</summary>
    public T Invoke<T>(Func<T> func) => Run(func).GetAwaiter().GetResult();

    public void Dispose() => _queue.CompleteAdding();
}
