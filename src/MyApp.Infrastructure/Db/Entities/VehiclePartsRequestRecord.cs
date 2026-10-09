namespace MyApp.Infrastructure.Db.Entities;

public sealed class VehiclePartsRequestRecord
{
    public Guid Id { get; set; }
    public Guid DefectId { get; set; }
    public DateOnly RequestDate { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RequiredParts { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
