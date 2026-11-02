using CPSystemMonitor.Core.Model;

namespace CPSystemMonitor.Core.Input;

public enum InputButton
{
    Left,
    Right,
    Middle,
    X1,
    X2,
    Other,
}

/// <param name="Suppress">このイベントを下のアプリに渡さない。</param>
/// <param name="Fire">トリガー成立。</param>
public readonly record struct KeyDecision(bool Suppress, bool Fire);

/// <param name="Suppress">このイベントを下のアプリに渡さない。</param>
/// <param name="Fire">トリガー成立。</param>
/// <param name="PassedThrough">トリガーと無関係に下のアプリへ通した（盤面外クリック判定に使う）。</param>
public readonly record struct MouseDecision(bool Suppress, bool Fire, bool PassedThrough);

/// <summary>ホットキーと修飾キー付きクリックの純粋な判定。</summary>
public sealed class TriggerDetector
{
    private readonly object _lock = new();
    private HotkeySetting? _hotkey;
    private List<MouseTriggerSetting> _mouse = [];
    private readonly HashSet<string> _heldHotkeyKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<InputButton> _suppressedDown = new();

    public void SetHotkey(HotkeySetting? hotkey)
    {
        lock (_lock)
        {
            _hotkey = hotkey is { Enabled: true } ? new HotkeySetting { Enabled = true, Key = hotkey.Key, Modifiers = hotkey.Modifiers } : null;
        }
    }

    public void SetMouse(TriggerSettings settings)
    {
        lock (_lock)
        {
            _mouse = settings.Mouse.Where(t => t.IsValid).Select(t => new MouseTriggerSetting
            { Enabled = t.Enabled, Button = t.Button, Modifiers = t.Modifiers }).ToList();
        }
    }

    // ---------------- キーボード ----------------

    /// <param name="key">キー名（"Space", "A", "F1" …）。修飾キー自身は呼ばなくてよい。</param>
    public KeyDecision OnKeyDown(string key, KeyModifiers modifiers, bool simulated = false)
    {
        if (simulated) return default;
        lock (_lock)
        {
            // 押しっぱなし中のリピート: 修飾キーの状態に関係なく抑止して無視（スペース等が漏れないように）
            if (_heldHotkeyKeys.Contains(key))
                return new KeyDecision(Suppress: true, Fire: false);

            if (_hotkey is { } hk
                && string.Equals(hk.Key, key, StringComparison.OrdinalIgnoreCase)
                && hk.Modifiers == modifiers)
            {
                _heldHotkeyKeys.Add(key);
                return new KeyDecision(Suppress: true, Fire: true);
            }
            return default;
        }
    }

    /// <returns>この離上を抑止するか。</returns>
    public bool OnKeyUp(string key, bool simulated = false)
    {
        if (simulated) return false;
        lock (_lock)
        {
            return _heldHotkeyKeys.Remove(key);
        }
    }

    // ---------------- マウス ----------------

    public MouseDecision OnMouseDown(InputButton button, KeyModifiers modifiers, bool simulated = false, bool allowFire = true)
    {
        if (simulated) return default;
        lock (_lock)
        {
            if (_suppressedDown.Contains(button)) return new(true, false, false);
            if (allowFire && _mouse.Any(t => t.Enabled && Matches(t, button) && t.Modifiers == modifiers))
            {
                _suppressedDown.Add(button);
                return new(true, true, false);
            }
            return new(false, false, true);
        }
    }

    public MouseDecision OnMouseUp(InputButton button, bool simulated = false)
    {
        if (simulated) return default;
        lock (_lock)
            return _suppressedDown.Remove(button) ? new(true, false, false) : new(false, false, true);
    }

    public void Reset()
    {
        lock (_lock) { _heldHotkeyKeys.Clear(); _suppressedDown.Clear(); }
    }

    private static bool Matches(MouseTriggerSetting t, InputButton button) => (t.Button, button) switch
    {
        (MouseButtonKind.Left, InputButton.Left) or (MouseButtonKind.Right, InputButton.Right) or
        (MouseButtonKind.Middle, InputButton.Middle) or (MouseButtonKind.X1, InputButton.X1) or
        (MouseButtonKind.X2, InputButton.X2) => true,
        _ => false,
    };
}
