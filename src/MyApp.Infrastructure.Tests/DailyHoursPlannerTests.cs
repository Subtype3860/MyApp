using MyApp.Application.DTO;
using MyApp.Application.Services;
using Xunit;

namespace MyApp.Infrastructure.Tests;

/// <summary>Тесты проверки ежедневных моточасов по двигателям.</summary>
public sealed class DailyHoursPlannerTests
{
    private static readonly VehicleResponse Truck = new(Guid.NewGuid(), "", "Самосвал", "New model", 901, "", "");
    private static readonly VehicleResponse Excavator = new(Guid.NewGuid(), "", "Экскаватор", "Sany", 91, "", "");
    /// <summary>Создаёт исходную строку ежедневных показаний для теста.</summary>
    private static DailyHoursRow Row(int day, decimal? hours, int engine = 1, bool invalid = false) => new(new(2026, 10, day), 901, engine, hours, invalid, $"{day}.10!D3");
    /// <summary>Создаёт сохранённое показание для проверки хронологии импорта.</summary>
    private static DailyHoursHistory History(int day, decimal hours, int engine = 1) => new(Truck.Id, new(2026, 10, day), engine, hours, DateTimeOffset.UtcNow);
    /// <summary>Запускает проверку ежедневного импорта на подготовленных тестовых данных.</summary>
    private static DailyHoursPreview Plan(DailyHoursRow[] rows, params DailyHoursHistory[] history) => DailyHoursPlanner.Build(rows, [Truck, Excavator], history);

    /// <summary>Проверяет сортировку дат и независимый перенос показаний каждого двигателя при простое.</summary>
    [Fact] public void Sorts_dates_and_carries_each_engine_independently()
    {
        var result = Plan([Row(3, null), Row(2, 110), Row(2, null, 2), Row(3, null, 2)], History(1, 100), History(1, 500, 2));
        Assert.Equal(new decimal?[] {110, 500, 110, 500}, result.Rows.Select(r => r.EngineHours));
        Assert.All(result.Rows, r => Assert.Equal("ready", r.Status));
        Assert.Equal(new TimeOnly(8, 0), result.ReadingTime);
    }
    /// <summary>Проверяет, что первое пустое показание не заполняется значением из будущего или той же даты.</summary>
    [Fact] public void Does_not_use_future_or_same_day_for_initial_blank()
    {
        Assert.Equal("error", Assert.Single(Plan([Row(1, null)], History(2, 100)).Rows).Status);
        Assert.Equal("error", Assert.Single(Plan([Row(1, null)], History(1, 100)).Rows).Status);
    }
    /// <summary>Проверяет, что ошибочный текст не считается простоем и блокирует последующий перенос показаний.</summary>
    [Fact] public void Invalid_text_is_not_downtime_and_blocks_following_blanks()
    {
        var result = Plan([Row(2, null, invalid:true), Row(3, null), Row(4, 120), Row(5, null)], History(1, 100));
        Assert.Equal(new[] {"error", "error", "ready", "ready"}, result.Rows.Select(r => r.Status));
    }
    /// <summary>Проверяет блокировку переноса при уменьшении счётчика без влияния на другой двигатель.</summary>
    [Fact] public void Decrease_blocks_carry_but_not_other_engine()
    {
        var result = Plan([Row(2, 99), Row(3, null), Row(3, null, 2)], History(1, 100), History(1, 500, 2));
        Assert.Equal(new[] {"error", "error", "ready"}, result.Rows.Select(r => r.Status));
    }
    /// <summary>Проверяет отклонение дублей в файле и отсутствие новых записей при повторном импорте.</summary>
    [Fact] public void Duplicate_input_is_rejected_and_repeat_import_is_unchanged()
    {
        Assert.All(Plan([Row(2, 110), Row(2, 120)]).Rows, r => Assert.Equal("error", r.Status));
        Assert.Equal("unchanged", Assert.Single(Plan([Row(2, 110)], History(2, 110)).Rows).Status);
        Assert.Equal("error", Assert.Single(Plan([Row(2, 120)], History(2, 110)).Rows).Status);
    }
    /// <summary>Проверяет обнаружение чрезмерного прироста моточасов и конфликта с будущим показанием.</summary>
    [Fact] public void Detects_unreasonable_growth_and_future_conflict()
    {
        Assert.Equal("warning", Assert.Single(Plan([Row(2, 125)], History(1, 100)).Rows).Status);
        Assert.Equal("error", Assert.Single(Plan([Row(2, 125)], History(3, 120)).Rows).Status);
    }
    /// <summary>Проверяет сопоставление по бортовому номеру и отклонение неоднозначных совпадений.</summary>
    [Fact] public void Matches_number_only_and_rejects_ambiguous_numbers()
    {
        Assert.Equal(Truck.Id, Assert.Single(Plan([Row(2, 100)]).Rows).VehicleId);
        var result = DailyHoursPlanner.Build([Row(2, 100)], [Truck, Truck with {Id=Guid.NewGuid()}], []);
        Assert.Equal("error", Assert.Single(result.Rows).Status);
    }
    /// <summary>Проверяет общий заголовок для одного ДВС и отказ от угадывания двигателя самосвала.</summary>
    [Fact] public void Single_engine_generic_heading_is_allowed_but_truck_is_not_guessed()
    {
        var rows = new[] { Row(2, 100, 0), Row(2, 100, 0) with {GarageNumber=91} };
        var result = Plan(rows);
        Assert.Equal("ready", result.Rows.Single(r => r.GarageNumber==91).Status);
        Assert.Equal("error", result.Rows.Single(r => r.GarageNumber==901).Status);
    }
    /// <summary>Проверяет изменение отпечатка импорта при изменении истории, используемой для переноса показаний.</summary>
    [Fact] public void Fingerprint_changes_when_carried_history_changes()
    {
        var rows = new[] {Row(2, null)};
        Assert.NotEqual(Plan(rows, History(1, 100)).Fingerprint, Plan(rows, History(1, 101)).Fingerprint);
    }
    /// <summary>Проверяет распознавание дублей после нормализации обозначений одного двигателя.</summary>
    [Fact] public void Normalized_engine_aliases_are_duplicates()
    {
        var rows = new[] {Row(2, 100, 0) with {GarageNumber=91}, Row(2, 100, 1) with {GarageNumber=91}};
        Assert.All(Plan(rows).Rows, r => Assert.Equal("error", r.Status));
    }
    /// <summary>Проверяет использование старых показаний без номера ДВС только для однодвигательной техники.</summary>
    [Fact] public void Legacy_history_is_usable_only_for_single_engine_equipment()
    {
        var rows = new[] {Row(2, null), Row(2, null) with {GarageNumber=91}};
        var result = Plan(rows, History(1, 100, 0), new(Excavator.Id, new(2026,10,1), 0, 500, DateTimeOffset.UtcNow));
        Assert.Equal(500, result.Rows.Single(r=>r.GarageNumber==91).EngineHours);
        Assert.Equal("error", result.Rows.Single(r=>r.GarageNumber==901).Status);
    }
}
