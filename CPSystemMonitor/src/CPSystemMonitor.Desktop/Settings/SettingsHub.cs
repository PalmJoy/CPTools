using Avalonia.Threading;
using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Core.Storage;

namespace CPSystemMonitor.App;

/// <summary>何が変わったか（購読側が必要な所だけ反映し直すため）。</summary>
[Flags]
internal enum SettingsChange
{
    None = 0,
    // 1 << 0 は旧 Theme（2026-10-04 にテーマの選択を廃止）
    Opacity = 1 << 1,
    BoardLayout = 1 << 2,
    Effects = 1 << 3,
    Popup = 1 << 4,
    Triggers = 1 << 5,
    // 1 << 6・1 << 7 は CPFileLauncher の表示モード・常駐（CPSystemMonitor には無い）
    History = 1 << 16,
    Paste = 1 << 14,
    Board = 1 << 15,
    Logging = 1 << 8,
    Data = 1 << 9,
    General = 1 << 10,
    Colors = 1 << 11,
    Hud = 1 << 13,
    Background = 1 << 12,
    All = ~0,
}

/// <summary>
/// 設定の保持・変更・保存（M5。SPEC §7 即時反映・自動保存）。変更は Update で行い、Changed で各所へ知らせ、
/// 保存は 500 ms まとめて書く（スライダーを動かしている間に何度も書かない）。読み取り専用（新しい版のデータ）なら書かない。
/// UI スレッドから呼ぶこと。
/// </summary>
internal sealed class SettingsHub
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly AppDataStore _store;
    private readonly DispatcherTimer _saveTimer;
    private bool _dirty;

    public SettingsHub(AppDataStore store, AppSettings settings)
    {
        _store = store;
        Current = settings;
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => Flush();
    }

    public AppSettings Current { get; private set; }

    public bool IsReadOnly => _store.SettingsStore.IsReadOnly;

    public event Action<SettingsChange>? Changed;

    /// <summary>背景画像の読み込みが終わった（設定画面のプレビュー・注記を読み直す）。</summary>
    public event Action? BackgroundLoaded;

    public void NotifyBackgroundLoaded() => BackgroundLoaded?.Invoke();

    /// <summary>設定を変える → 整える → 知らせる → 保存を予約。change = None なら知らせずに保存だけ。</summary>
    public void Update(Action<AppSettings> mutate, SettingsChange change)
    {
        mutate(Current);
        Current.Normalize();
        if (change != SettingsChange.None) Changed?.Invoke(change);
        ScheduleSave();
    }

    /// <summary>リセット・インポート用（M5 ステップ 4）。</summary>
    public void Replace(AppSettings fresh)
    {
        fresh.Normalize();
        Current = fresh;
        Changed?.Invoke(SettingsChange.All);
        ScheduleSave();
    }

    /// <summary>予約中の保存を今すぐ書く（設定画面を閉じたとき・終了時）。</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        _dirty = false;
        if (IsReadOnly)
        {
            AppLog.Info("設定は読み取り専用（新しい版のデータ）なので保存しません");
            return;
        }
        _store.SaveSettings(Current);
        AppLog.Info("設定を保存");
    }

    private void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
