namespace MyApp.Infrastructure.Db.Entities;

// Persistence representation of the existing vehicle_purchase_requests table.
public sealed class VehiclePurchaseEntity
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateOnly RequestDate { get; set; }
    public required string RequestNumber { get; set; }
    public required string ItemName { get; set; }
    public decimal Quantity { get; set; }
    public required string Status { get; set; }
    public required string Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
