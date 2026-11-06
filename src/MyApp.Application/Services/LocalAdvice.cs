using System.Text.RegularExpressions;

namespace MyApp.Application.Services;

public sealed record AdviceDefect(Guid Id, string Node, DateTimeOffset CreatedAt, string State, DateTimeOffset? Downtime);
public sealed record AdviceRepair(Guid Id, Guid DefectId, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string Status);
public sealed record LocalAdviceResult(string Status, string Recommendation, double? Confidence,
    string[] Factors, DateTimeOffset GeneratedAt, DateTimeOffset ExpiresAt, string ModelVersion = "local-rules-v1");
public sealed record DraftField(string Name, string Value, string Evidence);
public sealed record LocalDraft(string Status, DraftField[] Fields, string[] Limitations, string ModelVersion = "local-extract-v1");

/// <summary>Offline baseline. No fitted probabilities until prospective labels and temporal validation exist.</summary>
public static class LocalAdvice
{
    public const int RepeatWindowDays = 30;
    public static string NormalizeNode(string value) => Regex.Replace(value.Trim().ToLowerInvariant().Replace('ё', 'е'), @"\s+", " ");

    public static LocalAdviceResult Readiness(IEnumerable<AdviceDefect> defects, IEnumerable<AdviceRepair> repairs,
        DateTimeOffset now, int hourReadings)
    {
        var known = repairs.Where(x => x.CreatedAt <= now && x.CompletedAt <= now)
            .GroupBy(x => x.DefectId).ToDictionary(x => x.Key, x => x.OrderByDescending(r => r.CompletedAt).ThenByDescending(r => r.CreatedAt).ThenBy(r => r.Id).First());
        var open = defects.Where(x => x.CreatedAt <= now).DistinctBy(x => x.Id)
            .Where(x => !known.TryGetValue(x.Id, out var repair) || repair.Status != "repaired").ToArray();
        return new("insufficient_data", open.Length > 0 ? "Требуется проверка механиком перед сменой" : "Недостаточно данных о фактической готовности",
            null, [$"Незакрытых неисправностей: {open.Length}.", $"Доступных показаний моточасов: {hourReadings}.",
                $"Из них с отметкой об остановке: {open.Count(x => x.State == "stopped")}.",
                $"С зафиксированным началом простоя: {open.Count(x => x.Downtime <= now)}. Длительность ремонта к смене не установлена.",
                "Вероятность не рассчитана: нет проверенной на будущих сменах модели.",
                "Текущее отсутствие неисправностей не подтверждает готовность к выбранной смене."], now, now.AddMinutes(5));
    }

    public static LocalAdviceResult Recurrence(AdviceDefect current, IEnumerable<AdviceDefect> defects,
        IEnumerable<AdviceRepair> repairs, DateTimeOffset now)
    {
        var node = NormalizeNode(current.Node);
        var previous = defects.Where(x => x.Id != current.Id && x.CreatedAt < current.CreatedAt &&
            node.Length > 0 && NormalizeNode(x.Node) == node).DistinctBy(x => x.Id).ToArray();
        var matches = previous.Count(d => repairs.Any(r => r.DefectId == d.Id && r.Status == "repaired" &&
            r.CreatedAt <= current.CreatedAt && r.CompletedAt >= d.CreatedAt && r.CompletedAt < current.CreatedAt &&
            r.CompletedAt >= current.CreatedAt.AddDays(-RepeatWindowDays)));
        return new("insufficient_data", matches > 0 ? "Есть признаки повторной неисправности: проверьте узел" : "Недостаточно данных для оценки риска повторения", null,
            [$"Критерий: новый ID, та же машина и нормализованное название узла, до {RepeatWindowDays} дней после ремонта.",
             $"Подходящих предыдущих неисправностей с ремонтом: {matches}.",
             node.Length == 0 ? "Узел не указан. Начните фиксировать узел при регистрации неисправностей." : "Совпадение узла — признак для проверки, а не установленная причина отказа.",
             "История редактируема; вероятностный риск не валидирован."], now, now.AddMinutes(5));
    }

    public static LocalDraft Parse(string text, string kind)
    {
        if (kind is not ("repair" or "parts")) throw new ArgumentException("Unknown request kind");
        if (text.Length > 5000) throw new ArgumentException("Text too long");
        var fields = new List<DraftField>();
        var labels = kind == "repair"
            ? new Dictionary<string, string> { ["узел"] = "nodeName", ["симптомы"] = "symptoms", ["описание"] = "symptoms", ["причина"] = "failureReason", ["код ошибки"] = "errorCode" }
            : new Dictionary<string, string> { ["описание"] = "description", ["запчасти"] = "requiredParts" };
        foreach (var line in text.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon < 0 || !labels.TryGetValue(line[..colon].Trim().ToLowerInvariant(), out var name)) continue;
            var value = line[(colon + 1)..].Trim();
            if (value.Length > 0) fields.Add(new(name, value, line));
        }
        // Conflicting values are left for manual entry, never arbitrarily resolved.
        var unique = fields.GroupBy(x => x.Name).Where(x => x.Select(f => f.Value).Distinct().Count() == 1).Select(x => x.First()).ToList();
        var primary = kind == "repair" ? "symptoms" : "description";
        if (fields.Count == 0 && !string.IsNullOrWhiteSpace(text)) unique.Add(new(primary, text.Trim(), text.Trim()));
        return new(unique.Count == 0 ? "insufficient_data" : "draft", unique.ToArray(),
            ["Локальное извлечение явно указанных полей; понимание свободного текста не валидировано.",
             "Проверьте каждое поле. Причины отказа не диагностируются. Конфликтующие значения пропущены."]);
    }
}
