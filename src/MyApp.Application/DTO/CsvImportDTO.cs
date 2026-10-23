namespace MyApp.Application.DTO;

/// <summary>Итоги сохранения импортированных данных.</summary>
public sealed record CsvImportResult(int Added, int Updated, int Zeroed = 0)
{
    public int Imported => Added + Updated;
}

/// <summary>Ошибка проверки содержимого импортируемого файла.</summary>
public sealed class CsvImportValidationException(string message) : Exception(message);

/// <summary>Предварительные результаты проверки импортируемого файла.</summary>
public sealed record CsvImportPreview(int Added, int Updated, int Zeroed, int Imported,
    string[] Columns, IReadOnlyList<Dictionary<string, object?>> Rows);
