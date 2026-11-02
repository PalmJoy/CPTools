using Avalonia.Controls;
using Avalonia.Input;
using CPSystemMonitor.Core.Model;
using CoreModifiers = CPSystemMonitor.Core.Model.KeyModifiers;

namespace CPSystemMonitor.App;

/// <summary>
/// ホットキーの記録欄（設定画面のトリガータブ）。クリックで記録開始、次に押したキーの組み合わせを記録する。Esc で中止。
/// キー名は SharpHook の KeyCode 名から "Vc" を除いたもの（SharpHookInputService.KeyName と同じ表記）。
/// 入力フックが動いていれば、フックで次のキーを取る（IHotkeyService.BeginCapture）。⌃Space（macOS の入力ソース切替）の
/// ように OS が先に取るキーもフックには届き、記録中のキーは他のアプリへ渡さない。キー名もトリガー判定と同じものになる。
/// フックが動いていないとき（権限が無いなど）は、このボタンへのキー入力（物理キー）で記録する。
/// 記録中は全体のホットキーを外す（同じ組み合わせを押すと盤面が出てしまうのを防ぐ）。
/// </summary>
internal sealed class HotkeyRecorder : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    private static readonly Dictionary<PhysicalKey, string> Names = BuildNames();

    private readonly CPSystemMonitor.Platform.IPlatformServices _platform;
    private HotkeySetting _current = new();
    private bool _recording;
    private bool _viaHook;
    private int _session;

    public event Action? RecordingStarted;
    public event Action<HotkeySetting?>? RecordingEnded; // null = 中止

    public HotkeyRecorder(CPSystemMonitor.Platform.IPlatformServices platform)
    {
        _platform = platform;
        MinWidth = 220;
        Click += (_, _) => Start();
        LostFocus += (_, _) => { if (_recording && !_viaHook) Stop(null); };
        // 記録中に設定画面が閉じられてもフックの記録（全キーを握りつぶす）を残さない
        DetachedFromVisualTree += (_, _) => { if (_recording) Stop(null); };
    }

    public void SetValue(HotkeySetting hotkey)
    {
        _current = hotkey;
        if (!_recording) Content = Format(hotkey);
    }

    internal bool IsRecording => _recording;
    internal void Cancel() { if (_recording) Stop(null); }

    private void Start()
    {
        if (_recording) return;
        _recording = true;
        Content = Strings.Settings_Hotkey_Recording;
        RecordingStarted?.Invoke();
        _viaHook = _platform.InputHook.IsRunning;
        if (_viaHook)
            _platform.Hotkey.BeginCapture((key, mods) => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnCaptured(key, mods)));
        // 何も押されないまま放置されたら中止する（記録中は他のアプリにキーが届かないため）
        int session = ++_session;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { if (_recording && session == _session) Stop(null); }, TimeSpan.FromSeconds(10));
        Focus();
    }

    private void Stop(HotkeySetting? recorded)
    {
        if (_viaHook) _platform.Hotkey.EndCapture();
        _viaHook = false;
        _recording = false;
        if (recorded is not null) _current = recorded; // 次に中止したときの表示がこの値になるように
        Content = Format(recorded ?? _current);
        RecordingEnded?.Invoke(recorded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
        if (_viaHook) return; // フックが取る（ここに届くキーは使わない）
        if (e.Key == Key.Escape)
        {
            Stop(null);
            return;
        }
        if (!Names.TryGetValue(e.PhysicalKey, out var name)) return; // 修飾キーだけ・対応していないキーは待ち続ける
        OnCaptured(name, ToCore(e.KeyModifiers));
    }

    private void OnCaptured(string name, CoreModifiers mods)
    {
        if (!_recording) return;
        if (name == "Escape")
        {
            Stop(null);
            return;
        }
        bool isFunctionKey = name.Length >= 2 && name[0] == 'F' && char.IsDigit(name[1]);
        if (mods == CoreModifiers.None && !isFunctionKey)
        {
            Content = Strings.FormatSettings_Hotkey_NeedsModifier($"{Loc.Modifier(CoreModifiers.Ctrl)} / {Loc.Modifier(CoreModifiers.Alt)}");
            return;
        }
        Stop(new HotkeySetting { Enabled = _current.Enabled, Modifiers = mods, Key = name });
    }

    public static string Format(HotkeySetting h)
    {
        var parts = new List<string>();
        bool mac = OperatingSystem.IsMacOS();
        if (h.Modifiers.HasFlag(CoreModifiers.Ctrl)) parts.Add(mac ? "⌃" : "Ctrl");
        if (h.Modifiers.HasFlag(CoreModifiers.Alt)) parts.Add(mac ? "⌥" : "Alt");
        if (h.Modifiers.HasFlag(CoreModifiers.Shift)) parts.Add(mac ? "⇧" : "Shift");
        if (h.Modifiers.HasFlag(CoreModifiers.Meta)) parts.Add(mac ? "⌘" : "Win");
        parts.Add(h.Key);
        return string.Join(mac ? " " : " + ", parts);
    }

    private static CoreModifiers ToCore(Avalonia.Input.KeyModifiers m)
    {
        var r = CoreModifiers.None;
        if (m.HasFlag(Avalonia.Input.KeyModifiers.Control)) r |= CoreModifiers.Ctrl;
        if (m.HasFlag(Avalonia.Input.KeyModifiers.Alt)) r |= CoreModifiers.Alt;
        if (m.HasFlag(Avalonia.Input.KeyModifiers.Shift)) r |= CoreModifiers.Shift;
        if (m.HasFlag(Avalonia.Input.KeyModifiers.Meta)) r |= CoreModifiers.Meta;
        return r;
    }

    /// <summary>Avalonia の物理キー → SharpHook のキー名。</summary>
    private static Dictionary<PhysicalKey, string> BuildNames()
    {
        var d = new Dictionary<PhysicalKey, string>();
        // 名前から引く分は TryParse（Avalonia の版で名前が違っても例外で落ちないように。無いキーは記録できないだけ）
        void Add(string physicalName, string name)
        {
            if (Enum.TryParse<PhysicalKey>(physicalName, out var key)) d[key] = name;
        }
        for (char c = 'A'; c <= 'Z'; c++) Add(c.ToString(), c.ToString()); // PhysicalKey.A〜Z
        for (int i = 0; i <= 9; i++)
        {
            Add("Digit" + i, i.ToString());
            Add("NumPad" + i, "NumPad" + i);
        }
        for (int i = 1; i <= 24; i++) Add("F" + i, "F" + i);
        d[PhysicalKey.Space] = "Space";
        d[PhysicalKey.Enter] = "Enter";
        d[PhysicalKey.Tab] = "Tab";
        d[PhysicalKey.Backspace] = "Backspace";
        d[PhysicalKey.Insert] = "Insert";
        d[PhysicalKey.Delete] = "Delete";
        d[PhysicalKey.Home] = "Home";
        d[PhysicalKey.End] = "End";
        d[PhysicalKey.PageUp] = "PageUp";
        d[PhysicalKey.PageDown] = "PageDown";
        d[PhysicalKey.ArrowUp] = "Up";
        d[PhysicalKey.ArrowDown] = "Down";
        d[PhysicalKey.ArrowLeft] = "Left";
        d[PhysicalKey.ArrowRight] = "Right";
        d[PhysicalKey.Minus] = "Minus";
        d[PhysicalKey.Equal] = "Equals";
        d[PhysicalKey.BracketLeft] = "OpenBracket";
        d[PhysicalKey.BracketRight] = "CloseBracket";
        d[PhysicalKey.Backslash] = "Backslash";
        d[PhysicalKey.Semicolon] = "Semicolon";
        d[PhysicalKey.Quote] = "Quote";
        d[PhysicalKey.Backquote] = "BackQuote";
        d[PhysicalKey.Comma] = "Comma";
        d[PhysicalKey.Period] = "Period";
        d[PhysicalKey.Slash] = "Slash";
        d[PhysicalKey.PrintScreen] = "PrintScreen";
        d[PhysicalKey.Pause] = "Pause";
        d[PhysicalKey.CapsLock] = "CapsLock";
        return d;
    }
}
