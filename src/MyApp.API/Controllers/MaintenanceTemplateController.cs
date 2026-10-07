using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api")]
public class MaintenanceTemplateController : ControllerBase
{
    [HttpGet("/api/maintenance-templates")]
    [Authorize]
    public async Task<IActionResult> GetAll(
        [FromServices] IMaintenanceTemplateService service,
        CancellationToken cancellationToken)
    {
        var templates = await service.GetAllAsync(cancellationToken);
        return Ok(templates);
    }

    [HttpPost("/api/admin/maintenance-templates/equipment")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> CreateEquipment(
        [FromBody] MaintenanceEquipmentRequest request,
        [FromServices] IMaintenanceTemplateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateEquipmentAsync(request, cancellationToken);
        return this.ToActionResult(result, id => Created($"/api/admin/maintenance-templates/equipment/{id}", new { id }));
    }

    [HttpPut("/api/admin/maintenance-templates/equipment/{id:guid}")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> RenameEquipment(
        Guid id,
        [FromBody] MaintenanceEquipmentRequest request,
        [FromServices] IMaintenanceTemplateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.RenameEquipmentAsync(id, request, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    [HttpDelete("/api/admin/maintenance-templates/equipment/{id:guid}")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> DeleteEquipment(
        Guid id,
        [FromServices] IMaintenanceTemplateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteEquipmentAsync(id, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    [HttpPost("/api/admin/maintenance-templates/equipment/{equipmentId:guid}/intervals")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> CreateInterval(
        Guid equipmentId,
        [FromBody] MaintenanceIntervalRequest request,
        [FromServices] IMaintenanceTemplateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateIntervalAsync(equipmentId, request, cancellationToken);
        return this.ToActionResult(result, id => Created($"/api/admin/maintenance-templates/intervals/{id}", new { id }));
    }

    [HttpPut("/api/admin/maintenance-templates/intervals/{id:guid}")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> RenameInterval(
        Guid id,
        [FromBody] MaintenanceIntervalRequest request,
        [FromServices] IMaintenanceTemplateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.RenameIntervalAsync(id, request, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    [HttpDelete("/api/admin/maintenance-templates/intervals/{id:guid}")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> DeleteInterval(
        Guid id,
        [FromServices] IMaintenanceTemplateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteIntervalAsync(id, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    [HttpPost("/api/admin/maintenance-templates/intervals/{intervalId:guid}/items")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> AddItem(
        Guid intervalId,
        [FromBody] MaintenanceItemRequest request,
        [FromServices] IMaintenanceTemplateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.AddItemAsync(intervalId, request, cancellationToken);
        return this.ToActionResult(result, id => Created($"/api/admin/maintenance-templates/items/{id}", new { id }));
    }

    [HttpPut("/api/admin/maintenance-templates/items/{id:guid}")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> UpdateItem(
        Guid id,
        [FromBody] MaintenanceItemRequest request,
        [FromServices] IMaintenanceTemplateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateItemAsync(id, request, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }

    [HttpDelete("/api/admin/maintenance-templates/items/{id:guid}")]
    [Authorize(Roles = "administrator")]
    public async Task<IActionResult> DeleteItem(
        Guid id,
        [FromServices] IMaintenanceTemplateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteItemAsync(id, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }
}
