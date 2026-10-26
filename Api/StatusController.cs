using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JellyPrecache.Api;

// Task results and live cache totals for the config page.
[ApiController]
[Route("JellyPrecache")]
[Authorize(Policy = "RequiresElevation")]
[Produces(MediaTypeNames.Application.Json)]
public class StatusController : ControllerBase
{
    private readonly StatusStore _status;
    private readonly ResizedCache _cache;

    public StatusController(StatusStore status, ResizedCache cache)
    {
        _status = status;
        _cache = cache;
    }

    [HttpGet("Status")]
    public ActionResult<StatusResponse> GetStatus()
    {
        var status = _status.Load();
        return new StatusResponse(status.Tasks, status.FullGenerationSeconds, _cache.Survey());
    }
}

public sealed record StatusResponse(Dictionary<string, TaskResult> Tasks, double? FullGenerationSeconds, CacheSurvey Cache);
