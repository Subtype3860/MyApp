namespace MyApp.Infrastructure.Db.Entities;

public sealed class MaintenanceItemEntity
{
    public Guid Id { get; set; }
    public Guid IntervalId { get; set; }
    public required string MaterialName { get; set; }
    public decimal Quantity { get; set; }
    public int SortOrder { get; set; }
    public MaintenanceIntervalEntity Interval { get; set; } = null!;
}
