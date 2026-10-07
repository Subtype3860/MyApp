using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ComponentDocumentController : ControllerBase
{
    [HttpPost("/api/documents/components/preview")]
    public async Task<IActionResult> Preview(
        [FromBody] ComponentDocumentRequest request,
        [FromServices] IComponentDocumentService documentService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await documentService.PrepareAsync(request, userId, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }

    [HttpPost("/api/documents/components")]
    public async Task<IActionResult> Generate(
        [FromBody] ComponentDocumentRequest request,
        [FromServices] IComponentDocumentService documentService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await documentService.GenerateAsync(request, userId, cancellationToken);
        return this.ToActionResult(result, value => Ok(value));
    }

    [HttpGet("/api/documents/components/template")]
    public IActionResult GetTemplate([FromServices] IConfiguration configuration)
    {
        var templatePath = configuration.GetValue<string>("DocumentTemplates:ComponentIssuePath");
        if (string.IsNullOrWhiteSpace(templatePath) || !System.IO.File.Exists(templatePath))
        {
            return NotFound();
        }

        return File(templatePath, "application/pdf");
    }

    private bool TryGetUserId(out Guid userId)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(userIdValue, out userId);
    }
}
