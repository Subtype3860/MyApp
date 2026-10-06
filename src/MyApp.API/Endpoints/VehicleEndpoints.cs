using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MyApp.Application.DTO;
using MyApp.Application.Services;

namespace MyApp.API.Endpoints;

public static class VehicleEndpoints
{
    private const long MaximumHoursImportSize = 5 * 1024 * 1024;
    private const long MaximumPhotoSize = 8 * 1024 * 1024;
    private const long MaximumVideoSize = 100 * 1024 * 1024;
    private const long MaximumRequestFileSize = 20 * 1024 * 1024;

    public static IEndpointRouteBuilder MapVehicleEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles").RequireAuthorization();

        group.MapGet("/", async (
            IVehicleService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetVehiclesAsync(cancellationToken)));

        group.MapGet("/{vehicleId:guid}/journal", async (
            Guid vehicleId,
            DateOnly? from,
            DateOnly? to,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            var journal = await service.GetJournalAsync(
                vehicleId, from, to, cancellationToken);
            return journal is null ? Results.NotFound() : Results.Ok(journal);
        });

        group.MapPost("/{vehicleId:guid}/purchases", async (
            Guid vehicleId,
            VehiclePurchaseRequest request,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
            await CreateAsync(
                principal,
                userId => service.AddPurchaseAsync(
                    vehicleId, request, userId, cancellationToken),
                $"/api/vehicles/{vehicleId}/journal"));

        group.MapPost("/{vehicleId:guid}/defects", async (
            Guid vehicleId,
            VehicleDefectRequest request,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
            await CreateAsync(
                principal,
                userId => service.AddDefectAsync(
                    vehicleId, request, userId, cancellationToken),
                $"/api/vehicles/{vehicleId}/journal"));

        group.MapPost("/defects/{defectId:guid}/photos", async (
            Guid defectId,
            IFormFile file,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            if (file.Length is <= 0 or > MaximumPhotoSize)
            {
                return InvalidFile("Фотография должна быть не более 8 МБ.");
            }
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            var result = await service.AddDefectPhotosAsync(
                defectId,
                [
                    new VehicleWorkPhotoUpload(
                        Path.GetFileName(file.FileName),
                        file.ContentType,
                        stream.ToArray())
                ],
                userId,
                cancellationToken);
            return result.ToHttpResult(ids => Results.Created(
                $"/api/vehicles/defect-photos/{ids[0]}",
                new { id = ids[0] }));
        }).DisableAntiforgery();

        group.MapPost("/defects/{defectId:guid}/claim", async (
            Guid defectId,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            return (await service.ClaimDefectAsync(
                defectId, userId, cancellationToken))
                .ToHttpResult(_ => Results.NoContent());
        });

        group.MapPost("/defects/{defectId:guid}/videos", async (
            Guid defectId,
            IFormFile file,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            if (file.Length is <= 0 or > MaximumVideoSize)
            {
                return InvalidFile("Видео должно быть не более 100 МБ.");
            }
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            var result = await service.AddDefectVideosAsync(
                defectId,
                [new(
                    Path.GetFileName(file.FileName),
                    file.ContentType,
                    stream.ToArray())],
                userId,
                cancellationToken);
            return result.ToHttpResult(ids => Results.Created(
                $"/api/vehicles/defect-videos/{ids[0]}",
                new { id = ids[0] }));
        }).DisableAntiforgery();

        group.MapGet("/defect-videos/{videoId:guid}", async (
            Guid videoId,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            var video = await service.GetDefectVideoAsync(
                videoId, cancellationToken);
            return video is null
                ? Results.NotFound()
                : Results.File(
                    video.Content, video.ContentType, video.FileName,
                    enableRangeProcessing: true);
        });

        group.MapDelete("/defect-videos/{videoId:guid}", async (
            Guid videoId,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            return (await service.DeleteDefectVideoAsync(
                videoId, userId, cancellationToken))
                .ToHttpResult(_ => Results.NoContent());
        });

        group.MapGet("/defect-photos/{photoId:guid}", async (
            Guid photoId,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            var photo = await service.GetDefectPhotoAsync(
                photoId, cancellationToken);
            return photo is null
                ? Results.NotFound()
                : Results.File(
                    photo.Content,
                    photo.ContentType,
                    photo.FileName,
                    enableRangeProcessing: true);
        });

        group.MapDelete("/defect-photos/{photoId:guid}", async (
            Guid photoId,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            return (await service.DeleteDefectPhotoAsync(
                photoId, userId, cancellationToken))
                .ToHttpResult(_ => Results.NoContent());
        });

        group.MapPost("/{vehicleId:guid}/hours", async (
            Guid vehicleId,
            VehicleHoursRequest request,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
            await CreateAsync(
                principal,
                userId => service.AddHoursAsync(
                    vehicleId, request, userId, cancellationToken),
                $"/api/vehicles/{vehicleId}/journal"));

        group.MapPost("/hours/import", async (
            IFormFile file,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            var extension = Path.GetExtension(file.FileName);
            if (file.Length is <= 0 or > MaximumHoursImportSize ||
                !string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["file"] = ["Выберите файл CSV или XLSX размером не более 5 МБ."]
                    });
            }

            try
            {
                await using var stream = file.OpenReadStream();
                return Results.Ok(await service.ImportHoursAsync(
                    stream,
                    extension,
                    userId,
                    DateOnly.FromDateTime(DateTime.Today),
                    cancellationToken));
            }
            catch (DecoderFallbackException)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["file"] = ["CSV-файл должен быть сохранён в кодировке UTF-8."]
                    });
            }
        }).DisableAntiforgery();

        group.MapPost("/{vehicleId:guid}/works", async (
            Guid vehicleId,
            VehicleWorkRequest request,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
            await CreateAsync(
                principal,
                userId => service.AddWorkAsync(
                    vehicleId, request, userId, cancellationToken),
                $"/api/vehicles/{vehicleId}/journal"));

        group.MapPost("/works/{workId:guid}/photos", async (
            Guid workId,
            IFormFile file,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            if (file.Length is <= 0 or > MaximumPhotoSize)
            {
                return InvalidFile("Фотография должна быть не более 8 МБ.");
            }
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            var result = await service.AddWorkPhotosAsync(
                workId,
                [
                    new VehicleWorkPhotoUpload(
                        Path.GetFileName(file.FileName),
                        file.ContentType,
                        stream.ToArray())
                ],
                userId,
                cancellationToken);
            return result.ToHttpResult(ids => Results.Created(
                $"/api/vehicles/work-photos/{ids[0]}",
                new { id = ids[0] }));
        }).DisableAntiforgery();

        group.MapGet("/work-photos/{photoId:guid}", async (
            Guid photoId,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            var photo = await service.GetWorkPhotoAsync(
                photoId, cancellationToken);
            return photo is null
                ? Results.NotFound()
                : Results.File(
                    photo.Content,
                    photo.ContentType,
                    photo.FileName,
                    enableRangeProcessing: true);
        });

        group.MapDelete("/work-photos/{photoId:guid}", async (
            Guid photoId,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            return (await service.DeleteWorkPhotoAsync(
                photoId, userId, cancellationToken))
                .ToHttpResult(_ => Results.NoContent());
        });

        group.MapPost("/works/{workId:guid}/videos", async (
            Guid workId,
            IFormFile file,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            if (file.Length is <= 0 or > MaximumVideoSize)
            {
                return InvalidFile("Видео должно быть не более 100 МБ.");
            }
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            var result = await service.AddWorkVideosAsync(
                workId,
                [new(
                    Path.GetFileName(file.FileName),
                    file.ContentType,
                    stream.ToArray())],
                userId,
                cancellationToken);
            return result.ToHttpResult(ids => Results.Created(
                $"/api/vehicles/work-videos/{ids[0]}",
                new { id = ids[0] }));
        }).DisableAntiforgery();

        group.MapGet("/work-videos/{videoId:guid}", async (
            Guid videoId,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            var video = await service.GetWorkVideoAsync(
                videoId, cancellationToken);
            return video is null
                ? Results.NotFound()
                : Results.File(
                    video.Content, video.ContentType, video.FileName,
                    enableRangeProcessing: true);
        });

        group.MapDelete("/work-videos/{videoId:guid}", async (
            Guid videoId,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            return (await service.DeleteWorkVideoAsync(
                videoId, userId, cancellationToken))
                .ToHttpResult(_ => Results.NoContent());
        });

        group.MapPut("/works/{workId:guid}/parts-request", async (
            Guid workId,
            HttpRequest httpRequest,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            var form = await httpRequest.ReadFormAsync(cancellationToken);
            if (!DateOnly.TryParse(form["requestDate"], out var requestDate))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["requestDate"] = ["Укажите дату заявки."]
                    });
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
                new(
                    form["requestNumber"].ToString(),
                    requestDate,
                    file is null ? null : Path.GetFileName(file.FileName),
                    file?.ContentType,
                    content),
                userId,
                cancellationToken);
            return result.ToHttpResult(_ => Results.NoContent());
        }).DisableAntiforgery();

        group.MapDelete("/works/{workId:guid}/parts-request", async (
            Guid workId,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            return (await service.DeletePartsRequestAsync(
                workId, userId, cancellationToken))
                .ToHttpResult(_ => Results.NoContent());
        });

        group.MapGet("/works/{workId:guid}/parts-request/file", async (
            Guid workId,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            var file = await service.GetPartsRequestFileAsync(
                workId, cancellationToken);
            return file is null
                ? Results.NotFound()
                : Results.File(
                    file.Content, file.ContentType, file.FileName,
                    enableRangeProcessing: true);
        });

        group.MapDelete("/defects/{defectId:guid}/history", async (
            Guid defectId,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            return await service.DeleteEntryAsync(
                    "defects", defectId, userId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        }).RequireAuthorization(policy => policy.RequireRole("administrator"));

        group.MapDelete("/works/{workId:guid}/history", async (
            Guid workId,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }
            return await service.DeleteEntryAsync(
                    "works", workId, userId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        }).RequireAuthorization(policy => policy.RequireRole("administrator"));

        group.MapDelete("/{category}/{id:guid}", async (
            string category,
            Guid id,
            ClaimsPrincipal principal,
            IVehicleService service,
            CancellationToken cancellationToken) =>
            TryGetUserId(principal, out var userId) &&
            await service.DeleteEntryAsync(category, id, userId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound());

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        ClaimsPrincipal principal,
        Func<Guid, Task<MyApp.Application.Common.ServiceResult<Guid>>> create,
        string location)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }
        return (await create(userId)).ToHttpResult(
            id => Results.Created(location, new { id }));
    }

    private static bool TryGetUserId(
        ClaimsPrincipal principal,
        out Guid userId)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out userId);
    }

    private static IResult InvalidFile(string message) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { ["file"] = [message] });
}
