#if CPTOOLS_CLIPBOARD
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif
/// <summary>Validate encoded size and dimensions before allocating decoded pixels.</summary>
internal static class SafeBitmapLoader
{
    internal const long MaxFileBytes = 32 * 1024 * 1024;
    internal const long MaxPixels = 40_000_000;
    internal const int MaxDimension = 16_384;

    internal static Bitmap Load(string path, int maxEdge)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEdge);
        using var file = File.OpenRead(path);
        if (file.Length > MaxFileBytes) throw new InvalidDataException("Image file is too large.");
        int width, height;
        using (var managed = new SKManagedStream(file, false))
        using (var codec = SKCodec.Create(managed) ?? throw new InvalidDataException("Unsupported image."))
        {
            width = codec.Info.Width;
            height = codec.Info.Height;
            if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
                throw new InvalidDataException("Image dimensions exceed the safety limit.");
        }
        file.Position = 0;
        if (Math.Max(width, height) <= maxEdge) return new Bitmap(file);
        return width >= height ? Bitmap.DecodeToWidth(file, maxEdge) : Bitmap.DecodeToHeight(file, maxEdge);
    }
}
