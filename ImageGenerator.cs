using System.Diagnostics;
using System.Globalization;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace JellyPrecache;

// Generation pass shared by all tasks and generate-on-add: every configured width x format of every image,
// recording the cache file Jellyfin returns for each, so Prune knows exactly which files are needed.
public class ImageGenerator
{
    private readonly ILibraryManager _libraryManager;
    private readonly IImageProcessor _imageProcessor;
    private readonly ResizedCache _cache;
    private readonly ILogger<ImageGenerator> _logger;

    public ImageGenerator(ILibraryManager libraryManager, IImageProcessor imageProcessor, ResizedCache cache, ILogger<ImageGenerator> logger)
    {
        _libraryManager = libraryManager;
        _imageProcessor = imageProcessor;
        _cache = cache;
        _logger = logger;
    }

    // One library-wide task at a time (pre-generate, prune, rebuild): they would race on the same files
    public SemaphoreSlim TaskLock { get; } = new(1, 1);

    public IReadOnlyList<Guid> ItemIds(TypePlan[] plan)
    {
        return plan.Length == 0
            ? Array.Empty<Guid>()
            : _libraryManager.GetItemIds(new InternalItemsQuery { ImageTypes = plan.Select(p => p.Type).ToArray() });
    }

    public async Task<GenerationRun> GenerateAll(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var run = new GenerationRun();
        if (Plugin.Instance is not { } plugin)
        {
            return run;
        }

        var config = plugin.Configuration;
        var plan = ImageSizing.Plan(config);
        var ids = ItemIds(plan);
        for (var i = 0; i < ids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(100.0 * i / ids.Count);

            if (_libraryManager.GetItemById(ids[i]) is { } item)
            {
                await GenerateItem(item, plan, config.Quality, run).ConfigureAwait(false);
            }
        }

        progress.Report(100);
        run.Stop();
        _logger.LogInformation("JellyPrecache: {Summary}", run.Summary());
        return run;
    }

    public async Task GenerateItem(BaseItem item, TypePlan[] plan, int quality, GenerationRun run)
    {
        run.Items++;
        foreach (var typePlan in plan)
        {
            var images = item.GetImages(typePlan.Type).ToArray();
            for (var index = 0; index < images.Length; index++)
            {
                try
                {
                    await ProcessImage(item, images[index], index, typePlan, quality, run).ConfigureAwait(false);
                    run.Images++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    run.Failed++;
                    _logger.LogDebug(ex, "JellyPrecache: failed {Type}[{Index}] of {Item}", typePlan.Type, index, item.Name);
                }
            }
        }
    }

    // Same steps as ImageController: remote images are made local first, which changes the path in the cache key
    private async Task ProcessImage(BaseItem item, ItemImageInfo image, int index, TypePlan typePlan, int quality, GenerationRun run)
    {
        if (!image.IsLocalFile)
        {
            image = await _libraryManager.ConvertImageToLocal(item, image, index).ConfigureAwait(false);
        }

        foreach (var width in typePlan.Widths)
        {
            foreach (var formats in typePlan.FormatSets)
            {
                var (path, _, modified) = await _imageProcessor
                    .ProcessImage(ImageSizing.Options(item, image, index, width, quality, formats))
                    .ConfigureAwait(false);

                // Jellyfin answers with the original instead when it can't or needn't resize: nothing cached then
                var fullPath = Path.GetFullPath(path);
                if (_cache.Contains(fullPath) && run.Files.Add(fullPath) && modified >= run.StartedUtc)
                {
                    run.Created++;
                }
            }
        }
    }
}

public sealed class GenerationRun
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public DateTime StartedUtc { get; } = DateTime.UtcNow;

    // Cache files Jellyfin returned during this run (existing or just created)
    public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

    public int Items { get; set; }

    public int Images { get; set; }

    public int Created { get; set; }

    public int Failed { get; set; }

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    // Most files had to be encoded: its duration is what a rebuild costs
    public bool WasFull => Files.Count > 0 && Created * 2 > Files.Count;

    public void Stop() => _stopwatch.Stop();

    public string Summary() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Images:N0} images over {Items:N0} items: {Files.Count:N0} cached files, {Created:N0} newly generated, {Failed:N0} failed");
}
