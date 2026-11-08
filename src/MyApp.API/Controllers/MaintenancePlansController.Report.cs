using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.DTO;

namespace MyApp.API.Controllers;

public sealed partial class MaintenancePlansController
{
    [HttpGet("shift-report")]
    public async Task<IActionResult> MaintenanceShiftReport(CancellationToken ct)
    {
        // The context's fleet filter also applies to historical accepted cards.
        var cards = await db.MaintenanceCards.AsNoTracking()
            .Where(x => x.Status == "accepted").ToArrayAsync(ct);
        return Ok(cards.Select(card =>
        {
            var data = Decode<CardData>(card.Data);
            var template = Decode<CardTemplate>(card.Template);
            var executors = data.Answers.Where(x => x.Status == "done").Select(x => x.Executor)
                .Concat(data.Materials.Where(x => x.Actual > 0).Select(x => x.Executor))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => string.Join(" ", x.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new { card.Id, card.VehicleId, card.VehicleLabel, template.Service,
                data.StartedAt, data.FinishedAt, data.Notes, executors };
        }));
    }
}
