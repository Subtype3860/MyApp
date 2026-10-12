using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MyApp.Application.DTO;
using MyApp.Application.Abstractions;
using MyApp.Application.Security;
using MyApp.Application.Services;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/auxiliary-works")]
[Authorize]
public sealed class AuxiliaryWorkController(IVehicleService service, IMemoryCache cache) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.AuxiliaryWorksRead)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Ok(await service.GetAuxiliaryWorksAsync(cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.AuxiliaryWorksView)]
    public async Task<IActionResult> Create([FromBody] AuxiliaryWorkRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return this.ToActionResult(await service.AddAuxiliaryWorkAsync(request, userId, cancellationToken), id => Created($"/api/auxiliary-works/{id}", new { id }));
    }

    [HttpPost("{workId:guid}/photos")]
    [Authorize(Policy = Permissions.AuxiliaryWorksView)]
    [RequestSizeLimit(9 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(Guid workId, IFormFile file, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!await service.AuxiliaryWorkExistsAsync(workId, cancellationToken)) return NotFound();
        if (file.Length is <= 0 or > 8 * 1024 * 1024) return BadRequest("Фото должно быть не больше 8 МБ.");
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        var result = await service.AddWorkPhotosAsync(workId, [new VehicleWorkPhotoUpload(Path.GetFileName(file.FileName), file.ContentType, stream.ToArray())], userId, cancellationToken);
        return this.ToActionResult(result, ids => Ok(new { id = ids[0] }));
    }

    [HttpPost("{workId:guid}/videos")]
    [Authorize(Policy = Permissions.AuxiliaryWorksView)]
    [RequestSizeLimit(101 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 101 * 1024 * 1024)]
    public async Task<IActionResult> UploadVideo(Guid workId, IFormFile file, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!await service.AuxiliaryWorkExistsAsync(workId, cancellationToken)) return NotFound();
        if (file.Length is <= 0 or > 100 * 1024 * 1024) return BadRequest("Видео должно быть не больше 100 МБ.");
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        var result = await service.AddWorkVideosAsync(workId, [new VehicleMediaUpload(Path.GetFileName(file.FileName), file.ContentType, stream.ToArray())], userId, cancellationToken);
        return this.ToActionResult(result, ids => Ok(new { id = ids[0] }));
    }

    [HttpGet("photos/{photoId:guid}")]
    [Authorize(Policy = Permissions.AuxiliaryWorksRead)]
    public async Task<IActionResult> Photo(Guid photoId, CancellationToken cancellationToken)
    {
        if (!await service.AuxiliaryMediaExistsAsync(photoId, false, cancellationToken)) return NotFound();
        var photo = await service.GetWorkPhotoAsync(photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Content, photo.ContentType, enableRangeProcessing: true);
    }

    [HttpPost("videos/{videoId:guid}/stream-ticket")]
    [Authorize(Policy = Permissions.AuxiliaryWorksRead)]
    public async Task<IActionResult> VideoTicket(Guid videoId, CancellationToken cancellationToken)
    {
        if (!await service.AuxiliaryMediaExistsAsync(videoId, true, cancellationToken)) return NotFound();
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (!SessionIdentity.TryRead(User, out var userId, out var stamp)) return Unauthorized();
        cache.Set($"auxiliary-video:{ticket}", new VideoTicketSession(videoId, userId, stamp), TimeSpan.FromHours(1));
        return Ok(new { url = $"/api/auxiliary-works/video-stream/{ticket}" });
    }

    [HttpGet("video-stream/{ticket}")]
    [AllowAnonymous]
    public async Task<IActionResult> VideoStream(string ticket, [FromServices] IUserSessionStore sessions, CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue<VideoTicketSession>($"auxiliary-video:{ticket}", out var identity) || identity is null) return NotFound();
        if (!await sessions.IsCurrentAsync(identity.UserId, identity.Stamp, cancellationToken))
        {
            cache.Remove($"auxiliary-video:{ticket}");
            return Unauthorized();
        }
        var videoId = identity.VideoId;
        if (!await service.AuxiliaryMediaExistsAsync(videoId, true, cancellationToken)) return NotFound();
        var video = await service.GetWorkVideoStreamAsync(videoId, cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        return video is null ? NotFound() : File(video.Content, video.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("{workId:guid}/participants")]
    [Authorize(Policy = Permissions.AuxiliaryWorksView)]
    public async Task<IActionResult> Participants(Guid workId, CancellationToken cancellationToken)
    {
        if (!await service.AuxiliaryWorkExistsAsync(workId, cancellationToken)) return NotFound();
        return this.ToActionResult(await service.GetWorkParticipantsAsync(workId, cancellationToken), rows => Ok(rows));
    }

    [HttpPost("{workId:guid}/participants")]
    [Authorize(Policy = Permissions.AuxiliaryWorksView)]
    public async Task<IActionResult> Join(Guid workId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!await service.AuxiliaryWorkExistsAsync(workId, cancellationToken)) return NotFound();
        return this.ToActionResult(await service.RequestWorkParticipationAsync(workId, userId, cancellationToken), _ => Ok());
    }

    [HttpPost("{workId:guid}/participants/{participantId:guid}/review")]
    [Authorize(Policy = Permissions.AuxiliaryWorksView)]
    public async Task<IActionResult> Review(Guid workId, Guid participantId, [FromBody] VehicleWorkParticipantReviewRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!await service.AuxiliaryWorkExistsAsync(workId, cancellationToken)) return NotFound();
        return this.ToActionResult(await service.ReviewWorkParticipationAsync(workId, participantId, request.Approved, userId, cancellationToken), _ => Ok());
    }

    private sealed record VideoTicketSession(Guid VideoId, Guid UserId, Guid Stamp);

    private bool TryGetUserId(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub), out id);
}
