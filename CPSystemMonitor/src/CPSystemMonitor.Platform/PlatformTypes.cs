namespace CPSystemMonitor.Platform;

// 画面座標 ScreenPoint / ScreenRect は Core（CPSystemMonitor.Core.Input）にある。

public sealed record LaunchResult(bool Success, LaunchFailure Reason = LaunchFailure.None, string? Detail = null)
{
    public static LaunchResult Ok { get; } = new(true);
    public static LaunchResult Fail(LaunchFailure reason, string? detail = null) => new(false, reason, detail);

    /// <summary>ログ用の短い説明（英語）。ユーザー向けの文は App が Reason から作る（SPEC §10.3）。</summary>
    public string? Error => Success ? null : Detail is null ? Reason.ToString() : $"{Reason}: {Detail}";
}

/// <summary>起動に失敗した理由。文言にはしない（UI の言語は App が決める）。</summary>
public enum LaunchFailure
{
    None,
    /// <summary>対象が見つからない（Detail = パス）。</summary>
    NotFound,
    /// <summary>ユーザーが取り消した（UAC の拒否など）。</summary>
    Cancelled,
    /// <summary>起動の仕組み自体を動かせない（Mac の open コマンドなど）。</summary>
    LauncherUnavailable,
    /// <summary>OS が返したエラー（Detail = OS のメッセージ）。</summary>
    OsError,
}

/// <summary>macOS の権限状態（SPEC §4.3）。Windows は常に Granted。</summary>
public enum PermissionState
{
    Granted,
    Denied,
    Unknown,
}
