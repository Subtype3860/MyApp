using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Repositories;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

public sealed class MaintenanceCurrentModelPostgreSqlTests
{
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
}
