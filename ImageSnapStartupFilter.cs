using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace JellyPrecache;

// Puts ImageSnapMiddleware ahead of routing, so the rewritten query is what ImageController binds
public class ImageSnapStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<ImageSnapMiddleware>();
            next(app);
        };
    }
}
