using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CPSystemMonitor.Core.Monitoring;
namespace CPSystemMonitor.App;
internal sealed class MetricCard : Border
{
    private readonly MonitorSection _section;
    private readonly bool _compact;
    private readonly TextBlock _capacity, _label, _value, _rates, _top, _graphLabel, _networkSend;
    private readonly TextBlock _compressed, _swap;
    private readonly Grid _memoryDetails;
    private readonly TelemetryGraph _graph;
    private readonly MeterBar _capacityBar;
    private readonly UniformGrid _cores;
    private readonly List<(TextBlock Text, MeterBar Bar)> _meters = [];
    private readonly StackPanel _content;
    private readonly Border _topRow;
    private readonly SectionSettings _settings;
    private bool _topAppSeen;
    public MetricCard(MonitorSection section, bool compact, SectionSettings settings, bool perCore, bool topAppSeen = false)
    {
        _section = section; _compact = compact; _settings = settings; _topAppSeen = topAppSeen;
        BorderThickness = new Thickness(1); Padding = new Thickness(compact ? 10 : 13, 8);
        this[!Border.BorderBrushProperty] = Themed.Res("FlHeaderSeparator");
        Background = Brushes.Transparent;
        var title = Text(MonitorText.Section(section), compact ? 16 : 20, "FlAccent");
        title.FontWeight = FontWeight.SemiBold;
        _capacity = Text("", compact ? 10 : 12);
        var names = new StackPanel { Spacing = 3, Children = { title, _capacity } };
        _label = Text(section == MonitorSection.Memory ? "AVAILABLE" : section == MonitorSection.Storage ? "FREE" : "", compact ? 9 : 10, "FlAccent");
        _value = Text("—", compact ? 24 : 34, "FlAccent");
        _networkSend = Text("", compact ? 14 : 20, "FlAccent2");
        _networkSend.IsVisible = section == MonitorSection.Network;
        var headline = new StackPanel { Children = { _label, _value, _networkSend }, VerticalAlignment = VerticalAlignment.Center };
        var summary = new Grid { ColumnDefinitions = new ColumnDefinitions(compact ? "115,85,*" : "155,125,*"), ColumnSpacing = compact ? 6 : 12 };
        Grid.SetColumn(headline, 1); summary.Children.Add(names); summary.Children.Add(headline);
        _graphLabel = Text(section switch { MonitorSection.Cpu => "CPU USAGE", MonitorSection.Memory => "MEMORY IN USE", MonitorSection.Storage => "DISK I/O", _ => "NETWORK I/O" }, 9, "FlTextMuted");
        _graph = new TelemetryGraph { Section = section, Height = compact ? 27 : 42 };
        var history = new StackPanel { Spacing = 3, Children = { _graphLabel, _graph }, IsVisible = settings.History };
        _graphLabel.IsVisible = !compact;
        Grid.SetColumn(history, 2); summary.Children.Add(history);
        if (!settings.History) { Grid.SetColumnSpan(headline, 2); }
        _content = new StackPanel { Spacing = compact ? 5 : 7, Children = { summary } };
        _capacityBar = new MeterBar { Height = compact ? 4 : 6, IsVisible = section is MonitorSection.Memory or MonitorSection.Storage };
        _content.Children.Add(_capacityBar);
        _compressed = Text("", compact ? 10 : 12);
        _swap = Text("", compact ? 10 : 12);
        _memoryDetails = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = compact ? 8 : 20, IsVisible = false };
        Grid.SetColumn(_swap, 1);
        _memoryDetails.Children.Add(_compressed); _memoryDetails.Children.Add(_swap);
        ToolTip.SetTip(_compressed, MonitorText.T("Physical RAM occupied by compressed memory.", "圧縮メモリが実際に占めているRAM容量です。"));
        ToolTip.SetTip(_swap, MonitorText.T("Current disk space used for swap. Unavailable values are shown as —.", "Swapに現在使用しているディスク容量です。取得できない場合は — を表示します。"));
        _content.Children.Add(_memoryDetails);
        _rates = Text("", compact ? 11 : 13);
        _rates.IsVisible = section == MonitorSection.Storage;
        _content.Children.Add(_rates);
        _cores = new UniformGrid { Columns = compact ? 12 : 4, IsVisible = section == MonitorSection.Cpu && perCore };
        _content.Children.Add(_cores);
        _top = Text("", compact ? 11 : 12, "FlTextMuted");
        if (section is MonitorSection.Storage or MonitorSection.Network) _top.LineHeight = compact ? 16 : 18;
        _top.TextTrimming = TextTrimming.CharacterEllipsis;
        _topRow = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = _top, IsVisible = false };
        _topRow[!Border.BorderBrushProperty] = Themed.Res("FlHeaderSeparator");
        _content.Children.Add(_topRow);
        Child = _content;
        if (section == MonitorSection.Network)
        {
            _value.FontSize = compact ? 14 : 20;
            summary.ColumnDefinitions = new ColumnDefinitions(compact ? "95,135,*" : "155,205,*");
        }
        ToolTip.SetTip(this, section switch
        {
            MonitorSection.Memory => MonitorText.T("Available memory includes reclaimable pages; accounting differs by OS.", "利用可能メモリには再利用可能なページを含み、集計方法はOSにより異なります。"),
            MonitorSection.Storage => MonitorText.T("Capacity: selected volume. I/O: selected volume on Windows, backing physical disks on macOS. Top app: all disks.", "容量は選択ボリューム。入出力はWindowsでは選択ボリューム、macOSでは対応する物理ディスク。最大負荷アプリは全ディスクが対象です。"),
            _ => null,
        });
    }
    internal static TextBlock Text(string value, double size = 12, string key = "FlText")
    {
        var t = new TextBlock { Text = value, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t[!TextBlock.ForegroundProperty] = Themed.Res(key);
        t[!TextBlock.FontFamilyProperty] = Themed.Res("FlMonoFontFamily");
        return t;
    }
    public void Update(MonitorSample sample, IReadOnlyList<MonitorSample> history, int historySeconds)
    {
        _graph.Samples = history; _graph.Seconds = historySeconds; _graph.InvalidateVisual();
        AppLoad? top;
        string topText;
        switch (_section)
        {
            case MonitorSection.Cpu:
                _value.Text = MonitorText.Percent(sample.CpuPercent);
                UpdateCores(sample.Cores);
                top = sample.TopCpu; topText = top is null ? "" : top.CpuPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";
                break;
            case MonitorSection.Memory:
                _capacity.Text = MonitorText.Capacity(sample.AvailableMemory, sample.TotalMemory);
                _value.Text = MonitorText.Percent(sample.AvailablePercent);
                _capacityBar.Value = sample.AvailablePercent ?? 0;
                _memoryDetails.IsVisible = OperatingSystem.IsMacOS() || sample.CompressedMemory.HasValue || sample.SwapUsed.HasValue;
                _compressed.Text = "COMPRESSED  " + (sample.CompressedMemory is { } compressed ? MonitorText.Bytes(compressed) : "—");
                _swap.Text = "SWAP USED  " + (sample.SwapUsed is { } swap ? MonitorText.Bytes(swap) : "—");
                top = sample.TopMemory; topText = top is null ? "" : MonitorText.Bytes(top.MemoryBytes);
                break;
            case MonitorSection.Storage:
                _capacity.Text = MonitorText.Capacity(sample.DiskFree, sample.DiskTotal);
                _value.Text = MonitorText.Percent(sample.DiskFreePercent);
                _capacityBar.Value = sample.DiskFreePercent ?? 0;
                _rates.Text = $"READ {MonitorText.Rate(sample.ReadBytesPerSecond)}   WRITE {MonitorText.Rate(sample.WriteBytesPerSecond)}";
                top = sample.TopStorage; topText = top is null ? "" : $"↓ {MonitorText.Rate(top.ReadBytesPerSecond)}  ↑ {MonitorText.Rate(top.WriteBytesPerSecond)}";
                break;
            default:
                _value.Text = $"↓ {MonitorText.Rate(sample.ReceiveBytesPerSecond)}";
                _networkSend.Text = $"↑ {MonitorText.Rate(sample.SendBytesPerSecond)}";
                top = sample.TopNetwork; topText = top is null ? "" : $"↓ {MonitorText.Rate(top.ReceiveBytesPerSecond)}  ↑ {MonitorText.Rate(top.SendBytesPerSecond)}";
                break;
        }
        if (_settings.TopApp && top is not null && _section is MonitorSection.Storage or MonitorSection.Network)
            _topAppSeen = true;
        _topRow.IsVisible = _settings.TopApp && (top is not null || _topAppSeen);
        // A space retains the text line height without showing stale app data.
        _top.Text = top is null ? " " : $"{(_section == MonitorSection.Storage ? "TOP / ALL DISKS" : "TOP")}  {top.Name}   {topText}";
        if (_section == MonitorSection.Storage) ToolTip.SetTip(_top, MonitorText.T("Top app across all disks", "全ディスクに対する最大負荷アプリ"));
        ToolTip.SetTip(_capacity, _section == MonitorSection.Storage ? sample.Drive : null);
    }
    private void UpdateCores(double[] values)
    {
        if (!_cores.IsVisible) return;
        if (_meters.Count != values.Length)
        {
            _meters.Clear(); _cores.Children.Clear();
            for (int i = 0; i < values.Length; i++)
            {
                var text = Text("", _compact ? 9 : 10);
                var bar = new MeterBar { Height = _compact ? 24 : 6, Vertical = _compact };
                _cores.Children.Add(new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, _compact ? 5 : 10, _compact ? 2 : 6), Children = { text, bar } });
                _meters.Add((text, bar));
            }
        }
        for (int i = 0; i < values.Length; i++)
        {
            _meters[i].Text.Text = _compact ? $"{i:00}" : $"L{i:00}  {values[i]:0}%";
            ToolTip.SetTip(_meters[i].Bar, $"L{i:00}: {values[i]:0}%");
            _meters[i].Bar.Value = values[i];
            _meters[i].Bar[!MeterBar.AccentProperty] = Themed.Res(values[i] >= 70 ? "FlAccent2" : "FlAccent");
        }
    }
}
