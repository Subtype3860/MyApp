namespace MyApp.Application.DTO;

public sealed record CsvImportResult(int Added, int Updated, int Zeroed = 0)
{
    public int Imported => Added + Updated;
}

public sealed class CsvImportValidationException(string message) : Exception(message);

public sealed record CsvImportPreview(int Added, int Updated, int Zeroed, int Imported,
    string[] Columns, IReadOnlyList<Dictionary<string, object?>> Rows);
