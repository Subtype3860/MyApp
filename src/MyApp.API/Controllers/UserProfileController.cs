using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class UserProfileController : ControllerBase
{
    private const long MaximumAvatarSize = 2 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAvatarTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    [HttpGet]
    public async Task<IActionResult> GetProfile(
        [FromServices] IUserService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.GetProfileAsync(userId, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        [FromServices] IUserService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.UpdateProfileAsync(userId, request, cancellationToken);
        return this.ToActionResult(result, _ => Ok());
    }

    [HttpGet("avatar")]
    public async Task<IActionResult> GetAvatar(
        [FromServices] IUserService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.GetAvatarAsync(userId, cancellationToken);
        return this.ToActionResult(result, avatar => File(avatar.Content, avatar.ContentType, enableRangeProcessing: false));
    }

    [HttpPost("avatar")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UploadAvatar(
        IFormFile avatar,
        [FromServices] IUserService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (avatar.Length is <= 0 or > MaximumAvatarSize ||
            !AllowedAvatarTypes.Contains(avatar.ContentType))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["avatar"] = ["Выберите изображение JPEG, PNG или WebP размером не более 2 МБ."]
            }));
        }

        await using var stream = new MemoryStream();
        await avatar.CopyToAsync(stream, cancellationToken);

        var result = await service.UpdateAvatarAsync(userId, stream.ToArray(), avatar.ContentType, cancellationToken);
        return this.ToActionResult(result, _ => Ok());
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar(
        [FromServices] IUserService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.DeleteAvatarAsync(userId, cancellationToken);
        return this.ToActionResult(result, _ => Ok());
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out userId);
    }
}
