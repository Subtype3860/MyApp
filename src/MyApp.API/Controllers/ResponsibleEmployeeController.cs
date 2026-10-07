using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/employees")]
[Authorize]
public class ResponsibleEmployeeController : ControllerBase
{
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
