namespace MyApp.Infrastructure.Db.Entities;

public sealed class MaintenanceIntervalEntity
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public MaintenanceEquipmentEntity Equipment { get; set; } = null!;
    public ICollection<MaintenanceItemEntity> Items { get; set; } = [];
}
