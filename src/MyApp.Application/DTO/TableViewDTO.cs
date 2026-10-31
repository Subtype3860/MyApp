namespace MyApp.Application.DTO;

/// <summary>Параметры страницы, поиска и сортировки табличного представления.</summary>
public sealed record TableViewRequest(
    string TableName,
    int Page,
    string PageSize,
    string? Search,
    string? LastName,
    string? FirstName,
    string? Patronymic,
    string? Profession,
    bool IncludeSignatures);

/// <summary>Страница табличных данных и сведения для навигации по результатам.</summary>
public sealed record TableViewResponse(
    IReadOnlyList<string> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    long Total,
    int Page,
    string PageSize);
