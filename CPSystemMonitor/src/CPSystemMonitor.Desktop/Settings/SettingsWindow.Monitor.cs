using System.Diagnostics;
using Avalonia.Controls;
using CPSystemMonitor.Core.Monitoring;
using CPSystemMonitor.Platform;
namespace CPSystemMonitor.App;
internal sealed partial class SettingsWindow
{
    private Control MonitorTab()
    {
        var p = Panel();
        p.Children.Add(Row(MonitorText.T("View mode", "表示モード"), Combo([(MonitorText.T("Standard", "標準"), MonitorMode.Standard), (MonitorText.T("Compact", "コンパクト"), MonitorMode.Compact)], s => s.Monitor.Mode, (s, v) => s.Monitor.Mode = v, SettingsChange.BoardLayout)));
        p.Children.Add(Row(MonitorText.T("Refresh interval", "更新間隔"), Combo([("0.5 s", .5), ("1 s", 1d), ("2 s", 2d), ("5 s", 5d), ("10 s", 10d)], s => s.Monitor.RefreshSeconds, (s, v) => s.Monitor.RefreshSeconds = v, SettingsChange.BoardLayout)));
        p.Children.Add(Row(MonitorText.T("History duration", "履歴の表示期間"), Combo([("30 s", 30), ("60 s", 60), ("120 s", 120), ("300 s", 300), ("600 s", 600)], s => s.Monitor.HistorySeconds, (s, v) => s.Monitor.HistorySeconds = v, SettingsChange.BoardLayout)));
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable).Where(d => !OperatingSystem.IsMacOS() || d.Name == "/" || d.Name.StartsWith("/Volumes/", StringComparison.Ordinal)).Select(d => (d.Name, d.Name)).ToList();
        if (!drives.Any(d => d.Item2 == MonitorSampler.SystemDrive)) drives.Insert(0, (MonitorSampler.SystemDrive, MonitorSampler.SystemDrive));
        p.Children.Add(Row(MonitorText.T("Storage volume", "ストレージの対象"), Combo(drives.ToArray(), s => string.IsNullOrEmpty(s.Monitor.Drive) ? MonitorSampler.SystemDrive : s.Monitor.Drive, (s, v) => s.Monitor.Drive = v, SettingsChange.BoardLayout), MonitorText.T("Capacity: selected volume. macOS I/O: backing physical disk(s). Top app: all disks.", "容量は選択ボリューム、macOSの入出力は対応する物理ディスクが対象です。最大負荷アプリは全ディスクが対象です。")));
        p.Children.Add(Row(MonitorText.T("Always on top", "最前面に表示"), Toggle(s => s.Monitor.AlwaysOnTop, (s, v) => s.Monitor.AlwaysOnTop = v, SettingsChange.BoardLayout)));
        p.Children.Add(Row(MonitorText.T("Per-core meters", "コア別使用率"), Toggle(s => s.Monitor.PerCore, (s, v) => s.Monitor.PerCore = v, SettingsChange.BoardLayout)));
        foreach (var section in Enum.GetValues<MonitorSection>())
        {
            p.Children.Add(Row(MonitorText.Section(section), Toggle(s => s.Monitor.For(section).Visible, (s, v) => s.Monitor.For(section).Visible = v, SettingsChange.BoardLayout)));
            p.Children.Add(Row(Sub(MonitorText.T("Top app", "最大負荷アプリ")), Toggle(s => s.Monitor.For(section).TopApp, (s, v) => s.Monitor.For(section).TopApp = v, SettingsChange.BoardLayout)));
            p.Children.Add(Row(Sub(MonitorText.T("History graph", "履歴グラフ")), Toggle(s => s.Monitor.For(section).History, (s, v) => s.Monitor.For(section).History = v, SettingsChange.BoardLayout)));
        }
        p.Children.Add(Themed.Note(new TextBlock { Text = MonitorText.T("Top apps are ranked among readable processes. CPU is normalized to the whole machine; memory accounting differs by OS. Network totals sum active non-loopback, non-tunnel interfaces; VPN/bridge traffic may overlap.", "最大負荷は取得可能なプロセスをアプリ単位で集計します。CPUは全体を100%とし、メモリの集計方法はOSにより異なります。通信量は稼働中のループバック・トンネル以外の接続を合算するため、VPN等では重複する場合があります。"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 }));
        return Scroll(p);
    }
    private void AddMonitorPermissions(StackPanel p)
    {
        p.Children.Add(Row(MonitorText.T("Detailed attribution", "アプリ別の詳細計測"), Toggle(s => s.Monitor.DetailedAttribution, (s, v) => s.Monitor.DetailedAttribution = v, SettingsChange.BoardLayout),
            MonitorText.T("Enables storage/network top apps when available. Unavailable rows are hidden. Windows uses OS ETW and requires administrator rights. macOS uses process disk counters and the standard nettop tool; readable scope depends on OS permissions.", "ストレージ・ネットワークの最大負荷アプリを取得します。取得できない行は表示しません。Windowsは標準ETWのため管理者権限が必要です。macOSはプロセス別ディスク統計と標準nettopを使用し、取得範囲はOSの権限に依存します。")));
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 };
        void UpdateStatus() => status.Text = MonitorText.T("Collection status: ", "取得状態：") + MonitorText.AttributionStatus(_ctx.MonitorStatus?.Invoke() ?? "disabled");
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UpdateStatus(); timer.Start(); Closed += (_, _) => timer.Stop(); UpdateStatus();
        p.Children.Add(Row("", status));
        var request = new Button { Content = OperatingSystem.IsWindows() ? MonitorText.T("Request permissions…", "権限を要求…") : MonitorText.T("Retry collection", "計測を再試行") };
        request.Click += (_, _) => _ctx.RequestMonitorPermissions?.Invoke();
        p.Children.Add(Row("", request, MonitorText.T("Windows: confirm before restarting this app elevated. macOS: enable and retry collection within the readable scope. No permission prompts at startup.", "Windowsは確認後に管理者として再起動します。macOSは読み取り可能な範囲で計測を再試行します。起動時には要求しません。")));
    }
}
