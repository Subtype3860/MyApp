using MyApp.Application.DTO;
using MyApp.Infrastructure.Repositories;
using MyApp.Infrastructure.Db;

namespace MyApp.Infrastructure.Tests;

public sealed class VehicleDefectPostgreSqlTests
{
    private static VehicleRepository CreateRepository(
        PostgreSqlIntegrationDatabase database, AppDbContext context) =>
        new(
            database.DataSource,
            new VehiclePartsRepository(database.DataSource),
            new VehiclePurchaseRepository(context),
            new VehicleHoursRepository(database.DataSource),
            new VehicleWorkRepository(database.DataSource));

    private static VehicleWorkRequest WorkRequest(Guid defectId) =>
        new(defectId, "failed bearing", "replace bearing", "repaired", null);

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Only_assignee_can_complete_defect_and_completion_is_idempotent()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var repo = CreateRepository(database, context);
        var vehicleId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var mechanicId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@id)", ("id", vehicleId));
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@creator), (@mechanic), (@other)",
            ("creator", creatorId), ("mechanic", mechanicId), ("other", otherId));

        var defectId = await repo.AddDefectAsync(
            vehicleId, new VehicleDefectRequest("ERR", "does not start", DateTimeOffset.UtcNow),
            creatorId, CancellationToken.None);

        Xunit.Assert.True(await repo.ClaimDefectAsync(defectId, mechanicId, CancellationToken.None));
        Xunit.Assert.False(await repo.ClaimDefectAsync(defectId, otherId, CancellationToken.None));

        Xunit.Assert.Null(await repo.CompleteDefectAsync(
            defectId, WorkRequest(defectId), otherId, false, CancellationToken.None));

        var completedId = await repo.CompleteDefectAsync(
            defectId, WorkRequest(defectId), mechanicId, false, CancellationToken.None);
        Xunit.Assert.NotNull(completedId);
        Xunit.Assert.Null(await repo.CompleteDefectAsync(
            defectId, WorkRequest(defectId), mechanicId, false, CancellationToken.None));

        await using var count = database.DataSource.CreateCommand(
            "SELECT COUNT(*) FROM vehicle_works WHERE defect_id = @defectId AND completed_at IS NOT NULL");
        count.Parameters.AddWithValue("defectId", defectId);
        Xunit.Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Concurrent_completion_only_creates_one_completed_work()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var repo = CreateRepository(database, context);
        var vehicleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@id)", ("id", vehicleId));
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", userId));

        var defectId = await repo.AddDefectAsync(
            vehicleId, new VehicleDefectRequest(null, "broken", DateTimeOffset.UtcNow),
            userId, CancellationToken.None);
        var work = WorkRequest(defectId);

        var attempts = await Task.WhenAll(
            repo.CompleteDefectAsync(defectId, work, userId, true, CancellationToken.None),
            repo.CompleteDefectAsync(defectId, work, userId, true, CancellationToken.None));

        Xunit.Assert.Single(attempts, id => id.HasValue);
        Xunit.Assert.Single(attempts, id => !id.HasValue);

        await using var count = database.DataSource.CreateCommand(
            "SELECT COUNT(*) FROM vehicle_works WHERE defect_id = @defectId AND completed_at IS NOT NULL");
        count.Parameters.AddWithValue("defectId", defectId);
        Xunit.Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }
}
