using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;

namespace JellyPrecache.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool ShowInAdminMenu { get; set; } = true;

    // Below 90, else Jellyfin serves originals narrower than the target straight from disk
    public int Quality { get; set; } = 85;

    // Generates an item's images a few seconds after it is added or its images change
    public bool GenerateOnAdd { get; set; }

    public ImageTypeSetting[] ImageTypes { get; set; } = Array.Empty<ImageTypeSetting>();
}

public class ImageTypeSetting
{
    public ImageType Type { get; set; }

    public bool Enabled { get; set; } = true;

    // Comma-separated; empty = defaults
    public string Widths { get; set; } = string.Empty;

    // Formats pre-generated: WebP for browsers and most apps, Fallback (JPG, PNG if transparent) for clients without WebP
    public bool WebP { get; set; } = true;

    public bool Fallback { get; set; }

    // Read-only: sent to the config page as placeholder, never persisted
    public string DefaultWidths => string.Join(", ", ImageSizing.DefaultWidths(Type));
}
