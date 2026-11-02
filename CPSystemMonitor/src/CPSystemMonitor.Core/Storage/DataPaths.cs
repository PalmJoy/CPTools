namespace CPSystemMonitor.Core.Storage;

/// <summary>データフォルダ内の各ファイルの場所（SPEC §9.1, §9.2）。</summary>
public sealed record DataPaths(string Root, bool IsPortable)
{
    public const string AppFolderName = "CPSystemMonitor";

    /// <summary>実行ファイル名（拡張子なし）。portable.flag の探索で自分の実行ファイルかを見分ける。</summary>
    public const string ExecutableName = "CPSystemMonitor";

    public const string PortableFlagFile = "portable.flag";

    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string BackupDirectory => Path.Combine(Root, "backup");
    public string LogsDirectory => Path.Combine(Root, "logs");
    /// <summary>設定画面で選んだ背景画像のコピー（SPEC §3.7、§9.2）。使うときに作る。</summary>
    public string BackgroundDirectory => Path.Combine(Root, BackgroundImageStore.Folder);

    /// <summary>
    /// portable.flag を探すディレクトリ（SPEC §9.1）: 実行ファイルが CPSystemMonitor(.exe) ならそのディレクトリ
    /// （.app では Contents/MacOS、Windows の単一ファイルでは exe の隣）、それ以外（dotnet CPSystemMonitor.dll など）は baseDirectory。
    /// </summary>
    public static string ExecutableDirectory(string? processPath, string baseDirectory)
    {
        string? name = processPath is null ? null : Path.GetFileNameWithoutExtension(processPath);
        return name == ExecutableName && Path.GetDirectoryName(processPath) is { Length: > 0 } dir ? dir : baseDirectory;
    }

    /// <summary>
    /// 実行ファイルと同じフォルダに portable.flag があればその隣の data/、
    /// なければ OS のユーザーデータ領域（Win: %APPDATA%、Mac: ~/Library/Application Support）。
    /// </summary>
    /// <param name="userDataRoot">テスト用: ユーザーデータ領域の代わり（本物のデータフォルダに触れないように）。</param>
    public static DataPaths Resolve(string executableDirectory, string? userDataRoot = null)
    {
        if (File.Exists(Path.Combine(executableDirectory, PortableFlagFile)))
            return new DataPaths(Path.Combine(executableDirectory, "data"), IsPortable: true);

        string root = userDataRoot ?? UserDataRoot();
        return new DataPaths(Path.Combine(root, AppFolderName), IsPortable: false);
    }

    private static string UserDataRoot()
    {
        // .NET の SpecialFolder.ApplicationData は macOS だと ~/.config になるので明示する
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BackupDirectory);
    }
}
