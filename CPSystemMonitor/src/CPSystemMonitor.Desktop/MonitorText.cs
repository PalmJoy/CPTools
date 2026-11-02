using System.Globalization;
using CPSystemMonitor.Core.Monitoring;
namespace CPSystemMonitor.App;
internal static class MonitorText
{
    public static string T(string en, string ja) => Loc.StartupLanguage == "ja" ? ja : en;
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
