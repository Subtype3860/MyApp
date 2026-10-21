using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MyApp.Application.DTO;
using MyApp.Application.Abstractions;
using MyApp.Application.Services;
using MyApp.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace MyApp.API.Controllers;

/// <summary>
/// API-контроллер для работы с журналом техники: закупки, неисправности,
/// моточасы, ремонтные работы и их медиавложения (фото/видео), заявки на запчасти.
/// </summary>
[ApiController]
[Route("api/vehicles")]
[Authorize(Policy = Permissions.VehiclesView)]
public class VehicleController : ControllerBase
{
    private const long MaximumHoursImportSize = 5 * 1024 * 1024;
    private const long MaximumPhotoSize = 8 * 1024 * 1024;
    private const long MaximumVideoSize = 100 * 1024 * 1024;
    private const long MaximumRequestFileSize = 20 * 1024 * 1024;

    /// <summary>Возвращает список всей техники.</summary>
    [HttpGet]
    public async Task<IActionResult> GetVehicles(
        [FromQuery] bool includeArchived,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var vehicles = await service.GetVehiclesAsync(cancellationToken, includeArchived);
        return Ok(vehicles);
    }

    /// <summary>Возвращает справочники парка и доступные текущему пользователю действия.</summary>
    [HttpGet("fleet-options")]
    public async Task<IActionResult> FleetOptions([FromServices] IVehicleService service, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await service.GetVehicleCatalogOptionsAsync(userId, cancellationToken));
    }

    /// <summary>Создаёт карточку техники через сервис управления парком.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateVehicle([FromBody] VehicleCatalogRequest request, [FromServices] IVehicleService service, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return this.ToActionResult(await service.SaveVehicleCatalogAsync(null, request, userId, cancellationToken), id => Created($"/api/vehicles/{id}/journal", new { id }));
    }

    /// <summary>Обновляет карточку техники через сервис управления парком.</summary>
    [HttpPut("{vehicleId:guid}")]
    public async Task<IActionResult> UpdateVehicle(Guid vehicleId, [FromBody] VehicleCatalogRequest request, [FromServices] IVehicleService service, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return this.ToActionResult(await service.SaveVehicleCatalogAsync(vehicleId, request, userId, cancellationToken), id => Ok(new { id }));
    }

    /// <summary>Архивирует карточку техники с сохранением её истории.</summary>
    [HttpDelete("{vehicleId:guid}")]
    public async Task<IActionResult> RemoveVehicle(Guid vehicleId, [FromServices] IVehicleService service, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return this.ToActionResult(await service.ArchiveVehicleAsync(vehicleId, userId, cancellationToken), _ => NoContent());
    }

    /// <summary>Возвращает журнал техники (закупки, неисправности, моточасы, работы) за период.</summary>
    [HttpGet("{vehicleId:guid}/journal")]
    public async Task<IActionResult> GetJournal(
        Guid vehicleId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var journal = await service.GetJournalAsync(vehicleId, from, to, cancellationToken);
        return journal is null ? NotFound() : Ok(journal);
    }

    /// <summary>Returns repair data for all vehicles using batched queries.</summary>
    [HttpGet("repair-journal")]
    public async Task<IActionResult> GetRepairJournals(
        [FromQuery] bool includeArchived,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var journals = await service.GetRepairJournalsAsync(cancellationToken, includeArchived);
        return Ok(journals);
    }

    /// <summary>Добавляет запись о закупке техники в журнал.</summary>
    [HttpPost("{vehicleId:guid}/purchases")]
    public async Task<IActionResult> AddPurchase(
        Guid vehicleId,
        [FromBody] VehiclePurchaseRequest request,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        return await CreateAsync(
            userId => service.AddPurchaseAsync(vehicleId, request, userId, cancellationToken),
            $"/api/vehicles/{vehicleId}/journal");
    }

    /// <summary>Регистрирует новую неисправность техники.</summary>
    [HttpPost("{vehicleId:guid}/defects")]
    public async Task<IActionResult> AddDefect(
        Guid vehicleId,
        [FromBody] VehicleDefectRequest request,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        return await CreateAsync(
            userId => service.AddDefectAsync(vehicleId, request, userId, cancellationToken),
            $"/api/vehicles/{vehicleId}/journal");
    }

    /// <summary>Прикрепляет фотографии к неисправности.</summary>
    [HttpPost("defects/{defectId:guid}/photos")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AddDefectPhoto(
        Guid defectId,
        IFormFile file,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (file.Length is <= 0 or > MaximumPhotoSize)
        {
            return InvalidFile("Фотография должна быть не более 8 МБ.");
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);

        var result = await service.AddDefectPhotosAsync(
            defectId,
            [new VehicleWorkPhotoUpload(Path.GetFileName(file.FileName), file.ContentType, stream.ToArray())],
            userId,
            cancellationToken);

        return this.ToActionResult(result, ids => Created($"/api/vehicles/defect-photos/{ids[0]}", new { id = ids[0] }));
    }

    /// <summary>Переводит неисправность в статус «в работе» (принятие в работу).</summary>
    [HttpPost("defects/{defectId:guid}/claim")]
    public async Task<IActionResult> ClaimDefect(
        Guid defectId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.ClaimDefectAsync(defectId, userId, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    /// <summary>Прикрепляет видео к неисправности.</summary>
    [HttpPost("defects/{defectId:guid}/videos")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AddDefectVideo(
        Guid defectId,
        IFormFile file,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (file.Length is <= 0 or > MaximumVideoSize)
        {
            return InvalidFile("Видео должно быть не более 100 МБ.");
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);

        var result = await service.AddDefectVideosAsync(
            defectId,
            [new VehicleMediaUpload(Path.GetFileName(file.FileName), file.ContentType, stream.ToArray())],
            userId,
            cancellationToken);

        return this.ToActionResult(result, ids => Created($"/api/vehicles/defect-videos/{ids[0]}", new { id = ids[0] }));
    }

    /// <summary>Возвращает файл видео неисправности по идентификатору.</summary>
    [HttpGet("defect-videos/{videoId:guid}")]
    public async Task<IActionResult> GetDefectVideo(
        Guid videoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var video = await service.GetDefectVideoStreamAsync(
            videoId,
            cancellationToken);
        return video is null
            ? NotFound()
            : File(video.Content, video.ContentType, enableRangeProcessing: true);
    }

    /// <summary>Выдаёт временный билет просмотра видео неисправности.</summary>
    [HttpPost("defect-videos/{videoId:guid}/stream-ticket")]
    public IActionResult CreateDefectVideoStreamTicket(
        Guid videoId,
        [FromServices] IMemoryCache cache)
    {
        var ticket = CreateVideoStreamTicket(
            cache,
            CreateVideoIdentity(videoId, false));
        return Ok(new { url = $"/api/vehicles/video-stream/{ticket}" });
    }

    /// <summary>Удаляет видео неисправности.</summary>
    [HttpDelete("defect-videos/{videoId:guid}")]
    public async Task<IActionResult> DeleteDefectVideo(
        Guid videoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.DeleteDefectVideoAsync(videoId, userId, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    /// <summary>Возвращает файл фотографии неисправности по идентификатору.</summary>
    [HttpGet("defect-photos/{photoId:guid}")]
    public async Task<IActionResult> GetDefectPhoto(
        Guid photoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var photo = await service.GetDefectPhotoAsync(photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Content, photo.ContentType, photo.FileName, enableRangeProcessing: true);
    }

    /// <summary>Удаляет фотографию неисправности.</summary>
    [HttpDelete("defect-photos/{photoId:guid}")]
    public async Task<IActionResult> DeleteDefectPhoto(
        Guid photoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.DeleteDefectPhotoAsync(photoId, userId, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    /// <summary>Добавляет запись о моточасах техники.</summary>
    [HttpPost("{vehicleId:guid}/hours")]
    public async Task<IActionResult> AddHours(
        Guid vehicleId,
        [FromBody] VehicleHoursRequest request,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        return await CreateAsync(
            userId => service.AddHoursAsync(vehicleId, request, userId, cancellationToken),
            $"/api/vehicles/{vehicleId}/journal");
    }

    /// <summary>Проверяет ежедневные показания и возвращает предварительный результат без записи.</summary>
    [HttpPost("hours/daily/preview")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> PreviewDailyHours([FromBody] DailyHoursRequest request,
        [FromServices] IVehicleService service, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return this.ToActionResult(await service.ProcessDailyHoursAsync(request, false, userId, cancellationToken), value => Ok(value));
    }

    /// <summary>Сохраняет проверенные ежедневные показания с контролем отпечатка предварительного результата.</summary>
    [HttpPost("hours/daily/commit")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> CommitDailyHours([FromBody] DailyHoursRequest request,
        [FromServices] IVehicleService service, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return this.ToActionResult(await service.ProcessDailyHoursAsync(request, true, userId, cancellationToken), value => Ok(value));
    }

    /// <summary>Импортирует моточасы техники из файла.</summary>
    [HttpPost("hours/import")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ImportHours(
        IFormFile file,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken,
        [FromForm] DateOnly? readingDate = null)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var extension = Path.GetExtension(file.FileName);
        if (file.Length is <= 0 or > MaximumHoursImportSize ||
            (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["file"] = ["Выберите файл CSV или XLSX размером не более 5 МБ."]
            }));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await service.ImportHoursAsync(stream, extension, userId, readingDate ?? DateOnly.FromDateTime(DateTime.Today), cancellationToken);
            return this.ToActionResult(result, value => Ok(value));
        }
        catch (DecoderFallbackException)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["file"] = ["CSV-файл должен быть сохранён в кодировке UTF-8."]
            }));
        }
    }

    /// <summary>Создаёт запись о ремонтных работах для неисправности.</summary>
    [HttpPost("{vehicleId:guid}/works")]
    public async Task<IActionResult> AddWork(
        Guid vehicleId,
        [FromBody] VehicleWorkRequest request,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        return await CreateAsync(
            userId => service.AddWorkAsync(vehicleId, request, userId, cancellationToken),
            $"/api/vehicles/{vehicleId}/journal");
    }

    /// <summary>Возвращает участников работы и состояние согласования их участия.</summary>
    [HttpGet("works/{workId:guid}/participants")]
    public async Task<IActionResult> GetWorkParticipants(Guid workId, [FromServices] IVehicleService service, CancellationToken cancellationToken)
    {
        var result = await service.GetWorkParticipantsAsync(workId, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }

    // The participant identity always comes from the authenticated session.
    /// <summary>Создаёт заявку текущего сотрудника на участие в указанной работе.</summary>
    [HttpPost("works/{workId:guid}/participants")]
    public async Task<IActionResult> RequestWorkParticipation(Guid workId, [FromServices] IVehicleService service, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await service.RequestWorkParticipationAsync(workId, userId, cancellationToken);
        return this.ToActionResult(result, _ => Ok());
    }

    /// <summary>Сохраняет решение по заявке на участие в работе.</summary>
    [HttpPost("works/{workId:guid}/participants/{participantId:guid}/review")]
    public async Task<IActionResult> ReviewWorkParticipation(Guid workId, Guid participantId,
        [FromBody] VehicleWorkParticipantReviewRequest request, [FromServices] IVehicleService service, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await service.ReviewWorkParticipationAsync(workId, participantId, request.Approved, userId, cancellationToken);
        return this.ToActionResult(result, _ => Ok());
    }

    /// <summary>Прикрепляет фотографии к ремонтным работам.</summary>
    [HttpPost("works/{workId:guid}/photos")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AddWorkPhoto(
        Guid workId,
        IFormFile file,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (file.Length is <= 0 or > MaximumPhotoSize)
        {
            return InvalidFile("Фотография должна быть не более 8 МБ.");
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);

        var result = await service.AddWorkPhotosAsync(
            workId,
            [new VehicleWorkPhotoUpload(Path.GetFileName(file.FileName), file.ContentType, stream.ToArray())],
            userId,
            cancellationToken);

        return this.ToActionResult(result, ids => Created($"/api/vehicles/work-photos/{ids[0]}", new { id = ids[0] }));
    }

    /// <summary>Возвращает файл фотографии ремонтных работ по идентификатору.</summary>
    [HttpGet("work-photos/{photoId:guid}")]
    public async Task<IActionResult> GetWorkPhoto(
        Guid photoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var photo = await service.GetWorkPhotoAsync(photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Content, photo.ContentType, photo.FileName, enableRangeProcessing: true);
    }

    /// <summary>Удаляет фотографию ремонтных работ.</summary>
    [HttpDelete("work-photos/{photoId:guid}")]
    public async Task<IActionResult> DeleteWorkPhoto(
        Guid photoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.DeleteWorkPhotoAsync(photoId, userId, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    /// <summary>Прикрепляет видео к ремонтным работам.</summary>
    [HttpPost("works/{workId:guid}/videos")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AddWorkVideo(
        Guid workId,
        IFormFile file,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (file.Length is <= 0 or > MaximumVideoSize)
        {
            return InvalidFile("Видео должно быть не более 100 МБ.");
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);

        var result = await service.AddWorkVideosAsync(
            workId,
            [new VehicleMediaUpload(Path.GetFileName(file.FileName), file.ContentType, stream.ToArray())],
            userId,
            cancellationToken);

        return this.ToActionResult(result, ids => Created($"/api/vehicles/work-videos/{ids[0]}", new { id = ids[0] }));
    }

    /// <summary>Возвращает файл видео ремонтных работ по идентификатору.</summary>
    [HttpGet("work-videos/{videoId:guid}")]
    public async Task<IActionResult> GetWorkVideo(
        Guid videoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var video = await service.GetWorkVideoStreamAsync(
            videoId,
            cancellationToken);
        return video is null
            ? NotFound()
            : File(video.Content, video.ContentType, enableRangeProcessing: true);
    }

    /// <summary>Выдаёт временный билет просмотра видео выполненной работы.</summary>
    [HttpPost("work-videos/{videoId:guid}/stream-ticket")]
    public IActionResult CreateWorkVideoStreamTicket(
        Guid videoId,
        [FromServices] IMemoryCache cache)
    {
        var ticket = CreateVideoStreamTicket(
            cache,
            CreateVideoIdentity(videoId, true));
        return Ok(new { url = $"/api/vehicles/video-stream/{ticket}" });
    }

    /// <summary>Выдаёт видеопоток после проверки билета и актуальности пользовательской сессии.</summary>
    [HttpGet("video-stream/{ticket}")]
    [AllowAnonymous]
    public async Task<IActionResult> StreamVideo(
        string ticket,
        [FromServices] IUserSessionStore sessions,
        [FromServices] IMemoryCache cache,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue<VideoStreamTicket>(
                GetVideoStreamTicketKey(ticket),
                out var streamTicket) ||
            streamTicket is null)
        {
            return NotFound();
        }

        if (!await sessions.IsCurrentAsync(streamTicket.UserId, streamTicket.Stamp, cancellationToken))
        {
            cache.Remove(GetVideoStreamTicketKey(ticket));
            return Unauthorized();
        }

        var video = streamTicket.IsWorkVideo
            ? await service.GetWorkVideoStreamAsync(
                streamTicket.VideoId,
                cancellationToken)
            : await service.GetDefectVideoStreamAsync(
                streamTicket.VideoId,
                cancellationToken);
        if (video is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        return File(
            video.Content,
            video.ContentType,
            enableRangeProcessing: true);
    }

    /// <summary>Удаляет видео ремонтных работ.</summary>
    [HttpDelete("work-videos/{videoId:guid}")]
    public async Task<IActionResult> DeleteWorkVideo(
        Guid videoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.DeleteWorkVideoAsync(videoId, userId, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    /// <summary>Добавляет заявку на закупку запчастей для неисправности.</summary>
    [HttpPost("defects/{defectId:guid}/parts-requests")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AddPartsRequest(
        Guid defectId,
        [FromBody] VehiclePartsRequest request,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.AddPartsRequestAsync(
            defectId, request, userId, cancellationToken);
        return this.ToActionResult(result, id => Created(
            $"/api/vehicles/defects/{defectId}/parts-requests/{id}",
            new { id }));
    }

    /// <summary>Обновляет реквизиты и состояние заявки на закупку запчастей.</summary>
    [HttpPut("parts-requests/{requestId:guid}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UpdatePartsRequest(
        Guid requestId,
        [FromBody] VehiclePartsRequest request,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.UpdatePartsRequestAsync(
            requestId, request, userId, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    /// <summary>Удаляет заявку на закупку запчастей.</summary>
    [HttpDelete("parts-requests/{requestId:guid}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DeletePartsRequest(
        Guid requestId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.DeletePartsRequestAsync(
            requestId, userId, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    /// <summary>Удаляет запись журнала техники (закупка, неисправность, моточасы или работа).</summary>
    [HttpPut("history/{category}/{id:guid}")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> UpdateHistoryEntry(string category, Guid id,
        [FromBody] VehicleHistoryEditRequest request, [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (category is not ("works" or "defects") || string.IsNullOrWhiteSpace(request.Description)) return BadRequest();
        return await service.UpdateHistoryEntryAsync(category, id, request.Description, userId, cancellationToken)
            ? NoContent() : NotFound();
    }

    /// <summary>Удаляет указанную запись истории техники.</summary>
    [HttpDelete("{category}/{id:guid}")]
    public async Task<IActionResult> DeleteEntry(
        string category,
        Guid id,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (category == "hours" && !await service.CanManageHoursAsync(userId, cancellationToken)) return Forbid();
        var deleted = await service.DeleteEntryAsync(category, id, userId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Выполняет создание записи журнала от имени текущего пользователя и возвращает <c>201 Created</c>.</summary>
    private async Task<IActionResult> CreateAsync(
        Func<Guid, Task<MyApp.Application.Common.ServiceResult<Guid>>> create,
        string location)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await create(userId);
        return this.ToActionResult(result, id => Created(location, new { id }));
    }

    /// <summary>Пытается извлечь идентификатор текущего пользователя из claims.</summary>
    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out userId);
    }

    /// <summary>Формирует ответ <c>400 Bad Request</c> с сообщением об ошибке файла.</summary>
    private IActionResult InvalidFile(string message) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = [message] }));

    /// <summary>Создаёт временный билет, связывающий видео с текущей сессией пользователя.</summary>
    private static string CreateVideoStreamTicket(
        IMemoryCache cache,
        VideoStreamTicket ticket)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set(
            GetVideoStreamTicketKey(token),
            ticket,
            TimeSpan.FromHours(1));
        return token;
    }

    /// <summary>Формирует ключ кеша для временного билета просмотра видео.</summary>
    private static string GetVideoStreamTicketKey(string ticket) =>
        $"vehicle-video-stream:{ticket}";

    /// <summary>Считывает идентификатор пользователя и метку сессии для билета просмотра видео.</summary>
    private VideoStreamTicket CreateVideoIdentity(Guid videoId, bool isWorkVideo)
    {
        if (!SessionIdentity.TryRead(User, out var userId, out var stamp))
            throw new InvalidOperationException("Authenticated session identity is missing.");
        return new(videoId, isWorkVideo, userId, stamp);
    }

    /// <summary>Временное разрешение на чтение конкретного видео в рамках сессии.</summary>
    private sealed record VideoStreamTicket(Guid VideoId, bool IsWorkVideo, Guid UserId, Guid Stamp);
}
