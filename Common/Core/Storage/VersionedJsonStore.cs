#if CPTOOLS_CLIPBOARD
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CPClipboard.Core.Storage;
#elif CPTOOLS_MONITOR
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CPSystemMonitor.Core.Storage;
#elif CPTOOLS_LAUNCHER
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FileLauncher.Core.Storage;
#else
#error A CPTools product must be selected.
#endif
public enum LoadStatus
{
    /// <summary>正常に読み込めた。</summary>
    Loaded,

    /// <summary>ファイルが無かったので既定値を返した（まだ書き込んでいない）。</summary>
    CreatedDefault,

    /// <summary>本体が壊れていたのでバックアップから復旧した。壊れた本体は *.broken-* として残してある。</summary>
    RecoveredFromBackup,

    /// <summary>本体が壊れていて復旧できるバックアップも無かったので既定値を返した。壊れた本体は残してある。</summary>
    CorruptReplacedWithDefault,

    /// <summary>新しいバージョンのアプリで保存されたデータ。上書きで壊さないよう読み取り専用にして既定値を返した。</summary>
    NewerSchemaReadOnly,
}

public sealed record LoadResult<T>(T Value, LoadStatus Status, LoadDetail? Detail = null);

/// <summary>
/// 読み込みで起きたことの詳細（文言にはしない。通知の文は App が言語に合わせて作る。SPEC §10.3）。
/// BackupFile = 復旧に使ったバックアップ、BrokenFile = 退避した壊れたファイル（どちらもファイル名だけ）、Error = 例外のメッセージ、
/// FileVersion / SupportedVersion = 新しい版のデータだったときの schemaVersion。
/// </summary>
public sealed record LoadDetail(string? BackupFile = null, string? BrokenFile = null, string? Error = null, int? FileVersion = null, int? SupportedVersion = null)
{
    public override string ToString() =>
        string.Join(" ", new[] { BackupFile is null ? null : $"backup={BackupFile}", BrokenFile is null ? null : $"broken={BrokenFile}", Error,
            FileVersion is null ? null : $"schemaVersion {FileVersion} > {SupportedVersion}" }.Where(s => s is not null));
}

/// <summary>
/// schemaVersion 付き JSON ファイルの読み書き（SPEC §9.3, §11 データ保全）。
/// - 保存はアトミック（同じフォルダの .tmp に書いて rename）。
/// - backupDirectory を指定すると、保存のたびに直前の本体を世代バックアップする。
/// - 読み込み時に schemaVersion を見てマイグレーション。現行より新しければ読み取り専用。
/// - 壊れていたら本体を退避して、新しいバックアップから順に復旧を試みる。
/// </summary>
public sealed class VersionedJsonStore<T> where T : class
{
    private readonly string _filePath;
    private readonly string? _backupDirectory;
    private readonly Func<int> _backupGenerations;
    private readonly int _currentSchemaVersion;
    private readonly Func<T> _createDefault;
    private readonly Func<JsonObject, int, JsonObject>? _migrateOneStep;
    private readonly Action<T>? _normalize;
    private readonly TimeProvider _time;
    private readonly TimeSpan _backupMinInterval;
    private DateTimeOffset? _lastBackupAt;

    /// <param name="backupMinInterval">
    /// 世代バックアップの最短間隔（既定 0 = 保存のたび）。起動後の最初の保存は必ずバックアップする。
    /// 盤面の編集はこまめに保存されるので、世代がすぐ入れ替わらないよう間隔を空ける（SPEC §9.3）。
    /// </param>
    /// <param name="migrateOneStep">(旧データ, その版数) → 1 つ新しい版のデータ。現行版未満のデータにだけ呼ばれる。</param>
    public VersionedJsonStore(
        string filePath,
        int currentSchemaVersion,
        Func<T> createDefault,
        string? backupDirectory = null,
        Func<int>? backupGenerations = null,
        Func<JsonObject, int, JsonObject>? migrateOneStep = null,
        Action<T>? normalize = null,
        TimeProvider? time = null,
        TimeSpan? backupMinInterval = null)
    {
        _backupMinInterval = backupMinInterval ?? TimeSpan.Zero;
        _filePath = filePath;
        _currentSchemaVersion = currentSchemaVersion;
        _createDefault = createDefault;
        _backupDirectory = backupDirectory;
        _backupGenerations = backupGenerations ?? (() => 10);
        _migrateOneStep = migrateOneStep;
        _normalize = normalize;
        _time = time ?? TimeProvider.System;
    }

    public string FilePath => _filePath;

    /// <summary>true の間は Save が例外になる（新しい版のデータを上書きしないため）。</summary>
    public bool IsReadOnly { get; private set; }

    private string BaseName => Path.GetFileNameWithoutExtension(_filePath);

    public LoadResult<T> Load()
    {
        IsReadOnly = false;
        if (!File.Exists(_filePath))
            return new LoadResult<T>(CreateDefault(), LoadStatus.CreatedDefault);

        try
        {
            var outcome = TryRead(_filePath);
            if (outcome.NewerVersion is { } newer)
            {
                IsReadOnly = true;
                return new LoadResult<T>(CreateDefault(), LoadStatus.NewerSchemaReadOnly,
                    new LoadDetail(FileVersion: newer, SupportedVersion: _currentSchemaVersion));
            }
            return new LoadResult<T>(outcome.Value!, LoadStatus.Loaded);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            string broken = PreserveBroken();
            foreach (var backup in BackupsNewestFirst())
            {
                try
                {
                    var outcome = TryRead(backup);
                    if (outcome.Value is null) continue; // バックアップが新しい版ということは通常ないが念のため飛ばす
                    // 復旧した内容を本体に書き戻す（次回以降は通常どおり読める）
                    WriteAtomic(outcome.Value);
                    return new LoadResult<T>(outcome.Value, LoadStatus.RecoveredFromBackup,
                        new LoadDetail(BackupFile: Path.GetFileName(backup), BrokenFile: Path.GetFileName(broken), Error: ex.Message));
                }
                catch (Exception bex) when (bex is JsonException or InvalidDataException or NotSupportedException)
                {
                    // 次の世代を試す
                }
            }
            return new LoadResult<T>(CreateDefault(), LoadStatus.CorruptReplacedWithDefault,
                new LoadDetail(BrokenFile: Path.GetFileName(broken), Error: ex.Message));
        }
    }

    public void Save(T value)
    {
        if (IsReadOnly)
            throw new InvalidOperationException($"{_filePath} holds newer-version data and cannot be saved");

        BackupCurrent();
        WriteAtomic(value);
    }

    // ---------------- 内部 ----------------

    private T CreateDefault()
    {
        var value = _createDefault();
        _normalize?.Invoke(value);
        return value;
    }

    private (T? Value, int? NewerVersion) TryRead(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        if (node is not JsonObject obj) throw new InvalidDataException("the root is not a JSON object");

        int version;
        try { version = obj["schemaVersion"]?.GetValue<int>() ?? 1; }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("schemaVersion is not a number", ex);
        }
        if (version > _currentSchemaVersion) return (null, version);

        while (version < _currentSchemaVersion)
        {
            if (_migrateOneStep is null) throw new InvalidDataException($"no migration defined from schemaVersion {version}");
            obj = _migrateOneStep(obj, version);
            version++;
            obj["schemaVersion"] = version;
        }

        var value = obj.Deserialize<T>(JsonDefaults.Options) ?? throw new InvalidDataException("null");
        _normalize?.Invoke(value);
        return (value, null);
    }

    private void WriteAtomic(T value)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_filePath))!;
        Directory.CreateDirectory(dir);
        string tmp = _filePath + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(fs, value, JsonDefaults.Options);
            fs.Flush(flushToDisk: true);
        }
        // 同一ボリューム内の rename は置き換えがアトミック（Win: MoveFileEx REPLACE_EXISTING / Unix: rename(2)）
        File.Move(tmp, _filePath, overwrite: true);
    }

    private void BackupCurrent()
    {
        int generations = _backupGenerations();
        if (_backupDirectory is null || generations <= 0 || !File.Exists(_filePath)) return;
        var now = _time.GetUtcNow();
        if (_lastBackupAt is { } last && now - last < _backupMinInterval) return;
        _lastBackupAt = now;

        Directory.CreateDirectory(_backupDirectory);
        // 名前順 = 時刻順を保つため、同名が既にあれば接尾辞ではなく時刻を 1ms ずつ進める
        var at = _time.GetLocalNow();
        string dest;
        while (File.Exists(dest = Path.Combine(_backupDirectory, $"{BaseName}-{at:yyyyMMdd-HHmmss-fff}.json")))
            at = at.AddMilliseconds(1);
        File.Copy(_filePath, dest);

        foreach (var old in BackupsNewestFirst().Skip(generations))
        {
            try { File.Delete(old); } catch (IOException) { /* 次回の保存で再試行 */ }
        }
    }

    /// <summary>ファイル名にタイムスタンプが入っているので名前の降順 = 新しい順。</summary>
    private IEnumerable<string> BackupsNewestFirst()
    {
        if (_backupDirectory is null || !Directory.Exists(_backupDirectory)) return [];
        return Directory.GetFiles(_backupDirectory, $"{BaseName}-*.json")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    private string PreserveBroken()
    {
        string stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss");
        string dest = $"{_filePath}.broken-{stamp}";
        for (int i = 1; File.Exists(dest); i++) dest = $"{_filePath}.broken-{stamp}-{i}";
        File.Copy(_filePath, dest);
        return dest;
    }
}
