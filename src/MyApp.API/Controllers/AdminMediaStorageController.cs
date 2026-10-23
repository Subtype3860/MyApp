using System.ComponentModel.DataAnnotations;
using MyApp.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

/// <summary>HTTP-эндпоинты администрирования медиаархива.</summary>
[ApiController]
[Route("api/admin/media-storage")]
[Authorize(Roles = "administrator")]
public sealed class AdminMediaStorageController(
    IMediaStorageAdministrationRepository repository) : ControllerBase
{
    /// <summary>Возвращает текущий срок хранения временных медиафайлов.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var retentionDays = await repository.GetRetentionDaysAsync(cancellationToken);
        return Ok(new { retentionDays });
    }

    /// <summary>Проверяет и сохраняет новый срок хранения временных медиафайлов.</summary>
    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateMediaStorageSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await repository.SetRetentionDaysAsync(
            request.RetentionDays, cancellationToken);
        return Ok(new { retentionDays = request.RetentionDays });
    }
}

/// <summary>Запрос изменения срока хранения временных медиафайлов.</summary>
public sealed record UpdateMediaStorageSettingsRequest(
    [property: Range(1, 180)] int RetentionDays);
