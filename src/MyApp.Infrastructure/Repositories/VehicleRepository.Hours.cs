using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository
{
    public Task<Guid> AddHoursAsync(
        Guid vehicleId,
        VehicleHoursRequest request,
        Guid createdBy,
        CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO vehicle_hour_readings (
                id, vehicle_id, reading_date, engine_hours, note, created_by)
            VALUES (
                @id, @vehicleId, @date, @hours, @note, @createdBy)
            """,
            vehicleId,
            createdBy,
            cancellationToken,
            ("date", request.ReadingDate),
            ("hours", request.EngineHours),
            ("note", request.Note));

    public async Task ImportHoursAsync(
        DateOnly readingDate,
        IReadOnlyList<VehicleHoursImportItem> items,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        foreach (var item in items)
        {
            var engineHours = item.EngineHours;
            if (engineHours is null)
            {
                await using var previous = connection.CreateCommand();
                previous.Transaction = transaction;
                previous.CommandText =
                    """
                    SELECT engine_hours
                    FROM vehicle_hour_readings
                    WHERE vehicle_id = @vehicleId
                      AND reading_date <= @readingDate
                    ORDER BY reading_date DESC, created_at DESC
                    LIMIT 1
                    """;
                previous.Parameters.AddWithValue("vehicleId", item.VehicleId);
                previous.Parameters.AddWithValue("readingDate", readingDate);
                var value = await previous.ExecuteScalarAsync(cancellationToken);
                engineHours = value is decimal hours ? hours : 0m;
            }

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE vehicle_hour_readings
                SET engine_hours = @hours,
                    note = 'Импорт CSV',
                    created_by = @createdBy,
                    created_at = NOW()
                WHERE vehicle_id = @vehicleId
                  AND reading_date = @readingDate
                """;
            update.Parameters.AddWithValue("hours", engineHours.Value);
            update.Parameters.AddWithValue("createdBy", createdBy);
            update.Parameters.AddWithValue("vehicleId", item.VehicleId);
            update.Parameters.AddWithValue("readingDate", readingDate);
            if (await update.ExecuteNonQueryAsync(cancellationToken) > 0)
            {
                continue;
            }

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO vehicle_hour_readings (
                    id, vehicle_id, reading_date, engine_hours, note, created_by)
                VALUES (
                    @id, @vehicleId, @readingDate, @hours, 'Импорт CSV', @createdBy)
                """;
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("vehicleId", item.VehicleId);
            insert.Parameters.AddWithValue("readingDate", readingDate);
            insert.Parameters.AddWithValue("hours", engineHours.Value);
            insert.Parameters.AddWithValue("createdBy", createdBy);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<VehicleHoursResponse>> GetHoursAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        await using var command = CreateRangeCommand(
            """
            SELECT id, reading_date, engine_hours, note, created_at
            FROM vehicle_hour_readings
            WHERE vehicle_id = @vehicleId
              AND (@from IS NULL OR reading_date >= @from)
              AND (@to IS NULL OR reading_date <= @to)
            ORDER BY reading_date DESC, created_at DESC
            """,
            vehicleId,
            from,
            to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<VehicleHoursResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new VehicleHoursResponse(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetDecimal(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }
        return result;
    }
}
