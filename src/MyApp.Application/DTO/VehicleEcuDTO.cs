namespace MyApp.Application.DTO;

/// <summary>Отметка о замене блока управления ДВС.</summary>
public sealed record EcuReplacementRequest(int EngineNumber, DateOnly ReplacedOn, string Note = "");
public sealed record EcuReplacementView(Guid Id, Guid VehicleId, int EngineNumber, DateOnly ReplacedOn, string Note, DateTimeOffset CreatedAt);
