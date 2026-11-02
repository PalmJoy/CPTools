#if CPTOOLS_CLIPBOARD
using Avalonia.Media.Imaging;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia.Media.Imaging;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia.Media.Imaging;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
internal enum BackgroundLoadStatus { None, Ok, NotFound, Failed }

/// <summary>
/// 盤面の背景画像の読み込み（SPEC §3.7）。長辺 2048 px を超える画像は縮小して読む（メモリと表示遅延のため）。
/// デコードはバックグラウンド、結果は 1 枚だけキャッシュ（同じファイル・同じ更新時刻なら読み直さない）。
/// </summary>
internal sealed class BoardBackgroundLoader : IDisposable
{
    public const int MaxEdgePx = 2048;

    private Bitmap? _bitmap;
    private string? _path;
    private DateTime _writeTime;

    public BackgroundLoadStatus LastStatus { get; private set; } = BackgroundLoadStatus.None;
    public Bitmap? Current => _bitmap;

    /// <summary>resolvedPath が null なら「なし」。呼んだスレッド（UI）で結果を返す。読み込み中に次が来たら ct で前を捨てる。</summary>
    public async Task<Bitmap?> LoadAsync(string? resolvedPath, CancellationToken ct = default)
    {
        if (resolvedPath is null)
        {
            Replace(null, null, default);
            LastStatus = BackgroundLoadStatus.None;
            return null;
        }
        DateTime writeTime;
        try { writeTime = File.GetLastWriteTimeUtc(resolvedPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { writeTime = default; }

        if (_bitmap is not null && _path == resolvedPath && _writeTime == writeTime && LastStatus == BackgroundLoadStatus.Ok)
            return _bitmap;

        Bitmap? loaded;
        BackgroundLoadStatus status;
        try
        {
            loaded = await Task.Run(() => Decode(resolvedPath), ct);
            status = BackgroundLoadStatus.Ok;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            loaded = null;
            status = BackgroundLoadStatus.NotFound;
        }
        catch (Exception ex)
        {
            AppLog.Info($"背景画像を読み込めません: {resolvedPath}: {ex.Message}");
            loaded = null;
            status = BackgroundLoadStatus.Failed;
        }
        if (ct.IsCancellationRequested)
        {
            loaded?.Dispose();
            throw new OperationCanceledException(ct);
        }
        Replace(loaded, resolvedPath, writeTime);
        LastStatus = status;
        return loaded;
    }

    /// <summary>寸法を検査し、原寸の画素を確保する前に縮小デコードする。</summary>
    internal static Bitmap Decode(string path) => SafeBitmapLoader.Load(path, MaxEdgePx);

    private void Replace(Bitmap? bitmap, string? path, DateTime writeTime)
    {
        if (!ReferenceEquals(_bitmap, bitmap)) _bitmap?.Dispose();
        _bitmap = bitmap;
        _path = path;
        _writeTime = writeTime;
    }

    public void Dispose() => Replace(null, null, default);
}
