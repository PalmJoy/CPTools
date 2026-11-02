using System.Diagnostics;

namespace CPSystemMonitor.App;

/// <summary>
/// 簡易ログ。Trace には常に出し、ファイル（logs/cpsystemmonitor-yyyyMMdd.log）は設定「起動ログ出力」が ON のとき
/// または Debug ビルドのとき（SPEC §7 詳細）。（SPEC §10.2: 外部のログライブラリは使わない）
/// </summary>
internal static class AppLog
{
    private static readonly object Lock = new();
    private static string? _file;
    private static string? _directory;

    public static void Init(string logsDirectory, bool enabled)
    {
        _directory = logsDirectory;
        SetEnabled(enabled);
#if DEBUG
        // Platform 層の Debug 用 Trace（"[hook] ..." など）もファイルへ
        Trace.Listeners.Add(new ForwardingListener());
#endif
    }

    private sealed class ForwardingListener : TraceListener
    {
        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            // AppLog 自身の行は二重に書かない
            if (message is null || !(message.StartsWith("[hook]", StringComparison.Ordinal) || message.StartsWith("[ambient]", StringComparison.Ordinal)
                || message.StartsWith("[finder]", StringComparison.Ordinal))) return;
            Info(message);
        }
    }

    /// <summary>設定「ログを出力する」の切り替え（即時反映）。Debug ビルドは常に出す。</summary>
    public static void SetEnabled(bool enabled)
    {
#if DEBUG
        enabled = true;
#endif
        if (!enabled || _directory is null)
        {
            _file = null;
            return;
        }
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, $"cpsystemmonitor-{DateTime.Now:yyyyMMdd}.log");
    }

    public static string? LogsDirectory => _directory;

    public static string? FilePath => _file;

    public static void Info(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Trace.WriteLine(line);
        if (_file is null) return;
        lock (Lock)
        {
            try { File.AppendAllText(_file, line + Environment.NewLine); }
            catch (IOException) { /* ログ失敗で本体を止めない */ }
        }
    }
}
