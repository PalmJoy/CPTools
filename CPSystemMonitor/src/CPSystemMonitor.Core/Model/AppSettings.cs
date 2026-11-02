using CPSystemMonitor.Core.Effects;

namespace CPSystemMonitor.Core.Model;

/// <summary>アプリ設定（SPEC §7）。settings.json のルート。既定値は SPEC の記述に合わせる。</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public Monitoring.MonitorSettings Monitor { get; set; } = new();

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public GeneralSettings General { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public PopupSettings Popup { get; set; } = new();
    public TriggerSettings Triggers { get; set; } = new();
    public DataSettings Data { get; set; } = new();
    public AdvancedSettings Advanced { get; set; } = new();

    public void Normalize()
    {
        Monitor ??= new();
        Monitor.Normalize();
        General ??= new();
        if (General.Language is not ("system" or "ja" or "en")) General.Language = "system";
        Appearance ??= new();
        Popup ??= new();
        Triggers ??= new();
        Data ??= new();
        Advanced ??= new();

        // 30〜100% を 5 刻み（SPEC §3.1、spec/SETTINGS.md）
        Appearance.Opacity = (int)Math.Round(Math.Clamp(Appearance.Opacity, 30, 100) / 5.0, MidpointRounding.AwayFromZero) * 5;
        Appearance.BoardWidth = (int)Math.Round(Math.Clamp(Appearance.BoardWidth, 280, 800) / 20.0, MidpointRounding.AwayFromZero) * 20;
        Appearance.MaxRows = Math.Clamp(Appearance.MaxRows, 4, 30);
        Appearance.FontSize = Math.Clamp(Appearance.FontSize, 11, 20);
        Appearance.Vsync = Math.Clamp(Appearance.Vsync, 1, 3);
        Appearance.GlitchIntensity = GlitchLook.NormalizeIntensity(Appearance.GlitchIntensity);
        Appearance.Background ??= new();
        Appearance.Background.Overlay = (int)Math.Round(Math.Clamp(Appearance.Background.Overlay, 0, 90) / 5.0, MidpointRounding.AwayFromZero) * 5;
        Appearance.Background.ImageOpacity = (int)Math.Round(Math.Clamp(Appearance.Background.ImageOpacity, 10, 100) / 5.0, MidpointRounding.AwayFromZero) * 5;
        if (string.IsNullOrWhiteSpace(Appearance.Background.Image)) Appearance.Background.Image = null;
        Popup.Keyboard ??= new();
        Popup.Mouse ??= new();
        Triggers.Hotkey ??= new();
        Triggers.Mouse ??= new();
        Triggers.DoubleTap ??= new();
        Triggers.DoubleTap.IntervalMs = (int)Math.Round(Math.Clamp(Triggers.DoubleTap.IntervalMs, 200, 500) / 10.0, MidpointRounding.AwayFromZero) * 10;
        int removed = Triggers.Mouse.RemoveAll(m => m is null || !m.IsValid);
        if (removed > 0) System.Diagnostics.Trace.WriteLine($"[settings] Removed {removed} invalid mouse triggers.");
        if (!Enum.IsDefined(Triggers.DoubleTap.Modifier)) Triggers.DoubleTap.Modifier = DoubleTapModifier.Ctrl;
        Data.BackupGenerations = Math.Clamp(Data.BackupGenerations, 0, 100);
        Appearance.Effects = EffectCatalog.Normalize(Appearance.Effects);
        Appearance.Colors ??= new();
        Appearance.Colors.Normalize();
        Appearance.Hud ??= new();
        Appearance.Hud.GridOpacity = Math.Clamp(Appearance.Hud.GridOpacity, 0, 10);
        Appearance.Hud.ScanlineOpacity = (int)Math.Round(Math.Clamp(Appearance.Hud.ScanlineOpacity, 0, 50) / 5.0, MidpointRounding.AwayFromZero) * 5;
        if (!HudSettings.ScanlinePitches.Contains(Appearance.Hud.ScanlinePitch)) Appearance.Hud.ScanlinePitch = 3;
    }
    private static void NormalizeTriggers(TriggerSettings triggers)
    {
        triggers.DoubleTap ??= new() { Enabled = false };
        triggers.Hotkey ??= new();
        triggers.Mouse ??= [];
        triggers.DoubleTap.IntervalMs = (int)Math.Round(Math.Clamp(triggers.DoubleTap.IntervalMs, 200, 500) / 10.0, MidpointRounding.AwayFromZero) * 10;
        if (!Enum.IsDefined(triggers.DoubleTap.Modifier)) triggers.DoubleTap.Modifier = DoubleTapModifier.Ctrl;
        triggers.Mouse.RemoveAll(m => m is null || !m.IsValid);
    }

}

public sealed class GeneralSettings
{
    public bool AutoStart { get; set; }
    public bool HideStartupGuide { get; set; }

    /// <summary>"system" / "ja" / "en"</summary>
    public string Language { get; set; } = "system";
}

public sealed class AppearanceSettings
{
    /// <summary>常時の演出の更新間隔（VSync）。1 = 60 fps / 2 = 30 fps / 3 = 20 fps（2026-10-07 ユーザー要望。spec/EFFECTS.md「時計」）。</summary>
    public int Vsync { get; set; } = 2;

    public static readonly int[] VsyncValues = [1, 2, 3];

    /// <summary>グリッチのブレの強さ %（0〜200、10 刻み。既定 100）。spec/EFFECTS.md「強さの倍率」。</summary>
    public int GlitchIntensity { get; set; } = GlitchLook.DefaultIntensity;

    /// <summary>配色（主色・副色。SPEC §3.6「配色」）。見た目はサイバーパンク専用（テーマの選択は 2026-10-04 に廃止）。</summary>
    public ColorSettings Colors { get; set; } = new();

    /// <summary>HUD（ステータス行・時刻・背景グリッド。SPEC §3.9）。</summary>
    public HudSettings Hud { get; set; } = new();

    /// <summary>盤面の不透明度 %（30〜100、5 刻み。既定 80）。</summary>
    public int Opacity { get; set; } = 80;

    /// <summary>盤面の幅（発光の余白を含まない面の幅。280〜800、20 刻み。SPEC §3.1）。</summary>
    public int BoardWidth { get; set; } = 420;

    /// <summary>盤面に並べる最大行数（4〜30）。超えたら一覧の中をスクロール。</summary>
    public int MaxRows { get; set; } = 12;

    /// <summary>行の文字の大きさ（11〜20）。行の高さはこの 2 倍。</summary>
    public int FontSize { get; set; } = 13;

    /// <summary>全演出の ON/OFF（spec/EFFECTS.md）。OFF なら全部「なし」。</summary>
    public bool Animation { get; set; } = true;

    /// <summary>既定と違う演出だけ（キーは EffectCatalog の ID）。既定の値はコード内の EffectCatalog。</summary>
    public Dictionary<string, EffectSpec> Effects { get; set; } = new();

    /// <summary>盤面の背景画像（SPEC §3.7。全グループ共通）。</summary>
    public BackgroundSettings Background { get; set; } = new();
}

/// <summary>盤面の背景画像（SPEC §3.7、spec/SETTINGS.md 表示タブ）。</summary>
public sealed class BackgroundSettings
{
    /// <summary>null = なし。データフォルダからの相対パス "background/&lt;名前&gt;"（設定画面で選んだとき）または絶対パス（手編集）。</summary>
    public string? Image { get; set; }

    public BackgroundFit Fit { get; set; } = BackgroundFit.Fill;

    /// <summary>画像の上に重ねる面の色の不透明度 %（0〜90、5 刻み。既定 40）。</summary>
    public int Overlay { get; set; } = 40;

    /// <summary>画像の不透明度 %（10〜100、5 刻み。既定 100）。画像があるときは地の色を描かないので、下げると画像の部分からデスクトップが透ける。</summary>
    public int ImageOpacity { get; set; } = 100;
}

/// <summary>背景画像の表示方法（埋める / 収める / 引き伸ばす / 並べる / 中央）。</summary>
public enum BackgroundFit
{
    Fill,
    Fit,
    Stretch,
    Tile,
    Center,
}

/// <summary>配色（SPEC §3.6「配色」）。#RRGGBB。背景・文字は主色から導く（Core.Theming.CyberDerivation）。</summary>
public sealed class ColorSettings
{
    public string Primary { get; set; } = "#00E5FF";
    public string Secondary { get; set; } = "#FF2BD6";

    /// <summary>
    /// 読めない色はシアンのプリセットに戻し、表記を大文字の #RRGGBB にそろえる。直したら true（App がログに 1 行出す）。
    /// 暗い色を自動で明るくする補正はしない（カラーピッカーの操作中に値が勝手に変わるのが使いにくい。2026-10-04 ユーザー判断）。
    /// </summary>
    public bool Normalize()
    {
        var (defP, defS) = Theming.ColorPresets.For(AccentPreset.Cyan);
        var p = Theming.RgbColor.TryParse(Primary);
        var s = Theming.RgbColor.TryParse(Secondary);
        bool changed = p is null || s is null;
        Primary = (p ?? defP).ToHex();
        Secondary = (s ?? defS).ToHex();
        return changed;
    }
}

/// <summary>HUD（SPEC §3.9）。</summary>
public sealed class HudSettings
{
    /// <summary>盤面の下のステータス行（GRP / ITEMS / 時刻）。</summary>
    public bool StatusBar { get; set; } = true;
    public ClockMode Clock { get; set; } = ClockMode.Minutes;

    /// <summary>時刻の前に日付（yyyy/MM/dd）も出す（2026-10-04 ユーザー要望）。</summary>
    public bool Date { get; set; } = true;

    /// <summary>盤面の背景の細いグリッド。</summary>
    public bool Grid { get; set; } = true;

    /// <summary>グリッドの線の濃さ %（主色の不透明度。0〜10、既定 3。2026-10-04 ユーザー要望）。</summary>
    public int GridOpacity { get; set; } = 3;

    /// <summary>静止した走査線（主色の横線。SPEC §3.9 H14。2026-10-07 ユーザーが実物で選んで採用）。</summary>
    public bool Scanlines { get; set; } = true;

    /// <summary>走査線の濃さ %（0〜50、5 刻み、既定 25）。</summary>
    public int ScanlineOpacity { get; set; } = 25;

    /// <summary>走査線の間隔 px（<see cref="ScanlinePitches"/> のどれか、既定 3）。</summary>
    public int ScanlinePitch { get; set; } = 3;

    /// <summary>選択できる走査線の間隔（px）。</summary>
    public static readonly int[] ScanlinePitches = [2, 3, 4];
}

public enum ClockMode
{
    Off,
    Minutes,
    Seconds,
}

/// <summary>サイバーパンクの基調色プリセット（主色 / 副色。色の値は App 層 CyberPalette）。</summary>
/// <summary>配色のプリセットの ID（Core.Theming.ColorPresets。JSON には書かない）。</summary>
public enum AccentPreset
{
    Cyan,
    Red,
    Blue,
    Green,
    Purple,
}

public sealed class PopupSettings
{
    /// <summary>キーボード（2 回押し・ホットキー）で開いたときの表示位置（SPEC §3.3）。</summary>
    public PopupPlacementSettings Keyboard { get; set; } = new();

    /// <summary>マウストリガー・トレイ・メニュー・2 重起動で開いたときの表示位置。</summary>
    public PopupPlacementSettings Mouse { get; set; } = new();

    /// <summary>カーソル位置表示（座標が未保存で退避したときも）で、盤面のどの点をカーソルに合わせるか。キーボード・マウス共通（SPEC §3.3）。</summary>
    public CursorAnchor CursorAnchor { get; set; } = CursorAnchor.Top;

    public bool CloseOnOutsideClick { get; set; } = false;
    public bool ToggleOnTrigger { get; set; } = true;
    public bool CloseOnMouseLeave { get; set; }
    public int MouseLeaveDistance { get; set; } = 200;

    public PopupPlacementSettings PlacementFor(PopupTriggerKind kind) =>
        kind == PopupTriggerKind.Keyboard ? Keyboard : Mouse;

    /// <summary>
    /// 閉じたときの位置を「前回の位置」を選んでいる組すべてに書く（SPEC §3.3）。両方 lastPosition なら 1 つの記憶として振る舞い、
    /// 片方だけなら、もう片方で開いた場所に上書きされない。fixed の組の座標は変えない。1 つでも書いたら true。
    /// </summary>
    public bool RememberLastPosition(int x, int y)
    {
        bool any = false;
        foreach (var p in new[] { Keyboard, Mouse })
        {
            if (p.Position != PopupPosition.LastPosition) continue;
            p.X = x;
            p.Y = y;
            any = true;
        }
        return any;
    }
}

/// <summary>表示位置 1 組（SPEC §3.3）。X / Y は Fixed の座標、または LastPosition の前回位置（Win: 物理 px、Mac: pt）。</summary>
public sealed class PopupPlacementSettings
{
    public PopupPosition Position { get; set; } = PopupPosition.Cursor;
    public int? X { get; set; }
    public int? Y { get; set; }
}

/// <summary>
/// カーソル位置表示で盤面のどの点をカーソルに合わせるか（SPEC §3.3）。並びは 3 × 3 の行優先
/// （設定画面のグリッドと同じ。<c>(int)anchor % 3</c> = 列、<c>/ 3</c> = 行）。
/// </summary>
public enum CursorAnchor
{
    TopLeft, Top, TopRight,
    Left, Center, Right,
    BottomLeft, Bottom, BottomRight,
}

/// <summary>盤面を開いた操作の種類（表示位置の組を選ぶ）。</summary>
public enum PopupTriggerKind
{
    Keyboard,
    Mouse,
}

public enum PopupPosition
{
    Cursor,
    LastPosition,
    ScreenCenter,
    Fixed,
}

public sealed class TriggerSettings
{
    public static TriggerSettings Disabled() => new() { DoubleTap = new() { Enabled = false }, Mouse = [] };
    /// <summary>修飾キーの 2 回押し（SPEC §4.1。既定 ON・Ctrl）。</summary>
    public DoubleTapSetting DoubleTap { get; set; } = new();

    public HotkeySetting Hotkey { get; set; } = new();

    /// <summary>有効にするマウス操作（複数可）。既定は Alt+中クリックのみ（SPEC §4.2。CPFileLauncher の Ctrl+中クリックと重ねない）。</summary>
    public List<MouseTriggerSetting> Mouse { get; set; } =
    [
        new() { Button = MouseButtonKind.Middle, Modifiers = KeyModifiers.Ctrl | KeyModifiers.Alt },
    ];


}

/// <summary>修飾キーの 2 回押し（SPEC §4.1）。</summary>
public sealed class DoubleTapSetting
{
    public bool Enabled { get; set; } = true;
    public DoubleTapModifier Modifier { get; set; } = DoubleTapModifier.Ctrl;

    /// <summary>1 回目を離してから 2 回目を押すまでの上限 ms（200〜500、10 刻み）。</summary>
    public int IntervalMs { get; set; } = 300;
}

/// <summary>2 回押しに使う修飾キー（1 つだけ。左右は区別しない）。</summary>
public enum DoubleTapModifier
{
    Ctrl,
    Alt,
    Shift,
    Meta,
}

/// <summary>グローバルホットキー（SPEC §4.3）。既定 OFF・Ctrl+Alt+V（macOS は ⌃⌥V。Ctrl=⌃, Alt=⌥, Meta=Win/⌘）。</summary>
public sealed class HotkeySetting
{
    public bool Enabled { get; set; }
    public KeyModifiers Modifiers { get; set; } = KeyModifiers.Ctrl | KeyModifiers.Alt;

    /// <summary>キー名（"Space", "A", "F1" など）。</summary>
    public string Key { get; set; } = "M";
}

public sealed class MouseTriggerSetting
{
    public bool Enabled { get; set; } = true;
    public MouseButtonKind Button { get; set; } = MouseButtonKind.Middle;
    public KeyModifiers Modifiers { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Enum.IsDefined(Button)
        && (Modifiers & ~(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta)) == 0
        && (Button is not (MouseButtonKind.Left or MouseButtonKind.Right) || Modifiers != KeyModifiers.None);
}

public enum MouseButtonKind
{
    Left,
    Middle,
    X1,
    X2,
    Right,
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8,
}

/// <summary>盤面の操作（SPEC §3.2 / §3.4 / §6.1）。</summary>
public sealed class DataSettings
{
    /// <summary>settings.json の世代バックアップ数（SPEC §6.4、既定 10）。</summary>
    public int BackupGenerations { get; set; } = 10;
}

public sealed class AdvancedSettings
{
    public bool Logging { get; set; }
    // hardwareAcceleration は 2026-10-07 に廃止（描画方式はコードで固定。SPEC §10.7）。残ったキーは読み込みで無視される
}
