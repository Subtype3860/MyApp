using MyApp.Application.DTO;
using MyApp.Infrastructure.Repositories;

namespace MyApp.Infrastructure.Tests;

public sealed class VehicleHoursRepositoryPostgreSqlTests
{
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Independent_hours_repository_filters_vehicles_and_dates()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        var repo = new VehicleHoursRepository(database.DataSource);
        var vehicleId = Guid.NewGuid();
        var otherVehicleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO number_car(id) VALUES (@id), (@other)",
            ("id", vehicleId), ("other", otherVehicleId));
        await database.ExecuteAsync(
            "INSERT INTO app_users(id) VALUES (@id)", ("id", userId));

        var earlier = await repo.AddHoursAsync(
            vehicleId, new VehicleHoursRequest(new DateOnly(2026, 10, 1), 100m, "old"),
            userId, CancellationToken.None);
        var selected = await repo.AddHoursAsync(
            vehicleId, new VehicleHoursRequest(new DateOnly(2026, 10, 8), 120m, "selected"),
            userId, CancellationToken.None);
        await repo.AddHoursAsync(
            otherVehicleId, new VehicleHoursRequest(new DateOnly(2026, 10, 8), 300m, "other"),
            userId, CancellationToken.None);

        var filtered = await repo.GetHoursAsync(
            vehicleId, new DateOnly(2026, 10, 8),
            new DateOnly(2026, 10, 8), CancellationToken.None);
        var row = Xunit.Assert.Single(filtered);
        Xunit.Assert.Equal(selected, row.Id);
        Xunit.Assert.Equal(120m, row.EngineHours);

        var all = await repo.GetHoursAsync(
            vehicleId, null, null, CancellationToken.None);
        Xunit.Assert.Equal(new[] { selected, earlier }, all.Select(x => x.Id));
    }
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Blank_import_uses_latest_prior_date_and_keeps_later_readings()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        var repo = new VehicleHoursRepository(database.DataSource);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var user = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@first), (@second), (@third)",
            ("first", first), ("second", second), ("third", third));
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", user));

        await repo.AddHoursAsync(first,
            new VehicleHoursRequest(new DateOnly(2026, 10, 1), 110m, "old"),
            user, CancellationToken.None);
        await repo.AddHoursAsync(first,
            new VehicleHoursRequest(new DateOnly(2026, 10, 15), 160m, "future"),
            user, CancellationToken.None);
        await repo.AddHoursAsync(second,
            new VehicleHoursRequest(new DateOnly(2026, 9, 30), 35m, "old"),
            user, CancellationToken.None);

        var date = new DateOnly(2026, 10, 8);
        await repo.ImportHoursAsync(date,
            [
                new VehicleHoursImportItem(first, null),
                new VehicleHoursImportItem(second, null),
                new VehicleHoursImportItem(third, null)
            ],
            user, CancellationToken.None);

        var firstValues = await repo.GetHoursAsync(first, null, null, CancellationToken.None);
        Xunit.Assert.Equal(3, firstValues.Count);
        Xunit.Assert.Equal(110m, Xunit.Assert.Single(firstValues,
            value => value.ReadingDate == date).EngineHours);
        Xunit.Assert.Equal(160m, Xunit.Assert.Single(firstValues,
            value => value.ReadingDate == new DateOnly(2026, 10, 15)).EngineHours);

        var secondValues = await repo.GetHoursAsync(
            second, date, date, CancellationToken.None);
        Xunit.Assert.Equal(35m, Xunit.Assert.Single(secondValues).EngineHours);
        var thirdValues = await repo.GetHoursAsync(
            third, date, date, CancellationToken.None);
        Xunit.Assert.Equal(0m, Xunit.Assert.Single(thirdValues).EngineHours);
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Repeated_vehicle_lines_reuse_in_transaction_hours_and_do_not_duplicate_rows()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        var repo = new VehicleHoursRepository(database.DataSource);
        var vehicle = Guid.NewGuid();
        var user = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 8);
        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@id)", ("id", vehicle));
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", user));

        await repo.ImportHoursAsync(date,
            [
                new VehicleHoursImportItem(vehicle, 60m),
                new VehicleHoursImportItem(vehicle, null)
            ],
            user, CancellationToken.None);

        var one = await repo.GetHoursAsync(vehicle, date, date, CancellationToken.None);
        Xunit.Assert.Equal(60m, Xunit.Assert.Single(one).EngineHours);

        await repo.ImportHoursAsync(date,
            [
                new VehicleHoursImportItem(vehicle, null),
                new VehicleHoursImportItem(vehicle, 75m),
                new VehicleHoursImportItem(vehicle, null)
            ],
            user, CancellationToken.None);

        var current = await repo.GetHoursAsync(vehicle, date, date, CancellationToken.None);
        Xunit.Assert.Equal(75m, Xunit.Assert.Single(current).EngineHours);
    }

}
