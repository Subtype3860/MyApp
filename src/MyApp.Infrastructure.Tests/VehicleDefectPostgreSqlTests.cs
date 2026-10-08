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
            new VehicleWorkRepository(database.DataSource),
            new VehicleDefectRepository(database.DataSource));

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
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Defect_journal_returns_status_and_media_for_correct_vehicle()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        var repository = new VehicleDefectRepository(database.DataSource);
        var vehicle = Guid.NewGuid();
        var otherVehicle = Guid.NewGuid();
        var creator = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO number_car(id) VALUES (@a), (@b)",
            ("a", vehicle), ("b", otherVehicle));
        await database.ExecuteAsync(
            """
            INSERT INTO app_users(id, first_name, last_name)
            VALUES (@id, 'Alice', 'Smith')
            """, ("id", creator));

        var defectId = await repository.AddDefectAsync(
            vehicle, new VehicleDefectRequest(
                "ERR-1", "Hydraulic leak", DateTimeOffset.UtcNow),
            creator, CancellationToken.None);

        await database.ExecuteAsync(
            """
            INSERT INTO vehicle_defect_photos
                (id, defect_id, file_name, content_type, size)
            VALUES (@id, @defect, 'leak.jpg', 'image/jpeg', 512)
            """, ("id", Guid.NewGuid()), ("defect", defectId));
        await database.ExecuteAsync(
            """
            INSERT INTO vehicle_defect_videos
                (id, defect_id, file_name, content_type, size)
            VALUES (@id, @defect, 'inspection.mp4', 'video/mp4', 1024)
            """, ("id", Guid.NewGuid()), ("defect", defectId));

        var defects = await repository.GetDefectsAsync(
            vehicle, null, null, CancellationToken.None);
        var entry = Xunit.Assert.Single(defects);
        Xunit.Assert.Equal(defectId, entry.Id);
        Xunit.Assert.Equal("new", entry.Status);
        Xunit.Assert.Equal("Alice Smith", entry.CreatedByName);
        Xunit.Assert.Equal("leak.jpg",
            Xunit.Assert.Single(entry.Photos).FileName);
        Xunit.Assert.Equal("inspection.mp4",
            Xunit.Assert.Single(entry.Videos).FileName);
        Xunit.Assert.Empty(await repository.GetDefectsAsync(
            otherVehicle, null, null, CancellationToken.None));

        Xunit.Assert.True(await repository.ClaimDefectAsync(
            defectId, creator, CancellationToken.None));
        var claimed = Xunit.Assert.Single(await repository.GetDefectsAsync(
            vehicle, null, null, CancellationToken.None));
        Xunit.Assert.Equal("in_progress", claimed.Status);
        var completed = await repository.CompleteDefectAsync(
            defectId, WorkRequest(defectId), creator, false, CancellationToken.None);
        Xunit.Assert.NotNull(completed);
        var repaired = Xunit.Assert.Single(await repository.GetDefectsAsync(
            vehicle, null, null, CancellationToken.None));
        Xunit.Assert.Equal("repaired", repaired.Status);
    }

}
