using MyApp.Application.DTO;
using MyApp.Infrastructure.Repositories;
using MyApp.Infrastructure.Db;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

public sealed class VehicleHoursPostgreSqlTests
{
    private static VehicleRepository CreateRepository(
        PostgreSqlIntegrationDatabase database, AppDbContext context) =>
        new(
            database.DataSource,
            new VehiclePartsRepository(database.DataSource),
            new VehiclePurchaseRepository(context),
            new VehicleHoursRepository(database.DataSource),
            new VehicleWorkRepository(database.DataSource),
            new VehicleDefectRepository(database.DataSource));

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Blank_readings_carry_forward_and_reimport_updates_existing_date()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var repository = CreateRepository(database, context);
        var vehicleId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@id)", ("id", vehicleId));
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", userId));

        await repository.AddHoursAsync(
            vehicleId,
            new VehicleHoursRequest(new DateOnly(2026, 10, 1), 123.5m, "initial"),
            userId, CancellationToken.None);

        var date = new DateOnly(2026, 10, 8);
        await repository.ImportHoursAsync(
            date, [new VehicleHoursImportItem(vehicleId, null)],
            userId, CancellationToken.None);

        await using (var read = database.DataSource.CreateCommand(
            "SELECT engine_hours, note FROM vehicle_hour_readings WHERE vehicle_id = @id AND reading_date = @date"))
        {
            read.Parameters.AddWithValue("id", vehicleId);
            read.Parameters.AddWithValue("date", date);
            await using var reader = await read.ExecuteReaderAsync();
            Xunit.Assert.True(await reader.ReadAsync());
            Xunit.Assert.Equal(123.5m, reader.GetDecimal(0));
            Xunit.Assert.Equal("Импорт CSV", reader.GetString(1));
            Xunit.Assert.False(await reader.ReadAsync());
        }

        await repository.ImportHoursAsync(
            date, [new VehicleHoursImportItem(vehicleId, 145.75m)],
            userId, CancellationToken.None);

        await using var count = database.DataSource.CreateCommand(
            "SELECT COUNT(*) FROM vehicle_hour_readings WHERE vehicle_id = @id");
        count.Parameters.AddWithValue("id", vehicleId);
        Xunit.Assert.Equal(2L, (long)(await count.ExecuteScalarAsync())!);

        await using var actual = database.DataSource.CreateCommand(
            "SELECT engine_hours FROM vehicle_hour_readings WHERE vehicle_id = @id AND reading_date = @date");
        actual.Parameters.AddWithValue("id", vehicleId);
        actual.Parameters.AddWithValue("date", date);
        Xunit.Assert.Equal(145.75m, (decimal)(await actual.ExecuteScalarAsync())!);
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Import_is_atomic_when_any_vehicle_id_is_unknown()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var repository = CreateRepository(database, context);
        var vehicleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@id)", ("id", vehicleId));
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", userId));

        var date = new DateOnly(2026, 10, 8);
        var error = await Xunit.Assert.ThrowsAsync<PostgresException>(() =>
            repository.ImportHoursAsync(
                date,
                [
                    new VehicleHoursImportItem(vehicleId, 150m),
                    new VehicleHoursImportItem(Guid.NewGuid(), 75m)
                ],
                userId, CancellationToken.None));
        Xunit.Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);

        await using var verify = database.DataSource.CreateCommand(
            "SELECT COUNT(*) FROM vehicle_hour_readings WHERE vehicle_id = @vehicleId");
        verify.Parameters.AddWithValue("vehicleId", vehicleId);
        Xunit.Assert.Equal(0L, (long)(await verify.ExecuteScalarAsync())!);
    }
}
