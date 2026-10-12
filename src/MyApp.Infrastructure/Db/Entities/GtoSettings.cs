namespace MyApp.Infrastructure.Db.Entities;

public sealed class GtoUserSettings
{
    public Guid UserId { get; set; }
    public string Organisation { get; set; } = "";
    public string Site { get; set; } = "";
}
public sealed class GtoVehicleSettings
{
    public Guid VehicleId { get; set; }
    public string Climate { get; set; } = "unknown";
}
public sealed class GtoDefectSettings
{
    public Guid DefectId { get; set; }
    public string PlannedRelease { get; set; } = "";
}

public sealed class VehicleEngineSettings
{
    public Guid VehicleId { get; set; }
    public int EngineNumber { get; set; }
    public decimal? HoursAtReplacement { get; set; }
}
