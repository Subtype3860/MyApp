using System.Security.Cryptography;
using System.Text.Json;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Deterministic validation shared by preview and transactional commit.</summary>
public static class DailyHoursPlanner
{
    /// <summary>Проверяет хронологию показаний по каждому ДВС, переносит прошлое значение при простое и вычисляет отпечаток результата.</summary>
    public static DailyHoursPreview Build(IReadOnlyList<DailyHoursRow> input,
        IReadOnlyList<VehicleResponse> vehicles, IReadOnlyList<DailyHoursHistory> history)
    {
        // Generic single-engine headers and explicit ДВС1 identify the same counter.
        input = input.Select(r => r.EngineNumber == 0 && vehicles.Count(v => !v.IsArchived && v.GarageNumber == r.GarageNumber) == 1 &&
            vehicles.Single(v => !v.IsArchived && v.GarageNumber == r.GarageNumber).EngineCount == 1 ? r with { EngineNumber = 1 } : r).ToArray();
        var singleEngineIds = vehicles.Where(v => v.EngineCount == 1).Select(v => v.Id).ToHashSet();
        // Older readings without an engine number are usable only for single-engine equipment.
        history = history.Where(h => h.EngineNumber != 0 || singleEngineIds.Contains(h.VehicleId))
            .Select(h => h.EngineNumber == 0 ? h with { EngineNumber = 1 } : h).ToArray();
        var output = new List<DailyHoursPreviewRow>();
        var duplicates = input.GroupBy(r => (r.ReadingDate, r.GarageNumber, r.EngineNumber))
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var timelines = history.GroupBy(h => (h.VehicleId, h.EngineNumber)).ToDictionary(g => g.Key,
            g => g.GroupBy(h => h.ReadingDate).ToDictionary(d => d.Key, d => d.OrderByDescending(h => h.CreatedAt).First().EngineHours));
        var blocked = new HashSet<(Guid, int)>();
        var seen = new HashSet<(Guid, int, DateOnly)>();
        foreach (var row in input.OrderBy(r => r.ReadingDate).ThenBy(r => r.GarageNumber).ThenBy(r => r.EngineNumber))
        {
            var matches = vehicles.Where(v => !v.IsArchived && v.GarageNumber == row.GarageNumber).ToArray();
            var vehicle = matches.Length == 1 ? matches[0] : null;
            var engine = row.EngineNumber == 0 && vehicle?.EngineCount == 1 ? 1 : row.EngineNumber;
            /** Добавляет результат проверки текущей строки с её источником и сообщением. */
            void Add(string status, string message, decimal? value = null) => output.Add(new(vehicle?.Id,
                row.GarageNumber, row.ReadingDate, engine, value ?? row.EngineHours, row.Source, status, message));
            if (vehicle is null) { Add("error", matches.Length == 0 ? "Бортовой номер не найден." : "Бортовой номер неоднозначен."); continue; }
            var key = (vehicle.Id, engine);
            if (engine < 1 || engine > vehicle.EngineCount) { Add("error", "Укажите отдельные столбцы ДВС1 и ДВС2 для самосвала; для другой техники — один ДВС."); continue; }
            if (duplicates.Contains((row.ReadingDate, row.GarageNumber, row.EngineNumber)) || !seen.Add((vehicle.Id, engine, row.ReadingDate))) {
                blocked.Add(key); Add("error", "Повтор даты, бортового номера и ДВС в файле."); continue;
            }
            if (row.Invalid || row.EngineHours is < 0 or > 1000000000) { blocked.Add(key); Add("error", "Некорректное показание. Исправьте исходную ячейку."); continue; }
            if (!timelines.TryGetValue(key, out var timeline)) timelines[key] = timeline = new();
            var prior = timeline.Where(p => p.Key < row.ReadingDate).OrderByDescending(p => p.Key).ToArray();
            var next = timeline.Where(p => p.Key > row.ReadingDate).OrderBy(p => p.Key).ToArray();
            var value = row.EngineHours;
            if (value is null) {
                if (blocked.Contains(key)) { Add("error", "Простой после ошибочной записи: требуется верное показание."); continue; }
                if (prior.Length == 0) { Add("error", "Нет предыдущего показания для простоя."); continue; }
                value = prior[0].Value;
            }
            if (prior.Length > 0 && value < prior[0].Value || next.Length > 0 && value > next[0].Value) {
                blocked.Add(key); Add("error", "Показание нарушает хронологию счётчика. Проверьте ошибку или замену ДВС.", value); continue;
            }
            if (timeline.TryGetValue(row.ReadingDate, out var existing)) {
                if (existing != value) { blocked.Add(key); Add("error", $"За эту дату уже сохранено {existing}. Измените запись отдельно.", value); }
                else { blocked.Remove(key); Add("unchanged", "Уже сохранено; повторная запись не создаётся.", value); }
                continue;
            }
            var warning = prior.Length > 0 && value - prior[0].Value > 24m * (row.ReadingDate.DayNumber - prior[0].Key.DayNumber);
            timeline[row.ReadingDate] = value.Value;
            blocked.Remove(key);
            Add(warning ? "warning" : "ready", warning ? "Прирост превышает 24 м/ч в сутки. Требуется подтверждение."
                : row.EngineHours is null ? "Простой: перенесено предыдущее показание." : "Готово к сохранению.", value);
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(output)));
        return new(output, fingerprint);
    }
}
