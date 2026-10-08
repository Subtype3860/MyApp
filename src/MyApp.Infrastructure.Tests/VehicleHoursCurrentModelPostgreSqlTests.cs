using Microsoft.EntityFrameworkCore;
using MyApp.Application.DTO;
using MyApp.Application.Storage;
using MyApp.Infrastructure.Repositories;
using MyApp.Infrastructure.Storage;

namespace MyApp.Infrastructure.Tests;

public sealed class VehicleHoursCurrentModelPostgreSqlTests
{
    private static VehicleRepository CreateRepository(
        MyApp.Infrastructure.Db.AppDbContext db)
    {
        var storage = new MediaStorageOptions(
            Path.GetTempPath(), Path.GetTempPath(),
            Path.GetTempPath(), Path.GetTempPath());
        return new VehicleRepository(db, new MediaStorageService(storage), storage);
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Blank_values_use_last_reading_not_later_readings_or_other_vehicles()
    {
        await using var fixture = await VehicleHoursPostgreSqlDatabase.CreateAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var user = Guid.NewGuid();
        await fixture.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@a), (@b), (@c)",
            ("a", a), ("b", b), ("c", c));
        await fixture.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@user)", ("user", user));

        await using (var seed = fixture.CreateContext())
        {
            var repo = CreateRepository(seed);
            await repo.AddHoursAsync(a,
                new VehicleHoursRequest(new DateOnly(2026, 9, 1), 100m, "old"),
                user, CancellationToken.None);
            await repo.AddHoursAsync(a,
                new VehicleHoursRequest(new DateOnly(2026, 10, 15), 180m, "later"),
                user, CancellationToken.None);
            await repo.AddHoursAsync(b,
                new VehicleHoursRequest(new DateOnly(2026, 8, 20), 35m, "old"),
                user, CancellationToken.None);
        }

        var date = new DateOnly(2026, 10, 8);
        await using (var importer = fixture.CreateContext())
        {
            await CreateRepository(importer).ImportHoursAsync(
                date, [new(a, null), new(b, null), new(c, null)],
                user, CancellationToken.None);
        }

        var readings = await fixture.ReadingsAsync();
        Xunit.Assert.Equal(100m, Xunit.Assert.Single(readings,
            row => row.VehicleId == a && row.Date == date).Hours);
        Xunit.Assert.Equal(35m, Xunit.Assert.Single(readings,
            row => row.VehicleId == b && row.Date == date).Hours);
        Xunit.Assert.Equal(0m, Xunit.Assert.Single(readings,
            row => row.VehicleId == c && row.Date == date).Hours);
        Xunit.Assert.Equal(180m, Xunit.Assert.Single(readings,
            row => row.VehicleId == a &&
                   row.Date == new DateOnly(2026, 10, 15)).Hours);
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Repeated_rows_use_prior_import_updates_and_remain_single_row()
    {
        await using var fixture = await VehicleHoursPostgreSqlDatabase.CreateAsync();
        var vehicle = Guid.NewGuid();
        var user = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 8);
        await fixture.ExecuteAsync(
            "INSERT INTO number_car(id) VALUES (@id)", ("id", vehicle));
        await fixture.ExecuteAsync(
            "INSERT INTO app_users(id) VALUES (@id)", ("id", user));

        await using (var db = fixture.CreateContext())
        {
            await CreateRepository(db).ImportHoursAsync(
                date, [new(vehicle, 60m), new(vehicle, null)],
                user, CancellationToken.None);
        }
        Xunit.Assert.Equal(60m, Xunit.Assert.Single(await fixture.ReadingsAsync()).Hours);

        await using (var db = fixture.CreateContext())
        {
            await CreateRepository(db).ImportHoursAsync(
                date, [new(vehicle, null), new(vehicle, 75m), new(vehicle, null)],
                user, CancellationToken.None);
        }
        Xunit.Assert.Equal(75m, Xunit.Assert.Single(await fixture.ReadingsAsync()).Hours);
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Invalid_value_rolls_back_whole_import()
    {
        await using var fixture = await VehicleHoursPostgreSqlDatabase.CreateAsync();
        var vehicle = Guid.NewGuid();
        var user = Guid.NewGuid();
        await fixture.ExecuteAsync(
            "INSERT INTO number_car(id) VALUES (@id)", ("id", vehicle));
        await fixture.ExecuteAsync(
            "INSERT INTO app_users(id) VALUES (@id)", ("id", user));

        await using (var db = fixture.CreateContext())
        {
            var repo = CreateRepository(db);
            await Xunit.Assert.ThrowsAsync<DbUpdateException>(() =>
                repo.ImportHoursAsync(
                    new DateOnly(2026, 10, 8),
                    [new(vehicle, 20m), new(vehicle, -1m)],
                    user, CancellationToken.None));
        }
        Xunit.Assert.Empty(await fixture.ReadingsAsync());
    }
}
