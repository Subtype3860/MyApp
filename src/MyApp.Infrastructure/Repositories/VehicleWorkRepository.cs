using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

// Handles work records and work journal queries independently of the legacy facade.
public sealed class VehicleWorkRepository(NpgsqlDataSource dataSource) : IVehicleWorkRepository
{
    public Task<Guid> AddWorkAsync(
        Guid vehicleId,
        VehicleWorkRequest request,
        Guid createdBy,
        CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO vehicle_works (
                id, vehicle_id, work_date, description, engine_hours,
                performer, note, defect_id, purchase_request_number, created_by)
            VALUES (
                @id, @vehicleId, CURRENT_DATE, @description, NULL,
                '', '', @defectId, '', @createdBy)
            """,
            vehicleId,
            createdBy,
            cancellationToken,
            ("description", request.Description),
            ("defectId", request.DefectId));

    public async Task<bool> WorkExistsAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM vehicle_works WHERE id = @workId)");
        command.Parameters.AddWithValue("workId", workId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> IsWorkPerformerAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM vehicle_works
                WHERE id = @id
                  AND (performed_by = @userId OR created_by = @userId))
            """);
        command.Parameters.AddWithValue("id", workId);
        command.Parameters.AddWithValue("userId", userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<IReadOnlyList<VehicleWorkResponse>> GetWorksAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        await using var command = CreateRangeCommand(
            """
            SELECT works.id, works.defect_id, COALESCE(defects.node_name, ''),
                   works.description, works.purchase_request_number,
                   works.created_at, works.failure_cause, works.repair_status,
                   works.required_parts, works.performed_by,
                   COALESCE(CONCAT_WS(' ', performers.last_name,
                       performers.first_name, NULLIF(performers.middle_name, '')), ''),
                   works.completed_at, works.purchase_request_date,
                   COALESCE(works.purchase_request_file_name, ''),
                   COALESCE(works.purchase_request_content_type, ''),
                   works.purchase_request_content IS NOT NULL
            FROM vehicle_works AS works
            LEFT JOIN vehicle_defects AS defects ON defects.id = works.defect_id
            LEFT JOIN app_users AS performers ON performers.id = works.performed_by
            WHERE works.vehicle_id = @vehicleId
              AND (@from IS NULL OR works.created_at::date >= @from)
              AND (@to IS NULL OR works.created_at::date <= @to)
            ORDER BY works.created_at DESC
            """,
            vehicleId,
            from,
            to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<VehicleWorkResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new VehicleWorkResponse(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                [],
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.GetString(10),
                reader.IsDBNull(11)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetString(4),
                reader.IsDBNull(12)
                    ? null
                    : reader.GetFieldValue<DateOnly>(12),
                reader.GetString(13),
                reader.GetString(14),
                reader.GetBoolean(15),
                []));
        }
        await reader.DisposeAsync();

        await using var photoCommand = CreateRangeCommand(
            """
            SELECT photos.work_id, photos.id, photos.file_name,
                   photos.content_type,
                   COALESCE(NULLIF(photos.size, 0), OCTET_LENGTH(photos.content), 0)
            FROM vehicle_work_photos AS photos
            JOIN vehicle_works AS works ON works.id = photos.work_id
            WHERE works.vehicle_id = @vehicleId
              AND (@from IS NULL OR works.created_at::date >= @from)
              AND (@to IS NULL OR works.created_at::date <= @to)
            ORDER BY photos.created_at, photos.id
            """,
            vehicleId,
            from,
            to);
        await using var photoReader = await photoCommand.ExecuteReaderAsync(
            cancellationToken);
        var photosByWork = new Dictionary<Guid, List<VehicleWorkPhotoResponse>>();
        while (await photoReader.ReadAsync(cancellationToken))
        {
            var workId = photoReader.GetGuid(0);
            if (!photosByWork.TryGetValue(workId, out var photos))
            {
                photos = [];
                photosByWork[workId] = photos;
            }
            photos.Add(new VehicleWorkPhotoResponse(
                photoReader.GetGuid(1),
                photoReader.GetString(2),
                photoReader.GetString(3),
                photoReader.GetInt32(4)));
        }
        await using var videoCommand = CreateRangeCommand(
            """
            SELECT videos.work_id, videos.id, videos.file_name,
                   videos.content_type, videos.size
            FROM vehicle_work_videos AS videos
            JOIN vehicle_works AS works ON works.id = videos.work_id
            WHERE works.vehicle_id = @vehicleId
              AND (@from IS NULL OR works.created_at::date >= @from)
              AND (@to IS NULL OR works.created_at::date <= @to)
            ORDER BY videos.created_at, videos.id
            """,
            vehicleId, from, to);
        await using var videoReader = await videoCommand.ExecuteReaderAsync(
            cancellationToken);
        var videosByWork = await ReadMediaByParentAsync(
            videoReader, cancellationToken);
        return result
            .Select(work => work with
            {
                Photos = photosByWork.GetValueOrDefault(work.Id) ?? [],
                Videos = videosByWork.GetValueOrDefault(work.Id) ?? []
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
