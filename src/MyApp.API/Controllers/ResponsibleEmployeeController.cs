using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

/// <summary>HTTP-эндпоинты выбора ответственных сотрудников.</summary>
[ApiController]
[Route("api/employees")]
[Authorize]
public class ResponsibleEmployeeController : ControllerBase
{
    /// <summary>Возвращает сотрудников, доступных для выбора ответственного.</summary>
    [HttpGet("responsible")]
    public async Task<IActionResult> GetResponsible(
        [FromQuery] string sourceTable,
        [FromServices] IResponsibleEmployeeService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(sourceTable, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }
}
