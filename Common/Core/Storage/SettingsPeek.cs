#if CPTOOLS_CLIPBOARD
using System.Text.Json;

namespace CPClipboard.Core.Storage;
#elif CPTOOLS_MONITOR
using System.Text.Json;

namespace CPSystemMonitor.Core.Storage;
#elif CPTOOLS_LAUNCHER
using System.Text.Json;

namespace FileLauncher.Core.Storage;
#else
#error A CPTools product must be selected.
#endif
/// <summary>Avalonia を起動する前に settings.json から 1 キーだけ読む（表示言語など、起動時にしか変えられない設定）。</summary>
public static class SettingsPeek
{
    /// <summary>general.language（"system" / "ja" / "en"）。ファイルが無い・壊れている・キーが無い・知らない値なら "system"。</summary>
    public static string Language(string settingsFile)
    {
        try
        {
            using var stream = File.OpenRead(settingsFile);
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("general", out var general)
                && general.ValueKind == JsonValueKind.Object
                && general.TryGetProperty("language", out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is "ja" or "en")
                return value.GetString()!;
            return "system";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return "system";
        }
    }
}
