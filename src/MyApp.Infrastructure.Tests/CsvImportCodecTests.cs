using System.Text;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Storage;
using Xunit;

namespace MyApp.Infrastructure.Tests;

/// <summary>Тесты разбора и проверки CSV.</summary>
public sealed class CsvImportCodecTests
{
    /// <summary>Проверяет чтение BOM, кавычек, многострочных полей, десятичной запятой и дополнительных столбцов CSV.</summary>
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

    /// <summary>Проверяет отклонение неоднозначных, повреждённых и лишённых заголовка складских CSV.</summary>
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

    /// <summary>Проверяет отклонение некорректной кодировки UTF-8.</summary>
    [Fact]
    public void Rejects_invalid_utf8() => Assert.Throws<CsvImportValidationException>(() => CsvImportCodec.Parse("o.csv", [0xff, 0xff]));

    /// <summary>Проверяет использование полного имени в ключе сотрудника.</summary>
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

    /// <summary>Проверяет отклонение противоречий между полным именем и отдельными полями ФИО.</summary>
    [Fact]
    public void Rejects_conflicting_full_name_and_individual_columns() =>
        Assert.Throws<CsvImportValidationException>(() => CsvImportCodec.Parse("p.csv", Encoding.UTF8.GetBytes(
            "ФИО;LastName;FirstName;Profession\nИванов Иван;Петров;Иван;Механик")));

    /// <summary>Проверяет запрет импорта управляемого приложением статуса подписи.</summary>
    [Fact]
    public void Csv_cannot_supply_managed_signature_status() =>
        Assert.Throws<CsvImportValidationException>(() => CsvImportCodec.Parse("p.csv", Encoding.UTF8.GetBytes(
            "ФИО;Профессия;Роспись\nИванов Иван;Механик;true")));
}
