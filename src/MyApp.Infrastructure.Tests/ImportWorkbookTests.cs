using System.IO.Compression;
using System.Security;
using System.Text;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Infrastructure.Storage;
using Xunit;

namespace MyApp.Infrastructure.Tests;

/// <summary>Тесты чтения и проверки книг Excel.</summary>
public sealed class ImportWorkbookTests
{
    /// <summary>Проверяет применение общей проверки CSV к выбранному листу Excel.</summary>
    [Fact]
    public void Selected_excel_sheet_uses_the_same_validation_as_csv()
    {
        var content = Workbook("Wrong;Columns\nunused;row", "name;unit;amount\nFilter;pcs;2,5");
        Assert.Equal(new[] { "Sheet1", "Sheet2" }, ImportWorkbook.Sheets(content));
        Assert.Throws<CsvImportValidationException>(() => ImportWorkbook.ToCsv(content, null, default));
        Assert.Throws<CsvImportValidationException>(() => ImportWorkbook.ToCsv(content, "Missing", default));
        var csv = ImportWorkbook.ToCsv(content, "Sheet2", default);
        Assert.Equal(2.5m, Assert.Single(CsvImportCodec.Parse("o.csv", csv).Rows).Data["Количество"]);
    }

    /// <summary>Проверяет отклонение некорректного Excel до сохранения.</summary>
    [Theory]
    [InlineData("name;unit;amount\nFilter;pcs;-1")]
    [InlineData("name;unit;amount\nFilter;pcs;2\nFilter;pcs;3")]
    [InlineData("name;unit;amount")]
    public void Invalid_excel_snapshot_is_rejected_before_saving(string rows)
    {
        var csv = ImportWorkbook.ToCsv(Workbook(rows), null, default);
        Assert.Throws<CsvImportValidationException>(() => CsvImportCodec.Parse("o.csv", csv));
    }

    /// <summary>Проверяет представление повреждённого Excel как ошибки валидации.</summary>
    [Fact]
    public void Malformed_excel_is_reported_as_validation_error() =>
        Assert.Throws<CsvImportValidationException>(() => ImportWorkbook.Sheets(Encoding.UTF8.GetBytes("not a workbook")));

    /// <summary>Проверяет идентификаторы сотрудников и допустимость одинаковых имён при разных идентификаторах.</summary>
    [Fact]
    public void Invalid_id_is_rejected_and_distinct_worker_ids_allow_equal_names()
    {
        Assert.Throws<CsvImportValidationException>(() => CsvImportCodec.Parse("o.csv", Encoding.UTF8.GetBytes("id;name;unit;amount\nwrong;Filter;pcs;2")));
        var csv = $"id;ФИО;Профессия\n{Guid.NewGuid()};Иванов Иван;Механик\n{Guid.NewGuid()};Иванов Иван;Кладовщик";
        Assert.Equal(2, CsvImportCodec.Parse("p.csv", Encoding.UTF8.GetBytes(csv)).Rows.Count);
    }

    /// <summary>Собирает тестовую книгу Excel в памяти.</summary>
    private static byte[] Workbook(params string[] sheets)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            /** Добавляет XML-часть в ZIP-архив тестовой книги Excel. */
            void Entry(string path, string text) { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write(text); }
            Entry("[Content_Types].xml", """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/></Types>""");
            Entry("_rels/.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Entry("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" + string.Concat(sheets.Select((_, i) => $"<sheet name=\"Sheet{i + 1}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets></workbook>");
            Entry("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) + "</Relationships>");
            for (var i = 0; i < sheets.Length; i++)
            {
                var rows = sheets[i].Split('\n').Select((row, r) => $"<row r=\"{r + 1}\">" + string.Concat(row.Split(';').Select((value, c) => $"<c r=\"{(char)('A' + c)}{r + 1}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value)}</t></is></c>")) + "</row>");
                Entry($"xl/worksheets/sheet{i + 1}.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" + string.Concat(rows) + "</sheetData></worksheet>");
            }
        }
        return output.ToArray();
    }
}
