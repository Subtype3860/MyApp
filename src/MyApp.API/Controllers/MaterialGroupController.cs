using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

/// <summary>HTTP-эндпоинты групп материалов.</summary>
[ApiController]
[Route("api")]
[Authorize]
public class MaterialGroupController : ControllerBase
{
    /// <summary>Возвращает соответствия материалов их группам.</summary>
    [HttpGet("/api/material-groups/map")]
    public async Task<IActionResult> GetMappings(
        [FromQuery] string sourceTable,
        [FromServices] IMaterialGroupService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetMappingsAsync(sourceTable, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }

    /// <summary>Возвращает группы материалов для административного интерфейса.</summary>
    [HttpGet("/api/admin/material-groups")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> GetAllAdmin(
        [FromServices] IMaterialGroupService service,
        CancellationToken cancellationToken)
    {
        var groups = await service.GetAllAsync(cancellationToken);
        return Ok(groups);
    }

    /// <summary>Создаёт группу материалов из административного интерфейса.</summary>
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

    /// <summary>Добавляет материал в группу из административного интерфейса.</summary>
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

    /// <summary>Удаляет группу материалов из административного интерфейса.</summary>
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

    /// <summary>Удаляет материал из группы через административный интерфейс.</summary>
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
