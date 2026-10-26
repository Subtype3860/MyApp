using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using MyApp.Application.Security;
using MyApp.Application.Services;
using MyApp.Infrastructure.Db;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/repair-sync")]
[Authorize(Policy = Permissions.VehiclesView)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RepairSyncController(AppDbContext db, IVehicleService service) : ControllerBase
{
    public sealed record Command(Guid OperationId, Guid VehicleId, Guid DefectId, string Version, string Kind, VehicleWorkRequest? Work);
    private bool Allowed => User.HasClaim("permission", "vehicles.repair") || User.HasClaim("permission", "vehicles.works");

    [HttpGet]
    public async Task<IActionResult> Snapshot(CancellationToken ct)
    {
        if (!Allowed) return Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var journals = await service.GetRepairJournalsAsync(ct);
        var rows = new List<object>();
        foreach (var journal in journals)
            rows.Add(new { journal, version = await RepairSyncSchema.Version(db, journal.Vehicle.Id, ct) });
        await transaction.CommitAsync(ct);
        return Ok(new { protocol = 1, rows });
    }

    [HttpPost("commands")]
    [RequestSizeLimit(32000)]
    public async Task<IActionResult> Execute(Command command, CancellationToken ct)
    {
        if (!Allowed) return Forbid();
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor)) return Unauthorized();
        if (!Valid(command)) return BadRequest(new { message = "Некорректная команда ремонта." });
        var hash = RepairSyncSchema.Hash(command);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The old web APIs do not use version tokens. Table locks also serialize their writes
        // during compare-and-commit; locks last only for this short, database-only command.
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE vehicle_defects, vehicle_works IN SHARE ROW EXCLUSIVE MODE", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({command.OperationId.ToString()}, 719))", ct);
        var receipt = await db.RepairOperationReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.OperationId, ct);
        if (receipt is not null)
            return receipt.Owner == actor && receipt.Hash == hash ? Ok(new { id = receipt.ResultId })
                : Conflict(new { message = "Идентификатор операции уже использован с другими данными." });
        var defect = await db.VehicleDefects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.DefectId && x.VehicleId == command.VehicleId, ct);
        if (defect is null) return NotFound();
        if (await db.Vehicles.AnyAsync(x => x.Id == command.VehicleId && x.IsArchived, ct))
            return Conflict(new { message = "Техника архивирована. Обновите журнал." });
        if (command.Version != await RepairSyncSchema.Version(db, command.VehicleId, ct))
            return Conflict(new { message = "Журнал изменён. Обновите данные и проверьте запись; автоматической перезаписи нет." });
        Guid resultId;
        if (command.Kind == "claim") {
            var result = await service.ClaimDefectAsync(command.DefectId, actor, ct);
            if (result.Status != ServiceResultStatus.Success) return this.ToActionResult(result, _ => Ok());
            resultId = command.DefectId;
        } else {
            // Android enters Sakhalin wall time; PostgreSQL timestamptz requires UTC offsets.
            var work = command.Work! with {
                RepairDateTime = command.Work!.RepairDateTime?.ToUniversalTime(),
                DowntimeStartedAt = command.Work.DowntimeStartedAt?.ToUniversalTime()
            };
            var result = await service.AddWorkAsync(command.VehicleId, work, actor, ct);
            if (result.Status != ServiceResultStatus.Success) return this.ToActionResult(result, _ => Ok());
            resultId = result.Value;
        }
        db.RepairOperationReceipts.Add(new() { Id = command.OperationId, Owner = actor, Hash = hash, ResultId = resultId });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { id = resultId });
    }
    public static bool Valid(Command c) => c.OperationId != Guid.Empty && c.VehicleId != Guid.Empty && c.DefectId != Guid.Empty &&
        c.Version is { Length: 64 } && c.Version.All(Uri.IsHexDigit) &&
        (c.Kind == "claim" && c.Work is null || c.Kind == "work" && c.Work is not null && c.Work.DefectId == c.DefectId &&
         c.Work.RepairDateTime is not null && c.Work.Description?.Length is > 0 and <= 4000 && c.Work.Cause?.Length is > 0 and <= 4000 &&
         c.Work.RequiredParts?.Length is <= 4000 && c.Work.Location is "КАРЬЕР" or "РММ" && c.Work.OperationalState is "operating" or "stopped");
}
