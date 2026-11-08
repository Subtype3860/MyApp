namespace MyApp.Application.DTO;

public sealed record CardOperation(string Id, string Section, string Text, string Target = "", bool Control = false);
public sealed record CardMaterial(string Id, string Assembly, string Name, string PartNumber, string Unit, decimal? Quantity, string Note = "");
public sealed record CardTemplate(string Model, string Service, string Source, string ReviewNotes, CardOperation[] Operations, CardMaterial[] Materials,
    Guid? MaterialIntervalId = null, string? MaterialSource = null);
public sealed record CardAnswer(string Id, string Status, string Condition, string Reason, string Executor, string Note, decimal? Measurement = null, string Unit = "");
public sealed record CardConsumption(string Id, decimal? Actual, string Substitute, string Executor);
public sealed record CardData(DateOnly Date, string Driver, string EngineModel, decimal? Mileage,
    DateTimeOffset? StoppedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, DateTimeOffset? ReleasedAt,
    string Notes, CardAnswer[] Answers, CardConsumption[] Materials);
public sealed record CardAudit(int Revision, Guid Actor, DateTimeOffset At, string Action, string Reason, CardData Data);
