using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Расчёт ТО по фактическому приросту наработки наиболее нагруженного ДВС.</summary>
public static class MaintenancePlanner
{
    /// <summary>За пропущенные полные дни — 22 м/ч, за выбранный день — 11 м/ч.</summary>
    public static MaintenanceEngine Estimate(int number, decimal hours, DateOnly sourceDate, DateOnly targetDate)
    {
        var days = targetDate.DayNumber - sourceDate.DayNumber;
        if (days < 0) throw new ArgumentOutOfRangeException(nameof(targetDate));
        return new(number, hours + (days > 0 ? (days - 1) * 22m + 11m : 0m), days > 0, sourceDate, hours);
    }

    public static string? Validate(MaintenancePlan? plan, int engineCount, DateOnly today)
    {
        if (plan is null || string.IsNullOrWhiteSpace(plan.Name) || plan.Name.Length > 200)
            return "Укажите название графика (до 200 символов).";
        if (plan.Steps is null || plan.Steps.Length is < 1 or > 100 ||
            plan.Steps.Any(s => s is null || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 100 || s.Interval <= 0 || s.Interval > 100000))
            return "Каждый шаг должен иметь название и положительный интервал до 100000 м/ч.";
        if (plan.NextStepIndex < 0 || plan.NextStepIndex >= plan.Steps.Length)
            return "Выберите следующий шаг цикла.";
        if (plan.LastDate == default || plan.LastDate > today || string.IsNullOrWhiteSpace(plan.LastService) || plan.LastService.Length > 200)
            return "Укажите дату и вид последнего ТО.";
        if (!ValidEngines(plan.Engines, engineCount)) return "Укажите показания каждого ДВС на момент ТО.";
        if (plan.RunIns is null || plan.RunIns.Length > 10 || plan.RunIns.Any(r => r is null) ||
            plan.RunIns.Select(r => r.Id).Distinct().Count() != plan.RunIns.Length)
            return "Некорректный список обкаточных ТО.";
        foreach (var item in plan.RunIns)
            if (item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Length > 200 ||
                item.StartDate == default || item.StartDate > today || item.Interval <= 0 || item.Interval > 100000 ||
                item.Engines is null || item.Engines.Length == 0 ||
                !ValidEngines(item.Engines, engineCount, false))
                return "Проверьте причину, дату, интервал и показания для обкаточного ТО.";
        return null;
    }

    public static bool ValidEngines(MaintenanceEngine[]? engines, int count, bool requireAll = true) =>
        engines is not null && engines.Length >= 1 && (!requireAll || engines.Length == count) &&
        engines.All(e => e is not null && e.Number >= 1 && e.Number <= count && e.Hours >= 0 && e.Hours <= 1000000000) &&
        engines.Select(e => e.Number).Distinct().Count() == engines.Length;

    /// <summary>При отсутствии актуальных показаний хотя бы одного выбранного ДВС срок не угадывается.</summary>
    public static MaintenanceDue? Calculate(string kind, Guid? id, string name, decimal interval,
        DateOnly startDate, MaintenanceEngine[] baseline, IReadOnlyList<MaintenanceReading> readings)
    {
        var candidates = new List<MaintenanceDue>();
        foreach (var engine in baseline)
        {
            var reading = readings.FirstOrDefault(r => r.Number == engine.Number);
            if (reading is null || reading.Date < startDate || reading.Hours < engine.Hours) return null;
            var elapsed = reading.Hours - engine.Hours;
            candidates.Add(new(kind, id, name, interval, engine.Number, engine.Hours, reading.Hours,
                engine.Hours + interval, elapsed, interval - elapsed, reading.Date, reading.IsEstimated, reading.SourceDate));
        }
        return candidates.OrderBy(x => x.Remaining).ThenBy(x => x.EngineNumber).FirstOrDefault();
    }

    public static IReadOnlyList<MaintenanceDue> Due(MaintenancePlan plan, IReadOnlyList<MaintenanceReading> readings)
    {
        var step = plan.Steps[plan.NextStepIndex];
        var result = new List<MaintenanceDue>();
        var scheduled = Calculate("scheduled", null, step.Name, step.Interval, plan.LastDate, plan.Engines, readings);
        if (scheduled is not null) result.Add(scheduled);
        foreach (var runIn in plan.RunIns)
        {
            var due = Calculate("run-in", runIn.Id, "Обкаточное ТО: " + runIn.Reason, runIn.Interval, runIn.StartDate, runIn.Engines, readings);
            if (due is not null) result.Add(due);
        }
        return result.OrderBy(x => x.Remaining).ToArray();
    }

    /// <summary>Выполнение планового ТО сдвигает базу на фактические показания и продвигает цикл на один шаг.</summary>
    public static MaintenancePlan Complete(MaintenancePlan plan, MaintenanceCompletion completion) =>
        completion.RunInId is { } id
            ? plan with { RunIns = plan.RunIns.Where(r => r.Id != id).ToArray() }
            : plan with {
                LastDate = completion.Date,
                LastService = plan.Steps[plan.NextStepIndex].Name,
                Engines = completion.Engines,
                NextStepIndex = (plan.NextStepIndex + 1) % plan.Steps.Length
            };
}

