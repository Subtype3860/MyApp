using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class MaterialGroupController : ControllerBase
{
    [HttpGet("/api/material-groups/map")]
    public async Task<IActionResult> GetMappings(
        [FromQuery] string sourceTable,
        [FromServices] IMaterialGroupService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetMappingsAsync(sourceTable, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }

    [HttpGet("/api/admin/material-groups")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> GetAllAdmin(
        [FromServices] IMaterialGroupService service,
        CancellationToken cancellationToken)
    {
        var groups = await service.GetAllAsync(cancellationToken);
        return Ok(groups);
    }

    [HttpPost("/api/admin/material-groups")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> CreateAdmin(
        [FromBody] MaterialGroupRequest request,
        [FromServices] IMaterialGroupService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateGroupAsync(request, cancellationToken);
        return this.ToActionResult(result, id => Created($"/api/admin/material-groups/{id}", new { id }));
    }

    [HttpPost("/api/admin/material-groups/{groupId:guid}/items")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> AddItemAdmin(
        Guid groupId,
        [FromBody] MaterialGroupItemRequest request,
        [FromServices] IMaterialGroupService service,
        CancellationToken cancellationToken)
    {
        var result = await service.AddItemAsync(groupId, request, cancellationToken);
        return this.ToActionResult(result, id => Created($"/api/admin/material-groups/{groupId}/items/{id}", new { id }));
    }

    [HttpDelete("/api/admin/material-groups/{id:guid}")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> DeleteGroupAdmin(
        Guid id,
        [FromServices] IMaterialGroupService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteGroupAsync(id, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    [HttpDelete("/api/admin/material-groups/items/{id:guid}")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> DeleteItemAdmin(
        Guid id,
        [FromServices] IMaterialGroupService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteItemAsync(id, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }
}
