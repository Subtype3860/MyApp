using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Repositories;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

/// <summary>Тесты шаблонов ТО на текущей схеме PostgreSQL.</summary>
public sealed class MaintenanceCurrentModelPostgreSqlTests
{
    /// <summary>Проверяет сохранение иерархии шаблонов ТО и чтение складского представления.</summary>
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Template_hierarchy_and_stock_view_are_preserved()
    {
        await using var fixture = await MaintenancePostgreSqlDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var repo = new MaintenanceTemplateRepository(db);

        var equipmentId = await repo.CreateEquipmentAsync(
            "Excavators", CancellationToken.None);
        var intervalId = await repo.CreateIntervalAsync(
            equipmentId, "TO-500", CancellationToken.None);
        var itemId = await repo.AddItemAsync(
            intervalId, "Filter", 2m, CancellationToken.None);
        var response = Xunit.Assert.Single(await repo.GetAllAsync(CancellationToken.None));
        Xunit.Assert.Equal(equipmentId, response.Id);
        var interval = Xunit.Assert.Single(response.Intervals);
        Xunit.Assert.Equal(intervalId, interval.Id);
        var material = Xunit.Assert.Single(interval.Items);
        Xunit.Assert.Equal(itemId, material.Id);
        Xunit.Assert.Equal("Filter", material.MaterialName);
        Xunit.Assert.Equal("pcs", material.Unit);
        Xunit.Assert.Equal(2m, material.Quantity);
        Xunit.Assert.Equal(8m, material.AvailableQuantity);
    }

    /// <summary>Проверяет чтение числовых складских остатков в шаблонах ТО.</summary>
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Numeric_stock_view_is_readable_in_maintenance_templates()
    {
        await using var fixture = await MaintenancePostgreSqlDatabase.CreateAsync(
            numericStockView: true);
        await using var db = fixture.CreateContext();
        var repo = new MaintenanceTemplateRepository(db);
        var equipmentId = await repo.CreateEquipmentAsync(
            "Excavators", CancellationToken.None);
        var intervalId = await repo.CreateIntervalAsync(
            equipmentId, "TO-500", CancellationToken.None);
        await repo.AddItemAsync(
            intervalId, "Filter", 2m, CancellationToken.None);

        var equipment = Xunit.Assert.Single(
            await repo.GetAllAsync(CancellationToken.None));
        var interval = Xunit.Assert.Single(equipment.Intervals);
        var material = Xunit.Assert.Single(interval.Items);
        Xunit.Assert.Equal("Filter", material.MaterialName);
        Xunit.Assert.Equal(8m, material.AvailableQuantity);
    }

    /// <summary>Проверяет уникальность нормализованных названий через существующий индекс базы.</summary>
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Names_are_unique_using_existing_normalized_database_index()
    {
        await using var fixture = await MaintenancePostgreSqlDatabase.CreateAsync();
        await using (var db = fixture.CreateContext())
        {
            await new MaintenanceTemplateRepository(db).CreateEquipmentAsync(
                "Excavators", CancellationToken.None);
        }
        await using var check = fixture.CreateContext();
        var repository = new MaintenanceTemplateRepository(check);
        Xunit.Assert.True(await repository.EquipmentNameExistsAsync(
            " excavators ", null, CancellationToken.None));
        var exception = await Xunit.Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.CreateEquipmentAsync(" EXCAVATORS ", CancellationToken.None));
        var pg = Xunit.Assert.IsType<PostgresException>(exception.InnerException);
        Xunit.Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
    }

    /// <summary>Проверяет каскадное удаление интервалов и материалов оборудования.</summary>
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Deletion_cascades_to_intervals_and_materials()
    {
        await using var fixture = await MaintenancePostgreSqlDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var repo = new MaintenanceTemplateRepository(db);
        var equipment = await repo.CreateEquipmentAsync(
            "Haul trucks", CancellationToken.None);
        var interval = await repo.CreateIntervalAsync(
            equipment, "TO-100", CancellationToken.None);
        await repo.AddItemAsync(interval, "Filter", 1m, CancellationToken.None);

        Xunit.Assert.True(await repo.DeleteEquipmentAsync(equipment, CancellationToken.None));
        Xunit.Assert.Equal(0L, await fixture.ScalarLongAsync(
            "SELECT COUNT(*) FROM maintenance_intervals"));
        Xunit.Assert.Equal(0L, await fixture.ScalarLongAsync(
            "SELECT COUNT(*) FROM maintenance_interval_items"));
    }
    /// <summary>Проверяет уникальность порядка сортировки при параллельном создании элементов.</summary>
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Parallel_creates_assign_distinct_sort_orders_in_each_scope()
    {
        await using var fixture = await MaintenancePostgreSqlDatabase.CreateAsync();
        const int count = 12;

        // Each concurrent call gets its own DbContext (EF contexts are not thread-safe).
        /** Создаёт оборудование для проверки шаблонов ТО. */
        async Task<Guid> CreateEquipmentAsync(int index)
        {
            await using var db = fixture.CreateContext();
            return await new MaintenanceTemplateRepository(db).CreateEquipmentAsync(
                $"Equipment-{index}", CancellationToken.None);
        }

        var equipments = await Task.WhenAll(
            Enumerable.Range(0, count).Select(CreateEquipmentAsync));
        Xunit.Assert.Equal(Enumerable.Range(0, count),
            await fixture.SortOrdersAsync("maintenance_equipment"));

        /** Создаёт интервал ТО для проверки порядка и связей. */
        async Task<Guid> CreateIntervalAsync(int index)
        {
            await using var db = fixture.CreateContext();
            return await new MaintenanceTemplateRepository(db).CreateIntervalAsync(
                equipments[0], $"TO-{index}", CancellationToken.None);
        }

        var intervals = await Task.WhenAll(
            Enumerable.Range(0, count).Select(CreateIntervalAsync));
        Xunit.Assert.Equal(Enumerable.Range(0, count),
            await fixture.SortOrdersAsync(
                "maintenance_intervals", "equipment_id", equipments[0]));

        /** Создаёт позицию материала в тестовом интервале ТО. */
        async Task<Guid> CreateItemAsync(int index)
        {
            await using var db = fixture.CreateContext();
            return await new MaintenanceTemplateRepository(db).AddItemAsync(
                intervals[0], $"Material-{index}", 1m, CancellationToken.None);
        }

        await Task.WhenAll(Enumerable.Range(0, count).Select(CreateItemAsync));
        Xunit.Assert.Equal(Enumerable.Range(0, count),
            await fixture.SortOrdersAsync(
                "maintenance_interval_items", "interval_id", intervals[0]));
    }

    /// <summary>Проверяет независимую нумерацию интервалов для разных видов оборудования.</summary>
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Different_equipment_scopes_keep_independent_interval_orders()
    {
        await using var fixture = await MaintenancePostgreSqlDatabase.CreateAsync();
        Guid first;
        Guid second;
        await using (var db = fixture.CreateContext())
        {
            var repo = new MaintenanceTemplateRepository(db);
            first = await repo.CreateEquipmentAsync("First", CancellationToken.None);
            second = await repo.CreateEquipmentAsync("Second", CancellationToken.None);
        }

        /** Создаёт несколько интервалов для проверки независимого порядка оборудования. */
        async Task CreateIntervalsAsync(Guid id)
        {
            await Task.WhenAll(Enumerable.Range(0, 5).Select(async index =>
            {
                await using var db = fixture.CreateContext();
                await new MaintenanceTemplateRepository(db).CreateIntervalAsync(
                    id, $"TO-{index}", CancellationToken.None);
            }));
        }

        await Task.WhenAll(CreateIntervalsAsync(first), CreateIntervalsAsync(second));
        Xunit.Assert.Equal(Enumerable.Range(0, 5),
            await fixture.SortOrdersAsync("maintenance_intervals", "equipment_id", first));
        Xunit.Assert.Equal(Enumerable.Range(0, 5),
            await fixture.SortOrdersAsync("maintenance_intervals", "equipment_id", second));
    }

}
