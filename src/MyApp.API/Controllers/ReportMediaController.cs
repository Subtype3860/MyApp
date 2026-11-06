using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using MyApp.Application.Security;
using MyApp.Application.Services;
using MyApp.Infrastructure.Db;

namespace MyApp.API.Controllers;

/// <summary>Постоянные ссылки на отдельные вложения сменного отчёта.</summary>
[ApiController]
[Route("api/report-media")]
public sealed class ReportMediaController(JwtOptions options, IVehicleService service) : ControllerBase
{
    public sealed record MediaReference(Guid Id, string Type);
    public sealed record MediaLink(Guid Id, string Type, string Url);

    [HttpPost("links")]
    [Authorize(Policy = Permissions.ShiftReport)]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Links([FromBody] MediaReference[] items,
        [FromServices] AppDbContext db, CancellationToken ct)
    {
        if (items.Length is < 1 or > 200 || items.Any(item => item is null ||
            item.Id == Guid.Empty || item.Type is not ("image" or "video")))
            return BadRequest("Укажите от 1 до 200 вложений отчёта.");
        var unique = items.Distinct().ToArray();
        var photoIds = unique.Where(item => item.Type == "image").Select(item => item.Id).ToArray();
        var videoIds = unique.Where(item => item.Type == "video").Select(item => item.Id).ToArray();
        var photos = await db.VehicleWorkPhotos.AsNoTracking().Where(item => photoIds.Contains(item.Id)).Select(item => item.Id).ToListAsync(ct);
        var videos = await db.VehicleWorkVideos.AsNoTracking().Where(item => videoIds.Contains(item.Id)).Select(item => item.Id).ToListAsync(ct);
        if (photos.Count != photoIds.Length || videos.Count != videoIds.Length) return NotFound();
        return Ok(unique.Select(item => new MediaLink(item.Id, item.Type,
            $"/api/report-media/{item.Type}/{item.Id:D}/{Signature(item.Type, item.Id)}")).ToArray());
    }

    // This signature authorizes only reading this exact file; it is not a login token.
    [NonAction]
    public string Signature(string type, Guid id) => WebEncoders.Base64UrlEncode(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Key),
            Encoding.UTF8.GetBytes($"atlas:public-report-media:v1:{type}:{id:D}")));

    [HttpGet("{type}/{id:guid}/{signature}")]
    [AllowAnonymous]
    public async Task<IActionResult> Open(string type, Guid id, string signature, CancellationToken ct)
    {
        if (type is not ("image" or "video") || signature.Length != 43 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(signature),
                Encoding.ASCII.GetBytes(Signature(type, id)))) return NotFound();
        // Explicit public sharing remains supported. Only a verified, file-specific signature
        // may bypass region visibility; issuing links above still uses the caller's region scope.
        var scope = HttpContext.RequestServices?.GetService<AppDbContext>();
        var restricted = scope?.RestrictFleet;
        if (scope is not null) scope.RestrictFleet = false;
        try {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (type == "image") {
            var photo = await service.GetWorkPhotoAsync(id, ct);
            if (photo is null) return NotFound();
            if (photo.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) return NotFound();
            return File(photo.Content, photo.ContentType, enableRangeProcessing: true);
        }
        var video = await service.GetWorkVideoStreamAsync(id, ct);
        if (video is null) return NotFound();
        if (video.ContentType is not ("video/mp4" or "video/webm" or "video/quicktime")) {
            await video.Content.DisposeAsync();
            return NotFound();
        }
        return File(video.Content, video.ContentType, enableRangeProcessing: true);
        } finally {
            if (scope is not null) scope.RestrictFleet = restricted!.Value;
        }
    }
}
