using MyApp.Application.DTO;
using MyApp.Infrastructure.Repositories;

namespace MyApp.Infrastructure.Tests;

public sealed class VehicleWorkPostgreSqlTests
{
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Independent_repository_creates_and_filters_work_with_media()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        var repository = new VehicleWorkRepository(database.DataSource);
        var vehicle = Guid.NewGuid();
        var otherVehicle = Guid.NewGuid();
        var user = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var defect = Guid.NewGuid();

        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@a), (@b)",
            ("a", vehicle), ("b", otherVehicle));
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@a), (@b)",
            ("a", user), ("b", otherUser));
        await database.ExecuteAsync(
            """
            INSERT INTO vehicle_defects
                (id, vehicle_id, created_by, symptoms, downtime_started_at)
            VALUES (@id, @vehicle, @user, 'broken', NOW())
            """,
            ("id", defect), ("vehicle", vehicle), ("user", user));

        var workId = await repository.AddWorkAsync(
            vehicle, new VehicleWorkRequest(
                defect, "failure", "replaced bearing", "repaired", null),
            user, CancellationToken.None);
        Xunit.Assert.True(await repository.WorkExistsAsync(workId, CancellationToken.None));
        Xunit.Assert.True(await repository.IsWorkPerformerAsync(
            workId, user, CancellationToken.None));
        Xunit.Assert.False(await repository.IsWorkPerformerAsync(
            workId, otherUser, CancellationToken.None));
        Xunit.Assert.False(await repository.WorkExistsAsync(
            Guid.NewGuid(), CancellationToken.None));

        await database.ExecuteAsync(
            """
            INSERT INTO vehicle_work_photos
                (id, work_id, file_name, content_type, size)
            VALUES (@id, @work, 'bearing.jpg', 'image/jpeg', 123)
            """,
            ("id", Guid.NewGuid()), ("work", workId));
        await database.ExecuteAsync(
            """
            INSERT INTO vehicle_work_videos
                (id, work_id, file_name, content_type, size)
            VALUES (@id, @work, 'repair.mp4', 'video/mp4', 456)
            """,
            ("id", Guid.NewGuid()), ("work", workId));

        var selected = await repository.GetWorksAsync(
            vehicle, null, null, CancellationToken.None);
        var entry = Xunit.Assert.Single(selected);
        Xunit.Assert.Equal(workId, entry.Id);
        Xunit.Assert.Equal("replaced bearing", entry.Description);
        Xunit.Assert.Equal("bearing.jpg",
            Xunit.Assert.Single(entry.Photos).FileName);
        Xunit.Assert.NotNull(entry.Videos);
        Xunit.Assert.Equal("repair.mp4",
            Xunit.Assert.Single(entry.Videos).FileName);
        Xunit.Assert.Empty(await repository.GetWorksAsync(
            otherVehicle, null, null, CancellationToken.None));
        Xunit.Assert.Empty(await repository.GetWorksAsync(
            vehicle, new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31), CancellationToken.None));
    }

    [Xunit.Fact]
    public void Dedicated_work_port_is_implemented()
    {
        Xunit.Assert.True(
            typeof(MyApp.Application.Abstractions.IVehicleWorkRepository)
                .IsAssignableFrom(typeof(VehicleWorkRepository)));
    }
}
