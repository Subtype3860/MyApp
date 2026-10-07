using System.Security.Claims;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/tables")]
[Authorize]
public class TableViewController : ControllerBase
{
    [HttpGet("{tableName}")]
    public async Task<IActionResult> Get(
        string tableName,
        [FromServices] ITableViewService tableViewService,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] string pageSize = "20",
        [FromQuery] string? search = null,
        [FromQuery] string? lastName = null,
        [FromQuery] string? firstName = null,
        [FromQuery] string? patronymic = null,
        [FromQuery] string? profession = null)
    {
        var query = new TableViewRequest(
            tableName,
            page,
            pageSize,
            search,
            lastName,
            firstName,
            patronymic,
            profession,
            User.IsInRole("administrator"));

        var result = await tableViewService.GetAsync(query, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }
}
