using System.Diagnostics;
using System.Runtime.Versioning;

namespace CPSystemMonitor.Platform.MacOS;

/// <summary>フォルダ・ファイル・URL を open で開く（SPEC §10.3）。終了コード ≠ 0 を失敗とする。</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacShellService : IShellService
{
    private const string OpenCommand = "/usr/bin/open";

    /// <summary>open が失敗を返すのを待つ上限。成功時は 100ms 前後で終わる。</summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(2);

    public LaunchResult Open(string pathOrUrl)
    {
        bool url = Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
        if (!url && !File.Exists(pathOrUrl) && !Directory.Exists(pathOrUrl)) return LaunchResult.Fail(LaunchFailure.NotFound, pathOrUrl);
        return RunOpen([pathOrUrl]);
    }

    internal static LaunchResult RunOpen(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(OpenCommand) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return LaunchResult.Fail(LaunchFailure.LauncherUnavailable, OpenCommand);
            if (!p.WaitForExit(OpenTimeout)) return LaunchResult.Ok; // まだ動いている = 起動処理中とみなす
            if (p.ExitCode == 0) return LaunchResult.Ok;
            string err = p.StandardError.ReadToEnd().Trim();
            return LaunchResult.Fail(LaunchFailure.OsError, err.Length > 0 ? err : $"open exit code {p.ExitCode}");
        }
        catch (Exception ex)
        {
            return LaunchResult.Fail(LaunchFailure.OsError, ex.Message);
        }
    }
}
