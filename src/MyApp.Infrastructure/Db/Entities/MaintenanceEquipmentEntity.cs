namespace MyApp.Infrastructure.Db.Entities;

public sealed class MaintenanceEquipmentEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public ICollection<MaintenanceIntervalEntity> Intervals { get; set; } = [];
}
