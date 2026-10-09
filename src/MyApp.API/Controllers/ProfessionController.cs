using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/admin/professions")]
[Authorize(Roles = "administrator")]
public class ProfessionController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromServices] IProfessionService professionService,
        CancellationToken cancellationToken)
    {
        var professions = await professionService.GetAllAsync(cancellationToken);
        return Ok(professions);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] ProfessionRequest request,
        [FromServices] IProfessionService professionService,
        CancellationToken cancellationToken)
    {
        var result = await professionService.CreateAsync(request, cancellationToken);
        return this.ToActionResult(result, value => Created($"/api/admin/professions/{value.Id}", value));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] ProfessionRequest request,
        [FromServices] IProfessionService professionService,
        CancellationToken cancellationToken)
    {
        var result = await professionService.UpdateAsync(id, request, cancellationToken);
        return this.ToActionResult(result, _ => Ok());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] IProfessionService professionService,
        CancellationToken cancellationToken)
    {
        var result = await professionService.DeleteAsync(id, cancellationToken);
        return this.ToActionResult(result, _ => NoContent());
    }
}
