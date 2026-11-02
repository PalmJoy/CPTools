using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace CPSystemMonitor.Platform.Windows;

/// <summary>フォルダ・ファイル・URL を ShellExecute で開く（SPEC §10.3）。</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsShellService : IShellService
{
    public LaunchResult Open(string pathOrUrl)
    {
        bool url = Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
        if (!url && !File.Exists(pathOrUrl) && !Directory.Exists(pathOrUrl)) return LaunchResult.Fail(LaunchFailure.NotFound, pathOrUrl);
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            return LaunchResult.Ok;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return LaunchResult.Fail(LaunchFailure.OsError, $"{ex.Message}: {pathOrUrl}");
        }
    }
}
