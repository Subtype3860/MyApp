namespace MyApp.Application.DTO;

/// <summary>Название создаваемой или изменяемой профессии.</summary>
public record ProfessionRequest(string Profession);

/// <summary>Идентификатор и название профессии для клиента.</summary>
public sealed record ProfessionResponse(Guid Id, string Profession);
