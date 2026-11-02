#if CPTOOLS_CLIPBOARD
using CPClipboard.Core.Model;

namespace CPClipboard.Core.Input;
#elif CPTOOLS_MONITOR
using CPSystemMonitor.Core.Model;

namespace CPSystemMonitor.Core.Input;
#elif CPTOOLS_LAUNCHER
using FileLauncher.Core.Model;

namespace FileLauncher.Core.Input;
#else
#error A CPTools product must be selected.
#endif
/// <summary>イベント時刻（ms）を受ける単独修飾キーの二度押し判定。合成入力は状態も変えない。</summary>
public sealed class ModifierDoubleTapDetector
{
    public const int TapMaxMs = 250;
    private readonly object _gate = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly HashSet<InputButton> _buttons = [];
    private bool _enabled = true;
    private bool _capturing;
    private KeyModifiers _target = KeyModifiers.Ctrl;
    private int _interval = 300;
    private int _phase; // 0: 空、1: 最初の押下、2: 次の押下待ち、3: 二度目の押下
    private string? _candidate;
    private long _downAt, _upAt;

    public void Configure(DoubleTapSetting setting)
    {
        lock (_gate)
        {
            _enabled = setting.Enabled;
            _target = setting.Modifier switch
            {
                DoubleTapModifier.Alt => KeyModifiers.Alt, DoubleTapModifier.Shift => KeyModifiers.Shift,
                DoubleTapModifier.Meta => KeyModifiers.Meta, _ => KeyModifiers.Ctrl,
            };
            _interval = setting.IntervalMs;
            ClearCandidate();
        }
    }

    public void SetCapture(bool capturing)
    {
        lock (_gate) { _capturing = capturing; ClearCandidate(); }
    }

    public bool OnKey(string key, ModifierKey? modifier, bool down, long nowMs, bool simulated = false)
    {
        if (simulated) return false;
        lock (_gate)
        {
            if (down)
            {
                if (!_keys.Add(key)) return false; // OS リピート
                if (!_enabled || _capturing || modifier is null || ModifierState.Flag(modifier.Value) != _target
                    || _keys.Count != 1 || _buttons.Count != 0)
                { ClearCandidate(); return false; }
                bool second = _phase == 2 && nowMs >= _upAt && nowMs - _upAt <= _interval;
                _phase = second ? 3 : 1;
                _candidate = key;
                _downAt = nowMs;
                return false;
            }
            if (!_keys.Remove(key)) return false;
            if (_candidate != key) return false;
            bool valid = nowMs >= _downAt && nowMs - _downAt <= TapMaxMs;
            if (!valid || !_enabled || _capturing) { ClearCandidate(); return false; }
            if (_phase == 3) { ClearCandidate(); return true; }
            _phase = 2;
            _candidate = null;
            _upAt = nowMs;
            return false;
        }
    }

    public void OnMouseButton(InputButton button, bool down, bool simulated = false)
    {
        if (simulated) return;
        lock (_gate)
        {
            if (down) { _buttons.Add(button); ClearCandidate(); }
            else _buttons.Remove(button);
        }
    }
    public void OnWheel(bool simulated = false)
    { if (!simulated) lock (_gate) ClearCandidate(); }
    /// <summary>OS が解放済みと確認した入力を除去する。欠落した離上をタップとして数えない。</summary>
    public void ReconcilePressedInputs(Func<string, bool> isKeyPressed, Func<InputButton, bool> isButtonPressed)
    {
        lock (_gate)
        {
            int keys = _keys.RemoveWhere(key => !isKeyPressed(key));
            int buttons = _buttons.RemoveWhere(button => !isButtonPressed(button));
            if (keys != 0 || buttons != 0) ClearCandidate();
        }
    }

    public void Reset()
    { lock (_gate) { _keys.Clear(); _buttons.Clear(); ClearCandidate(); } }
    private void ClearCandidate() { _phase = 0; _candidate = null; }
}
