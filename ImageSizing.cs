using System.Globalization;
using JellyPrecache.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;

namespace JellyPrecache;

// Single source of the width/quality options used by both the request rewrite and the pre-generation task,
// so both land on the same Jellyfin resize cache key.
public static class ImageSizing
{
    public static readonly string[] SizeKeys = { "maxWidth", "maxHeight", "width", "height", "fillWidth", "fillHeight", "quality" };

    // Types with a variable requested size. Others (Chapter = fixed 400, Art/Box/Menu/... unused by jellyfin-web) pass through.
    private static readonly Dictionary<ImageType, int[]> Defaults = new()
    {
        [ImageType.Primary] = new[] { 300, 600 },
        [ImageType.Backdrop] = new[] { 960, 1920 },
        [ImageType.Thumb] = new[] { 640 },
        [ImageType.Logo] = new[] { 800 },
        [ImageType.Banner] = new[] { 1000 },
        [ImageType.Disc] = new[] { 600 },
    };

    public static IEnumerable<ImageType> SupportedTypes => Defaults.Keys;

    public static int[] DefaultWidths(ImageType type) => Defaults.GetValueOrDefault(type, Array.Empty<int>());

    // One entry per supported type, keeping saved values and dropping unsupported ones
    public static ImageTypeSetting[] Normalize(ImageTypeSetting[]? saved)
    {
        return SupportedTypes
            .Select(t => saved?.FirstOrDefault(s => s.Type == t) ?? new ImageTypeSetting { Type = t })
            .ToArray();
    }

    // Sorted widths for an enabled supported type, null when the type is passed through untouched
    public static int[]? WidthsFor(PluginConfiguration config, ImageType type)
    {
        var setting = config.ImageTypes.FirstOrDefault(s => s.Type == type);
        if (!Defaults.ContainsKey(type) || setting is { Enabled: false })
        {
            return null;
        }

        var parsed = (setting?.Widths ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => int.TryParse(w, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .Order()
            .ToArray();

        return parsed.Length > 0 ? parsed : DefaultWidths(type);
    }

    // Smallest configured width >= requested, else the largest
    public static int Snap(int[] widths, int requested)
    {
        foreach (var w in widths)
        {
            if (w >= requested)
            {
                return w;
            }
        }

        return widths[^1];
    }

    public static IEnumerable<KeyValuePair<string, string>> QueryParams(int width, int quality)
    {
        yield return new("maxWidth", width.ToString(CultureInfo.InvariantCulture));
        yield return new("quality", quality.ToString(CultureInfo.InvariantCulture));
    }

    // Mirrors what ImageController builds from QueryParams (MaxWidth + Quality only)
    public static ImageProcessingOptions Options(BaseItem item, ItemImageInfo image, int imageIndex, int width, int quality, IReadOnlyCollection<ImageFormat> formats)
    {
        return new ImageProcessingOptions
        {
            ItemId = item.Id,
            Item = item,
            Image = image,
            ImageIndex = imageIndex,
            MaxWidth = width,
            Quality = quality,
            SupportedOutputFormats = formats
        };
    }
}
