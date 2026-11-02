using System.Diagnostics;
using CPSystemMonitor.Core.Input;
using CPSystemMonitor.Core.Model;
using SharpHook;
using SharpHook.Data;

namespace CPSystemMonitor.Platform.Input;

/// <summary>
/// SharpHook（libuiohook）によるグローバル入力フック。ホットキーとマウストリガーで 1 つのフックを共有する。
/// Windows / macOS 共通（OS 差はライブラリが吸収）。判定ロジックは Core の TriggerDetector。
/// イベント抑止（SuppressEvent）が要るので SimpleGlobalHook を使う（TaskPoolGlobalHook は抑止できない）。
/// </summary>
public sealed class SharpHookInputService : IInputHookService, IHotkeyService, IMouseTriggerService, IHistoryTriggerService
{
    private readonly object _gate = new();
    private readonly object _inputGate = new();
    private SimpleGlobalHook? _hook;
    private readonly TriggerDetector _detector;
    private readonly ModifierDoubleTapDetector _doubleTap = new();
    private readonly TriggerDetector _historyDetector = new();
    private readonly ModifierDoubleTapDetector _historyDoubleTap = new();
    private readonly ModifierState _modifiers = new();
    private volatile bool _running;

    private readonly Func<KeyModifiers>? _currentModifiers;
    private readonly Func<ushort, bool>? _isKeyPressed;
    private readonly Func<InputButton, bool>? _isButtonPressed;
    private readonly Dictionary<string, ushort> _nativeKeyCodes = new(StringComparer.Ordinal);

    /// <param name="currentModifiers">
    /// マウス押下時の修飾キーを OS から直接読む関数（任意）。macOS では自アプリが前面のときに押した ⌃ などを
    /// フックが修飾キーとして記録しないことがあるため（2026-10-03 確認）、Mac はこれを渡す。null ならフックの値を使う。
    /// </param>
    /// <param name="isKeyPressed">OS 固有キーコードの押下照会。解放イベント欠落の補正に使う（任意）。</param>
    /// <param name="isButtonPressed">マウスボタンの押下照会（任意）。</param>
    public SharpHookInputService(Func<KeyModifiers>? currentModifiers = null,
        Func<ushort, bool>? isKeyPressed = null, Func<InputButton, bool>? isButtonPressed = null)
    {
        _detector = new TriggerDetector();
        _currentModifiers = currentModifiers;
        _isKeyPressed = isKeyPressed;
        _isButtonPressed = isButtonPressed;
        _historyDoubleTap.Configure(new() { Enabled = false });
    }

    public bool IsRunning => _running;

    public event Action<string>? Failed;
    public event Action<long>? Pressed;
    public event Action<ScreenPoint, long>? Triggered;
    public event Action<ScreenPoint>? PassedThroughDown;
    public event Action<long>? DoubleTapped;
    public event Action<ScreenPoint?, long>? HistoryTriggered;
    public void ConfigureHistory(TriggerSettings settings)
    {
        lock (_inputGate)
        {
            _historyDetector.SetHotkey(settings.Hotkey);
            _historyDetector.SetMouse(settings);
            _historyDoubleTap.Configure(settings.DoubleTap);
        }
    }
    // 自アプリへ切り替わった後のキーはフックから欠けることがあるため、OS照会があればそちらを使う。
    public KeyModifiers PressedModifiers => _currentModifiers?.Invoke() ?? _modifiers.Pressed;
    public void ConfigureDoubleTap(DoubleTapSetting setting) => _doubleTap.Configure(setting);
    public event Action<ScreenPoint>? PassedThroughUp;

    public void Start()
    {
        SimpleGlobalHook hook;
        lock (_gate)
        {
            if (_running) return;
            // 失敗・停止したフックは再利用できないので作り直す（SPEC §10.3。Mac で権限を許可した後の再試行）
            _hook?.Dispose();
            _hook = hook = CreateHook();
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await hook.RunAsync(useBackgroundThread: true);
            }
            catch (HookException ex)
            {
                // macOS: アクセシビリティ未許可だと ErrorAxApiDisabled でここに来る（SPEC §4.3）
                Failed?.Invoke($"{ex.Result}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Failed?.Invoke(ex.ToString());
            }
        });
    }

    private SimpleGlobalHook CreateHook()
    {
        var hook = new SimpleGlobalHook();
        hook.HookEnabled += (_, _) => { ResetInput(); _running = true; };
        hook.HookDisabled += (_, _) => { _running = false; ResetInput(); };
        hook.KeyPressed += OnKeyPressed;
        hook.KeyReleased += OnKeyReleased;
        hook.MousePressed += OnMousePressed;
        hook.MouseReleased += OnMouseReleased;
        hook.MouseWheel += OnMouseWheel;
        return hook;
    }

    // ---------------- IHotkeyService ----------------

    public bool Register(HotkeySetting hotkey, out string? error)
    {
        // フック方式は登録という手続きが無く、他アプリとの競合も検知できない（SPEC §13.1-2）
        _detector.SetHotkey(hotkey);
        error = null;
        return true;
    }

    public void Unregister() => _detector.SetHotkey(null);

    private volatile Action<string, KeyModifiers>? _capture;
    private readonly HashSet<string> _capturedKeys = new(); // フックスレッドのみで操作

    public void BeginCapture(Action<string, KeyModifiers> onKey)
    {
        lock (_inputGate)
        {
            _doubleTap.SetCapture(true);
            _historyDoubleTap.SetCapture(true);
            _capture = onKey;
        }
    }

    public void EndCapture()
    {
        lock (_inputGate)
        {
            _capture = null;
            _doubleTap.SetCapture(false);
            _historyDoubleTap.SetCapture(false);
        }
    }

    // ---------------- IMouseTriggerService ----------------

    public void Configure(TriggerSettings settings) => _detector.SetMouse(settings);

    // ---------------- フックスレッド ----------------

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        lock (_inputGate)
        {
            if (e.IsEventSimulated) return;
            var modifier = ToModifierKey(e.Data.KeyCode);
            string key = KeyName(e.Data.KeyCode);
            // Windows の非同期状態はフック中の当該イベントをまだ反映しないため、現在のキーは照合しない。
            ReconcileDoubleTap(key);
            _nativeKeyCodes[key] = e.Data.RawCode;
            if (modifier is { } m) _modifiers.Update(m, true);
            _doubleTap.OnKey(KeyName(e.Data.KeyCode), modifier, true, e.EventTime.Ticks / TimeSpan.TicksPerMillisecond);
            _historyDoubleTap.OnKey(KeyName(e.Data.KeyCode), modifier, true, e.EventTime.Ticks / TimeSpan.TicksPerMillisecond);
            if (modifier is not null) return;
            if (_capturedKeys.Contains(KeyName(e.Data.KeyCode))) { e.SuppressEvent = true; return; }
            if (_capture is { } capture)
            {
                e.SuppressEvent = true;
                _capturedKeys.Add(KeyName(e.Data.KeyCode));
                capture(KeyName(e.Data.KeyCode), ToModifiers(e.RawEvent.Mask));
                return;
            }
            var d = _detector.OnKeyDown(KeyName(e.Data.KeyCode), ToModifiers(e.RawEvent.Mask));
            if (d.Suppress) e.SuppressEvent = true;
            if (d.Fire) Pressed?.Invoke(Stopwatch.GetTimestamp());
            if (!d.Suppress)
            {
                var history = _historyDetector.OnKeyDown(KeyName(e.Data.KeyCode), ToModifiers(e.RawEvent.Mask));
                if (history.Suppress) e.SuppressEvent = true;
                if (history.Fire) HistoryTriggered?.Invoke(null, Stopwatch.GetTimestamp());
            }
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        lock (_inputGate)
        {
            if (e.IsEventSimulated) return;
            var modifier = ToModifierKey(e.Data.KeyCode);
            if (modifier is { } m) _modifiers.Update(m, false);
            bool tapped = _doubleTap.OnKey(KeyName(e.Data.KeyCode), modifier, false, e.EventTime.Ticks / TimeSpan.TicksPerMillisecond);
            bool historyTapped = _historyDoubleTap.OnKey(KeyName(e.Data.KeyCode), modifier, false, e.EventTime.Ticks / TimeSpan.TicksPerMillisecond);
            if (tapped) DoubleTapped?.Invoke(Stopwatch.GetTimestamp());
            else if (historyTapped) HistoryTriggered?.Invoke(null, Stopwatch.GetTimestamp());
            if (modifier is not null) return;
            if (_capturedKeys.Remove(KeyName(e.Data.KeyCode)))
            {
                e.SuppressEvent = true;
                _detector.OnKeyUp(KeyName(e.Data.KeyCode));
                return;
            }
            if (_detector.OnKeyUp(KeyName(e.Data.KeyCode))) e.SuppressEvent = true;
            if (_historyDetector.OnKeyUp(KeyName(e.Data.KeyCode))) e.SuppressEvent = true;
        }
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        lock (_inputGate)
        {
            if (e.IsEventSimulated) return;
            _doubleTap.OnMouseButton(ToButton(e.Data.Button), true);
            _historyDoubleTap.OnMouseButton(ToButton(e.Data.Button), true);
            long ts = Stopwatch.GetTimestamp();
            var p = new ScreenPoint(e.Data.X, e.Data.Y);
            var mods = _currentModifiers?.Invoke() ?? ToModifiers(e.RawEvent.Mask);
            var d = _detector.OnMouseDown(ToButton(e.Data.Button), mods, allowFire: _capture is null);
            DebugLog($"mouse down {e.Data.Button} mods={mods} hookMask={e.RawEvent.Mask} → {d}");
            if (d.Suppress) e.SuppressEvent = true;
            if (d.Fire) Triggered?.Invoke(p, ts);
            if (!d.Suppress)
            {
                var history = _historyDetector.OnMouseDown(ToButton(e.Data.Button), mods, allowFire: _capture is null);
                if (history.Suppress) e.SuppressEvent = true;
                if (history.Fire) HistoryTriggered?.Invoke(p, ts);
                if (history.PassedThrough) PassedThroughDown?.Invoke(p);
            }
        }
    }

    private void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        lock (_inputGate)
        {
            if (e.IsEventSimulated) return;
            _doubleTap.OnMouseButton(ToButton(e.Data.Button), false);
            _historyDoubleTap.OnMouseButton(ToButton(e.Data.Button), false);
            var d = _detector.OnMouseUp(ToButton(e.Data.Button));
            DebugLog($"mouse up {e.Data.Button} → {d}");
            if (d.Suppress) e.SuppressEvent = true;
            var history = _historyDetector.OnMouseUp(ToButton(e.Data.Button));
            if (history.Suppress) e.SuppressEvent = true;
            if (d.PassedThrough && history.PassedThrough) PassedThroughUp?.Invoke(new ScreenPoint(e.Data.X, e.Data.Y));
        }
    }

    private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        lock (_inputGate)
        {
            if (e.IsEventSimulated) return;
            _doubleTap.OnWheel();
            _historyDoubleTap.OnWheel();
        }
    }

    private void ReconcileDoubleTap(string currentKey)
    {
        if (_isKeyPressed is null && _isButtonPressed is null) return;
        bool KeyPressed(string key) => key == currentKey || _isKeyPressed is null
            || !_nativeKeyCodes.TryGetValue(key, out ushort code) || code == 0 || _isKeyPressed(code);
        bool ButtonPressed(InputButton button) => _isButtonPressed?.Invoke(button) ?? true;
        _doubleTap.ReconcilePressedInputs(KeyPressed, ButtonPressed);
        _historyDoubleTap.ReconcilePressedInputs(KeyPressed, ButtonPressed);
    }

    private void ResetInput()
    {
        lock (_inputGate)
        {
            _doubleTap.Reset();
            _historyDoubleTap.Reset();
            _historyDetector.Reset();
            _modifiers.Reset();
            _detector.Reset();
            _capturedKeys.Clear();
            _nativeKeyCodes.Clear();
        }
    }

    /// <summary>Debug ビルドだけ、入力の判定を Trace に出す（App の AppLog がファイルへ転送する）。</summary>
    [Conditional("DEBUG")]
    private static void DebugLog(string message) => Trace.WriteLine("[hook] " + message);

    // ---------------- 変換 ----------------

    private static ModifierKey? ToModifierKey(KeyCode key) => key switch
    {
        KeyCode.VcLeftControl => ModifierKey.LeftCtrl, KeyCode.VcRightControl => ModifierKey.RightCtrl,
        KeyCode.VcLeftAlt => ModifierKey.LeftAlt, KeyCode.VcRightAlt => ModifierKey.RightAlt,
        KeyCode.VcLeftShift => ModifierKey.LeftShift, KeyCode.VcRightShift => ModifierKey.RightShift,
        KeyCode.VcLeftMeta => ModifierKey.LeftMeta, KeyCode.VcRightMeta => ModifierKey.RightMeta,
        _ => null,
    };

    /// <summary>KeyCode.VcSpace → "Space"、VcA → "A"、VcF1 → "F1"（設定の HotkeySetting.Key と同じ表記）。</summary>
    internal static string KeyName(KeyCode k)
    {
        var s = k.ToString();
        return s.StartsWith("Vc", StringComparison.Ordinal) ? s[2..] : s;
    }

    private static KeyModifiers ToModifiers(EventMask m)
    {
        var r = KeyModifiers.None;
        if ((m & EventMask.Ctrl) != 0) r |= KeyModifiers.Ctrl;
        if ((m & EventMask.Alt) != 0) r |= KeyModifiers.Alt;
        if ((m & EventMask.Shift) != 0) r |= KeyModifiers.Shift;
        if ((m & EventMask.Meta) != 0) r |= KeyModifiers.Meta;
        return r;
    }

    private static InputButton ToButton(MouseButton b) => b switch
    {
        MouseButton.Button1 => InputButton.Left,
        MouseButton.Button2 => InputButton.Right,
        MouseButton.Button3 => InputButton.Middle,
        MouseButton.Button4 => InputButton.X1,
        MouseButton.Button5 => InputButton.X2,
        _ => InputButton.Other,
    };

    public void Dispose()
    {
        lock (_gate) _hook?.Dispose();
    }
}
