using MyApp.Application.DTO;

namespace MyApp.Application.Services;

public static class MaintenanceCardRules
{
    public static string Normalize(string? value) => new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    public static string? ValidateTemplate(CardTemplate? template)
    {
        if (template is null || string.IsNullOrWhiteSpace(template.Model) || template.Model.Length > 200 ||
            string.IsNullOrWhiteSpace(template.Service) || template.Service.Length > 100 ||
            template.Operations is null || template.Operations.Length is < 1 or > 300 || template.Materials is null || template.Materials.Length > 100)
            return "Укажите модель, вид ТО и от 1 до 300 операций.";
        if (template.Operations.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Text) || x.Text.Length > 3000 || string.IsNullOrWhiteSpace(x.Section)) ||
            template.Materials.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Unit) || x.Quantity < 0) ||
            template.Operations.Select(x => x.Id).Distinct().Count() != template.Operations.Length ||
            template.Materials.Select(x => x.Id).Distinct().Count() != template.Materials.Length)
            return "Проверьте операции, единицы и нормы материалов. Идентификаторы строк должны быть уникальны.";
        return null;
    }

    public static string? Validate(CardTemplate template, CardData? data, DateOnly today, bool submitting)
    {
        if (data is null || data.Date == default || data.Date > today || data.Mileage < 0 || data.Answers is null || data.Materials is null ||
            data.Answers.Length != template.Operations.Length || data.Materials.Length != template.Materials.Length)
            return "Проверьте дату, пробег и состав карты.";
        if (data.Answers.Any(x => x is null) || data.Materials.Any(x => x is null) ||
            !data.Answers.Select(x => x.Id).Order().SequenceEqual(template.Operations.Select(x => x.Id).Order()) ||
            !data.Materials.Select(x => x.Id).Order().SequenceEqual(template.Materials.Select(x => x.Id).Order()))
            return "Состав карты отличается от сохранённого шаблона.";
        if (data.Answers.Any(x => !new[] { "", "done", "not-done", "na" }.Contains(x.Status) ||
            !new[] { "", "ok", "defect" }.Contains(x.Condition) || (x.Reason?.Length ?? 0) > 3000 || (x.Note?.Length ?? 0) > 5000 || (x.Executor?.Length ?? 0) > 200) ||
            data.Materials.Any(x => x.Actual < 0 || x.Actual > 1000000) || (data.Notes?.Length ?? 0) > 10000)
            return "Проверьте результаты операций и фактические количества.";
        var times = new[] { data.StoppedAt, data.StartedAt, data.FinishedAt, data.ReleasedAt }.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        if (!times.SequenceEqual(times.Order())) return "Время остановки, начала, окончания и возврата в работу должно идти по порядку.";
        if (!submitting) return null;
        if (data.StartedAt is null || data.FinishedAt is null || data.FinishedAt > DateTimeOffset.UtcNow)
            return "Укажите начало и окончание работ (не в будущем).";
        foreach (var answer in data.Answers)
        {
            if (string.IsNullOrEmpty(answer.Status) || string.IsNullOrWhiteSpace(answer.Executor)) return "Заполните все операции и укажите исполнителей.";
            if (answer.Status is "not-done" or "na" && string.IsNullOrWhiteSpace(answer.Reason)) return "Укажите причины невыполнения и неприменимости.";
            if (template.Operations.Single(x => x.Id == answer.Id).Control && answer.Status != "done") return "Заключительные контрольные операции должны быть выполнены.";
            if (answer.Condition == "defect" && string.IsNullOrWhiteSpace(answer.Note)) return "Опишите обнаруженные дефекты.";
        }
        if (data.Materials.Any(x => x.Actual is null || (x.Actual > 0 && string.IsNullOrWhiteSpace(x.Executor))))
            return "Укажите фактический расход всех материалов (включая нулевой) и исполнителей для использованных материалов.";
        return null;
    }
}
