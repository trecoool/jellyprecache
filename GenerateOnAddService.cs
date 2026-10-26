using System.Collections.Concurrent;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyPrecache;

// When enabled, generates an item's cached images once it has been quiet for a few seconds after being added or getting new images.
public class GenerateOnAddService : BackgroundService
{
    private static readonly TimeSpan QuietDelay = TimeSpan.FromSeconds(5);

    // Item id -> time of its last event
    private readonly ConcurrentDictionary<Guid, DateTime> _pending = new();
    private readonly ILibraryManager _libraryManager;
    private readonly ImageGenerator _generator;
    private readonly ILogger<GenerateOnAddService> _logger;

    public GenerateOnAddService(ILibraryManager libraryManager, ImageGenerator generator, ILogger<GenerateOnAddService> logger)
    {
        _libraryManager = libraryManager;
        _generator = generator;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            foreach (var entry in _pending)
            {
                // Removes only if no newer event came in meanwhile
                if (DateTime.UtcNow - entry.Value >= QuietDelay && _pending.TryRemove(entry))
                {
                    await Generate(entry.Key).ConfigureAwait(false);
                }
            }
        }
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e) => Enqueue(e);

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (e.UpdateReason.HasFlag(ItemUpdateType.ImageUpdate))
        {
            Enqueue(e);
        }
    }

    private void Enqueue(ItemChangeEventArgs e)
    {
        if (Plugin.Instance?.Configuration.GenerateOnAdd == true && e.Item is { } item)
        {
            _pending[item.Id] = DateTime.UtcNow;
        }
    }

    private async Task Generate(Guid id)
    {
        if (Plugin.Instance is not { } plugin || _libraryManager.GetItemById(id) is not { } item)
        {
            return;
        }

        var run = new GenerationRun();
        try
        {
            await _generator.GenerateItem(item, ImageSizing.Plan(plugin.Configuration), plugin.Configuration.Quality, run).ConfigureAwait(false);
            _logger.LogDebug("JellyPrecache: {Item}: {Summary}", item.Name, run.Summary());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JellyPrecache: generate on add failed for {Item}", item.Name);
        }
    }
}
