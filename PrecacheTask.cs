using MediaBrowser.Model.Tasks;

namespace JellyPrecache;

// Fills Jellyfin's resize cache with every configured width and format of every enabled image type.
public class PrecacheTask : IScheduledTask
{
    public const string TaskKey = "JellyPrecachePregenerate";

    private readonly ImageGenerator _generator;
    private readonly StatusStore _status;

    public PrecacheTask(ImageGenerator generator, StatusStore status)
    {
        _generator = generator;
        _status = status;
    }

    public string Name => "Pre-generate image sizes";

    public string Key => TaskKey;

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
        await _generator.TaskLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var run = await _generator.GenerateAll(progress, cancellationToken).ConfigureAwait(false);
            _status.Record(TaskKey, run.Elapsed, run.Summary(), run);
        }
        finally
        {
            _generator.TaskLock.Release();
        }
    }
}
