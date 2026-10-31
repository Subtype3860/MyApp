using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

/// <summary>HTTP-эндпоинты профиля пользователя.</summary>
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

    /// <summary>Возвращает профиль указанного пользователя.</summary>
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

    /// <summary>Обновляет личные данные пользователя с проверкой запроса.</summary>
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
        return this.ToActionResult(result, value => Ok(value));
    }

    /// <summary>Возвращает изображение аватара пользователя.</summary>
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

    /// <summary>Принимает файл нового аватара текущего пользователя.</summary>
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

    /// <summary>Удаляет аватар пользователя.</summary>
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

    /// <summary>Извлекает идентификатор пользователя из утверждений текущей сессии.</summary>
    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out userId);
    }
}
