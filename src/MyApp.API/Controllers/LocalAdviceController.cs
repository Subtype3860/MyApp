using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Security;
using MyApp.Application.Services;
using MyApp.Infrastructure.Db;
using Npgsql;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/local-advice")]
[Authorize(Policy = Permissions.VehiclesView)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LocalAdviceController(AppDbContext db, IVehicleService vehicles) : ControllerBase
{
    public sealed record ParseRequest(string? Text, string Kind);
    public sealed record OutcomeRequest(DateOnly Date, int Shift, bool? ReadyWholeShift, bool Confirmed);
    private bool Has(params string[] permissions) => permissions.Any(p => User.HasClaim("permission", p));
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Sakhalin").DateTime);

    [HttpGet("vehicles/{vehicleId:guid}/readiness")]
    public async Task<IActionResult> Readiness(Guid vehicleId, DateOnly date, int shift, CancellationToken ct)
    {
        if (!Has(Permissions.GtoReport, "vehicles.report")) return Forbid();
        if (date < Today || date > Today.AddDays(7) || shift is not (1 or 2))
            return BadRequest(new { message = "Выберите смену сегодня или в ближайшие 7 дней. Исторический прогноз не восстанавливается из текущих данных." });
        return await ReadSafely(async () => {
            if (!await db.Vehicles.AnyAsync(x => x.Id == vehicleId, ct)) return NotFound();
            var now = DateTimeOffset.UtcNow;
            var defects = await Defects(vehicleId).ToArrayAsync(ct);
            var repairs = await Repairs(vehicleId).ToArrayAsync(ct);
            var hours = await db.VehicleHourReadings.CountAsync(x => x.VehicleId == vehicleId && x.CreatedAt <= now && x.ReadingDate <= Today, ct);
            return Ok(new { vehicleId, date, shift, advice = LocalAdvice.Readiness(defects, repairs, now, hours) });
        });
    }

    [HttpGet("defects/{defectId:guid}/recurrence")]
    public async Task<IActionResult> Recurrence(Guid defectId, CancellationToken ct)
    {
        if (!Has("vehicles.repair", "vehicles.repair_history", "vehicles.works")) return Forbid();
        return await ReadSafely(async () => {
            var defect = await db.VehicleDefects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == defectId, ct);
            if (defect is null) return NotFound();
            var defects = await Defects(defect.VehicleId).ToArrayAsync(ct);
            return Ok(new { defectId, advice = LocalAdvice.Recurrence(defects.Single(x => x.Id == defectId), defects,
                await Repairs(defect.VehicleId).ToArrayAsync(ct), DateTimeOffset.UtcNow) });
        });
    }

    [HttpPost("parse")]
    [RequestSizeLimit(32000)]
    public IActionResult Parse(ParseRequest request)
    {
        if (request.Kind is not ("repair" or "parts") || (request.Text?.Length ?? 0) > 5000) return BadRequest();
        if (!Has(request.Kind == "repair" ? "vehicles.repair_request" : "vehicles.parts_request")) return Forbid();
        return Ok(LocalAdvice.Parse(request.Text ?? "", request.Kind));
    }

    [HttpGet("quality")]
    public async Task<IActionResult> Quality(CancellationToken ct)
    {
        if (!Has(Permissions.GtoReport, "vehicles.report")) return Forbid();
        return await ReadSafely(async () => {
            var total = await db.VehicleDefects.CountAsync(ct);
            var withNode = await db.VehicleDefects.CountAsync(x => x.NodeName.Trim() != "", ct);
            var repairs = await db.VehicleWorks.CountAsync(x => x.DefectId != null && x.RepairStatus == "repaired", ct);
            var withCompletion = await db.VehicleWorks.CountAsync(x => x.DefectId != null && x.RepairStatus == "repaired" && x.CompletedAt != null, ct);
            return Ok(new { generatedAt = DateTimeOffset.UtcNow, defects = total, defectsWithNode = withNode,
                repairedWorks = repairs, repairsWithCompletion = withCompletion,
                hourReadings = await db.VehicleHourReadings.CountAsync(ct),
                confirmedShiftOutcomes = await db.VehicleShiftOutcomes.CountAsync(ct),
                validationStatus = "not_validated", confidence = (double?)null,
                limitations = new[] { "История редактируема и не является снимком признаков на дату прогноза.",
                    "Нужны проспективные снимки признаков, подтверждённые итоги смен и размеченные тексты заявок.",
                    "Сохранённый прогноз ГТО не используется как фактическая метка." } });
        });
    }

    [HttpPost("vehicles/{vehicleId:guid}/outcomes")]
    public async Task<IActionResult> ConfirmOutcome(Guid vehicleId, OutcomeRequest request, CancellationToken ct)
    {
        if (!Has(Permissions.GtoReport, "vehicles.report")) return Forbid();
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor)) return Unauthorized();
        if (!(await vehicles.GetVehicleCatalogOptionsAsync(actor, ct)).CanManage) return Forbid();
        if (!request.Confirmed || request.ReadyWholeShift is null || request.Date == default || request.Date >= Today || request.Shift is not (1 or 2))
            return BadRequest(new { message = "Подтвердите фактическую готовность за всю завершённую смену. Доступны даты до сегодняшней." });
        return await ReadSafely(async () => {
            if (!await db.Vehicles.AnyAsync(x => x.Id == vehicleId, ct)) return NotFound();
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO vehicle_shift_outcomes (vehicle_id, date, shift, ready_whole_shift, confirmed_by, recorded_at)
                VALUES ({vehicleId}, {request.Date}, {request.Shift}, {request.ReadyWholeShift.Value}, {actor}, {DateTimeOffset.UtcNow})
                ON CONFLICT (vehicle_id, date, shift) DO NOTHING
                """, ct);
            return affected == 0 ? Conflict(new { message = "Факт этой смены уже подтверждён. Существующая запись не изменена." }) : Ok(new { saved = true });
        });
    }

    private IQueryable<AdviceDefect> Defects(Guid vehicleId) => db.VehicleDefects.AsNoTracking().Where(x => x.VehicleId == vehicleId)
        .Select(x => new AdviceDefect(x.Id, x.NodeName, x.CreatedAt, x.OperationalState, x.DowntimeStartedAt));
    private IQueryable<AdviceRepair> Repairs(Guid vehicleId) => db.VehicleWorks.AsNoTracking().Where(x => x.VehicleId == vehicleId && x.DefectId != null)
        .Select(x => new AdviceRepair(x.Id, x.DefectId!.Value, x.CreatedAt, x.CompletedAt, x.RepairStatus));
    private async Task<IActionResult> ReadSafely(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (Exception error) when (error is NpgsqlException or TimeoutException || error is InvalidOperationException { InnerException: NpgsqlException })
        {
            // Do not log source text, database parameters or production records.
            return StatusCode(503, new { status = "unavailable", message = "Локальный помощник недоступен. Повторите запрос; прежний результат не актуален." });
        }
    }
}
