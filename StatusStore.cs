using System.Text.Json;

namespace JellyPrecache;

// Last result of each JellyPrecache task, in the plugin data folder.
// Not in PluginConfiguration: a task saving it would overwrite edits in progress on the config page.
public class StatusStore
{
    private readonly object _lock = new();

    private static string? FilePath => Plugin.Instance is { } plugin ? Path.Combine(plugin.DataFolderPath, "status.json") : null;

    public PluginStatus Load()
    {
        lock (_lock)
        {
            try
            {
                return FilePath is { } path && File.Exists(path)
                    ? JsonSerializer.Deserialize<PluginStatus>(File.ReadAllText(path)) ?? new PluginStatus()
                    : new PluginStatus();
            }
            catch (JsonException)
            {
                return new PluginStatus();
            }
        }
    }

    // A run that had to encode most files also sets the "last full generation" duration
    public void Record(string taskKey, TimeSpan elapsed, string message, GenerationRun? run = null)
    {
        lock (_lock)
        {
            if (FilePath is not { } path)
            {
                return;
            }

            var status = Load();
            status.Tasks[taskKey] = new TaskResult { Finished = DateTime.UtcNow, Seconds = elapsed.TotalSeconds, Message = message };
            if (run is { WasFull: true })
            {
                status.FullGenerationSeconds = run.Elapsed.TotalSeconds;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(status));
        }
    }
}

public sealed class PluginStatus
{
    public Dictionary<string, TaskResult> Tasks { get; set; } = new();

    public double? FullGenerationSeconds { get; set; }
}

public sealed class TaskResult
{
    public DateTime Finished { get; set; }

    public double Seconds { get; set; }

    public string Message { get; set; } = string.Empty;
}
