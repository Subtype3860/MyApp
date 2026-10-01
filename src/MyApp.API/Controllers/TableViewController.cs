using System.Security.Claims;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/tables")]
[Authorize(Policy = Permissions.TablesView)]
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
        var requiredPermission = tableName switch
        {
            "v_full_ost" => "tables.v_full_ost",
            "v_meh_ost" => "tables.v_meh_ost",
            "v_workers" => "tables.v_workers",
            _ => null
        };
        if (requiredPermission is not null &&
            !User.IsInRole("administrator") &&
            !User.HasClaim("permission", requiredPermission))
        {
            return Forbid();
        }

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
