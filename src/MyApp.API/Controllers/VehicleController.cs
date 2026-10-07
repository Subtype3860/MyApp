using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/vehicles")]
[Authorize]
public class VehicleController : ControllerBase
{
    private const long MaximumHoursImportSize = 5 * 1024 * 1024;
    private const long MaximumPhotoSize = 8 * 1024 * 1024;
    private const long MaximumVideoSize = 100 * 1024 * 1024;
    private const long MaximumRequestFileSize = 20 * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> GetVehicles(
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var vehicles = await service.GetVehiclesAsync(cancellationToken);
        return Ok(vehicles);
    }

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

    [HttpGet("defect-videos/{videoId:guid}")]
    public async Task<IActionResult> GetDefectVideo(
        Guid videoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var video = await service.GetDefectVideoAsync(videoId, cancellationToken);
        return video is null ? NotFound() : File(video.Content, video.ContentType, video.FileName, enableRangeProcessing: true);
    }

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

    [HttpGet("defect-photos/{photoId:guid}")]
    public async Task<IActionResult> GetDefectPhoto(
        Guid photoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var photo = await service.GetDefectPhotoAsync(photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Content, photo.ContentType, photo.FileName, enableRangeProcessing: true);
    }

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

    [HttpPost("hours/import")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ImportHours(
        IFormFile file,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
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
            var result = await service.ImportHoursAsync(stream, extension, userId, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
            return Ok(result);
        }
        catch (DecoderFallbackException)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["file"] = ["CSV-файл должен быть сохранён в кодировке UTF-8."]
            }));
        }
    }

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

    [HttpGet("work-photos/{photoId:guid}")]
    public async Task<IActionResult> GetWorkPhoto(
        Guid photoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var photo = await service.GetWorkPhotoAsync(photoId, cancellationToken);
        return photo is null ? NotFound() : File(photo.Content, photo.ContentType, photo.FileName, enableRangeProcessing: true);
    }

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

    [HttpGet("work-videos/{videoId:guid}")]
    public async Task<IActionResult> GetWorkVideo(
        Guid videoId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var video = await service.GetWorkVideoAsync(videoId, cancellationToken);
        return video is null ? NotFound() : File(video.Content, video.ContentType, video.FileName, enableRangeProcessing: true);
    }

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

    [HttpPut("works/{workId:guid}/parts-request")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UpdatePartsRequest(
        Guid workId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var form = await Request.ReadFormAsync(cancellationToken);
        if (!DateOnly.TryParse(form["requestDate"], out var requestDate))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["requestDate"] = ["Укажите дату заявки."]
            }));
        }

        var file = form.Files.GetFile("file");
        if (file?.Length > MaximumRequestFileSize)
        {
            return InvalidFile("Файл заявки должен быть не более 20 МБ.");
        }

        byte[]? content = null;
        if (file is { Length: > 0 })
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            content = stream.ToArray();
        }

        var result = await service.UpdatePartsRequestAsync(
            workId,
            new VehiclePartsRequest(
                form["requestNumber"].ToString(),
                requestDate,
                file is null ? null : Path.GetFileName(file.FileName),
                file?.ContentType,
                content),
            userId,
            cancellationToken);

        return this.ToActionResult(result, _ => NoContent());
    }

    [HttpDelete("works/{workId:guid}/parts-request")]
    public async Task<IActionResult> DeletePartsRequest(
        Guid workId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await service.DeletePartsRequestAsync(
            workId, userId, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    [HttpGet("works/{workId:guid}/parts-request/file")]
    public async Task<IActionResult> GetPartsRequestFile(
        Guid workId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        var file = await service.GetPartsRequestFileAsync(workId, cancellationToken);
        return file is null ? NotFound() : File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    [HttpDelete("defects/{defectId:guid}/history")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> DeleteDefectHistory(
        Guid defectId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        return await service.DeleteEntryAsync(
                "defects", defectId, userId, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpDelete("works/{workId:guid}/history")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> DeleteWorkHistory(
        Guid workId,
        [FromServices] IVehicleService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        return await service.DeleteEntryAsync(
                "works", workId, userId, cancellationToken)
            ? NoContent()
            : NotFound();
    }

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

        var deleted = await service.DeleteEntryAsync(category, id, userId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

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

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out userId);
    }

    private IActionResult InvalidFile(string message) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = [message] }));
}
