using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Security;
using MyApp.Application.Services;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.API.Controllers;

/// <summary>Общие для пользователей снимки отчётов ГТО с контролем одновременного редактирования.</summary>
[ApiController]
[Route("api/gto-reports")]
[Authorize(Policy = Permissions.VehiclesView)]
public sealed class GtoReportsController(AppDbContext db, IVehicleService service) : ControllerBase
{
    public sealed record SaveRequest(int Revision, JsonElement Snapshot);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int offset = 0, CancellationToken ct = default)
    {
        if (offset < 0) return BadRequest();
        return Ok(await db.GtoReports.AsNoTracking().OrderByDescending(x => x.ReportDate).ThenByDescending(x => x.Shift)
            .Skip(offset).Take(100).Select(x => new { Date = x.ReportDate, x.Shift, x.Revision, x.UpdatedAt }).ToArrayAsync(ct));
    }

    [HttpGet("{date}/{shift:int}")]
    public async Task<IActionResult> Get(DateOnly date, int shift, CancellationToken ct)
    {
        var report = await db.GtoReports.AsNoTracking().SingleOrDefaultAsync(x => x.ReportDate == date && x.Shift == shift, ct);
        return report is null ? NotFound() : Ok(new { report.Revision, report.UpdatedAt, Snapshot = JsonSerializer.Deserialize<JsonElement>(report.Snapshot) });
    }

    [HttpGet("defaults")]
    public async Task<IActionResult> Defaults(DateOnly date, int shift, CancellationToken ct)
    {
        var previous = await db.GtoReports.AsNoTracking().Where(x => x.ReportDate < date || (x.ReportDate == date && x.Shift < shift))
            .OrderByDescending(x => x.ReportDate).ThenByDescending(x => x.Shift).Select(x => x.Snapshot).FirstOrDefaultAsync(ct);
        return Ok(previous is null ? (object)new { rows = Array.Empty<object>() } : JsonSerializer.Deserialize<JsonElement>(previous));
    }

    [HttpPut("{date}/{shift:int}")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> Save(DateOnly date, int shift, SaveRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor)) return Unauthorized();
        if (!(await service.GetVehicleCatalogOptionsAsync(actor, ct)).CanManage) return Forbid();
        if (!ValidSnapshot(date, shift, request)) return BadRequest(new { message = "Некорректные данные отчёта или КТГ. Проверьте дату, смену и значения от 0 до 100%." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize create/update for a single report and compare the revision inside the lock.
        var key = $"gto:{date:yyyy-MM-dd}:{shift}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 113))", ct);
        var report = await db.GtoReports.SingleOrDefaultAsync(x => x.ReportDate == date && x.Shift == shift, ct);
        if ((report?.Revision ?? 0) != request.Revision)
            return Conflict(new { message = "Отчёт изменён другим пользователем. Откройте сохранённую версию перед продолжением." });
        if (report is null) {
            report = new GtoReport { ReportDate = date, Shift = shift };
            db.GtoReports.Add(report);
        }
        report.Revision++;
        report.Snapshot = request.Snapshot.GetRawText();
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
