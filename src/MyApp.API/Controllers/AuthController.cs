using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MyApp.Application.Abstractions;
using MyApp.Application.Security;

namespace MyApp.API.Controllers;

/// <summary>HTTP-эндпоинты аутентификации.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    /// <summary>Отзывает текущую сессию пользователя.</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromServices] IUserSessionStore sessions, CancellationToken cancellationToken)
    {
        if (!SessionIdentity.TryRead(User, out var userId, out var stamp)) return Unauthorized();
        await sessions.RevokeAsync(userId, stamp, cancellationToken);
        return NoContent();
    }

    /// <summary>Проверяет учётные данные и возвращает результат входа с токеном доступа.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] IAuthenticationService authenticationService,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginAsync(request, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }
}
