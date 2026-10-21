using System.Globalization;
using System.IO.Compression;
using System.Text;
using ExcelDataReader;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Чтение листов Excel и преобразование выбранного листа в формат CSV.</summary>
public static class ImportWorkbook
{
    /// <summary>Открывает загруженную книгу Excel для чтения.</summary>
    private static IExcelDataReader Open(byte[] content)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (content.Length is 0 or > 20 * 1024 * 1024)
            throw new CsvImportValidationException("Выберите Excel-файл размером не более 20 МБ.");
        using (var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read))
        {
            if (archive.Entries.Count > 10000 || archive.Entries.Sum(entry => entry.Length) > 100 * 1024 * 1024)
                throw new CsvImportValidationException("Распакованный Excel-файл слишком большой.");
        }
        return ExcelReaderFactory.CreateOpenXmlReader(new MemoryStream(content));
    }

    /// <summary>Перечисляет доступные листы книги Excel.</summary>
    public static string[] Sheets(byte[] content)
    {
        try
        {
            using var reader = Open(content);
            var names = new List<string>();
            do { names.Add(reader.Name); } while (reader.NextResult());
            return names.ToArray();
        }
        catch (CsvImportValidationException) { throw; }
        catch (Exception exception) when (exception is InvalidDataException or IOException or System.Xml.XmlException or ExcelDataReader.Exceptions.HeaderException)
        { throw new CsvImportValidationException("Не удалось прочитать XLSX-файл."); }
    }

    /// <summary>Преобразует выбранный лист Excel в CSV для общей проверки импорта.</summary>
    public static byte[] ToCsv(byte[] content, string? sheetName, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = Open(content);
            if (sheetName is null && reader.ResultsCount > 1)
                throw new CsvImportValidationException("Выберите лист Excel для импорта.");
            if (sheetName is not null)
            {
                while (reader.Name != sheetName)
                    if (!reader.NextResult()) throw new CsvImportValidationException("Выбранный лист Excel не найден.");
            }
            var output = new StringBuilder();
            var rows = 0;
            var width = 0;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.FieldCount > 80) throw new CsvImportValidationException("В файле допускается не больше 80 колонок.");
                var cells = Enumerable.Range(0, reader.FieldCount)
                    .Select(index => Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? "").ToArray();
                if (cells.All(string.IsNullOrWhiteSpace)) continue;
                if (++rows > 100001) throw new CsvImportValidationException("В файле допускается не больше 100000 записей.");
                if (width == 0) width = Array.FindLastIndex(cells, cell => !string.IsNullOrWhiteSpace(cell)) + 1;
                if (cells.Skip(width).Any(cell => !string.IsNullOrWhiteSpace(cell)))
                    throw new CsvImportValidationException("Строка содержит данные за пределами заголовков.");
                if (cells.Any(cell => cell.Length > 16384)) throw new CsvImportValidationException("Поле длиннее 16384 символов.");
                output.AppendJoin(';', cells.Take(width).Select(cell => "\"" + cell.Replace("\"", "\"\"") + "\"")).Append("\r\n");
                if (output.Length > 20 * 1024 * 1024) throw new CsvImportValidationException("Данные выбранного листа слишком большие.");
            }
            var csv = Encoding.UTF8.GetBytes(output.ToString());
            if (csv.Length > 20 * 1024 * 1024) throw new CsvImportValidationException("Данные выбранного листа слишком большие.");
            return csv;
        }
        catch (CsvImportValidationException) { throw; }
        catch (Exception exception) when (exception is InvalidDataException or IOException or System.Xml.XmlException or ExcelDataReader.Exceptions.HeaderException)
        { throw new CsvImportValidationException("Не удалось прочитать XLSX-файл."); }
    }
}
