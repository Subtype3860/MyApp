using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApp.Application.Security;
using MyApp.Infrastructure.Sany;

namespace MyApp.API.Controllers;

/// <summary>Подключение к порталу SANY SIS: капча и вход по логину и паролю.</summary>
[ApiController]
[Route("api/sany")]
[Authorize(Policy = Permissions.VehiclesView)]
public sealed class SanyController(SanySessionStore store, SanyClient client, IConfiguration configuration) : ControllerBase
{
    public sealed record ConnectRequest(string Account, string Password);
    public sealed record LoginRequest(string CaptchaCode);
    private const int MaxAttempts = 5;
    private static readonly Dictionary<string, int> Attempts = new();

    /// <summary>Создаёт подключение и возвращает капчу для ручного ввода.</summary>
    [HttpPost("session")]
    [RequestSizeLimit(8 * 1024)]
    public async Task<IActionResult> Connect([FromBody] ConnectRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Укажите логин и пароль SANY." });
        SanyConnection? connection = null;
        try
        {
            connection = store.Create(request.Account, request.Password);
            var captcha = await client.GetCaptchaAsync(connection, ct);
            return Ok(new
            {
                sessionId = connection.Id,
                captchaImage = Convert.ToBase64String(captcha.Image),
                contentType = "image/jpeg",
                expiresAt = connection.ExpiresAt,
            });
        }
        catch (Exception)
        {
            if (connection is not null) store.Remove(connection.Id);
            return StatusCode(502, new { message = "Портал SANY недоступен. Попробуйте позже." });
        }
    }

    /// <summary>Входит в портал с введённым кодом капчи.</summary>
    [HttpPost("session/{id}/login")]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Login(string id, [FromBody] LoginRequest request, CancellationToken ct)
    {
        var connection = store.Find(id);
        if (connection is null) return NotFound(new { message = "Сессия SANY истекла. Начните вход заново." });
        Attempts.TryGetValue(id, out var attempts);
        if (attempts >= MaxAttempts)
            return StatusCode(429, new { message = "Слишком много попыток ввода капчи. Начните вход заново." });
        if (string.IsNullOrWhiteSpace(request.CaptchaCode))
            return BadRequest(new { message = "Введите код с картинки." });

        var hours = configuration.GetValue("Sany:TokenHours", 8);
        SanyLoginResult result;
        try
        {
            result = await client.LoginAsync(connection, request.CaptchaCode, hours, ct);
        }
        catch (Exception)
        {
            return StatusCode(502, new { message = "Портал SANY не ответил при входе." });
        }

        if (!result.Ok)
        {
            Attempts[id] = attempts + 1;
            if (attempts + 1 >= MaxAttempts) store.Remove(id);
            return Unauthorized(new { message = result.Message ?? "Не удалось войти в SANY." });
        }

        Attempts.Remove(id);
        return Ok(new { connected = true, expiresAt = connection.ExpiresAt });
    }

    /// <summary>Возвращает состояние подключения.</summary>
    [HttpGet("session/{id}")]
    public IActionResult Status(string id)
    {
        var connection = store.Find(id);
        return connection is null
            ? NotFound(new { connected = false })
            : Ok(new { connected = connection.Connected, expiresAt = connection.ExpiresAt });
    }

    /// <summary>Завершает подключение и стирает секреты.</summary>
    [HttpDelete("session/{id}")]
    public IActionResult Disconnect(string id)
    {
        store.Remove(id);
        Attempts.Remove(id);
        return NoContent();
    }
}
