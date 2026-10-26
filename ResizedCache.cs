using System.Globalization;
using MediaBrowser.Common.Configuration;

namespace JellyPrecache;

// Jellyfin's resized image cache folder: membership test, per-format survey, and clearing its files.
public class ResizedCache
{
    public ResizedCache(IApplicationPaths paths)
    {
        // Same construction as ImageProcessor.ResizedImageCachePath
        Root = Path.GetFullPath(Path.Combine(paths.ImageCachePath, "resized-images"));
    }

    public string Root { get; }

    public bool Contains(string path) => path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    public IEnumerable<FileInfo> Files()
    {
        return Directory.Exists(Root)
            ? new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories)
            : Enumerable.Empty<FileInfo>();
    }

    public CacheSurvey Survey()
    {
        var byFormat = new SortedDictionary<string, FileTotal>(StringComparer.Ordinal);
        foreach (var file in Files())
        {
            var ext = file.Extension.TrimStart('.').ToLowerInvariant();
            var total = byFormat.GetValueOrDefault(ext);
            byFormat[ext] = new FileTotal(total.Files + 1, total.Bytes + file.Length);
        }

        return new CacheSurvey(byFormat.Values.Sum(t => t.Files), byFormat.Values.Sum(t => t.Bytes), byFormat);
    }

    // Deletes every file inside the folder (subfolders and the folder itself stay). Refuses anything but .../images/resized-images.
    public FileTotal Clear()
    {
        var parent = Path.GetDirectoryName(Root);
        if (!Path.IsPathFullyQualified(Root)
            || !string.Equals(Path.GetFileName(Root), "resized-images", StringComparison.Ordinal)
            || !string.Equals(Path.GetFileName(parent), "images", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to clear unexpected cache path " + Root);
        }

        var deleted = new FileTotal(0, 0);
        foreach (var file in Files().ToList())
        {
            var size = file.Length;
            file.Delete();
            deleted = new FileTotal(deleted.Files + 1, deleted.Bytes + size);
        }

        return deleted;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return size.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}

public readonly record struct FileTotal(int Files, long Bytes)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Files:N0} files ({ResizedCache.FormatSize(Bytes)})");
}

public sealed record CacheSurvey(int Files, long Bytes, IDictionary<string, FileTotal> ByFormat);
