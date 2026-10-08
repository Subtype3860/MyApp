using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed class VehicleHoursRepository(NpgsqlDataSource dataSource) : IVehicleHoursRepository
{
    // Serializes imports for the same day without requiring a new DB index.
    // Direct AddHoursAsync calls and external writers do not use this lock.
    private const int ImportLockNamespace = 4386201;

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
        await using (var importLock = connection.CreateCommand())
        {
            importLock.Transaction = transaction;
            importLock.CommandText =
                "SELECT pg_advisory_xact_lock(@lockNamespace, @dateKey)";
            importLock.Parameters.AddWithValue("lockNamespace", ImportLockNamespace);
            importLock.Parameters.AddWithValue("dateKey", readingDate.DayNumber);
            await importLock.ExecuteNonQueryAsync(cancellationToken);
        }
        // Load the latest eligible reading for every blank entry in ONE query.
        // Keep a mutable in-transaction cache because the import can contain
        // repeated rows for the same vehicle, including later blank rows.
        var carryForwardHours = new Dictionary<Guid, decimal>();
        var blankVehicleIds = items
            .Where(item => !item.EngineHours.HasValue)
            .Select(item => item.VehicleId)
            .Distinct()
            .ToArray();
        if (blankVehicleIds.Length > 0)
        {
            await using var previous = connection.CreateCommand();
            previous.Transaction = transaction;
            previous.CommandText =
                """
                SELECT DISTINCT ON (vehicle_id) vehicle_id, engine_hours
                FROM vehicle_hour_readings
                WHERE vehicle_id = ANY(@vehicleIds)
                  AND reading_date <= @readingDate
                ORDER BY vehicle_id, reading_date DESC, created_at DESC
                """;
            previous.Parameters.AddWithValue("vehicleIds", blankVehicleIds);
            previous.Parameters.AddWithValue("readingDate", readingDate);
            await using var reader = await previous.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                carryForwardHours[reader.GetGuid(0)] = reader.GetDecimal(1);
            }
        }

        foreach (var item in items)
        {
            var engineHours = item.EngineHours ??
                carryForwardHours.GetValueOrDefault(item.VehicleId);

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
            update.Parameters.AddWithValue("hours", engineHours);
            update.Parameters.AddWithValue("createdBy", createdBy);
            update.Parameters.AddWithValue("vehicleId", item.VehicleId);
            update.Parameters.AddWithValue("readingDate", readingDate);
            if (await update.ExecuteNonQueryAsync(cancellationToken) > 0)
            {
                carryForwardHours[item.VehicleId] = engineHours;
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
            insert.Parameters.AddWithValue("hours", engineHours);
            insert.Parameters.AddWithValue("createdBy", createdBy);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            carryForwardHours[item.VehicleId] = engineHours;
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VehicleHoursResponse>> GetHoursAsync(
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

    private NpgsqlCommand CreateRangeCommand(
        string sql,
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to)
    {
        var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        command.Parameters.AddWithValue(
            "from",
            NpgsqlTypes.NpgsqlDbType.Date,
            from is null ? DBNull.Value : from.Value);
        command.Parameters.AddWithValue(
            "to",
            NpgsqlTypes.NpgsqlDbType.Date,
            to is null ? DBNull.Value : to.Value);
        return command;
    }

    private async Task<Guid> InsertAsync(
        string sql,
        Guid vehicleId,
        Guid createdBy,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        var id = Guid.NewGuid();
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        command.Parameters.AddWithValue("createdBy", createdBy);
        foreach (var (name, value) in parameters)
        {
            if (value is null)
            {
                command.Parameters.Add(
                    name,
                    name == "resolvedDate"
                        ? NpgsqlTypes.NpgsqlDbType.Date
                        : NpgsqlTypes.NpgsqlDbType.Numeric).Value = DBNull.Value;
            }
            else
            {
                command.Parameters.AddWithValue(name, value);
            }
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }
}
