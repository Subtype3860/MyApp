using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Repositories;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

public sealed class VehiclePurchasePostgreSqlTests
{
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Purchase_roundtrip_is_scoped_by_vehicle_and_date()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var repository = new VehiclePurchaseRepository(context);
        var vehicleId = Guid.NewGuid();
        var otherVehicleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@id), (@otherId)",
            ("id", vehicleId), ("otherId", otherVehicleId));
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", userId));

        var selectedId = await repository.AddPurchaseAsync(
            vehicleId,
            new VehiclePurchaseRequest(new DateOnly(2026, 10, 8), "REQ-1",
                "Hydraulic pump", 2m, "new", "urgent"),
            userId, CancellationToken.None);
        await repository.AddPurchaseAsync(
            vehicleId,
            new VehiclePurchaseRequest(new DateOnly(2026, 10, 1), "REQ-old",
                "Filter", 1m, "new", ""),
            userId, CancellationToken.None);
        await repository.AddPurchaseAsync(
            otherVehicleId,
            new VehiclePurchaseRequest(new DateOnly(2026, 10, 8), "OTHER",
                "Belt", 3m, "new", ""),
            userId, CancellationToken.None);

        var result = await repository.GetPurchasesAsync(
            vehicleId, new DateOnly(2026, 10, 7),
            new DateOnly(2026, 10, 8), CancellationToken.None);

        var purchase = Xunit.Assert.Single(result);
        Xunit.Assert.Equal(selectedId, purchase.Id);
        Xunit.Assert.Equal("REQ-1", purchase.RequestNumber);
        Xunit.Assert.Equal("Hydraulic pump", purchase.ItemName);
        Xunit.Assert.Equal(2m, purchase.Quantity);
        Xunit.Assert.Equal("urgent", purchase.Note);
        Xunit.Assert.Equal(new DateOnly(2026, 10, 8), purchase.RequestDate);
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Purchase_cannot_bypass_positive_quantity_constraint()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var repository = new VehiclePurchaseRepository(context);
        var vehicleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@id)", ("id", vehicleId));
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", userId));

        var error = await Xunit.Assert.ThrowsAsync<PostgresException>(
            () => repository.AddPurchaseAsync(
                vehicleId,
                new VehiclePurchaseRequest(
                    new DateOnly(2026, 10, 8), "REQ-invalid",
                    "Pump", 0m, "new", ""),
                userId, CancellationToken.None));
        Xunit.Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }
}
