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
}
