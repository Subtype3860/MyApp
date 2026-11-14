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
        var cardIds = cards.Select(card => card.Id).ToArray();
        var photos = await db.MaintenanceCardPhotos.AsNoTracking()
            .Where(photo => cardIds.Contains(photo.CardId))
            .Select(photo => new { photo.CardId, photo.Id, photo.OperationId })
            .ToArrayAsync(ct);
        return Ok(cards.Select(card =>
        {
            var data = Decode<CardData>(card.Data);
            var template = Decode<CardTemplate>(card.Template);
            var executors = data.Answers.Where(x => x.Status == "done").Select(x => x.Executor)
                .Concat(data.Materials.Where(x => x.Actual > 0).Select(x => x.Executor))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => string.Join(" ", x.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            // Выполненные операции и дефекты — по ним в отчёте показываем описание и исполнителя.
            var operations = template.Operations.Select(operation =>
            {
                var answer = data.Answers.FirstOrDefault(item => item.Id == operation.Id);
                return new
                {
                    operation.Id,
                    operation.Text,
                    operation.Section,
                    status = answer?.Status ?? "",
                    condition = answer?.Condition ?? "",
                    executor = string.Join(" ", (answer?.Executor ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
                    note = answer?.Note ?? "",
                    photos = photos.Where(photo => photo.CardId == card.Id && photo.OperationId == operation.Id)
                        .Select(photo => photo.Id).ToArray(),
                };
            }).Where(item => item.status == "done" || item.condition == "defect").ToArray();
            // Разделы карты: состав из шаблона (без заключительного контроля) и отметки участия.
            var sectionNames = template.Operations.Select(operation => operation.Section)
                .Where(name => !string.IsNullOrWhiteSpace(name) && name != "Заключительный контроль")
                .Concat((data.Sections ?? []).Select(section => section.Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var sections = sectionNames.Select(name => new
            {
                Name = name,
                Participants = (data.Sections ?? []).FirstOrDefault(section => string.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase))?
                    .Participants ?? [],
                Photos = photos.Where(photo => photo.CardId == card.Id && photo.OperationId == "section:" + name)
                    .Select(photo => photo.Id).ToArray(),
            }).ToArray();
            return new { card.Id, card.VehicleId, card.VehicleLabel, template.Service,
                data.StartedAt, data.FinishedAt, data.Notes, executors, operations, sections };
        }));
    }
}
