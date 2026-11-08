namespace MyApp.Infrastructure.Db.Entities;

/// <summary>Заявка на закупку запчастей, прикреплённая к технике.</summary>
public sealed class VehiclePartsRequestRecord
{
    public Guid Id { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? DefectId { get; set; }
    public DateOnly RequestDate { get; set; }
    public DateOnly? CompletedDate { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RequiredParts { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
