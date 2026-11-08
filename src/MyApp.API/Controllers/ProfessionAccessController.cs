using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Security;
using MyApp.Infrastructure.Db;

namespace MyApp.API.Controllers;

[ApiController, Route("api/admin/profession-access"), Authorize(Roles = "administrator")]
public sealed class ProfessionAccessController(AppDbContext db) : ControllerBase
{
    public sealed record Target(Guid Id, int Revision);
    public sealed record AccessWrite(Target[] Professions, string[]? Permissions, string[] HiddenBlocks);

    [HttpGet]
    public async Task<IActionResult> Read(CancellationToken ct) => Ok(new {
        permissions = Permissions.Catalog, blocks = PageBlocks.Catalog,
        professions = (await db.Professions.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync(ct)).Select(x => new {
            x.Id, name = x.Name, revision = x.AccessRevision,
            permissions = x.PermissionOverride?.Split(',', StringSplitOptions.RemoveEmptyEntries),
            hiddenBlocks = x.HiddenPageBlocks.Split(',', StringSplitOptions.RemoveEmptyEntries)
        })
    });

    [HttpPut]
    public async Task<IActionResult> Save(AccessWrite request, CancellationToken ct)
    {
        if (request.Professions is not { Length: > 0 } || request.HiddenBlocks is null ||
            request.Professions.Any(x => x is null) ||
            request.Professions.Select(x => x.Id).Distinct().Count() != request.Professions.Length ||
            (request.Permissions ?? []).Any(x => x is null || !Permissions.All.Contains(x)) ||
            request.HiddenBlocks.Any(x => !PageBlocks.Catalog.Any(b => b.Key == x)))
            return BadRequest(new { message = "Выберите профессии и допустимые разделы и блоки." });
        var ids = request.Professions.Select(x => x.Id).ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var professions = await db.Professions.Where(x => ids.Contains(x.Id)).ToArrayAsync(ct);
        if (professions.Length != ids.Length) return NotFound();
        if (professions.Any(x => request.Professions.Single(p => p.Id == x.Id).Revision != x.AccessRevision))
            return Conflict(new { message = "Настройки изменены другим администратором. Обновите список и повторите изменения." });
        var permissions = request.Permissions is null ? null : string.Join(',', Permissions.Expand(request.Permissions).Order());
        var hidden = string.Join(',', request.HiddenBlocks.Distinct().Order());
        var changed = professions.Where(x => x.PermissionOverride != permissions || x.HiddenPageBlocks != hidden).ToArray();
        foreach (var profession in changed)
        {
            profession.PermissionOverride = permissions;
            profession.HiddenPageBlocks = hidden;
            profession.AccessRevision++;
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Настройки уже изменены. Обновите список." }); }
        var changedIds = changed.Select(x => x.Id).ToArray();
        // Revoke stale tokens atomically with the policy. Administrators retain their session and access.
        await db.Users.Where(x => changedIds.Contains(x.PositionId) && x.Role != "administrator")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionStamp, Guid.NewGuid()), ct);
        await transaction.CommitAsync(ct);
        return Ok(new { message = "Настройки сохранены. Сотрудникам изменённых профессий потребуется повторный вход." });
    }
}
