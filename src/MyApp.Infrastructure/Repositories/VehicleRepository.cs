using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository(NpgsqlDataSource dataSource) : IVehicleRepository
{
    private const string ImageDirectory = "/mnt/dietpi/img";
    private const string VideoDirectory = "/mnt/dietpi/video";
    private const string VehicleSelect =
        """
        SELECT
            vehicles.id,
            COALESCE(groups.full_name, ''),
            COALESCE(types.full_name, ''),
            COALESCE(models.full_name, ''),
            vehicles.gar_number,
            COALESCE(vehicles.gos_number, ''),
            COALESCE(vehicles.vim, '')
        FROM number_car AS vehicles
        LEFT JOIN model_car AS models ON models.id = vehicles.car_mode_id
        LEFT JOIN type_car AS types ON types.id = models.car_type_id
        LEFT JOIN group_car AS groups ON groups.id = types.car_group_id
        """;

    public async Task<IReadOnlyList<VehicleResponse>> GetVehiclesAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            VehicleSelect +
            """

            ORDER BY
                COALESCE(groups.full_name, ''),
                COALESCE(types.full_name, ''),
                COALESCE(models.full_name, ''),
                vehicles.gar_number NULLS LAST
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<VehicleResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadVehicle(reader));
        }
        return result;
    }

    public async Task<VehicleJournalResponse?> GetJournalAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var vehicle = await GetVehicleAsync(vehicleId, cancellationToken);
        if (vehicle is null)
        {
            return null;
        }

        var purchases = await GetPurchasesAsync(
            vehicleId, from, to, cancellationToken);
        var defects = await GetDefectsAsync(
            vehicleId, from, to, cancellationToken);
        var hours = await GetHoursAsync(
            vehicleId, from, to, cancellationToken);
        var works = await GetWorksAsync(
            vehicleId, from, to, cancellationToken);
        return new VehicleJournalResponse(
            vehicle, purchases, defects, hours, works);
    }

    public async Task<bool> VehicleExistsAsync(
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM number_car WHERE id = @vehicleId)");
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public Task<Guid> AddPurchaseAsync(
        Guid vehicleId,
        VehiclePurchaseRequest request,
        Guid createdBy,
        CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO vehicle_purchase_requests (
                id, vehicle_id, request_date, request_number, item_name,
                quantity, status, note, created_by)
            VALUES (
                @id, @vehicleId, @date, @number, @item, @quantity,
                @status, @note, @createdBy)
            """,
            vehicleId,
            createdBy,
            cancellationToken,
            ("date", request.RequestDate),
            ("number", request.RequestNumber),
            ("item", request.ItemName),
            ("quantity", request.Quantity),
            ("status", request.Status),
            ("note", request.Note));

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

    public async Task<bool> UpdatePartsRequestAsync(
        Guid workId,
        VehiclePartsRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE vehicle_works
            SET purchase_request_number = @number,
                purchase_request_date = @date,
                purchase_request_file_name = @fileName,
                purchase_request_content_type = @contentType,
                purchase_request_content = @content
            WHERE id = @workId AND repair_status = 'awaiting_parts'
            """);
        command.Parameters.AddWithValue("workId", workId);
        command.Parameters.AddWithValue("number", request.RequestNumber);
        command.Parameters.AddWithValue("date", request.RequestDate);
        AddNullable(command, "fileName", NpgsqlTypes.NpgsqlDbType.Varchar, request.FileName);
        AddNullable(command, "contentType", NpgsqlTypes.NpgsqlDbType.Varchar, request.ContentType);
        AddNullable(command, "content", NpgsqlTypes.NpgsqlDbType.Bytea, request.Content);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<VehicleRequestFileContent?> GetPartsRequestFileAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT purchase_request_file_name,
                   purchase_request_content_type,
                   purchase_request_content
            FROM vehicle_works
            WHERE id = @workId AND purchase_request_content IS NOT NULL
            """);
        command.Parameters.AddWithValue("workId", workId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new VehicleRequestFileContent(
                reader.GetString(0), reader.GetString(1),
                reader.GetFieldValue<byte[]>(2))
            : null;
    }

    public async Task<bool> DeletePartsRequestAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE vehicle_works
            SET purchase_request_number = NULL,
                purchase_request_date = NULL,
                purchase_request_file_name = NULL,
                purchase_request_content_type = NULL,
                purchase_request_content = NULL
            WHERE id = @workId
            """);
        command.Parameters.AddWithValue("workId", workId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> DeleteEntryAsync(
        string category,
        Guid id,
        CancellationToken cancellationToken)
    {
        var table = category switch
        {
            "purchases" => "vehicle_purchase_requests",
            "defects" => "vehicle_defects",
            "hours" => "vehicle_hour_readings",
            "works" => "vehicle_works",
            _ => null
        };
        if (table is null)
        {
            return false;
        }

        var paths = category switch
        {
            "defects" => await GetDefectMediaPathsAsync(id, cancellationToken),
            "works" => await GetWorkMediaPathsAsync(id, cancellationToken),
            _ => []
        };
        if (paths.Any(path => !IsManagedPath(path)))
        {
            throw new InvalidOperationException(
                "A repair media file is outside the managed storage directories.");
        }
        await using var command = dataSource.CreateCommand(
            $"DELETE FROM {table} WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (deleted)
        {
            foreach (var path in paths)
            {
                DeleteManagedFile(path);
            }
        }
        return deleted;
    }

    private async Task<VehicleResponse?> GetVehicleAsync(
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            VehicleSelect + "\nWHERE vehicles.id = @vehicleId");
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadVehicle(reader)
            : null;
    }

    private async Task<IReadOnlyList<VehiclePurchaseResponse>> GetPurchasesAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        await using var command = CreateRangeCommand(
            """
            SELECT id, request_date, request_number, item_name, quantity,
                   status, note, created_at
            FROM vehicle_purchase_requests
            WHERE vehicle_id = @vehicleId
              AND (@from IS NULL OR request_date >= @from)
              AND (@to IS NULL OR request_date <= @to)
            ORDER BY request_date DESC, created_at DESC
            """,
            vehicleId,
            from,
            to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<VehiclePurchaseResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new VehiclePurchaseResponse(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }
        return result;
    }

    private async Task<IReadOnlyList<VehicleWorkResponse>> GetWorksAsync(
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

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        NpgsqlTypes.NpgsqlDbType type,
        object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;

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

    private static VehicleResponse ReadVehicle(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6));
}
