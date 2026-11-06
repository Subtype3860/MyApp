using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.API.Controllers;

[ApiController, Route("api/fleet-regions"), Authorize]
public sealed class FleetRegionsController(AppDbContext db) : ControllerBase
{
    public sealed record RegionRequest(string Name, bool IsArchived = false);
    public sealed record AccessRequest(Guid[] RegionIds);
    public sealed record TransferRequest(Guid RegionId, Guid? ExpectedAssignmentId, DateTimeOffset? StartedAt, string? Note);
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var query = db.FleetRegions.AsNoTracking();
        if (db.RestrictFleet) query = query.Where(r => db.FleetRegionGrants.Any(g => g.UserId == Actor && g.RegionId == r.Id));
        return Ok(new { canManage = !db.RestrictFleet, regions = await query.OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.IsArchived, vehicleCount = db.VehicleRegionAssignments.Count(a => a.RegionId == r.Id && a.EndedAt == null) }).ToArrayAsync(ct) });
    }

    private Task<bool> CanManageRegions(CancellationToken ct) => db.Users.AnyAsync(u => u.Id == Actor &&
        (u.Role == "administrator" || u.Profession.Name.Trim().ToLower() == "главный механик" || u.Profession.Name.Trim().ToLower() == "старший механик"), ct);

    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities(CancellationToken ct) => Ok(new { canCreate = await CanManageRegions(ct) });

    // Management receives only fleet identifiers and assignment data, not unrestricted work reports.
    [HttpGet("management")]
    public async Task<IActionResult> Management(CancellationToken ct)
    {
        if (!await CanManageRegions(ct)) return Forbid();
        var regions = await db.FleetRegions.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.IsArchived }).ToArrayAsync(ct);
        var vehicles = await (from v in db.Vehicles.IgnoreQueryFilters().AsNoTracking()
            join m in db.VehicleModels on v.CarModeId equals m.Id into models
            from m in models.DefaultIfEmpty()
            orderby v.GarageNumber
            select new { v.Id, v.GarageNumber, v.StateNumber, v.IsArchived, modelName = m == null ? "" : m.FullName }).ToArrayAsync(ct);
        var assignments = await (from a in db.VehicleRegionAssignments.IgnoreQueryFilters().AsNoTracking()
            join r in db.FleetRegions on a.RegionId equals r.Id where a.EndedAt == null
            select new { a.VehicleId, assignmentId = a.Id, a.RegionId, regionName = r.Name, a.StartedAt }).ToArrayAsync(ct);
        var users = await db.Users.AsNoTracking().OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName, u.Role }).ToArrayAsync(ct);
        return Ok(new { canManage = true, regions, vehicles, assignments, users });
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        if (!await CanManageRegions(ct)) return Forbid();
        return Ok(await db.FleetRegions.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.IsArchived }).ToArrayAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(RegionRequest request, CancellationToken ct)
    {
        if (!await CanManageRegions(ct)) return Forbid();
        return await Save(null, request, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, RegionRequest request, CancellationToken ct)
    {
        if (!await CanManageRegions(ct)) return Forbid();
        return await Save(id, request, ct);
    }

    private async Task<IActionResult> Save(Guid? id, RegionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200) return BadRequest(new { message = "Название участка: от 1 до 200 символов." });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(ct);
        var name = request.Name.Trim();
        if (await db.FleetRegions.AnyAsync(r => r.Id != id && r.Name.ToLower() == name.ToLower(), ct))
            return Conflict(new { message = "Участок с таким названием уже существует." });
        var region = id is null ? new FleetRegion { Id = Guid.NewGuid() } : await db.FleetRegions.FindAsync([id.Value], ct);
        if (region is null) return NotFound();
        if (request.IsArchived && await db.VehicleRegionAssignments.IgnoreQueryFilters().AnyAsync(a => a.RegionId == region.Id && a.EndedAt == null, ct))
            return Conflict(new { message = "Сначала переместите закреплённую технику на другой участок." });
        region.Name = name; region.IsArchived = request.IsArchived;
        if (id is null) db.FleetRegions.Add(region);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { region.Id });
    }

    [HttpGet("users/{userId:guid}")]
    public async Task<IActionResult> Access(Guid userId, CancellationToken ct)
    {
        if (!await CanManageRegions(ct)) return Forbid();
        return Ok(await db.FleetRegionGrants.Where(g => g.UserId == userId).Select(g => g.RegionId).ToArrayAsync(ct));
    }

    [HttpPut("users/{userId:guid}")]
    public async Task<IActionResult> SetAccess(Guid userId, AccessRequest request, CancellationToken ct)
    {
        if (!await CanManageRegions(ct)) return Forbid();
        if (request.RegionIds is null || request.RegionIds.Length > 1000) return BadRequest();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(ct);
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) return NotFound();
        var ids = request.RegionIds.Distinct().ToArray();
        if (await db.FleetRegions.CountAsync(r => ids.Contains(r.Id), ct) != ids.Length) return BadRequest(new { message = "Один из участков не найден." });
        await db.FleetRegionGrants.Where(g => g.UserId == userId).ExecuteDeleteAsync(ct);
        db.FleetRegionGrants.AddRange(ids.Select(id => new FleetRegionGrant { UserId = userId, RegionId = id }));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpGet("vehicles")]
    public async Task<IActionResult> Assignments(CancellationToken ct) => Ok(await (
        from a in db.VehicleRegionAssignments.AsNoTracking() join r in db.FleetRegions on a.RegionId equals r.Id
        where a.EndedAt == null select new { a.VehicleId, assignmentId = a.Id, a.RegionId, regionName = r.Name, a.StartedAt }).ToArrayAsync(ct));

    [HttpGet("vehicles/{vehicleId:guid}/history")]
    public async Task<IActionResult> History(Guid vehicleId, CancellationToken ct)
    {
        var manager = await CanManageRegions(ct);
        var vehicles = manager ? db.Vehicles.IgnoreQueryFilters() : db.Vehicles;
        var assignments = manager ? db.VehicleRegionAssignments.IgnoreQueryFilters() : db.VehicleRegionAssignments;
        if (!await vehicles.AnyAsync(v => v.Id == vehicleId, ct)) return NotFound();
        return Ok(await (from a in assignments.AsNoTracking()
            join r in db.FleetRegions on a.RegionId equals r.Id join u in db.Users on a.CreatedBy equals u.Id
            where a.VehicleId == vehicleId orderby a.StartedAt descending
            select new { a.Id, a.RegionId, regionName = r.Name, a.StartedAt, a.EndedAt, a.RecordedAt, a.Note,
                recordedBy = u.LastName + " " + u.FirstName }).ToArrayAsync(ct));
    }

    [HttpPost("vehicles/{vehicleId:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid vehicleId, TransferRequest request, CancellationToken ct)
    {
        if (!await CanManageRegions(ct)) return Forbid();
        var now = DateTimeOffset.UtcNow;
        var started = (request.StartedAt ?? now).ToUniversalTime();
        if (started > now || started == default || (request.Note?.Length ?? 0) > 2000)
            return BadRequest(new { message = "Укажите дату перемещения не позднее текущего времени и примечание до 2000 символов." });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(ct);
        if (!await db.Vehicles.IgnoreQueryFilters().AnyAsync(v => v.Id == vehicleId && !v.IsArchived, ct)) return NotFound();
        if (!await db.FleetRegions.AnyAsync(r => r.Id == request.RegionId && !r.IsArchived, ct))
            return BadRequest(new { message = "Выберите действующий участок." });
        var current = await db.VehicleRegionAssignments.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.VehicleId == vehicleId && a.EndedAt == null, ct);
        if (current?.Id != request.ExpectedAssignmentId) return Conflict(new { message = "Закрепление изменено другим пользователем. Обновите историю." });
        if (current?.RegionId == request.RegionId) return Conflict(new { message = "Техника уже закреплена за этим участком." });
        if (current is not null) {
            if (started <= current.StartedAt) return BadRequest(new { message = "Перемещение должно быть позже начала текущего закрепления." });
            current.EndedAt = started;
            // Close the old period before adding the new one: the partial unique index allows only one current assignment.
            await db.SaveChangesAsync(ct);
        }
        var assignment = new VehicleRegionAssignment { Id = Guid.NewGuid(), VehicleId = vehicleId, RegionId = request.RegionId,
            StartedAt = started, CreatedBy = Actor, RecordedAt = now, Note = request.Note?.Trim() ?? "" };
        db.VehicleRegionAssignments.Add(assignment);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { assignment.Id });
    }

    private Task Lock(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(740410119)", ct);
}
