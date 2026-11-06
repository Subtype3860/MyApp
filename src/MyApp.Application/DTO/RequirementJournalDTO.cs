namespace MyApp.Application.DTO;

/// <summary>Сводные данные записи журнала требований.</summary>
public sealed record RequirementJournalEntry(
    Guid Id,
    DateTimeOffset CreatedAt,
    string AuthorName,
    string IssuerName,
    string VehicleNumber,
    IReadOnlyList<ComponentDocumentItem> Items);
