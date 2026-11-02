#if CPTOOLS_CLIPBOARD
namespace CPClipboard.Core.Storage;
#elif CPTOOLS_MONITOR
namespace CPSystemMonitor.Core.Storage;
#elif CPTOOLS_LAUNCHER
namespace FileLauncher.Core.Storage;
#else
#error A CPTools product must be selected.
#endif
/// <summary>
/// 配布物に同梱した文書（THIRD-PARTY-NOTICES.txt など）の場所（SPEC §6.3「について」/ §10.4「ライセンス文の同梱」）。
/// Windows は exe の隣、macOS は .app の Contents/Resources/（= 実行ファイルのフォルダ Contents/MacOS/ の 1 つ上の Resources/）。
/// OS で分岐せず、探す順序で両方に対応する。
/// </summary>
public static class BundledFile
{
    /// <summary>実行ファイルのフォルダ → その 1 つ上の Resources/ の順に探す。どちらにも無ければ null。</summary>
    public static string? Find(string baseDirectory, string fileName)
    {
        string[] candidates =
        [
            Path.Combine(baseDirectory, fileName),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "Resources", fileName)),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
