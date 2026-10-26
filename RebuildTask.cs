using System.Diagnostics;
using System.Globalization;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyPrecache;

// Clears the resized cache, re-downloads originals whose file is gone, then regenerates everything.
public class RebuildTask : IScheduledTask
{
    public const string TaskKey = "JellyPrecacheRebuild";

    private readonly ImageGenerator _generator;
    private readonly ResizedCache _cache;
    private readonly StatusStore _status;
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<RebuildTask> _logger;

    public RebuildTask(ImageGenerator generator, ResizedCache cache, StatusStore status, ILibraryManager libraryManager, IFileSystem fileSystem, ILogger<RebuildTask> logger)
    {
        _generator = generator;
        _cache = cache;
        _status = status;
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public string Name => "Rebuild image cache";

    public string Key => TaskKey;

    public string Description => "Deletes every resized image, re-downloads missing originals, then regenerates all configured sizes and formats.";

    public string Category => "JellyPrecache";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _generator.TaskLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        var steps = new List<string>(3);
        try
        {
            var cleared = _cache.Clear();
            steps.Add($"Deleted {cleared}.");
            progress.Report(2);

            var (refreshed, failed) = await RedownloadMissing(new Progress<double>(p => progress.Report(2 + (p * 0.18))), cancellationToken).ConfigureAwait(false);
            steps.Add(string.Create(CultureInfo.InvariantCulture, $"Re-downloaded missing originals of {refreshed:N0} items ({failed:N0} failed)."));

            var run = await _generator.GenerateAll(new Progress<double>(p => progress.Report(20 + (p * 0.8))), cancellationToken).ConfigureAwait(false);
            steps.Add("Generated " + run.Summary() + ".");

            var message = string.Join(" ", steps);
            _logger.LogInformation("JellyPrecache rebuild: {Message}", message);
            _status.Record(TaskKey, stopwatch.Elapsed, message, run);
        }
        catch (OperationCanceledException)
        {
            _status.Record(TaskKey, stopwatch.Elapsed, string.Join(" ", steps.Append("Cancelled: remaining images are generated on first view or by the next pre-generation.")));
            throw;
        }
        finally
        {
            _generator.TaskLock.Release();
        }
    }

    // Same refresh as the "Refresh metadata > search for missing images" dialog, on the item only (not its children)
    private async Task<(int Refreshed, int Failed)> RedownloadMissing(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var ids = Plugin.Instance is { } plugin ? _generator.ItemIds(ImageSizing.Plan(plugin.Configuration)) : Array.Empty<Guid>();
        var refreshed = 0;
        var failed = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(100.0 * i / ids.Count);

            var item = _libraryManager.GetItemById(ids[i]);
            if (item is null || !item.ImageInfos.Any(image => image.IsLocalFile && !File.Exists(image.Path)))
            {
                continue;
            }

            var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.Default,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllImages = false,
                ReplaceAllMetadata = false,
                ForceSave = true,
                IsAutomated = false
            };
            try
            {
                await item.RefreshMetadata(options, cancellationToken).ConfigureAwait(false);
                refreshed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _logger.LogWarning(ex, "JellyPrecache: refresh failed for {Item}", item.Name);
            }
        }

        return (refreshed, failed);
    }
}
