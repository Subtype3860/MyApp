namespace MyApp.Infrastructure.Db.Entities;

/// <summary>Explicit mechanic confirmation, independent of forecasts. Append-only.</summary>
public sealed class VehicleShiftOutcome
{
    public Guid VehicleId { get; set; }
    public DateOnly Date { get; set; }
    public int Shift { get; set; }
    public bool ReadyWholeShift { get; set; }
    public Guid ConfirmedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
