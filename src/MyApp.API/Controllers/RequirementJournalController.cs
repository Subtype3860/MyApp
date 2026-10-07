using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class RequirementJournalController : ControllerBase
{
    [HttpGet("/api/requirements")]
    public async Task<IActionResult> GetRecent(
        [FromServices] IRequirementJournalService service,
        CancellationToken cancellationToken)
    {
        var requirements = await service.GetRecentAsync(cancellationToken);
        return Ok(requirements);
    }

    [HttpGet("/api/requirements/{id:guid}/document")]
    public async Task<IActionResult> GetDocument(
        Guid id,
        [FromServices] IRequirementJournalService service,
        CancellationToken cancellationToken)
    {
        var document = await service.GetDocumentAsync(id, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpDelete("/api/requirements/{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] IRequirementJournalService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await service.DeleteAsync(id, cancellationToken);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Не удалось восстановить остаток");
        }
    }
}
