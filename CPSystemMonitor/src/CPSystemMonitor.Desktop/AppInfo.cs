namespace CPSystemMonitor.App;

/// <summary>製品名（SPEC §1.3 / §13.2 T1。2026-10-04 確定）。画面・タイトル・ツールチップの製品名はここから取る。</summary>
internal static class AppInfo
{
    public const string ProductName = "CPSystemMonitor";
    public const string Subtitle = "CyberPunk System Monitor"; // i18n:ignore 製品名の副題（旧名ではない）
    public const string TitlePrefix = ProductName + " — ";

    // ライセンス（SPEC §6.3「について」/ §13.2 T2）
    public const string Copyright = "Copyright © 2026 PalmJoy"; // i18n:ignore 著作権表示（訳さない）
    public const string LicenseId = "GPL-3.0-only";
    public const string SourceUrl = "https://github.com/PalmJoy/CPFileLauncher";
    public const string ThirdPartyNoticesFileName = "THIRD-PARTY-NOTICES.txt";
    public const string ThirdPartyNoticesUrl = SourceUrl + "/blob/main/CPSystemMonitor/" + ThirdPartyNoticesFileName; // 同梱が見つからないとき
}
