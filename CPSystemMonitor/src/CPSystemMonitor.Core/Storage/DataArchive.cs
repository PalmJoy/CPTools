using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CPSystemMonitor.Core.Storage;

/// <summary>インポートできない理由（文言にはしない。App が言語に合わせて文にする）。</summary>
public enum ArchiveError
{
    /// <summary>zip として読めない。</summary>
    NotZip,
    /// <summary>Archive size, expanded size or entry count exceeds a safety limit.</summary>
    TooLarge,
    /// <summary>settings.json が入っていない。</summary>
    NoKnownEntries,
    /// <summary>エントリが JSON のオブジェクトでない（Entry = 名前）。</summary>
    EntryNotObject,
    /// <summary>エントリの JSON が壊れている（Entry = 名前、Detail = パーサのメッセージ）。</summary>
    EntryCorrupt,
}

/// <summary>インポートできない zip。Message はログ用の英語。</summary>
public sealed class ArchiveException(ArchiveError kind, string? entry = null, string? detail = null)
    : Exception($"{kind}{(entry is null ? "" : $" {entry}")}{(detail is null ? "" : $": {detail}")}")
{
    public ArchiveError Kind { get; } = kind;
    public string? Entry { get; } = entry;
    public string? Detail { get; } = detail;
}

/// <summary>zip から読み出した settings.json の中身（無いほうは null）と、background/ 配下のファイル（キーは "background/&lt;名前&gt;"）。</summary>
public sealed record ArchiveContents(string? Settings, IReadOnlyDictionary<string, byte[]>? Files = null);

/// <summary>
/// 設定と盤面のエクスポート / インポート（spec/SETTINGS.md データタブ）。
/// zip には settings.json と、背景画像が background/ のコピーならその画像を入れる。
/// </summary>
public static class DataArchive
{
    public const string SettingsEntry = "settings.json";
    public const int MaxEntries = 128;
    public const int MaxJsonBytes = 4 * 1024 * 1024;
    public const int MaxFileBytes = 32 * 1024 * 1024;
    public const int MaxTotalBytes = 64 * 1024 * 1024;
    public const int MaxArchiveBytes = 64 * 1024 * 1024;

    public static string DefaultFileName(DateTime localNow) => $"CPSystemMonitor-backup-{localNow:yyyyMMdd-HHmmss}.zip";

    /// <summary>今ディスクにある 2 ファイルを zip に書く（呼ぶ前に保存待ちを書き出しておくこと）。</summary>
    public static void Export(DataPaths paths, Stream destination)
    {
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (entry, file) in new[] { (SettingsEntry, paths.SettingsFile) })
        {
            if (File.Exists(file)) zip.CreateEntryFromFile(file, entry, CompressionLevel.Optimal);
        }
        if (ManagedBackground(paths) is { } value && BackgroundImageStore.Resolve(paths, value) is { } image && File.Exists(image))
            zip.CreateEntryFromFile(image, BackgroundImageStore.Folder + "/" + Path.GetFileName(image), CompressionLevel.NoCompression); // 画像は圧縮済み
    }

    /// <summary>settings.json の appearance.background.image が background/ のコピーならその値。</summary>
    private static string? ManagedBackground(DataPaths paths)
    {
        try
        {
            if (!File.Exists(paths.SettingsFile)) return null;
            var node = JsonNode.Parse(File.ReadAllText(paths.SettingsFile), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var value = node?["appearance"]?["background"]?["image"]?.GetValue<string>();
            return BackgroundImageStore.IsManaged(value) ? value : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>zip を読み、中身が JSON のオブジェクトであることだけ確かめる（版の扱いは読み込み時の通常の規則に任せる）。</summary>
    /// <exception cref="ArchiveException">FileLauncher のエクスポートでない・壊れている。</exception>
    public static ArchiveContents Read(Stream source)
    {
        try { return ReadCore(source); }
        catch (InvalidDataException) { throw new ArchiveException(ArchiveError.NotZip); }
    }

    private static ArchiveContents ReadCore(Stream source)
    {
        // Bound non-seekable streams too, before ZipArchive buffers the input.
        using var snapshot = new MemoryStream();
        CopyLimited(source, snapshot, MaxArchiveBytes);
        ValidateDirectory(snapshot);
        snapshot.Position = 0;
        ZipArchive zip;
        try { zip = new ZipArchive(snapshot, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new ArchiveException(ArchiveError.NotZip); }
        using (zip)
        {
            if (zip.Entries.Count > MaxEntries) throw new ArchiveException(ArchiveError.TooLarge);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.Length > MaxFileBytes || entry.Length > MaxTotalBytes - total)
                    throw new ArchiveException(ArchiveError.TooLarge, entry.Name);
                total += entry.Length;
            }
            int remaining = MaxTotalBytes;
            string? settings = ReadEntry(zip, SettingsEntry, ref remaining);
            if (settings is null)
                throw new ArchiveException(ArchiveError.NoKnownEntries);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                // background/ 直下のファイルだけ（".." や深い階層は捨てる）
                var parts = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                int at = Array.IndexOf(parts, BackgroundImageStore.Folder);
                if (at < 0 || at != parts.Length - 2 || parts.Contains("..") || entry.Length == 0) continue;
                using var ms = new MemoryStream();
                using (var es = entry.Open()) CopyLimited(es, ms, Math.Min(MaxFileBytes, remaining));
                remaining -= checked((int)ms.Length);
                files[BackgroundImageStore.Folder + "/" + parts[^1]] = ms.ToArray();
            }
            return new ArchiveContents(settings, files);
        }
    }

    private static string? ReadEntry(ZipArchive zip, string name, ref int remaining)
    {
        // フォルダごと圧縮された zip でも見つけられるよう、ファイル名で探す
        var entry = zip.GetEntry(name) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        if (entry.Length > MaxJsonBytes) throw new ArchiveException(ArchiveError.TooLarge, name);
        using var bytes = new MemoryStream();
        using (var stream = entry.Open()) CopyLimited(stream, bytes, Math.Min(MaxJsonBytes, remaining));
        remaining -= checked((int)bytes.Length);
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, Encoding.UTF8);
        string text = reader.ReadToEnd();
        try
        {
            if (JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject)
                throw new ArchiveException(ArchiveError.EntryNotObject, name);
        }
        catch (JsonException ex)
        {
            throw new ArchiveException(ArchiveError.EntryCorrupt, name, ex.Message);
        }
        return text;
    }

    // Inspect the bounded input before ZipArchive allocates an object for every entry.
    // These small backups never need multi-disk or ZIP64 archives.
    private static void ValidateDirectory(MemoryStream source)
    {
        ReadOnlySpan<byte> bytes = source.GetBuffer().AsSpan(0, checked((int)source.Length));
        for (int offset = bytes.Length - 22; offset >= Math.Max(0, bytes.Length - 22 - ushort.MaxValue); offset--)
        {
            var record = bytes[offset..];
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(record) != 0x06054b50) continue;
            if (offset + 22 + System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record[20..]) != bytes.Length)
                throw new ArchiveException(ArchiveError.NotZip);
            ushort count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            uint size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
            uint start = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            if (count > MaxEntries || size > 256 * 1024) throw new ArchiveException(ArchiveError.TooLarge);
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) != 0
                || System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record[8..]) != count
                || (ulong)start + size > (ulong)offset)
                throw new ArchiveException(ArchiveError.NotZip);
            int at = checked((int)start), end = checked((int)(start + size)), found = 0;
            while (at < end)
            {
                var header = bytes[at..end];
                if (header.Length < 46 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50)
                    throw new ArchiveException(ArchiveError.NotZip);
                int length = 46 + System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[28..])
                    + System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[30..])
                    + System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
                if (length > header.Length) throw new ArchiveException(ArchiveError.NotZip);
                if (++found > MaxEntries) throw new ArchiveException(ArchiveError.TooLarge);
                at += length;
            }
            if (found != count) throw new ArchiveException(ArchiveError.NotZip);
            return;
        }
        throw new ArchiveException(ArchiveError.NotZip);
    }

    private static void CopyLimited(Stream source, Stream destination, int limit)
    {
        byte[] buffer = new byte[81920];
        int remaining = limit;
        int read;
        while ((read = source.Read(buffer, 0, Math.Min(buffer.Length, remaining + 1))) > 0)
        {
            if (read > remaining) throw new ArchiveException(ArchiveError.TooLarge);
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

}
