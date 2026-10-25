using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;

namespace JellyPrecache.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool ShowInAdminMenu { get; set; } = true;

    // Below 90, else Jellyfin serves originals narrower than the target straight from disk
    public int Quality { get; set; } = 85;

    public ImageTypeSetting[] ImageTypes { get; set; } = Array.Empty<ImageTypeSetting>();
}

public class ImageTypeSetting
{
    public ImageType Type { get; set; }

    public bool Enabled { get; set; } = true;

    // Comma-separated; empty = defaults
    public string Widths { get; set; } = string.Empty;

    // Read-only: sent to the config page as placeholder, never persisted
    public string DefaultWidths => string.Join(", ", ImageSizing.DefaultWidths(Type));
}
