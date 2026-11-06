namespace MyApp.Infrastructure.Db.Entities;

/// <summary>Организация и участок пользователя для отчёта ГТО.</summary>
public sealed class GtoUserSettings
{
    public Guid UserId { get; set; }
    public string Organisation { get; set; } = "";
    public string Site { get; set; } = "";
}
/// <summary>Сохранённые настройки техники для отчёта ГТО.</summary>
public sealed class GtoVehicleSettings
{
    public Guid VehicleId { get; set; }
    public string Climate { get; set; } = "unknown";
}
/// <summary>Планируемый выпуск по неисправности в отчёте ГТО.</summary>
public sealed class GtoDefectSettings
{
    public Guid DefectId { get; set; }
    public string PlannedRelease { get; set; } = "";
}

/// <summary>Настройки двигателя, включая моточасы на момент замены.</summary>
public sealed class VehicleEngineSettings
{
    public Guid VehicleId { get; set; }
    public int EngineNumber { get; set; }
    public decimal? HoursAtReplacement { get; set; }
}
