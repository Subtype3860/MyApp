using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

/// <summary>HTTP-эндпоинты администрирования пользователей.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "administrator")]
public class AdminUserController : ControllerBase
{
    /// <summary>Возвращает каталог разрешений для настройки доступа пользователей.</summary>
    [HttpGet("permissions")]
    public IActionResult GetPermissions() => Ok(Permissions.Catalog);

    /// <summary>Возвращает список пользователей для администрирования.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromServices] IUserService userService,
        CancellationToken cancellationToken)
    {
        var users = await userService.GetAllAsync(cancellationToken);
        return Ok(users);
    }

    /// <summary>Создаёт учётную запись пользователя через прикладной сервис.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserRequest request,
        [FromServices] IUserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.CreateAsync(request, cancellationToken);
        return this.ToActionResult(result, value => Created($"/api/admin/users/{value.Id}", value));
    }

    /// <summary>Обновляет учётную запись и права пользователя через прикладной сервис.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateUserRequest request,
        [FromServices] IUserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.UpdateAsync(id, request, cancellationToken);
        return this.ToActionResult(result, _ => Ok());
    }
}
