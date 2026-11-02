using CPSystemMonitor.Core.Input;
using CPSystemMonitor.Core.Model;

namespace CPSystemMonitor.Platform;

// SPEC §10.3 のプラットフォーム抽象。OS 固有処理は必ずこの裏に置く。
// イベントは入力フックのスレッドから発火しうる。UI を触る側で Dispatcher.UIThread.Post すること。
// イベントの long 引数は Stopwatch.GetTimestamp()（入力を受けた時刻。表示遅延の計測用）。

/// <summary>グローバル入力フックの開始・停止（ホットキーとマウストリガーで 1 つのフックを共有する）。</summary>
public interface IInputHookService : IDisposable
{
    /// <summary>
    /// フックを別スレッドで開始する。失敗は Failed で通知（macOS の権限未許可など）。
    /// 失敗後・停止後に再度呼べば作り直して開始する（Mac で権限を許可した後の再試行用）。
    /// </summary>
    void Start();

    bool IsRunning { get; }
    void ConfigureDoubleTap(DoubleTapSetting setting);
    KeyModifiers PressedModifiers { get; }
    event Action<long>? DoubleTapped;

    event Action<string>? Failed;
}

/// <summary>グローバルホットキー（SPEC §4.1）。キーリピートは実装側で無視し、押下 1 回につき 1 回だけ発火する。</summary>
public interface IHotkeyService
{
    /// <summary>登録。失敗（他アプリと競合など）は false と理由を返す。</summary>
    bool Register(HotkeySetting hotkey, out string? error);

    void Unregister();

    /// <summary>
    /// ホットキーの記録（設定画面）。終わるまで、修飾キー以外のキー押下をすべて他のアプリへ渡さずに onKey へ知らせる
    /// （キー名は HotkeySetting.Key と同じ表記。Esc も知らせるので中止は呼び出し側で判断する）。OS が先に取るキー
    /// （macOS の ⌃Space = 入力ソース切替など）もフックには届くので、ウィンドウのキー入力より確実。フックスレッドから呼ばれる。
    /// </summary>
    void BeginCapture(Action<string, KeyModifiers> onKey);

    void EndCapture();

    event Action<long>? Pressed;
}

/// <summary>
/// マウス操作トリガー（SPEC §4.2）とグローバルなマウス観測。
/// 盤面外クリックの判定（SPEC §3.3）にも使うため、トリガーにならず下のアプリへ通した押下・離上も通知する。
/// </summary>
public interface IMouseTriggerService
{
    void Configure(TriggerSettings settings);

    /// <summary>トリガー成立（位置, 入力時刻）。修飾キー+クリックは押下・離上とも抑止済み。</summary>
    event Action<ScreenPoint, long>? Triggered;

    /// <summary>トリガーにならず下のアプリへ通したボタン押下。</summary>
    event Action<ScreenPoint>? PassedThroughDown;

    /// <summary>トリガーにならず下のアプリへ通したボタン離上。</summary>
    event Action<ScreenPoint>? PassedThroughUp;
}

/// <summary>フォルダ・ファイル・URL を OS の既定で開く（設定フォルダ・ログフォルダ・ライセンス文・配布サイト。SPEC §10.3）。</summary>
public interface IShellService
{
    LaunchResult Open(string pathOrUrl);
}

/// <summary>盤面ウィンドウの OS 依存処理（SPEC §3）。hwnd は Avalonia の TryGetPlatformHandle().Handle。</summary>
public interface IWindowService
{
    /// <summary>
    /// 画面座標（フック・Avalonia の Position / Screens）が論理座標か。Mac は pt（true）、Win は物理 px（false）。
    /// true のときウィンドウの大きさ・矩形に拡大率を掛けない（SPEC §3.5）。
    /// </summary>
    bool UsesLogicalScreenCoordinates { get; }

    /// <summary>
    /// 盤面ウィンドウの初期設定。タスクバー / Dock / Alt+Tab に出さない、
    /// Mac は全画面アプリの上や別の Space でも出せるようにする。最初の表示時に 1 回呼ぶ。
    /// </summary>
    void ConfigureBoardWindow(nint windowHandle);

    /// <summary>フォーカスを取らずクリックを下のウィンドウへ通すプレビュー。</summary>
    void ConfigurePreviewWindow(nint windowHandle);

    /// <summary>表示前に前面だったウィンドウを記録する（閉じたときに返すため）。</summary>
    nint CaptureForeground();

    /// <summary>
    /// フックで入力を抑止して呼び出した後でも確実に前面化する
    /// （Win はフォアグラウンドロックを AttachThreadInput で回避。SPEC §3.3）。
    /// </summary>
    bool BringToForeground(nint windowHandle);

    bool RestoreForeground(nint previous);
    bool IsForeground(nint target);
    bool IsTargetValid(nint target);
    nint PreviousForeground();

    /// <summary>OS の「視差効果を減らす」（Mac）/「アニメーション効果」OFF（Win）。true なら常時の演出（光の玉・明滅）を止める。</summary>
    bool PrefersReducedMotion => false;

    /// <summary>自アプリを隠して直前のアプリを前面に戻す（Mac の NSApp hide:。返却先が分からないときの代わり。Win は何もしない）。</summary>
    void HideApplication() { }

    /// <summary>現在のカーソル位置（物理 px）。ホットキーで呼ばれたときの表示位置に使う。</summary>
    ScreenPoint GetCursorPosition();
}

/// <summary>OS ログイン時の自動起動（SPEC §7 一般）。</summary>
public interface IAutoStartService
{
    /// <summary>この起動のしかたで自動起動を設定できるか（Mac は .app から起動したときだけ。使えなければ設定画面に行を出さない。SPEC §1.2）。</summary>
    bool IsAvailable => true;

    bool IsEnabled { get; }
    void SetEnabled(bool enabled, string executablePath);
}

/// <summary>入力監視などの権限（SPEC §4.3）。</summary>
public interface IPermissionService
{
    PermissionState InputMonitoring { get; }
    PermissionState Accessibility { get; }

    /// <summary>OS の許可ダイアログを出す（Mac: AXIsProcessTrustedWithOptions の prompt）。Win は何もしない。</summary>
    void RequestAccessibility();

    void OpenSystemSettings();
}

/// <summary>OS ごとの実装一式。App 起動時に OperatingSystem.IsWindows() 等で選ぶ。</summary>
public interface IPlatformServices : IDisposable
{
    IInputHookService InputHook { get; }
    IHotkeyService Hotkey { get; }
    IMouseTriggerService MouseTrigger { get; }
    IShellService Shell { get; }
    IWindowService Window { get; }
    IAutoStartService AutoStart { get; }
    IPermissionService Permissions { get; }

    /// <summary>常時の演出を OS の層で描く（macOS = Core Animation）。既定は非対応（App は Avalonia の層で描く）。</summary>
    IAmbientLayerService Ambient => AmbientLayers.Unsupported;
}


public interface IHistoryTriggerService
{
    void ConfigureHistory(TriggerSettings settings);
    event Action<ScreenPoint?, long>? HistoryTriggered;
}
