namespace MyApp.Application.DTO;

/// <summary>Исходная строка ежедневных моточасов с датой, бортовым номером и номером ДВС.</summary>
public sealed record DailyHoursRow(DateOnly ReadingDate, int GarageNumber, int EngineNumber,
    decimal? EngineHours, bool Invalid, string Source);
/// <summary>Запрос проверки или сохранения ежедневных показаний с подтверждением предупреждений.</summary>
public sealed record DailyHoursRequest(IReadOnlyList<DailyHoursRow> Rows, string? Fingerprint = null,
    bool AcceptWarnings = false);
/// <summary>Сохранённое показание для проверки хронологии счётчика при импорте.</summary>
public sealed record DailyHoursHistory(Guid VehicleId, DateOnly ReadingDate, int EngineNumber,
    decimal EngineHours, DateTimeOffset CreatedAt);
/// <summary>Результат проверки одной строки ежедневных показаний с пояснением статуса.</summary>
public sealed record DailyHoursPreviewRow(Guid? VehicleId, int GarageNumber, DateOnly ReadingDate,
    int EngineNumber, decimal? EngineHours, string Source, string Status, string Message);
/// <summary>Проверенные строки показаний и отпечаток результата для последующего сохранения.</summary>
public sealed record DailyHoursPreview(IReadOnlyList<DailyHoursPreviewRow> Rows, string Fingerprint,
    int Imported = 0)
{
    public TimeOnly ReadingTime => new(8, 0);
}
