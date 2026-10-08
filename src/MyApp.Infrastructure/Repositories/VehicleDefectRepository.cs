using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

// Owns defect lifecycle and defect journal reads without coupling to VehicleRepository.
public sealed class VehicleDefectRepository(NpgsqlDataSource dataSource) : IVehicleDefectRepository
{
    public Task<Guid> AddDefectAsync(
        Guid vehicleId,
        VehicleDefectRequest request,
        Guid createdBy,
        CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO vehicle_defects (
                id, vehicle_id, node_name, failure_reason, error_code,
                symptoms, downtime_started_at, created_by)
            VALUES (
                @id, @vehicleId, @nodeName, @failureReason, @errorCode,
                @symptoms, @downtimeStartedAt, @createdBy)
            """,
            vehicleId,
            createdBy,
            cancellationToken,
            ("nodeName", string.Empty),
            ("failureReason", request.Symptoms),
            ("errorCode", request.ErrorCode ?? string.Empty),
            ("symptoms", request.Symptoms),
            ("downtimeStartedAt", request.DowntimeStartedAt));

    public async Task<bool> DefectExistsAsync(
        Guid defectId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM vehicle_defects WHERE id = @defectId)");
        command.Parameters.AddWithValue("defectId", defectId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> DefectBelongsToVehicleAsync(
        Guid defectId,
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM vehicle_defects
                WHERE id = @defectId AND vehicle_id = @vehicleId)
            """);
        command.Parameters.AddWithValue("defectId", defectId);
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> ClaimDefectAsync(
        Guid defectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE vehicle_defects
            SET assigned_to = @userId, repair_started_at = NOW()
            WHERE id = @defectId
              AND assigned_to IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM vehicle_works
                  WHERE defect_id = @defectId AND completed_at IS NOT NULL)
            """);
        command.Parameters.AddWithValue("defectId", defectId);
        command.Parameters.AddWithValue("userId", userId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> IsDefectCreatorAsync(
        Guid defectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM vehicle_defects WHERE id = @id AND created_by = @userId)");
        command.Parameters.AddWithValue("id", defectId);
        command.Parameters.AddWithValue("userId", userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<Guid?> CompleteDefectAsync(
        Guid defectId,
        VehicleWorkRequest request,
        Guid performedBy,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText =
            """
            SELECT vehicle_id, assigned_to
            FROM vehicle_defects
            WHERE id = @defectId
            FOR UPDATE
            """;
        select.Parameters.AddWithValue("defectId", defectId);
        Guid vehicleId;
        Guid? assignedTo;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }
            vehicleId = reader.GetGuid(0);
            assignedTo = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        }
        if (!administrator && assignedTo != performedBy)
        {
            return null;
        }

        await using var completed = connection.CreateCommand();
        completed.Transaction = transaction;
        completed.CommandText =
            """
            SELECT EXISTS (
                SELECT 1 FROM vehicle_works
                WHERE defect_id = @defectId AND completed_at IS NOT NULL)
            """;
        completed.Parameters.AddWithValue("defectId", defectId);
        if ((bool)(await completed.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO vehicle_works (
                id, vehicle_id, work_date, description, engine_hours,
                performer, note, defect_id, purchase_request_number,
                failure_cause, repair_status, required_parts, performed_by,
                completed_at, created_by)
            VALUES (
                @id, @vehicleId, CURRENT_DATE, @description, NULL,
                '', '', @defectId, @requestNumber, @failureCause, @status,
                @requiredParts, @performedBy, NOW(), @performedBy)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        command.Parameters.AddWithValue("defectId", defectId);
        command.Parameters.AddWithValue("description", request.Description);
        command.Parameters.AddWithValue("requestNumber", string.Empty);
        command.Parameters.AddWithValue("failureCause", request.Cause);
        command.Parameters.AddWithValue("status", request.Status!);
        command.Parameters.AddWithValue(
            "requiredParts", request.RequiredParts ?? string.Empty);
        command.Parameters.AddWithValue("performedBy", performedBy);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<VehicleDefectResponse>> GetDefectsAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        await using var command = CreateRangeCommand(
            """
            SELECT defects.id, defects.error_code, defects.symptoms,
                   COALESCE(completed.repair_status,
                       CASE WHEN defects.assigned_to IS NULL
                           THEN 'new' ELSE 'in_progress' END),
                   defects.created_at, defects.downtime_started_at,
                   defects.created_by,
                   CONCAT_WS(' ', creators.last_name, creators.first_name,
                       NULLIF(creators.middle_name, '')),
                   defects.assigned_to,
                   COALESCE(CONCAT_WS(' ', assignees.last_name,
                       assignees.first_name, NULLIF(assignees.middle_name, '')), ''),
                   defects.repair_started_at, defects.node_name,
                   defects.failure_reason
            FROM vehicle_defects AS defects
            JOIN app_users AS creators ON creators.id = defects.created_by
            LEFT JOIN app_users AS assignees ON assignees.id = defects.assigned_to
            LEFT JOIN LATERAL (
                SELECT works.repair_status
                FROM vehicle_works AS works
                WHERE works.defect_id = defects.id
                  AND works.completed_at IS NOT NULL
                ORDER BY works.completed_at DESC
                LIMIT 1
            ) AS completed ON TRUE
            WHERE defects.vehicle_id = @vehicleId
              AND (@from IS NULL OR defects.created_at::date >= @from)
              AND (@to IS NULL OR defects.created_at::date <= @to)
            ORDER BY defects.created_at DESC
            """,
            vehicleId,
            from,
            to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<VehicleDefectResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new VehicleDefectResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetGuid(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.GetString(9),
                reader.IsDBNull(10)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(10),
                [],
                [],
                reader.GetString(11),
                reader.GetString(12)));
        }
        await reader.DisposeAsync();

        await using var photoCommand = CreateRangeCommand(
            """
            SELECT photos.defect_id, photos.id, photos.file_name,
                   photos.content_type,
                   COALESCE(NULLIF(photos.size, 0), OCTET_LENGTH(photos.content), 0)
            FROM vehicle_defect_photos AS photos
            JOIN vehicle_defects AS defects ON defects.id = photos.defect_id
            WHERE defects.vehicle_id = @vehicleId
              AND (@from IS NULL OR defects.created_at::date >= @from)
              AND (@to IS NULL OR defects.created_at::date <= @to)
            ORDER BY photos.created_at, photos.id
            """,
            vehicleId,
            from,
            to);
        await using var photoReader = await photoCommand.ExecuteReaderAsync(
            cancellationToken);
        var photosByDefect = new Dictionary<Guid, List<VehicleWorkPhotoResponse>>();
        while (await photoReader.ReadAsync(cancellationToken))
        {
            var defectId = photoReader.GetGuid(0);
            if (!photosByDefect.TryGetValue(defectId, out var photos))
            {
                photos = [];
                photosByDefect[defectId] = photos;
            }
            photos.Add(new VehicleWorkPhotoResponse(
                photoReader.GetGuid(1),
                photoReader.GetString(2),
                photoReader.GetString(3),
                photoReader.GetInt32(4)));
        }
        await using var videoCommand = CreateRangeCommand(
            """
            SELECT videos.defect_id, videos.id, videos.file_name,
                   videos.content_type, videos.size
            FROM vehicle_defect_videos AS videos
            JOIN vehicle_defects AS defects ON defects.id = videos.defect_id
            WHERE defects.vehicle_id = @vehicleId
              AND (@from IS NULL OR defects.created_at::date >= @from)
              AND (@to IS NULL OR defects.created_at::date <= @to)
            ORDER BY videos.created_at, videos.id
            """,
            vehicleId, from, to);
        await using var videoReader = await videoCommand.ExecuteReaderAsync(
            cancellationToken);
        var videosByDefect = await ReadMediaByParentAsync(
            videoReader, cancellationToken);
        return result
            .Select(defect => defect with
            {
                Photos = photosByDefect.GetValueOrDefault(defect.Id) ?? [],
                Videos = videosByDefect.GetValueOrDefault(defect.Id) ?? []
            })
            .ToArray();
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

    private static async Task<Dictionary<Guid, List<VehicleMediaResponse>>>
        ReadMediaByParentAsync(
            NpgsqlDataReader reader,
            CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, List<VehicleMediaResponse>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var parentId = reader.GetGuid(0);
            if (!result.TryGetValue(parentId, out var media))
            {
                media = [];
                result[parentId] = media;
            }
            media.Add(new(
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4)));
        }
        return result;
    }
}
