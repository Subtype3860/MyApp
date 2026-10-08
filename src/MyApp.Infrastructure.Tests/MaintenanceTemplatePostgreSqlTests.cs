using MyApp.Infrastructure.Repositories;
using MyApp.Infrastructure.Db;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

public sealed class MaintenanceTemplatePostgreSqlTests
{
    private static MaintenanceTemplateRepository CreateRepository(
        PostgreSqlIntegrationDatabase database, AppDbContext context) =>
        new(context, database.DataSource);

    private static Task CreateStockViewAsync(PostgreSqlIntegrationDatabase database) =>
        database.ExecuteAsync(
            """
            CREATE VIEW v_full_ost AS
            SELECT 'Hydraulic pump'::text AS "Наименование",
                   'pcs'::text AS "Ед.изм.",
                   27::numeric AS "Количество"
            UNION ALL
            SELECT 'Filter'::text, 'pcs'::text, 8::numeric;
            """);

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Equipment_intervals_items_keep_hierarchy_order_and_stock_columns()
    {
        await using var db = await PostgreSqlIntegrationDatabase.CreateAsync();
        await CreateStockViewAsync(db);
        await using var context = db.CreateDbContext();
        var repository = CreateRepository(db, context);

        var firstEquipment = await repository.CreateEquipmentAsync(
            "Excavators", CancellationToken.None);
        var secondEquipment = await repository.CreateEquipmentAsync(
            "Dump trucks", CancellationToken.None);
        var laterInterval = await repository.CreateIntervalAsync(
            firstEquipment, "TO-500", CancellationToken.None);
        var earlierInterval = await repository.CreateIntervalAsync(
            firstEquipment, "TO-100", CancellationToken.None);
        var itemId = await repository.AddItemAsync(
            laterInterval, "Hydraulic pump", 2m, CancellationToken.None);
        var unknownItemId = await repository.AddItemAsync(
            earlierInterval, "No stock entry", 1m, CancellationToken.None);

        var all = await repository.GetAllAsync(CancellationToken.None);
        Xunit.Assert.Equal(
            new[] { "Excavators", "Dump trucks" },
            all.Select(e => e.Name).ToArray());
        var first = Xunit.Assert.Single(all, e => e.Id == firstEquipment);
        Xunit.Assert.Equal(
            new[] { "TO-500", "TO-100" },
            first.Intervals.Select(i => i.Name).ToArray());
        var item = Xunit.Assert.Single(
            first.Intervals[0].Items, i => i.Id == itemId);
        Xunit.Assert.Equal("pcs", item.Unit);
        Xunit.Assert.Equal(2m, item.Quantity);
        Xunit.Assert.Equal(27m, item.AvailableQuantity);
        var unknown = Xunit.Assert.Single(
            first.Intervals[1].Items, i => i.Id == unknownItemId);
        Xunit.Assert.Equal(string.Empty, unknown.Unit);
        Xunit.Assert.Equal(0m, unknown.AvailableQuantity);
        Xunit.Assert.Empty(Xunit.Assert.Single(
            all, e => e.Id == secondEquipment).Intervals);
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Name_checks_and_renaming_respect_normalized_uniqueness()
    {
        await using var db = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = db.CreateDbContext();
        var repository = CreateRepository(db, context);
        var equipment = await repository.CreateEquipmentAsync(
            "Excavators", CancellationToken.None);
        var second = await repository.CreateEquipmentAsync(
            "Trucks", CancellationToken.None);
        var interval = await repository.CreateIntervalAsync(
            equipment, "TO-100", CancellationToken.None);

        Xunit.Assert.True(await repository.EquipmentExistsAsync(
            equipment, CancellationToken.None));
        Xunit.Assert.False(await repository.EquipmentExistsAsync(
            Guid.NewGuid(), CancellationToken.None));
        Xunit.Assert.True(await repository.EquipmentNameExistsAsync(
            "  EXCAVATORS ", null, CancellationToken.None));
        Xunit.Assert.False(await repository.EquipmentNameExistsAsync(
            "Excavators", equipment, CancellationToken.None));
        Xunit.Assert.True(await repository.IntervalNameExistsAsync(
            equipment, " to-100 ", null, CancellationToken.None));
        Xunit.Assert.False(await repository.IntervalNameExistsAsync(
            equipment, "TO-100", interval, CancellationToken.None));
        Xunit.Assert.True(await repository.RenameEquipmentAsync(
            second, "Dump trucks", CancellationToken.None));
        Xunit.Assert.True(await repository.RenameIntervalAsync(
            interval, "TO-200", CancellationToken.None));
        Xunit.Assert.False(await repository.RenameIntervalAsync(
            Guid.NewGuid(), "TO-300", CancellationToken.None));

        await repository.CreateIntervalAsync(
            equipment, "TO-300", CancellationToken.None);
        Xunit.Assert.False(await repository.RenameIntervalAsync(
            interval, " to-300 ", CancellationToken.None));
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Quantity_constraints_and_cascade_deletes_remain_enforced()
    {
        await using var db = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = db.CreateDbContext();
        var repository = CreateRepository(db, context);
        var equipment = await repository.CreateEquipmentAsync(
            "Dump trucks", CancellationToken.None);
        var interval = await repository.CreateIntervalAsync(
            equipment, "TO-350", CancellationToken.None);
        var item = await repository.AddItemAsync(
            interval, "Filter", 2m, CancellationToken.None);

        Xunit.Assert.True(await repository.ItemExistsAsync(
            interval, " Filter ", CancellationToken.None));
        Xunit.Assert.True(await repository.UpdateItemAsync(
            item, 3m, CancellationToken.None));
        Xunit.Assert.False(await repository.UpdateItemAsync(
            Guid.NewGuid(), 3m, CancellationToken.None));

        var invalid = await Xunit.Assert.ThrowsAsync<PostgresException>(() =>
            repository.UpdateItemAsync(item, 0m, CancellationToken.None));
        Xunit.Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);

        Xunit.Assert.True(await repository.DeleteEquipmentAsync(
            equipment, CancellationToken.None));
        Xunit.Assert.False(await repository.IntervalExistsAsync(
            interval, CancellationToken.None));
        Xunit.Assert.False(await repository.ItemExistsAsync(
            interval, "Filter", CancellationToken.None));
        Xunit.Assert.False(await repository.DeleteItemAsync(
            item, CancellationToken.None));
        Xunit.Assert.False(await repository.DeleteIntervalAsync(
            interval, CancellationToken.None));
        Xunit.Assert.False(await repository.DeleteEquipmentAsync(
            equipment, CancellationToken.None));
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Stock_presence_remains_a_postgresql_view_query()
    {
        await using var db = await PostgreSqlIntegrationDatabase.CreateAsync();
        await CreateStockViewAsync(db);
        await using var context = db.CreateDbContext();
        var repository = CreateRepository(db, context);
        Xunit.Assert.True(await repository.MaterialExistsAsync(
            " Hydraulic pump ", CancellationToken.None));
        Xunit.Assert.False(await repository.MaterialExistsAsync(
            "Unknown material", CancellationToken.None));
    }
}
