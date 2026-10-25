using System.Globalization;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace JellyPrecache;

// Rewrites item image requests to a configured width so every client hits a small, pre-generated set of cache entries.
public class ImageSnapMiddleware
{
    private static readonly Regex ImageRoute = new(
        @"/Items/(?<id>[0-9a-f]{8}-?(?:[0-9a-f]{4}-?){3}[0-9a-f]{12})/Images/(?<type>[a-z]+)(?:/(?<index>\d+))?/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] BypassKeys = { "blur", "percentPlayed", "unplayedCount", "backgroundColor", "foregroundLayer" };

    private readonly RequestDelegate _next;
    private readonly ILibraryManager _libraryManager;

    public ImageSnapMiddleware(RequestDelegate next, ILibraryManager libraryManager)
    {
        _next = next;
        _libraryManager = libraryManager;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) && Plugin.Instance is { } plugin)
        {
            TryRewrite(context.Request, plugin);
        }

        return _next(context);
    }

    private void TryRewrite(HttpRequest request, Plugin plugin)
    {
        var match = ImageRoute.Match(request.Path.Value ?? string.Empty);
        if (!match.Success
            || !Enum.TryParse<ImageType>(match.Groups["type"].Value, true, out var type)
            || ImageSizing.WidthsFor(plugin.Configuration, type) is not { } widths)
        {
            return;
        }

        var query = request.Query;
        if (BypassKeys.Any(query.ContainsKey))
        {
            return;
        }

        var requested = FirstInt(query, "fillWidth", "maxWidth", "width")
            ?? WidthFromHeight(query, match, type);
        if (requested is not { } w)
        {
            return;
        }

        var rebuilt = new QueryBuilder(query
            .Where(kv => !ImageSizing.SizeKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string>(kv.Key, v ?? string.Empty))));
        foreach (var param in ImageSizing.QueryParams(ImageSizing.Snap(widths, w), plugin.Configuration.Quality))
        {
            rebuilt.Add(param.Key, param.Value);
        }

        request.QueryString = rebuilt.ToQueryString();
    }

    // Height-only requests: convert through the image's stored aspect ratio, skip when unknown
    private int? WidthFromHeight(IQueryCollection query, Match match, ImageType type)
    {
        if (FirstInt(query, "fillHeight", "maxHeight", "height") is not { } h
            || !Guid.TryParse(match.Groups["id"].Value, out var id))
        {
            return null;
        }

        var index = match.Groups["index"].Success
            ? int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture)
            : FirstInt(query, "imageIndex") ?? 0;
        var info = _libraryManager.GetItemById(id)?.GetImageInfo(type, index);
        if (info is null || info.Width <= 0 || info.Height <= 0)
        {
            return null;
        }

        return (int)Math.Round((double)h * info.Width / info.Height);
    }

    private static int? FirstInt(IQueryCollection query, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (int.TryParse(query[key].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0)
            {
                return v;
            }
        }

        return null;
    }
}
