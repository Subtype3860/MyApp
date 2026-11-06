using System.ComponentModel.DataAnnotations;
using MyApp.Application.Abstractions;
using MyApp.Application.Storage;
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
    private const long BytesPerGigabyte = 1024L * 1024 * 1024;

    /// <summary>Возвращает настройки временного и постоянного хранения медиа.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var settings = await repository.GetSettingsAsync(cancellationToken);
        return Ok(new
        {
            retentionDays = settings.StagingRetentionDays,
            permanentRetentionDays = settings.PermanentRetentionDays,
            maxStorageGb = ToGigabytes(settings.MaxStorageBytes),
            maxStorageBytes = settings.MaxStorageBytes
        });
    }

    /// <summary>Проверяет и сохраняет настройки хранения медиафайлов.</summary>
    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateMediaStorageSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = new MediaStorageSettings(
            request.RetentionDays,
            request.PermanentRetentionDays,
            request.MaxStorageGb * BytesPerGigabyte);
        await repository.SetSettingsAsync(settings, cancellationToken);
        return Ok(new
        {
            retentionDays = settings.StagingRetentionDays,
            permanentRetentionDays = settings.PermanentRetentionDays,
            maxStorageGb = request.MaxStorageGb,
            maxStorageBytes = settings.MaxStorageBytes
        });
    }

    /// <summary>Переводит байты в целые гигабайты для административного интерфейса.</summary>
    private static int ToGigabytes(long bytes)
    {
        var gigabytes = (int)Math.Round(
            bytes / (double)BytesPerGigabyte,
            MidpointRounding.AwayFromZero);
        return Math.Max(1, gigabytes);
    }
}

/// <summary>Запрос изменения настроек хранения медиафайлов.</summary>
public sealed record UpdateMediaStorageSettingsRequest(
    [property: Range(1, 180)] int RetentionDays,
    [property: Range(1, 3650)] int PermanentRetentionDays,
    [property: Range(1, 1024)] int MaxStorageGb);
