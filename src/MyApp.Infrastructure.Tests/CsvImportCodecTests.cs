using System.Text;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Storage;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class CsvImportCodecTests
{
    [Fact]
    public void Reads_bom_quotes_multiline_fields_decimal_comma_and_additional_columns()
    {
        var csv = CsvImportCodec.Parse("o.csv", Encoding.UTF8.GetBytes("\uFEFFname;unit;amount;Комментарий\r\n\"Фильтр; А\";шт;1,5;\"Первая\r\nВторая \"\"строка\"\"\"\r\n"));
        var row = Assert.Single(csv.Rows);
        Assert.Equal("Фильтр; А", row.Key);
        Assert.Equal(1.5m, row.Data["Количество"]);
        Assert.Equal("Первая\r\nВторая \"строка\"", row.Data["Комментарий"]);
        var again = CsvImportCodec.Parse("o.csv", CsvImportCodec.Export(csv.Columns, csv.Rows.Select(item => item.Data)));
        Assert.Equal(row.Data["Комментарий"], Assert.Single(again.Rows).Data["Комментарий"]);
    }

    [Theory]
    [InlineData("name;unit;amount\nFilter;шт;-1")]
    [InlineData("name;unit;amount\nFilter;шт;not-a-number")]
    [InlineData("name;unit;amount\nFilter;шт;1\n Filter ;шт;2")]
    [InlineData("name;unit;amount\nFilter;шт")]
    [InlineData("name;unit;amount\n\"Filter;шт;2")]
    [InlineData("name;unit;amount\n;шт;1")]
    [InlineData("name;unit;amount\nFilter;;1")]
    [InlineData("name;unit;quantity;amount\nFilter;шт;1;2")]
    [InlineData("Filter;шт;10")]
    [InlineData("name;unit;amount")]
    public void Rejects_ambiguous_invalid_and_headerless_stock_files(string csv) =>
        Assert.Throws<CsvImportValidationException>(() => CsvImportCodec.Parse("o.csv", Encoding.UTF8.GetBytes(csv)));

    [Fact]
    public void Rejects_invalid_utf8() => Assert.Throws<CsvImportValidationException>(() => CsvImportCodec.Parse("o.csv", [0xff, 0xff]));

    [Fact]
    public void Worker_key_uses_full_name()
    {
        var csv = CsvImportCodec.Parse("p.csv", Encoding.UTF8.GetBytes("ФИО,Профессия\nИванов Иван Иванович,Механик"));
        var row = Assert.Single(csv.Rows);
        Assert.Equal("Иванов Иван Иванович", row.Key);
        Assert.Equal("Иванов", row.Data["LastName"]);
        Assert.Equal("Иван", row.Data["FirstName"]);
        Assert.Equal("Иванович", row.Data["Patronymic"]);
    }

    [Fact]
    public void Rejects_conflicting_full_name_and_individual_columns() =>
        Assert.Throws<CsvImportValidationException>(() => CsvImportCodec.Parse("p.csv", Encoding.UTF8.GetBytes(
            "ФИО;LastName;FirstName;Profession\nИванов Иван;Петров;Иван;Механик")));

    [Fact]
    public void Csv_cannot_supply_managed_signature_status() =>
        Assert.Throws<CsvImportValidationException>(() => CsvImportCodec.Parse("p.csv", Encoding.UTF8.GetBytes(
            "ФИО;Профессия;Роспись\nИванов Иван;Механик;true")));
}
