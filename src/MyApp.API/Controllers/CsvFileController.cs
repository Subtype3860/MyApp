using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/admin/csv-files")]
[Authorize(Roles = "administrator")]
public class CsvFileController : ControllerBase
{
    private const long MaximumCsvSize = 20 * 1024 * 1024;

    [HttpGet("{fileName}")]
    public async Task<IActionResult> Get(
        string fileName,
        [FromServices] ICsvFileService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetAsync(fileName, cancellationToken);
            return this.ToActionResult(result, content => File(content, "text/csv", fileName));
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

    [HttpPost("{fileName}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Replace(
        string fileName,
        IFormFile file,
        [FromServices] ICsvFileService service,
        CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumCsvSize ||
            !string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["file"] = ["Выберите CSV-файл размером не более 20 МБ."]
            }));
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);

        try
        {
            var result = await service.ReplaceAsync(fileName, stream.ToArray(), cancellationToken);
            return this.ToActionResult(result, _ => Ok());
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
