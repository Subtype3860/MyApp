using MyApp.Application.Services;
using MyApp.Application.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace MyApp.API.Controllers;

/// <summary>HTTP-эндпоинты импорта и экспорта табличных данных.</summary>
[ApiController]
[Route("api/admin/csv-files")]
[Route("api/admin/imports")]
[Authorize(Roles = "administrator")]
public class CsvFileController : ControllerBase
{
    private const long MaximumCsvSize = 20 * 1024 * 1024;

    /// <summary>Возвращает данные табличного источника.</summary>
    [HttpGet("{fileName}")]
    public async Task<IActionResult> Get(
        string fileName,
        [FromServices] ICsvFileService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetAsync(fileName, cancellationToken);
            return this.ToActionResult(result, content => File(content, "text/csv", Path.ChangeExtension(fileName, ".csv")));
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError, title: "Не удалось прочитать CSV-файл");
        }
        catch (PostgresException exception)
        {
            return Problem(exception.MessageText, statusCode: StatusCodes.Status500InternalServerError, title: "PostgreSQL не разрешил прочитать CSV-файл");
        }
    }

    /// <summary>Возвращает имена листов загруженной книги Excel.</summary>
    [HttpPost("sheets")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 21 * 1024 * 1024)]
    public async Task<IActionResult> Sheets(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumCsvSize || !Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { errors = new { file = new[] { "Выберите XLSX размером не более 20 МБ." } } });
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        try { return Ok(ImportWorkbook.Sheets(stream.ToArray())); }
        catch (CsvImportValidationException exception)
        { return BadRequest(new { errors = new { file = new[] { exception.Message } } }); }
    }

    /// <summary>Возвращает результаты проверки загруженного файла перед импортом.</summary>
    [HttpPost("{fileName}/preview")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 21 * 1024 * 1024)]
    public async Task<IActionResult> Preview(string fileName, IFormFile file, [FromForm] string? sheetName,
        [FromServices] ICsvFileService service, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumCsvSize || !(new[] { ".csv", ".xlsx" }).Contains(Path.GetExtension(file.FileName), StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { errors = new { file = new[] { "Выберите CSV или XLSX размером не более 20 МБ." } } });
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        try
        {
            var result = await service.PreviewAsync(fileName, stream.ToArray(), cancellationToken, Path.GetExtension(file.FileName), sheetName);
            return this.ToActionResult(result, value => Ok(value));
        }
        catch (CsvImportValidationException exception)
        { return BadRequest(new { errors = new { file = new[] { exception.Message } } }); }
    }

    /// <summary>Заменяет данные табличного источника содержимым проверенного импорта.</summary>
    [HttpPost("{fileName}")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 21 * 1024 * 1024)]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Replace(
        string fileName,
        IFormFile file,
        [FromForm] string? sheetName,
        [FromServices] ICsvFileService service,
        CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumCsvSize ||
            !(new[] { ".csv", ".xlsx" }).Contains(Path.GetExtension(file.FileName), StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["file"] = ["Выберите CSV или XLSX размером не более 20 МБ."]
            }));
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);

        try
        {
            var result = await service.ReplaceAsync(fileName, stream.ToArray(), cancellationToken, Path.GetExtension(file.FileName), sheetName);
            return this.ToActionResult(result, value => Ok(value));
        }
        catch (CsvImportValidationException exception)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = [exception.Message] }));
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError, title: "Не удалось заменить CSV-файл");
        }
        catch (PostgresException exception)
        {
            return Problem(exception.MessageText, statusCode: StatusCodes.Status500InternalServerError, title: "PostgreSQL не разрешил заменить CSV-файл");
        }
    }
}
