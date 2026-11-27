using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Db;

namespace MyApp.API.Controllers;

/// <summary>Постоянные ссылки на вложения карты ТО для печатной формы.</summary>
public sealed partial class MaintenancePlansController
{
    public sealed record CardMediaLink(Guid Id, string Url);

    /// <summary>Выдаёт подписанные ссылки только на медиа указанной карты.</summary>
    [HttpPost("cards/{id:guid}/media-links")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> CardMediaLinks(Guid id, [FromBody] Guid[] photoIds, CancellationToken ct)
    {
        if (photoIds.Length is < 1 or > 200) return BadRequest(new { message = "Укажите от 1 до 200 вложений карты." });
        var ids = photoIds.Distinct().ToArray();
        if (ids.Contains(Guid.Empty)) return BadRequest(new { message = "Укажите от 1 до 200 вложений карты." });
        if (!await db.MaintenanceCards.AnyAsync(card => card.Id == id, ct)) return NotFound();
        var found = await db.MaintenanceCardPhotos.AsNoTracking()
            .Where(photo => photo.CardId == id && ids.Contains(photo.Id))
            .Select(photo => photo.Id).ToArrayAsync(ct);
        if (found.Length != ids.Length) return NotFound();
        return Ok(ids.Select(photoId => new CardMediaLink(photoId,
            $"/api/maintenance-plans/cards/{id:D}/media/{photoId:D}/{MediaSignature(id, photoId)}")).ToArray());
    }

    /// <summary>Отдаёт вложение по подписанной ссылке без токена: подпись подтверждает только этот файл.</summary>
    [HttpGet("cards/{id:guid}/media/{photoId:guid}/{signature}")]
    [AllowAnonymous]
    public async Task<IActionResult> CardMedia(Guid id, Guid photoId, string signature, CancellationToken ct)
    {
        if (signature.Length != 43 || !CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(signature), Encoding.ASCII.GetBytes(MediaSignature(id, photoId))))
            return NotFound();
        // A verified file-specific signature may bypass region visibility; issuing links still uses the caller's scope.
        var scope = HttpContext.RequestServices?.GetService<AppDbContext>();
        var restricted = scope?.RestrictFleet;
        if (scope is not null) scope.RestrictFleet = false;
        try
        {
            var photo = await db.MaintenanceCardPhotos.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CardId == id && item.Id == photoId, ct);
            if (photo is null) return NotFound();
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(photo.Content, photo.ContentType, enableRangeProcessing: true);
        }
        finally
        {
            if (scope is not null) scope.RestrictFleet = restricted!.Value;
        }
    }

    /// <summary>Подписывает пару «карта — вложение», чтобы ссылка не открывала другие файлы.</summary>
    private string MediaSignature(Guid cardId, Guid photoId) => WebEncoders.Base64UrlEncode(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwtOptions.Key),
            Encoding.UTF8.GetBytes($"atlas:maintenance-card-media:v1:{cardId:D}:{photoId:D}")));
}
