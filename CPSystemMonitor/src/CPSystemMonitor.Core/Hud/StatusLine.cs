using System.Globalization;
using CPSystemMonitor.Core.Model;

namespace CPSystemMonitor.Core.Hud;

/// <summary>盤面のステータス行（HUD。SPEC §3.9）の文字列と、次に書き換える時刻。</summary>
public static class StatusLine
{
    /// <param name="date">時刻の前に日付（yyyy/MM/dd）を付ける。時刻なし（Off）のときは日付だけ。</param>
    /// <param name="groupName">グループ名。空なら番号だけ（`GRP 2`）、あれば `GRP 2 · 名前`。</param>
    public static (string Group, string Items, string? Clock) Format(int groupKey, string? groupName, int itemCount, DateTime now, ClockMode clock, bool date = false)
    {
        string page = string.IsNullOrEmpty(groupName)
            ? $"GRP {groupKey.ToString(CultureInfo.InvariantCulture)}"
            : $"GRP {groupKey.ToString(CultureInfo.InvariantCulture)} · {groupName}";
        string items = $"ITEMS {itemCount.ToString(CultureInfo.InvariantCulture)}";
        string? time = clock switch
        {
            ClockMode.Minutes => now.ToString("HH:mm", CultureInfo.InvariantCulture),
            ClockMode.Seconds => now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            _ => null,
        };
        if (date)
        {
            string d = now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            time = time is null ? d : $"{d} {time}";
        }
        return (page, items, time);
    }

    /// <summary>次に表示が変わるまでの時間（分なら次の分の変わり目、秒なら次の秒）。時刻なしなら無限。</summary>
    public static TimeSpan NextTick(DateTime now, ClockMode clock, bool date = false) => clock switch
    {
        ClockMode.Minutes => TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond),
        ClockMode.Seconds => TimeSpan.FromMilliseconds(1000 - now.Millisecond),
        // 日付だけなら次の日付の変わり目
        _ when date => now.Date.AddDays(1) - now,
        _ => Timeout.InfiniteTimeSpan,
    };
}
