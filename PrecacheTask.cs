using System.Diagnostics;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyPrecache;

// Fills Jellyfin's resize cache with every configured width (WebP) of every enabled image type.
public class PrecacheTask : IScheduledTask
{
    private static readonly ImageFormat[] OutputFormats = { ImageFormat.Webp };

    private readonly ILibraryManager _libraryManager;
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<PrecacheTask> _logger;

    public PrecacheTask(ILibraryManager libraryManager, IImageProcessor imageProcessor, ILogger<PrecacheTask> logger)
    {
        _libraryManager = libraryManager;
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    public string Name => "Pre-generate image sizes";

    public string Key => "JellyPrecachePregenerate";

    public string Description => "Resizes library images to the widths configured in JellyPrecache, so browsing never reads originals.";

    public string Category => "JellyPrecache";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance is not { } plugin)
        {
            return;
        }

        var config = plugin.Configuration;
        var types = ImageSizing.SupportedTypes
            .Select(t => (Type: t, Widths: ImageSizing.WidthsFor(config, t)))
            .Where(t => t.Widths is not null)
            .ToArray();
        if (types.Length == 0)
        {
            return;
        }

        var ids = _libraryManager.GetItemIds(new InternalItemsQuery { ImageTypes = types.Select(t => t.Type).ToArray() });
        var stopwatch = Stopwatch.StartNew();
        var processed = 0;
        var failed = 0;

        for (var i = 0; i < ids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(100.0 * i / ids.Count);

            if (_libraryManager.GetItemById(ids[i]) is not { } item)
            {
                continue;
            }

            foreach (var (type, widths) in types)
            {
                var images = item.GetImages(type).ToArray();
                for (var index = 0; index < images.Length; index++)
                {
                    try
                    {
                        await ProcessImage(item, images[index], index, widths!, config.Quality).ConfigureAwait(false);
                        processed++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failed++;
                        _logger.LogDebug(ex, "JellyPrecache: failed {Type}[{Index}] of {Item}", type, index, item.Name);
                    }
                }
            }
        }

        progress.Report(100);
        _logger.LogInformation(
            "JellyPrecache: {Images} images over {Items} items in {Elapsed}, {Failed} failed",
            processed, ids.Count, stopwatch.Elapsed, failed);
    }

    // Same steps as ImageController: remote images are made local first, which changes the path in the cache key
    private async Task ProcessImage(BaseItem item, ItemImageInfo image, int index, int[] widths, int quality)
    {
        if (!image.IsLocalFile)
        {
            image = await _libraryManager.ConvertImageToLocal(item, image, index).ConfigureAwait(false);
        }

        foreach (var width in widths)
        {
            await _imageProcessor.ProcessImage(ImageSizing.Options(item, image, index, width, quality, OutputFormats)).ConfigureAwait(false);
        }
    }
}
