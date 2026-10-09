using System.ComponentModel.DataAnnotations;
using MyApp.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/admin/media-storage")]
[Authorize(Roles = "administrator")]
public sealed class AdminMediaStorageController(
    IMediaStorageAdministrationRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var retentionDays = await repository.GetRetentionDaysAsync(cancellationToken);
        return Ok(new { retentionDays });
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateMediaStorageSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await repository.SetRetentionDaysAsync(
            request.RetentionDays, cancellationToken);
        return Ok(new { retentionDays = request.RetentionDays });
    }
}

public sealed record UpdateMediaStorageSettingsRequest(
    [property: Range(1, 180)] int RetentionDays);
