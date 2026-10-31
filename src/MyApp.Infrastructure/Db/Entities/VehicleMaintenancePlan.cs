namespace MyApp.Infrastructure.Db.Entities;

/// <summary>Текущий график ТО конкретной техники с оптимистической блокировкой.</summary>
public sealed class VehicleMaintenancePlan
{
    public Guid VehicleId { get; set; }
    public int Revision { get; set; }
    public string Snapshot { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
}
/// <summary>Неизменяемый журнал версий и выполнения ТО; хранит график до и после операции.</summary>
public sealed class VehicleMaintenanceEvent
{
    public string? RequestHash { get; set; }
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public int Revision { get; set; }
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Before { get; set; } = "{}";
    public string After { get; set; } = "{}";
    public string Readings { get; set; } = "[]";
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

