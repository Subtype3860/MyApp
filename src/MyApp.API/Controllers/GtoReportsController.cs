using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Security;
using MyApp.Application.Services;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.API.Controllers;

/// <summary>Снимки прогнозов по участку, дате и смене.</summary>
[ApiController]
[Route("api/gto-reports")]
[Authorize(Policy = Permissions.VehiclesView)]
public sealed class GtoReportsController(AppDbContext db, IVehicleService service) : ControllerBase
{
    public sealed record SaveRequest(int Revision, JsonElement Snapshot);

    private async Task<FleetRegion?> Region(Guid regionId, CancellationToken ct)
    {
        if (regionId == Guid.Empty) return null;
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (db.RestrictFleet && (!Guid.TryParse(actor, out var id) ||
            !await db.FleetRegionGrants.AnyAsync(g => g.UserId == id && g.RegionId == regionId, ct))) return null;
        return await db.FleetRegions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == regionId, ct);
    }
    private async Task<HashSet<Guid>> VehicleIds(Guid regionId, CancellationToken ct) =>
        (await db.VehicleRegionAssignments.Where(a => a.RegionId == regionId && a.EndedAt == null)
            .Select(a => a.VehicleId).ToArrayAsync(ct)).ToHashSet();

    private static JsonObject Snapshot(string source, FleetRegion region, HashSet<Guid> ids)
    {
        var data = FleetReportScope.Filter(source, ids);
        data["regionId"] = region.Id.ToString();
        data["organisation"] = region.Name;
        data["site"] = region.Name;
        return data;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid regionId, [FromQuery] int offset = 0, CancellationToken ct = default)
    {
        if (offset < 0) return BadRequest();
        if (await Region(regionId, ct) is null) return NotFound();
        // Original reports remain available as a fallback; a regional snapshot takes precedence.
        return Ok(await db.GtoReports.AsNoTracking().Where(x => x.RegionId == regionId ||
            (x.RegionId == Guid.Empty && !db.GtoReports.Any(r => r.RegionId == regionId && r.ReportDate == x.ReportDate && r.Shift == x.Shift)))
            .OrderByDescending(x => x.ReportDate).ThenByDescending(x => x.Shift).Skip(offset).Take(100)
            .Select(x => new { Date = x.ReportDate, x.Shift, Revision = x.RegionId == Guid.Empty ? 0 : x.Revision, x.UpdatedAt }).ToArrayAsync(ct));
    }

    [HttpGet("{date}/{shift:int}")]
    public async Task<IActionResult> Get(DateOnly date, int shift, [FromQuery] Guid regionId, CancellationToken ct)
    {
        var region = await Region(regionId, ct);
        if (region is null) return NotFound();
        var report = await db.GtoReports.AsNoTracking().Where(x => x.ReportDate == date && x.Shift == shift &&
            (x.RegionId == regionId || x.RegionId == Guid.Empty)).OrderByDescending(x => x.RegionId == regionId).FirstOrDefaultAsync(ct);
        return report is null ? NotFound() : Ok(new { Revision = report.RegionId == Guid.Empty ? 0 : report.Revision,
            report.UpdatedAt, Snapshot = Snapshot(report.Snapshot, region, await VehicleIds(regionId, ct)) });
    }

    [HttpGet("defaults")]
    public async Task<IActionResult> Defaults(DateOnly date, int shift, [FromQuery] Guid regionId, CancellationToken ct)
    {
        var region = await Region(regionId, ct);
        if (region is null) return NotFound();
        var previous = await db.GtoReports.AsNoTracking().Where(x => (x.RegionId == regionId || x.RegionId == Guid.Empty) &&
            (x.ReportDate < date || (x.ReportDate == date && x.Shift < shift)))
            .OrderByDescending(x => x.ReportDate).ThenByDescending(x => x.Shift).ThenByDescending(x => x.RegionId == regionId)
            .Select(x => x.Snapshot).FirstOrDefaultAsync(ct);
        return Ok(previous is null ? (object)new { rows = Array.Empty<object>() } : Snapshot(previous, region, await VehicleIds(regionId, ct)));
    }

    [HttpPut("{date}/{shift:int}")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> Save(DateOnly date, int shift, [FromQuery] Guid regionId, SaveRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor)) return Unauthorized();
        var region = await Region(regionId, ct);
        if (region is null) return NotFound();
        if (region.IsArchived) return BadRequest(new { message = "Участок находится в архиве." });
        if (!(await service.GetVehicleCatalogOptionsAsync(actor, ct)).CanManage) return Forbid();
        if (!ValidSnapshot(date, shift, request)) return BadRequest(new { message = "Некорректные данные отчёта или КТГ. Проверьте дату, смену и значения от 0 до 100%." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Coordinate assignment changes with validation of the submitted fleet.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(740410119)", ct);
        var ids = await VehicleIds(regionId, ct);
        if (request.Snapshot.GetProperty("rows").EnumerateArray().Any(row =>
            !Guid.TryParse(row.GetProperty("vehicle").GetProperty("id").GetString(), out var id) || !ids.Contains(id)))
            return BadRequest(new { message = "В отчёте есть техника другого участка. Обновите прогноз." });
        var key = $"gto:{regionId}:{date:yyyy-MM-dd}:{shift}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 113))", ct);
        var report = await db.GtoReports.SingleOrDefaultAsync(x => x.RegionId == regionId && x.ReportDate == date && x.Shift == shift, ct);
        if ((report?.Revision ?? 0) != request.Revision)
            return Conflict(new { message = "Отчёт изменён другим пользователем. Откройте сохранённую версию перед продолжением." });
        if (report is null) {
            report = new GtoReport { RegionId = regionId, ReportDate = date, Shift = shift };
            db.GtoReports.Add(report);
        }
        report.Revision++;
        report.Snapshot = Snapshot(request.Snapshot.GetRawText(), region, ids).ToJsonString();
        report.UpdatedBy = actor;
        report.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { report.Revision, report.UpdatedAt });
    }

    /// <summary>Проверяет версию формата, ключ отчёта и редактируемые поля до записи JSON.</summary>
    public static bool ValidSnapshot(DateOnly date, int shift, SaveRequest request)
    {
        var json = request.Snapshot;
        if (date == default || shift is not (1 or 2) || request.Revision < 0 || json.ValueKind != JsonValueKind.Object) return false;
        bool Text(JsonElement item, string name, int max) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= max;
        if (!json.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 ||
            !Text(json, "date", 10) || json.GetProperty("date").GetString() != date.ToString("yyyy-MM-dd") ||
            !Text(json, "shift", 1) || json.GetProperty("shift").GetString() != shift.ToString() ||
            !Text(json, "organisation", 200) || !Text(json, "site", 200) || !Text(json, "mechanic", 400) ||
            !json.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 10000) return false;
        foreach (var row in rows.EnumerateArray()) {
            if (row.ValueKind != JsonValueKind.Object || !Text(row, "maintenance", 2000) || !Text(row, "note", 2000) ||
                !Text(row, "forecast", 20) || row.GetProperty("forecast").GetString() is not ("healthy" or "faulty") ||
                !row.TryGetProperty("vehicle", out var vehicle) || vehicle.ValueKind != JsonValueKind.Object || !Text(vehicle, "id", 36) ||
                !row.TryGetProperty("ktg", out var ktg)) return false;
            if (ktg.ValueKind == JsonValueKind.Null || (ktg.ValueKind == JsonValueKind.String && ktg.GetString() == "")) continue;
            var value = ktg.ValueKind == JsonValueKind.String ? ktg.GetString() : ktg.GetRawText();
            if (!decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent) || percent < 0 || percent > 100) return false;
        }
        return true;
    }
}
