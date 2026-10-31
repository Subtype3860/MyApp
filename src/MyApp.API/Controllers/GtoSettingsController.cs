using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Security;
using MyApp.Application.Services;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.API.Controllers;

/// <summary>HTTP-эндпоинты настроек отчёта ГТО.</summary>
[ApiController]
[Route("api/gto-settings")]
[Authorize(Policy = Permissions.VehiclesView)]
public sealed class GtoSettingsController(AppDbContext db, IVehicleService service) : ControllerBase
{
    /// <summary>Организация и участок пользователя для сохранения настроек ГТО.</summary>
    public sealed record UserSettingsRequest(string Organisation, string Site);
    /// <summary>Климатическая установка, план выпуска и актуальные неисправности техники.</summary>
    public sealed record VehicleSettingsRequest(string Climate, string PlannedRelease, Guid[] DefectIds);
    /// <summary>Показания двигателя и моточасы на момент его замены.</summary>
    public sealed record EngineSettingsRequest(DateOnly ReadingDate, decimal? EngineHours, decimal? HoursAtReplacement);
    private Guid? Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
    /// <summary>Проверяет право пользователя изменять настройки ГТО через права управления парком.</summary>
    private async Task<bool> CanEdit(Guid id, CancellationToken ct) => (await service.GetVehicleCatalogOptionsAsync(id, ct)).CanManage;

    /// <summary>Возвращает пользовательские настройки ГТО, климатические установки, планы выпуска и настройки ДВС.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (Actor is not Guid actor) return Unauthorized();
        var preferences = await db.GtoUserSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == actor, ct);
        var vehicles = await db.GtoVehicleSettings.AsNoTracking().Select(x => new { x.VehicleId, x.Climate }).ToArrayAsync(ct);
        var plans = await db.GtoDefectSettings.AsNoTracking().Select(x => new { x.DefectId, x.PlannedRelease }).ToArrayAsync(ct);
        var engines = await db.VehicleEngineSettings.AsNoTracking().Select(x => new { x.VehicleId, x.EngineNumber, x.HoursAtReplacement }).ToArrayAsync(ct);
        return Ok(new { Engines = engines, HasUserSettings = preferences != null, Organisation = preferences?.Organisation ?? "", Site = preferences?.Site ?? "", Vehicles = vehicles, Plans = plans });
    }

    /// <summary>Сохраняет настройки двигателя и, при наличии, показание моточасов в одной транзакции.</summary>
    [HttpPut("vehicles/{vehicleId:guid}/engines/{engineNumber:int}")]
    public async Task<IActionResult> SaveEngine(Guid vehicleId, int engineNumber, EngineSettingsRequest request, CancellationToken ct)
    {
        if (Actor is not Guid actor) return Unauthorized();
        if (!await service.CanManageHoursAsync(actor, ct)) return Forbid();
        var vehicle = (await service.GetVehiclesAsync(ct)).FirstOrDefault(x => x.Id == vehicleId);
        if (vehicle is null) return NotFound();
        if (engineNumber < 1 || engineNumber > vehicle.EngineCount || request.EngineHours < 0 || request.HoursAtReplacement < 0 || request.ReadingDate == default)
            return BadRequest(new { message = "Проверьте номер ДВС, дату и неотрицательные показания моточасов." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({vehicleId.ToString()}, 112))", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO vehicle_engine_settings(vehicle_id,engine_number,hours_at_replacement) VALUES ({vehicleId},{engineNumber},{request.HoursAtReplacement}) ON CONFLICT(vehicle_id,engine_number) DO UPDATE SET hours_at_replacement=EXCLUDED.hours_at_replacement", ct);
        if (request.EngineHours.HasValue) {
            db.VehicleHourReadings.Add(new VehicleHourReadingRecord { Id = Guid.NewGuid(), VehicleId = vehicleId, EngineNumber = engineNumber,
                ReadingDate = request.ReadingDate, EngineHours = request.EngineHours.Value, Note = "Показания ДВС", CreatedBy = actor });
            await db.SaveChangesAsync(ct);
        }
        var latest = await db.VehicleHourReadings.AsNoTracking().Where(x => x.VehicleId == vehicleId && (x.EngineNumber == engineNumber || (vehicle.EngineCount == 1 && x.EngineNumber == 0)))
            .OrderByDescending(x => x.ReadingDate).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new { x.EngineHours, x.ReadingDate }).FirstOrDefaultAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { EngineNumber = engineNumber, EngineHours = latest?.EngineHours, ReadingDate = latest?.ReadingDate, request.HoursAtReplacement });
    }

    /// <summary>Сохраняет организацию и участок пользователя для отчёта ГТО.</summary>
    [HttpPut("user")]
    public async Task<IActionResult> SaveUser(UserSettingsRequest request, CancellationToken ct)
    {
        if (Actor is not Guid actor) return Unauthorized();
        if (!await CanEdit(actor, ct)) return StatusCode(403);
        if (request.Organisation is null || request.Site is null || request.Organisation.Length > 200 || request.Site.Length > 200) return BadRequest(new { message = "Организация и участок: не более 200 символов." });
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO gto_user_settings(user_id,organisation,site) VALUES ({actor},{request.Organisation.Trim()},{request.Site.Trim()}) ON CONFLICT(user_id) DO UPDATE SET organisation=EXCLUDED.organisation,site=EXCLUDED.site", ct);
        return NoContent();
    }

    /// <summary>Сохраняет климатическую установку и план выпуска для актуального состава неисправностей.</summary>
    [HttpPut("vehicles/{vehicleId:guid}")]
    public async Task<IActionResult> SaveVehicle(Guid vehicleId, VehicleSettingsRequest request, CancellationToken ct)
    {
        if (Actor is not Guid actor) return Unauthorized();
        if (!await CanEdit(actor, ct)) return StatusCode(403);
        if (!new[] { "unknown", "healthy", "faulty", "absent" }.Contains(request.Climate) || request.PlannedRelease is null || request.PlannedRelease.Length > 2000 || request.DefectIds is null) return BadRequest(new { message = "Некорректные настройки техники." });
        if (!await db.Vehicles.AnyAsync(x => x.Id == vehicleId && !x.IsArchived, ct)) return NotFound();
        var openIds = await db.VehicleDefects.Where(x => x.VehicleId == vehicleId &&
            db.VehicleWorks.Where(w => w.DefectId == x.Id && w.CompletedAt != null)
                .OrderByDescending(w => w.CompletedAt).ThenByDescending(w => w.CreatedAt)
                .Select(w => w.RepairStatus).FirstOrDefault() != "repaired")
            .Select(x => x.Id).ToArrayAsync(ct);
        if (!openIds.Order().SequenceEqual(request.DefectIds.Distinct().Order())) return Conflict(new { message = "Состав ремонтных заявок изменился. Обновите данные ремонта." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO gto_vehicle_settings(vehicle_id,climate) VALUES ({vehicleId},{request.Climate}) ON CONFLICT(vehicle_id) DO UPDATE SET climate=EXCLUDED.climate", ct);
        foreach (var id in openIds)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO gto_defect_settings(defect_id,planned_release) VALUES ({id},{request.PlannedRelease.Trim()}) ON CONFLICT(defect_id) DO UPDATE SET planned_release=EXCLUDED.planned_release", ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }
}
