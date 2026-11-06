namespace MyApp.Application.Services;

public sealed record ExtractionSample(string Id, DateTimeOffset CreatedAt, DateTimeOffset CapturedAt,
    string Kind, string Text, Dictionary<string, string> Expected);
public sealed record ExtractionEvaluation(string ModelVersion, DateTimeOffset Cutoff, int BeforeCutoff, int Holdout,
    int Rejected, double? Precision, double? Recall, double? F1, double? ExactMatch,
    double? Coverage, string Status, string[] Limitations);

public static class LocalAdviceEvaluation
{
    /// <summary>Evaluate a frozen extractor on strictly later, immutable snapshots. No fitting on holdout.</summary>
    public static ExtractionEvaluation Evaluate(IEnumerable<ExtractionSample> source, DateTimeOffset cutoff)
    {
        var all = source.ToArray();
        var duplicateIds = all.GroupBy(x => x.Id).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet();
        // A text reconstructed/edited after its original timestamp is not an as-of feature snapshot.
        var usable = all.Where(x => !string.IsNullOrWhiteSpace(x.Id) && !duplicateIds.Contains(x.Id) && x.CapturedAt == x.CreatedAt &&
            x.CreatedAt <= DateTimeOffset.UtcNow && x.Kind is "repair" or "parts" && x.Text.Length <= 5000).ToArray();
        var holdout = usable.Where(x => x.CreatedAt > cutoff).ToArray();
        int tp = 0, fp = 0, fn = 0, exact = 0, covered = 0;
        foreach (var item in holdout) {
            var predicted = LocalAdvice.Parse(item.Text, item.Kind).Fields.ToDictionary(x => x.Name, x => x.Value);
            tp += predicted.Count(x => item.Expected.TryGetValue(x.Key, out var value) && value == x.Value);
            fp += predicted.Count(x => !item.Expected.TryGetValue(x.Key, out var value) || value != x.Value);
            fn += item.Expected.Count(x => !predicted.TryGetValue(x.Key, out var value) || value != x.Value);
            if (predicted.Count == item.Expected.Count && predicted.All(x => item.Expected.TryGetValue(x.Key, out var value) && value == x.Value)) exact++;
            if (predicted.Count > 0) covered++;
        }
        double? precision = tp + fp == 0 ? null : (double)tp / (tp + fp);
        double? recall = tp + fn == 0 ? null : (double)tp / (tp + fn);
        return new("local-extract-v1", cutoff, usable.Length - holdout.Length, holdout.Length, all.Length - usable.Length,
            precision, recall, 2 * tp + fp + fn == 0 ? null : 2.0 * tp / (2 * tp + fp + fn),
            holdout.Length == 0 ? null : (double)exact / holdout.Length, holdout.Length == 0 ? null : (double)covered / holdout.Length,
            holdout.Length == 0 ? "insufficient_data" : "baseline_evaluated_not_approved",
            ["Разделение по времени; одинаковые ID и тексты, восстановленные после события, исключены.",
             "Это оценка извлечения полей, не диагностики причин отказа.",
             "Готовность и риск: precision/recall, Brier и калибровка не определены; модель воздерживается от вероятностей.",
             "Для готовности нужны снимки признаков до смены и подтверждённые итоги. Для риска — полные 30 дней наблюдения после ремонта."]);
    }
}
