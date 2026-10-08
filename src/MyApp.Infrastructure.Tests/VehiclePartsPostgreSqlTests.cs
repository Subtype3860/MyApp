using MyApp.Application.DTO;
using MyApp.Infrastructure.Repositories;

namespace MyApp.Infrastructure.Tests;

public sealed class VehiclePartsPostgreSqlTests
{
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Parts_request_can_be_saved_read_and_deleted()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        var repository = new VehiclePartsRepository(database.DataSource);
        var vehicleId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@id)", ("id", vehicleId));
        var userId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", userId));
        await database.ExecuteAsync(
            """
            INSERT INTO vehicle_works
                (id, vehicle_id, work_date, description, created_by, repair_status)
            VALUES (@id, @vehicle, CURRENT_DATE, 'repair', @user, 'awaiting_parts')
            """,
            ("id", workId), ("vehicle", vehicleId), ("user", userId));

        var bytes = new byte[] { 37, 80, 68, 70, 45 };
        var changed = await repository.UpdatePartsRequestAsync(
            workId,
            new VehiclePartsRequest(
                "PART-1", new DateOnly(2026, 10, 8),
                "request.pdf", "application/pdf", bytes),
            CancellationToken.None);
        Xunit.Assert.True(changed);

        var attachment = await repository.GetPartsRequestFileAsync(
            workId, CancellationToken.None);
        Xunit.Assert.NotNull(attachment);
        Xunit.Assert.Equal("request.pdf", attachment.FileName);
        Xunit.Assert.Equal("application/pdf", attachment.ContentType);
        Xunit.Assert.Equal(bytes, attachment.Content);

        Xunit.Assert.True(await repository.DeletePartsRequestAsync(
            workId, CancellationToken.None));
        Xunit.Assert.Null(await repository.GetPartsRequestFileAsync(
            workId, CancellationToken.None));

        await using var verify = database.DataSource.CreateCommand(
            """
            SELECT purchase_request_number, purchase_request_date,
                   purchase_request_content
            FROM vehicle_works WHERE id = @id
            """);
        verify.Parameters.AddWithValue("id", workId);
        await using var reader = await verify.ExecuteReaderAsync();
        Xunit.Assert.True(await reader.ReadAsync());
        Xunit.Assert.Equal(string.Empty, reader.GetString(0));
        Xunit.Assert.True(reader.IsDBNull(1));
        Xunit.Assert.True(reader.IsDBNull(2));
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Parts_update_rejects_completed_work_and_supports_null_attachment()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        var repository = new VehiclePartsRepository(database.DataSource);
        var vehicleId = Guid.NewGuid();
        var pendingWork = Guid.NewGuid();
        var completedWork = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@id)", ("id", vehicleId));
        var userId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", userId));
        await database.ExecuteAsync(
            """
            INSERT INTO vehicle_works
                (id, vehicle_id, work_date, description, created_by, repair_status)
            VALUES (@pending, @vehicle, CURRENT_DATE, 'repair', @user, 'awaiting_parts'),
                   (@complete, @vehicle, CURRENT_DATE, 'repair', @user, 'repaired')
            """,
            ("pending", pendingWork), ("complete", completedWork),
            ("vehicle", vehicleId), ("user", userId));

        var request = new VehiclePartsRequest(
            "PART-2", new DateOnly(2026, 10, 8), null, null, null);

        Xunit.Assert.True(await repository.UpdatePartsRequestAsync(
            pendingWork, request, CancellationToken.None));
        Xunit.Assert.Null(await repository.GetPartsRequestFileAsync(
            pendingWork, CancellationToken.None));
        Xunit.Assert.False(await repository.UpdatePartsRequestAsync(
            completedWork, request, CancellationToken.None));
        Xunit.Assert.False(await repository.UpdatePartsRequestAsync(
            Guid.NewGuid(), request, CancellationToken.None));
    }
}
