namespace MyApp.Infrastructure.Db.Entities;

public sealed class FleetRegion
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsArchived { get; set; }
}

public sealed class FleetRegionGrant
{
    public Guid UserId { get; set; }
    public Guid RegionId { get; set; }
}

/// <summary>Период закрепления техники. Перемещение закрывает период и создаёт новый, не изменяя журнал машины.</summary>
public sealed class VehicleRegionAssignment
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public Guid RegionId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string Note { get; set; } = "";
}
