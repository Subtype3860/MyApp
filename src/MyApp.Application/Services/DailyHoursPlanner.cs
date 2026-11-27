using System.Security.Cryptography;
using System.Text.Json;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Deterministic validation shared by preview and transactional commit.</summary>
public static class DailyHoursPlanner
{
    /// <summary>Проверяет хронологию показаний по каждому ДВС, переносит прошлое значение при простое и вычисляет отпечаток результата.</summary>
    public static DailyHoursPreview Build(IReadOnlyList<DailyHoursRow> input,
        IReadOnlyList<VehicleResponse> vehicles, IReadOnlyList<DailyHoursHistory> history,
        IReadOnlySet<(Guid VehicleId, int EngineNumber, DateOnly Date)>? ecuReplacements = null)
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
        foreach (var group in input.GroupBy(r => (r.ReadingDate, r.GarageNumber))
            .OrderBy(g => g.Key.ReadingDate).ThenBy(g => g.Key.GarageNumber))
        {
            var originalRows = group.OrderBy(r => r.EngineNumber).ToArray();
            var candidates = vehicles.Where(v => !v.IsArchived && v.GarageNumber == group.Key.GarageNumber).ToArray();
            var pair = candidates.Length == 1 && candidates[0].EngineCount == 2
                ? Synchronize(originalRows, candidates[0].Id, timelines, blocked, ecuReplacements)
                : (Rows: originalRows, Error: (string?)null, Message: (string?)null);
            foreach (var row in pair.Rows)
            {
                var matches = vehicles.Where(v => !v.IsArchived && v.GarageNumber == row.GarageNumber).ToArray();
                var vehicle = matches.Length == 1 ? matches[0] : null;
                var engine = row.EngineNumber == 0 && vehicle?.EngineCount == 1 ? 1 : row.EngineNumber;
                /** Добавляет результат проверки текущей строки с её источником и сообщением. */
                void Add(string status, string message, decimal? value = null) => output.Add(new(vehicle?.Id,
                    row.GarageNumber, row.ReadingDate, engine, value ?? row.EngineHours, row.Source, status, message));
                if (vehicle is null) { Add("error", matches.Length == 0 ? "Бортовой номер не найден." : "Бортовой номер неоднозначен."); continue; }
                var key = (vehicle.Id, engine);
                if (pair.Error is not null) { blocked.Add(key); Add("error", pair.Error); continue; }
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
                // Замена ЭБУ: показание на эту дату — новое начало отсчёта, скачок допустим.
                if (ecuReplacements?.Contains((vehicle.Id, engine, row.ReadingDate)) == true)
                {
                    timeline[row.ReadingDate] = value.Value;
                    blocked.Remove(key);
                    Add("ready", "Замена ЭБУ: показание принято как новое начало отсчёта.", value);
                    continue;
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
                if (warning && vehicle.EngineCount == 2) {
                    blocked.Add(key); Add("error", "Прирост превышает 24 м/ч в сутки. Для расчёта по норме 23 часа нужны показания обоих ДВС.", value); continue;
                }
                timeline[row.ReadingDate] = value.Value;
                blocked.Remove(key);
                Add(warning ? "warning" : "ready", pair.Message ?? (warning ? "Прирост превышает 24 м/ч в сутки. Требуется подтверждение."
                    : row.EngineHours is null ? "Простой: перенесено предыдущее показание." : "Готово к сохранению."), value);
            }
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(output)));
        return new(output, fingerprint);
    }

    /// <summary>Согласует пару счётчиков по большему приросту за одинаковый период, сохраняя отдельные начальные показания.</summary>
    private static (DailyHoursRow[] Rows, string? Error, string? Message) Synchronize(
        DailyHoursRow[] rows, Guid vehicleId,
        Dictionary<(Guid, int), Dictionary<DateOnly, decimal>> timelines, HashSet<(Guid, int)> blocked,
        IReadOnlySet<(Guid VehicleId, int EngineNumber, DateOnly Date)>? ecuReplacements = null)
    {
        // An incomplete pair or duplicate is handled by the ordinary row validation.
        if (rows.Length != 2 || rows[0].EngineNumber != 1 || rows[1].EngineNumber != 2)
            return (rows, null, null);
        // При замене ЭБУ пара не выравнивается: каждая строка принимается как новый отсчёт.
        var date = rows[0].ReadingDate;
        if (ecuReplacements is not null && rows.Any(r => ecuReplacements.Contains((vehicleId, r.EngineNumber, date))))
            return (rows, null, null);
        if (rows.Any(r => r.Invalid || r.EngineHours is < 0 or > 1000000000))
            return (rows, "Некорректное показание одного из ДВС. Исправьте пару показаний.", null);
        var histories = rows.Select(r => timelines.GetValueOrDefault((vehicleId, r.EngineNumber)) ?? new()).ToArray();
        var prior = histories.Select(h => h.Where(p => p.Key < date).OrderByDescending(p => p.Key).ToArray()).ToArray();
        // Two explicit initial readings establish the baseline; there is no elapsed interval to compare yet.
        if (prior.All(p => p.Length == 0)) return (rows, null, null);
        if (prior.Any(p => p.Length == 0) || prior[0][0].Key != prior[1][0].Key)
            return (rows, "Для сравнения ДВС нужны предыдущие показания обоих двигателей на одну дату.", null);
        if (rows.Any(r => r.EngineHours is null && blocked.Contains((vehicleId, r.EngineNumber))))
            return (rows, "Простой после ошибочной записи: требуется верная пара показаний.", null);
        var days = date.DayNumber - prior[0][0].Key.DayNumber;
        var deltas = rows.Select((r, i) => (r.EngineHours ?? prior[i][0].Value) - prior[i][0].Value).ToArray();
        if (deltas.Any(d => d < 0))
            return (rows, "Оба счётчика ДВС уменьшились. Проверьте показания или замену двигателей.", null);
        var limit = 24m * days;
        var norm = MaintenancePlanner.DailyHoursNorm * days;
        var spread = Math.Abs(deltas[0] - deltas[1]);
        var withinLimit = deltas.Select(d => d <= limit).ToArray();
        // 24 м/ч в сутки физически возможно: техника зимой работает круглосуточно.
        // Поэтому любые значения в пределах порога считаем достоверными и не выравниваем.
        if (withinLimit.All(x => x)) return (rows, null, null);
        decimal delta;
        string reason;
        if (withinLimit.Count(x => x) == 1)
        {
            var index = Array.IndexOf(withinLimit, true);
            delta = deltas[index];
            reason = $"Расхождение показаний ДВС {spread} м/ч; принят прирост по ДВС №{index + 1}.";
        }
        else
        {
            delta = norm;
            reason = "Прирост обоих ДВС превышает 24 м/ч в сутки; применена расчётная норма 23 м/ч в сутки.";
        }
        var adjusted = rows.Select((r, i) => r with { EngineHours = prior[i][0].Value + delta }).ToArray();
        for (var i = 0; i < adjusted.Length; i++)
        {
            var value = adjusted[i].EngineHours!.Value;
            if (value > 1000000000 || histories[i].Any(p => p.Key > date && p.Value < value) ||
                histories[i].TryGetValue(date, out var existing) && existing != value)
                return (rows, "Согласованная пара ДВС конфликтует с сохранёнными показаниями. Измените записи отдельно.", null);
        }
        var changed = rows.Where((r, i) => (r.EngineHours ?? prior[i][0].Value) != adjusted[i].EngineHours).Any();
        var message = changed
            ? reason + " " +
              $"Прирост обоих ДВС: +{delta} м/ч за {days} дн. " +
              $"Исходные ДВС1/ДВС2: {rows[0].EngineHours?.ToString() ?? "пусто"}/{rows[1].EngineHours?.ToString() ?? "пусто"}; " +
              $"итог: {adjusted[0].EngineHours}/{adjusted[1].EngineHours}."
            : null;
        return (adjusted, null, message);
    }
}
