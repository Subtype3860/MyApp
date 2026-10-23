using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using MyApp.Application.DTO;

namespace MyApp.Infrastructure.Storage;

/// <summary>Распознанная строка CSV, подготовленная к проверке и сохранению.</summary>
public sealed record CsvImportRow(string Key, Dictionary<string, object?> Data);
/// <summary>Результат разбора CSV с заголовками и строками данных.</summary>
public sealed record ParsedCsv(string[] Columns, IReadOnlyList<CsvImportRow> Rows);

/// <summary>Разбор, проверка и экспорт CSV для складских данных и сотрудников.</summary>
public static class CsvImportCodec
{
    /// <summary>Разбирает и проверяет CSV с учётом типа импортируемых данных.</summary>
    public static ParsedCsv Parse(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        if (content.Length > 20 * 1024 * 1024) throw new CsvImportValidationException("CSV должен быть не больше 20 МБ.");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(content).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new CsvImportValidationException("CSV должен быть в кодировке UTF-8."); }
        using var lines = new StringReader(text);
        var firstLine = lines.ReadLine() ?? "";
        while (string.IsNullOrWhiteSpace(firstLine) && lines.Peek() >= 0) firstLine = lines.ReadLine()!;
        var delimiter = DetectDelimiter(firstLine);
        using var parser = new TextFieldParser(new StringReader(text)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(delimiter);
        try
        {
            var rawHeaders = parser.ReadFields() ?? [];
            var columns = rawHeaders.Select(header => CanonicalHeader(header, fileName == "p.csv")).ToArray();
            if (columns.Length is < 2 or > 80 || columns.Any(column => string.IsNullOrWhiteSpace(column) || column.Length > 256) || columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
                throw new CsvImportValidationException("Заголовки CSV должны быть непустыми и уникальными (не больше 80 колонок).");
            if (fileName == "p.csv")
            {
                if (columns.Contains("Роспись"))
                    throw new CsvImportValidationException("Подписи работников задаются отдельно; удалите колонку «Роспись» из CSV.");
                if (!columns.Contains("Profession") || !columns.Contains("FullName") && !(columns.Contains("LastName") && columns.Contains("FirstName")))
                    throw new CsvImportValidationException("CSV работников требует ФИО и профессию либо колонки LastName, FirstName, Patronymic, Profession.");
            }
            else if (fileName is "o.csv" or "c.csv")
            {
                if (!new[] { "Наименование", "Ед.изм.", "Количество" }.All(columns.Contains))
                    throw new CsvImportValidationException("CSV остатков требует заголовки: Наименование, Ед.изм., Количество (или name, unit, amount).");
            }
            else throw new CsvImportValidationException("Неизвестный CSV-файл.");

            var keys = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<CsvImportRow>();
            while (!parser.EndOfData)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lineNumber = parser.LineNumber;
                var fields = parser.ReadFields()!;
                if (fields.Length != columns.Length) throw Error(lineNumber, "Количество полей не совпадает с заголовком.");
                if (fields.Any(field => field.Length > 16384)) throw Error(lineNumber, "Поле длиннее 16384 символов.");
                var data = columns.Select((column, index) => (column, value: (object?)fields[index].Trim()))
                    .ToDictionary(item => item.column, item => item.value, StringComparer.Ordinal);
                if (data.TryGetValue("ID", out var id) && !string.IsNullOrWhiteSpace((string?)id) && !Guid.TryParse((string?)id, out _))
                    throw Error(lineNumber, "ID должен быть UUID или пустым для новой записи.");
                string key;
                if (fileName == "p.csv")
                {
                    if (data.TryGetValue("FullName", out var fullName))
                    {
                        var parts = ((string)fullName!).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2) throw Error(lineNumber, "ФИО должно содержать фамилию и имя.");
                        var names = new Dictionary<string, string> { ["LastName"] = parts[0], ["FirstName"] = parts[1], ["Patronymic"] = string.Join(' ', parts.Skip(2)) };
                        foreach (var (column, value) in names)
                        {
                            if (data.TryGetValue(column, out var existing) && (string)existing! != value)
                                throw Error(lineNumber, "ФИО не соответствует отдельным колонкам имени.");
                            data[column] = value;
                        }
                    }
                    data.TryAdd("Patronymic", "");
                    if (new[] { "LastName", "FirstName", "Profession" }.Any(column => string.IsNullOrWhiteSpace((string?)data[column])))
                        throw Error(lineNumber, "Укажите фамилию, имя и профессию работника.");
                    key = string.Join(' ', new[] { data["LastName"], data["FirstName"], data["Patronymic"] }.Cast<string>().Where(value => value.Length > 0));
                    data["FullName"] = key;
                }
                else
                {
                    key = (string)data["Наименование"]!;
                    if (key.Length == 0 || string.IsNullOrWhiteSpace((string?)data["Ед.изм."])) throw Error(lineNumber, "Наименование и единица измерения обязательны.");
                    if (!decimal.TryParse(((string)data["Количество"]!).Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var quantity) || quantity < 0)
                        throw Error(lineNumber, "Количество должно быть неотрицательным числом.");
                    data["Количество"] = quantity;
                }
                if (!keys.Add(fileName == "p.csv" && data.TryGetValue("ID", out var workerId) && Guid.TryParse((string?)workerId, out var workerGuid) ? workerGuid.ToString() : key)) throw Error(lineNumber, $"Повторяющаяся запись «{key}»; импорт отменён.");
                rows.Add(new(key, data));
                if (rows.Count > 100_000) throw Error(lineNumber, "В одном файле допускается не больше 100000 записей.");
            }
            if (rows.Count == 0) throw new CsvImportValidationException("CSV не содержит записей.");
            return new(columns, rows);
        }
        catch (MalformedLineException error) { throw Error(error.LineNumber, "Некорректные кавычки или разделители CSV."); }
    }

    /// <summary>Сериализует заголовки и строки данных в CSV.</summary>
    public static byte[] Export(string[] columns, IEnumerable<Dictionary<string, object?>> rows)
    {
        /** Экранирует значение ячейки для записи в CSV. */
        static string Cell(object? value)
        {
            var text = value is JsonElement json ? json.ValueKind == JsonValueKind.Null ? "" : json.ToString()
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return text.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
        }
        var output = new StringBuilder().AppendJoin(';', columns.Select(Cell)).Append("\r\n");
        foreach (var row in rows) output.AppendJoin(';', columns.Select(column => Cell(row.GetValueOrDefault(column)))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(output.ToString())).ToArray();
    }

    /// <summary>Создаёт ошибку проверки импортируемого CSV.</summary>
    private static CsvImportValidationException Error(long line, string message) => new($"Строка {line}: {message}");

    /// <summary>Определяет разделитель столбцов импортируемого CSV.</summary>
    private static string DetectDelimiter(string line)
    {
        var counts = new Dictionary<char, int> { [';'] = 0, [','] = 0, ['\t'] = 0 };
        var quoted = false;
        foreach (var character in line)
        {
            if (character == '"') quoted = !quoted;
            else if (!quoted && counts.ContainsKey(character)) counts[character]++;
        }
        return counts.MaxBy(item => item.Value).Key.ToString();
    }

    /// <summary>Приводит заголовок столбца к принятому имени для сопоставления.</summary>
    private static string CanonicalHeader(string header, bool workers)
    {
        var key = string.Concat(header.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        if (key is "id" or "ид") return "ID";
        return workers ? key switch {
            "fullname" or "fio" or "фио" => "FullName",
            "lastname" or "фамилия" => "LastName",
            "firstname" or "имя" => "FirstName",
            "patronymic" or "middlename" or "отчество" => "Patronymic",
            "profession" or "профессия" or "должность" or "position" => "Profession",
            _ => header.Trim()
        } : key switch {
            "name" or "наименование" or "materialname" or "номенклатура" => "Наименование",
            "unit" or "едизм" or "единицаизмерения" => "Ед.изм.",
            "amount" or "quantity" or "qty" or "количество" or "остаток" => "Количество",
            _ => header.Trim()
        };
    }
}
