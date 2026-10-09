using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ComponentDocumentController : ControllerBase
{
    [HttpPost("/api/documents/components/preview")]
    public async Task<IActionResult> Preview(
        [FromBody] ComponentDocumentRequest request,
        [FromServices] IComponentDocumentService documentService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await documentService.PrepareAsync(request, userId, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }

    [HttpPost("/api/documents/components")]
    public async Task<IActionResult> Generate(
        [FromBody] ComponentDocumentRequest request,
        [FromServices] IComponentDocumentService documentService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await documentService.GenerateAsync(request, userId, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }

    [HttpGet("/api/documents/components/template")]
    [Produces("application/pdf")]
    public IActionResult GetTemplate(
        [FromServices] IConfiguration configuration,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] ILogger<ComponentDocumentController> logger)
    {
        var configuredPath =
            configuration.GetValue<string>("DocumentTemplates:ComponentIssuePath");
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            logger.LogError("PDF component issue template path is not configured.");
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "PDF-шаблон не настроен",
                detail: "Администратору необходимо указать DocumentTemplates:ComponentIssuePath.");
        }

        try
        {
            // Relative paths are resolved against the published API directory.
            // Keep the stream open for MVC to send it; MVC disposes it afterward.
            var template = ComponentPdfTemplateFile.OpenRead(
                configuredPath, environment.ContentRootPath);
            return File(template, "application/pdf", enableRangeProcessing: true);
        }
        catch (FileNotFoundException error)
        {
            logger.LogWarning(error, "PDF component issue template not found: {Path}",
                configuredPath);
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "PDF-шаблон не найден",
                detail: "Файл шаблона требования не найден на сервере. " +
                    "Администратору необходимо проверить DocumentTemplates:ComponentIssuePath.");
        }
        catch (DirectoryNotFoundException error)
        {
            logger.LogWarning(error, "PDF template directory does not exist: {Path}",
                configuredPath);
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "PDF-шаблон не найден",
                detail: "Каталог с PDF-шаблоном отсутствует на сервере. " +
                    "Проверьте DocumentTemplates:ComponentIssuePath.");
        }
        catch (InvalidDataException error)
        {
            logger.LogError(error, "Invalid PDF component issue template: {Path}",
                configuredPath);
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Некорректный PDF-шаблон",
                detail: "Настроенный файл не является PDF-документом. " +
                    "Администратору необходимо заменить файл шаблона.");
        }
        catch (UnauthorizedAccessException error)
        {
            logger.LogError(error, "PDF template access denied: {Path}",
                configuredPath);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Нет доступа к PDF-шаблону",
                detail: "Серверу не разрешено читать PDF-шаблон. " +
                    "Администратору необходимо проверить права доступа к файлу.");
        }
        catch (IOException error)
        {
            logger.LogError(error, "Unable to read PDF template: {Path}",
                configuredPath);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Не удалось прочитать PDF-шаблон",
                detail: "На сервере произошла ошибка чтения PDF-шаблона.");
        }
        catch (ArgumentException error)
        {
            logger.LogError(error, "Invalid PDF template path configuration.");
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Некорректный путь PDF-шаблона",
                detail: "Администратору необходимо исправить DocumentTemplates:ComponentIssuePath.");
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(userIdValue, out userId);
    }
}
