using System.Globalization;
using CPSystemMonitor.Core.Monitoring;
namespace CPSystemMonitor.App;
internal static class MonitorText
{
    public static string T(string en, string ja) => Loc.StartupLanguage == "ja" ? ja : en;
    public static string StorageScope(bool mac) => mac
        ? T("Capacity: selected volume. I/O: backing physical disks. Top app: all disks.", "容量は選択ボリューム、入出力は対応する物理ディスクが対象です。最大負荷アプリは全ディスクが対象です。")
        : T("Capacity and I/O: selected volume. Top app: all disks.", "容量と入出力は選択ボリュームが対象です。最大負荷アプリは全ディスクが対象です。");
    public static string MemoryScope(bool mac) => mac
        ? T("Available memory includes free and inactive pages. App memory uses physical footprint.", "利用可能メモリはfree・inactiveページの合計です。アプリ別メモリはphysical footprintを集計します。")
        : T("Available memory uses available physical memory. App memory uses working sets.", "利用可能メモリはOSの利用可能な物理メモリです。アプリ別メモリはワーキングセットを集計します。");
    public static string DetailedCollectionNote(bool mac) => mac
        ? T("Collects storage and network top apps using process disk statistics and nettop. Unavailable rows are hidden.", "プロセス別ディスク統計とnettopを使用し、ストレージ・ネットワークの最大負荷アプリを取得します。取得できない行は表示しません。")
        : T("Collects storage and network top apps using ETW. Administrator permission is required. Unavailable rows are hidden.", "ETWを使用し、ストレージ・ネットワークの最大負荷アプリを取得します。管理者権限が必要です。取得できない行は表示しません。");
    public static string ProcessPermissionNote(bool mac) => mac
        ? T("Request administrator authentication to read system process CPU, memory and disk statistics using a read-only helper. Permission lasts until this app exits. Some protected processes may remain unavailable. No prompts at startup.", "管理者認証により、計測専用の補助プロセスでシステムプロセスのCPU・メモリ・ディスク統計を取得します。許可はアプリ終了まで有効です。保護されたプロセスは取得できない場合があります。起動時には要求しません。")
        : T("Restart as administrator after confirmation to read more system process CPU and memory statistics. Some protected processes may remain unavailable. No prompts at startup.", "確認後に管理者として再起動し、システムプロセスのCPU・メモリ統計の取得範囲を広げます。保護されたプロセスは取得できない場合があります。起動時には要求しません。");
    public static string ProcessPermissionState(string state) => state switch
    {
        "starting" => T("Waiting for administrator authentication…", "管理者認証を待っています…"),
        "active" => T("Administrator collection enabled", "管理者権限で計測中"),
        "cancelled" => T("Authentication cancelled; using readable processes", "認証をキャンセルしました。取得可能な範囲で計測します"),
        "failed" => T("Administrator collection unavailable; request permission again", "管理者権限での計測を利用できません。権限を再度要求してください"),
        _ => T("Limited to readable processes", "取得可能なプロセスのみ計測中"),
    };
    public static string Section(MonitorSection s) => s switch { MonitorSection.Cpu => "CPU", MonitorSection.Memory => "MEMORY", MonitorSection.Storage => "STORAGE", _ => "NETWORK" };
    public static string AttributionStatus(string state) => state switch
    {
        "disabled" => T("Off", "オフ"),
        "starting" => T("Starting collection", "計測を開始中"),
        "active" => T("Collecting", "計測中"),
        "permission-required" => T("Administrator permission required", "管理者権限が必要です"),
        _ => T("Some counters are unavailable; their app rows are hidden", "取得できない項目のアプリ行は非表示です"),
    };
    public static string Percent(double? value) => value.HasValue ? value.Value.ToString("0", CultureInfo.InvariantCulture) + "%" : "—";
    public static string Bytes(double value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int i = 0; while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return value.ToString(value >= 100 || i == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + units[i];
    }
    public static string Rate(double? value) => value.HasValue ? Bytes(value.Value) + "/s" : "—";
    public static string Capacity(long available, long total) => total > 0 ? Bytes(available) + " / " + Bytes(total) : "—";
}
