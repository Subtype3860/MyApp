using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/employees/signature")]
[Authorize(Roles = "administrator")]
public class EmployeeSignatureController : ControllerBase
{
    private const long MaximumSignatureSize = 2 * 1024 * 1024;
    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string lastName,
        [FromQuery] string firstName,
        [FromQuery] string? patronymic,
        [FromServices] IEmployeeSignatureService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(CreateKey(lastName, firstName, patronymic), cancellationToken);
        return this.ToActionResult(result, signature => File(signature.Content, signature.ContentType, enableRangeProcessing: false));
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Save(
        IFormFile signature,
        [FromQuery] string lastName,
        [FromQuery] string firstName,
        [FromQuery] string? patronymic,
        [FromServices] IEmployeeSignatureService service,
        CancellationToken cancellationToken)
    {
        if (signature.Length is <= 0 or > MaximumSignatureSize ||
            !AllowedContentTypes.Contains(signature.ContentType))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["signature"] = ["Выберите изображение JPEG, PNG или WebP размером не более 2 МБ."]
            }));
        }

        await using var stream = new MemoryStream();
        await signature.CopyToAsync(stream, cancellationToken);

        var result = await service.SaveAsync(
            CreateKey(lastName, firstName, patronymic),
            stream.ToArray(),
            signature.ContentType,
            cancellationToken);

        return this.ToActionResult(result, _ => Ok());
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        [FromQuery] string lastName,
        [FromQuery] string firstName,
        [FromQuery] string? patronymic,
        [FromServices] IEmployeeSignatureService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(CreateKey(lastName, firstName, patronymic), cancellationToken);
        return this.ToActionResult(result, _ => Ok());
    }

    private static EmployeeSignatureKey CreateKey(string lastName, string firstName, string? patronymic) =>
        new(lastName, firstName, patronymic ?? string.Empty);
}
