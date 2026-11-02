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
public enum ModifierKey { LeftCtrl, RightCtrl, LeftAlt, RightAlt, LeftShift, RightShift, LeftMeta, RightMeta }

/// <summary>物理キーの左右を保持し、片側の離上で他方の状態を失わない。</summary>
public sealed class ModifierState
{
    private readonly object _gate = new();
    private readonly HashSet<ModifierKey> _held = [];
    public KeyModifiers Pressed { get { lock (_gate) return _held.Aggregate(KeyModifiers.None, (m, k) => m | Flag(k)); } }
    public void Update(ModifierKey key, bool down, bool simulated = false)
    {
        if (simulated) return;
        lock (_gate) { if (down) _held.Add(key); else _held.Remove(key); }
    }
    public void Reset() { lock (_gate) _held.Clear(); }
    public static KeyModifiers Flag(ModifierKey key) => key switch
    {
        ModifierKey.LeftCtrl or ModifierKey.RightCtrl => KeyModifiers.Ctrl,
        ModifierKey.LeftAlt or ModifierKey.RightAlt => KeyModifiers.Alt,
        ModifierKey.LeftShift or ModifierKey.RightShift => KeyModifiers.Shift,
        ModifierKey.LeftMeta or ModifierKey.RightMeta => KeyModifiers.Meta,
        _ => KeyModifiers.None,
    };
}
