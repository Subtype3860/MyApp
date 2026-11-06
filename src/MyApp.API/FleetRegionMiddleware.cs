using System.Security.Claims;
using MyApp.Infrastructure.Db;

namespace MyApp.API;

/// <summary>Права участка читаются из БД на каждом запросе, а не из устаревающего JWT.</summary>
public sealed class FleetRegionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        await db.SetFleetScopeAsync(context.User.Identity?.IsAuthenticated == true && Guid.TryParse(value, out var id) ? id : null,
            context.RequestAborted);
        if (context.Request.Path.StartsWithSegments("/api/vehicles") || context.Request.Path.StartsWithSegments("/api/fleet-regions"))
            context.Response.Headers.CacheControl = "private, no-store";
        await next(context);
    }
}
