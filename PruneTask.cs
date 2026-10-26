using System.Diagnostics;
using System.Globalization;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyPrecache;

// Deletes every resized-cache file that isn't a configured width (WebP or fallback) of a current library image.
// Expected names are computed, after checking a sample against the paths Jellyfin returns. Stops untouched if over half the cache would go.
public class PruneTask : IScheduledTask
{
    public const string TaskKey = "JellyPrecachePrune";

    private const int SampleSize = 200;

    private readonly ImageGenerator _generator;
    private readonly ILibraryManager _libraryManager;
    private readonly IImageProcessor _imageProcessor;
    private readonly ResizedCache _cache;
    private readonly StatusStore _status;
    private readonly ILogger<PruneTask> _logger;

    public PruneTask(ImageGenerator generator, ILibraryManager libraryManager, IImageProcessor imageProcessor, ResizedCache cache, StatusStore status, ILogger<PruneTask> logger)
    {
        _generator = generator;
        _libraryManager = libraryManager;
        _imageProcessor = imageProcessor;
        _cache = cache;
        _status = status;
        _logger = logger;
    }

    public string Name => "Prune leftover cached images";

    public string Key => TaskKey;

    public string Description => "Deletes resized images that aren't a configured width of a current library image (old sizes, removed items, unsnapped requests).";

    public string Category => "JellyPrecache";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _generator.TaskLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var started = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var config = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin not loaded");
            var (keep, samples) = Expected(ImageSizing.Plan(config, allFormats: true), config.Quality, progress, cancellationToken);
            var message = await CheckNaming(samples, config.Quality).ConfigureAwait(false) ?? Prune(keep, started);
            _logger.LogInformation("JellyPrecache prune: {Message}", message);
            _status.Record(TaskKey, stopwatch.Elapsed, message);
            progress.Report(100);
        }
        catch (OperationCanceledException)
        {
            _status.Record(TaskKey, stopwatch.Elapsed, "Cancelled. Nothing deleted.");
            throw;
        }
        catch (Exception ex)
        {
            _status.Record(TaskKey, stopwatch.Elapsed, "Failed: " + ex.Message + " Nothing deleted.");
            throw;
        }
        finally
        {
            _generator.TaskLock.Release();
        }
    }

    // Every file name a snapped request can produce, plus a random sample to check against Jellyfin
    private (HashSet<string> Keep, List<Sample> Samples) Expected(TypePlan[] plan, int quality, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        var samples = new List<Sample>(SampleSize);
        var seen = 0;
        var ids = _generator.ItemIds(plan);
        for (var i = 0; i < ids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(90.0 * i / ids.Count);

            if (_libraryManager.GetItemById(ids[i]) is not { } item)
            {
                continue;
            }

            foreach (var typePlan in plan)
            {
                var images = item.GetImages(typePlan.Type).ToArray();
                for (var index = 0; index < images.Length; index++)
                {
                    if (!CacheNaming.IsCacheable(images[index]))
                    {
                        continue;
                    }

                    foreach (var width in typePlan.Widths)
                    {
                        foreach (var formats in typePlan.FormatSets)
                        {
                            var path = CacheNaming.FilePath(_cache.Root, images[index], width, quality, CacheNaming.FormatFor(formats, images[index].Path));
                            keep.Add(path);

                            // Reservoir sampling: uniform over all expected files without storing them
                            var sample = new Sample(item, images[index], index, width, formats, path);
                            seen++;
                            if (samples.Count < SampleSize)
                            {
                                samples.Add(sample);
                            }
                            else if (Random.Shared.Next(seen) < SampleSize)
                            {
                                samples[Random.Shared.Next(SampleSize)] = sample;
                            }
                        }
                    }
                }
            }
        }

        return (keep, samples);
    }

    // Asks Jellyfin for each sample (generating it if missing) and compares paths; any difference stops the prune
    private async Task<string?> CheckNaming(List<Sample> samples, int quality)
    {
        var compared = 0;
        foreach (var s in samples)
        {
            var (path, _, _) = await _imageProcessor
                .ProcessImage(ImageSizing.Options(s.Item, s.Image, s.Index, s.Width, quality, s.Formats))
                .ConfigureAwait(false);
            var fullPath = Path.GetFullPath(path);
            if (!_cache.Contains(fullPath))
            {
                continue;
            }

            compared++;
            if (!string.Equals(fullPath, s.ExpectedPath, StringComparison.Ordinal))
            {
                _logger.LogWarning("JellyPrecache prune: Jellyfin returned {Actual}, expected {Expected}", fullPath, s.ExpectedPath);
                return "Stopped: Jellyfin names cached files differently than expected (Jellyfin update?). Nothing deleted.";
            }
        }

        return compared > 0
            ? null
            : "Stopped: could not check file naming against Jellyfin (no sample image was cached). Nothing deleted.";
    }

    private string Prune(HashSet<string> keep, DateTime started)
    {
        var files = _cache.Files().ToList();

        // Files written after the prune started (live requests, generate-on-add, the samples) are never candidates
        var stale = files
            .Where(f => f.LastWriteTimeUtc < started && !keep.Contains(f.FullName))
            .ToList();
        if (stale.Count * 2 > files.Count)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Stopped: would have deleted {stale.Count:N0} of {files.Count:N0} files (over half). Nothing deleted. Use the full rebuild button.");
        }

        // Before deleting: a deleted FileInfo no longer has a length
        var kept = new FileTotal(files.Count - stale.Count, files.Sum(f => f.Length) - stale.Sum(f => f.Length));
        var deleted = new FileTotal(0, 0);
        var failed = 0;
        foreach (var file in stale)
        {
            try
            {
                var size = file.Length;
                file.Delete();
                deleted = new FileTotal(deleted.Files + 1, deleted.Bytes + size);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }

        var message = $"Deleted {deleted}, kept {kept}.";
        return failed > 0 ? message + string.Create(CultureInfo.InvariantCulture, $" {failed:N0} could not be deleted.") : message;
    }

    private sealed record Sample(BaseItem Item, ItemImageInfo Image, int Index, int Width, ImageFormat[] Formats, string ExpectedPath);
}
