namespace MyApp.Application.DTO;

/// <summary>Шаг цикла: тип ТО и интервал после предыдущего шага, а не абсолютный порог счётчика.</summary>
public sealed record MaintenanceStep(string Name, decimal Interval);
public sealed record MaintenanceEngine(int Number, decimal Hours, bool IsEstimated = false, DateOnly? SourceDate = null, decimal? SourceHours = null);
public sealed record MaintenanceRunIn(Guid Id, string Reason, DateOnly StartDate, decimal Interval, MaintenanceEngine[] Engines);
/// <summary>План отдельной машины. Разовые ТО не меняют основной цикл.</summary>
public sealed record MaintenancePlan(
    string Name, MaintenanceStep[] Steps, int NextStepIndex,
    DateOnly LastDate, string LastService, MaintenanceEngine[] Engines,
    MaintenanceRunIn[] RunIns);
public sealed record MaintenanceReading(int Number, decimal Hours, DateOnly Date, bool IsEstimated = false, DateOnly? SourceDate = null);
public sealed record MaintenanceDue(
    string Kind, Guid? RunInId, string Name, decimal Interval,
    int EngineNumber, decimal StartHours, decimal CurrentHours,
    decimal DueHours, decimal Elapsed, decimal Remaining, DateOnly ReadingDate, bool IsEstimated = false, DateOnly? SourceDate = null);
public sealed record MaintenanceCompletion(int Revision, DateOnly Date, MaintenanceEngine[] Engines, string Description, Guid? RunInId = null);

