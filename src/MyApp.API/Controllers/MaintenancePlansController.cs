using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.DTO;
using MyApp.Application.Security;
using MyApp.Application.Services;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.API.Controllers;

/// <summary>Графики ТО отдельных машин, регистрация выполнения и журнал изменений.</summary>
[ApiController]
[Route("api/maintenance-plans")]
[Authorize(Policy = Permissions.VehiclesView)]
public sealed class MaintenancePlansController(AppDbContext db, IVehicleService vehicles) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public sealed record SaveRequest(int Revision, MaintenancePlan Plan);
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : Guid.Empty;
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Sakhalin")));
    private Task<bool> CanManage(CancellationToken ct) => Manage(ct);
    private async Task<bool> Manage(CancellationToken ct) => Actor != Guid.Empty && (await vehicles.GetVehicleCatalogOptionsAsync(Actor, ct)).CanManage;
    private static MaintenancePlan Read(VehicleMaintenancePlan item) => JsonSerializer.Deserialize<MaintenancePlan>(item.Snapshot, JsonOptions)!;

    [HttpGet("sync-capabilities")]
    public IActionResult SyncCapabilities() => Ok(new { completionIdempotency = 1 });

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var fleet = await vehicles.GetVehiclesAsync(ct);
        var plans = await db.VehicleMaintenancePlans.AsNoTracking().ToDictionaryAsync(p => p.VehicleId, ct);
        var today = Today;
        // Group in PostgreSQL: one latest reading per vehicle and numbered engine.
        var latest = await db.VehicleHourReadings.AsNoTracking()
            .Where(r => r.ReadingDate <= today && r.EngineNumber > 0)
            .GroupBy(r => new { r.VehicleId, r.EngineNumber })
            .Select(g => g.OrderByDescending(r => r.ReadingDate).ThenByDescending(r => r.ReadingTime)
                .ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).First())
            .ToArrayAsync(ct);
        var readings = latest.ToLookup(r => r.VehicleId);
        return Ok(new {
            canManage = await CanManage(ct),
            rows = fleet.Select(vehicle => {
                plans.TryGetValue(vehicle.Id, out var stored);
                var plan = stored is null ? null : Read(stored);
                var current = readings[vehicle.Id].Select(r => {
                    var value = MaintenancePlanner.Estimate(r.EngineNumber, r.EngineHours, r.ReadingDate, today);
                    return new MaintenanceReading(value.Number, value.Hours, today, value.IsEstimated, value.SourceDate);
                }).ToList();
                var due = plan is null ? Array.Empty<MaintenanceDue>() : MaintenancePlanner.Due(plan, current).ToArray();
                return new { vehicle, revision = stored?.Revision ?? 0, plan, readings = current, due,
                    incomplete = plan is not null && due.Length < 1 + plan.RunIns.Length };
            }).ToArray()
        });
    }

    [HttpGet("{vehicleId:guid}/readings")]
    public async Task<IActionResult> Readings(Guid vehicleId, DateOnly date, CancellationToken ct)
    {
        var vehicle = (await vehicles.GetVehiclesAsync(ct)).FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is null) return NotFound();
        if (date == default || date > Today) return BadRequest(new { message = "Выберите дату не позднее сегодняшней." });
        var engines = await ReadingsOn(vehicleId, date, ct);
        return Ok(new { engines, missing = Enumerable.Range(1, vehicle.EngineCount).Except(engines.Select(e => e.Number)) });
    }

    private async Task<MaintenanceEngine[]> ReadingsOn(Guid vehicleId, DateOnly date, CancellationToken ct)
    {
        var readings = await db.VehicleHourReadings.AsNoTracking()
            .Where(r => r.VehicleId == vehicleId && r.ReadingDate <= date && r.EngineNumber > 0)
            .OrderByDescending(r => r.ReadingDate).ThenByDescending(r => r.ReadingTime).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .ToArrayAsync(ct);
        return readings.DistinctBy(r => r.EngineNumber).Select(r => MaintenancePlanner.Estimate(r.EngineNumber, r.EngineHours, r.ReadingDate, date)).ToArray();
    }
    private IActionResult MissingReadings(DateOnly date) => BadRequest(new { message = $"За {date:dd.MM.yyyy} нет исходных моточасов для всех выбранных ДВС. Загрузите показания за эту или более раннюю дату." });

    [HttpGet("{vehicleId:guid}/history")]
    public async Task<IActionResult> History(Guid vehicleId, CancellationToken ct)
    {
        var events = await db.VehicleMaintenanceEvents.AsNoTracking().Where(e => e.VehicleId == vehicleId)
            .OrderByDescending(e => e.Revision).ToArrayAsync(ct);
        return Ok(events.Select(e => new { e.Id, e.Revision, e.Kind, e.Description, e.Date, e.CreatedBy, e.CreatedAt,
            before = JsonSerializer.Deserialize<JsonElement>(e.Before),
            after = JsonSerializer.Deserialize<JsonElement>(e.After),
            readings = JsonSerializer.Deserialize<JsonElement>(e.Readings) }));
    }

    [HttpPut("{vehicleId:guid}")]
    [RequestSizeLimit(100_000)]
    public async Task<IActionResult> Save(Guid vehicleId, SaveRequest request, CancellationToken ct)
    {
        if (!await CanManage(ct)) return Forbid();
        var vehicle = (await vehicles.GetVehiclesAsync(ct)).FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is null) return NotFound();
        if (request.Plan is null || request.Revision < 0) return BadRequest(new { message = "Некорректный график или его версия." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(vehicleId, ct);
        var stored = await db.VehicleMaintenancePlans.SingleOrDefaultAsync(p => p.VehicleId == vehicleId, ct);
        if ((stored?.Revision ?? 0) != request.Revision) return VersionConflict();
        if (stored is null)
        {
            var engines = await ReadingsOn(vehicleId, request.Plan.LastDate, ct);
            if (!MaintenancePlanner.ValidEngines(engines, vehicle.EngineCount)) return MissingReadings(request.Plan.LastDate);
            request = request with { Plan = request.Plan with { Engines = engines } };
        }
        var oldRunIns = stored is null ? [] : Read(stored).RunIns;
        if (request.Plan.RunIns is not null)
        {
            var runIns = new List<MaintenanceRunIn>();
            foreach (var runIn in request.Plan.RunIns)
            {
                if (runIn is null || !MaintenancePlanner.ValidEngines(runIn.Engines, vehicle.EngineCount, false))
                    return BadRequest(new { message = "Выберите ДВС для обкаточного ТО." });
                var old = oldRunIns.FirstOrDefault(r => r.Id == runIn.Id && r.StartDate == runIn.StartDate);
                var readings = old?.Engines ?? await ReadingsOn(vehicleId, runIn.StartDate, ct);
                var engines = readings.Where(e => runIn.Engines.Any(r => r.Number == e.Number)).ToArray();
                if (engines.Length != runIn.Engines.Length) return MissingReadings(runIn.StartDate);
                runIns.Add(runIn with { Engines = engines });
            }
            request = request with { Plan = request.Plan with { RunIns = runIns.ToArray() } };
        }
        var error = MaintenancePlanner.Validate(request.Plan, vehicle.EngineCount, Today);
        if (error is not null) return BadRequest(new { message = error });
        // Editing the schedule must not silently rewrite the last actual service.
        if (stored is not null)
        {
            var old = Read(stored);
            if (old.LastDate != request.Plan.LastDate || old.LastService != request.Plan.LastService ||
                !old.Engines.OrderBy(e => e.Number).SequenceEqual(request.Plan.Engines.OrderBy(e => e.Number)))
                return BadRequest(new { message = "Последнее ТО меняется через регистрацию выполнения, а не редактирование графика." });
        }
        stored ??= new VehicleMaintenancePlan { VehicleId = vehicleId };
        if (stored.Revision == 0) db.VehicleMaintenancePlans.Add(stored);
        Record(stored, request.Plan, stored.Revision == 0 ? "initial" : "schedule", "Настройка графика ТО", Today, []);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { stored.Revision });
    }

    [HttpPost("{vehicleId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid vehicleId, MaintenanceCompletion request, CancellationToken ct)
    {
        if (!await CanManage(ct)) return Forbid();
        if (request.OperationId == Guid.Empty) return BadRequest(new { message = "Некорректный идентификатор операции." });
        var vehicle = (await vehicles.GetVehiclesAsync(ct)).FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is null) return NotFound();
        if (request.Date == default || request.Date > Today ||
            string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 5000)
            return BadRequest(new { message = "Укажите дату и описание выполненного ТО." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(vehicleId, ct);
        // Keep deduplication and the business write in the same transaction. A retry after
        // a lost response must return the original result, even after subsequent edits.
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { vehicleId, Actor, request.Revision, request.Date,
                request.Description, request.RunInId }, JsonOptions))));
        if (request.OperationId is { } operationId)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({operationId.ToString()}, 228))", ct);
            var previous = await db.VehicleMaintenanceEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == operationId, ct);
            if (previous is not null)
            {
                if (previous.RequestHash != requestHash || previous.VehicleId != vehicleId || previous.CreatedBy != Actor)
                    return Conflict(new { message = "Идентификатор операции уже использован для других данных." });
                return Ok(new { previous.Revision });
            }
        }
        var stored = await db.VehicleMaintenancePlans.SingleOrDefaultAsync(p => p.VehicleId == vehicleId, ct);
        if (stored is null) return NotFound();
        if (stored.Revision != request.Revision) return VersionConflict();
        var plan = Read(stored);
        var runIn = request.RunInId is { } id ? plan.RunIns.FirstOrDefault(r => r.Id == id) : null;
        if (request.RunInId is not null && runIn is null) return BadRequest(new { message = "Обкаточное ТО не найдено или уже выполнено." });
        var engines = await ReadingsOn(vehicleId, request.Date, ct);
        if (!MaintenancePlanner.ValidEngines(engines, vehicle.EngineCount)) return MissingReadings(request.Date);
        request = request with { Engines = engines };
        var baseline = runIn?.Engines ?? plan.Engines;
        if (request.Date < (runIn?.StartDate ?? plan.LastDate) ||
            baseline.Any(e => request.Engines.Single(r => r.Number == e.Number).Hours < e.Hours))
            return BadRequest(new { message = "Дата и показания не могут предшествовать началу отсчёта ТО." });
        // Reject a backdated reading that contradicts already known later readings.
        var later = await db.VehicleHourReadings.AsNoTracking().Where(r => r.VehicleId == vehicleId && r.ReadingDate > request.Date && r.EngineNumber > 0).ToArrayAsync(ct);
        if (later.Any(r => request.Engines.Any(e => e.Number == r.EngineNumber && e.Hours > r.EngineHours)))
            return BadRequest(new { message = "Показания ТО превышают показания, записанные на более позднюю дату." });
        var next = MaintenancePlanner.Complete(plan, request);
        var name = runIn is null ? plan.Steps[plan.NextStepIndex].Name : "Обкаточное ТО: " + runIn.Reason;
        Record(stored, next, runIn is null ? "scheduled" : "run-in", name + ". " + request.Description.Trim(), request.Date, request.Engines, request.OperationId, requestHash);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { stored.Revision });
    }

    private async Task Lock(Guid id, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 227))", ct);
    private IActionResult VersionConflict() => Conflict(new { message = "График изменён другим пользователем. Обновите данные перед сохранением." });
    private void Record(VehicleMaintenancePlan stored, MaintenancePlan plan, string kind, string description, DateOnly date, MaintenanceEngine[] readings, Guid? operationId = null, string? requestHash = null)
    {
        var before = stored.Snapshot;
        stored.Revision++;
        stored.Snapshot = JsonSerializer.Serialize(plan, JsonOptions);
        stored.UpdatedAt = DateTimeOffset.UtcNow;
        stored.UpdatedBy = Actor;
        db.VehicleMaintenanceEvents.Add(new VehicleMaintenanceEvent {
            Id = operationId ?? Guid.NewGuid(), RequestHash = requestHash, VehicleId = stored.VehicleId, Revision = stored.Revision, Kind = kind,
            Description = description, Date = date, Before = before, After = stored.Snapshot,
            Readings = JsonSerializer.Serialize(readings, JsonOptions), CreatedBy = Actor, CreatedAt = stored.UpdatedAt
        });
    }
}

