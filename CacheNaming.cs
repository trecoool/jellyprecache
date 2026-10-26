using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Drawing;

namespace JellyPrecache;

// Copy of ImageProcessor.GetCacheFilePath for the options ImageSizing produces (MaxWidth + Quality, no overlays).
// Prune checks a sample against the paths Jellyfin itself returns before trusting it.
public static class CacheNaming
{
    // ImageProcessor's cache version, bumped by Jellyfin when the naming changes
    private const char Version = '4';

    private static readonly HashSet<string> TransparentExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".webp", ".gif", ".svg" };

    // Jellyfin serves GIF originals as-is, never cached
    public static bool IsCacheable(ItemImageInfo image)
    {
        return image.IsLocalFile && !string.Equals(Path.GetExtension(image.Path), ".gif", StringComparison.OrdinalIgnoreCase);
    }

    // Format ImageProcessor picks for a client without WebP
    public static ImageFormat FallbackFormat(string originalPath)
    {
        return TransparentExtensions.Contains(Path.GetExtension(originalPath)) ? ImageFormat.Png : ImageFormat.Jpg;
    }

    public static ImageFormat FormatFor(ImageFormat[] formatSet, string originalPath)
    {
        return formatSet.Contains(ImageFormat.Webp) ? ImageFormat.Webp : FallbackFormat(originalPath);
    }

    public static string FilePath(string cacheRoot, ItemImageInfo image, int maxWidth, int quality, ImageFormat format)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{image.Path},quality={quality},datemodified={image.DateModified.Ticks},f={format},maxwidth={maxWidth},v={Version}");
        var name = new Guid(MD5.HashData(Encoding.Unicode.GetBytes(key))).ToString() + format.GetExtension();
        return Path.Join(cacheRoot, name[..1], name);
    }
}
