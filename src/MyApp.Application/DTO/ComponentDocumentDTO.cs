namespace MyApp.Application.DTO;

/// <summary>Исходные данные для формирования требования на материалы.</summary>
public sealed record ComponentDocumentRequest(
    DateOnly Date,
    string VehicleNumber,
    string SourceTable,
    ResponsibleEmployeeSelection ResponsibleEmployee,
    IReadOnlyList<ComponentDocumentItem> Items)
{
    public IReadOnlyList<string> VehicleNumbers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<ComponentDocumentItem>> Pages { get; init; } = [];
    public string JobName { get; init; } = "Аварийная";
    public string AuthorPosition { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string IssuerPosition { get; init; } = string.Empty;
    public string IssuerName { get; init; } = string.Empty;
}

/// <summary>Материал и запрошенное количество в документе требования.</summary>
public sealed record ComponentDocumentItem(
    string Name,
    string Unit,
    decimal Quantity,
    decimal AvailableQuantity);

/// <summary>Данные сформированного требования и его страниц для клиента.</summary>
public sealed record ComponentDocumentResponse(
    ComponentDocumentRequest Document);
