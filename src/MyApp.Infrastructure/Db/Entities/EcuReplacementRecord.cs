namespace MyApp.Infrastructure.Db.Entities;

/// <summary>Отметка о замене блока управления ДВС: показание на эту дату — новое начало отсчёта.</summary>
public sealed class EcuReplacementRecord
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public int EngineNumber { get; set; }
    public DateOnly ReplacedOn { get; set; }
    public string Note { get; set; } = "";
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
