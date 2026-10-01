using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Application.Storage;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

/// <summary>
/// Реализация <see cref="IVehicleRepository"/> на PostgreSQL (Npgsql): хранит
/// технику, журнал закупок/неисправностей/моточасов/работ и их медиавложения,
/// используя <see cref="IMediaStorageService"/> для файлов на диске.
/// </summary>
public sealed class VehicleRepository(
    NpgsqlDataSource dataSource,
    IMediaStorageService mediaStorage,
    MediaStorageOptions mediaStorageOptions) : IVehicleRepository
{
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

    public async Task<int> GetDefectPhotoCountAsync(
        Guid defectId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT COUNT(*) FROM vehicle_defect_photos WHERE defect_id = @defectId");
        command.Parameters.AddWithValue("defectId", defectId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    public Task<IReadOnlyList<Guid>> AddDefectPhotosAsync(
        Guid defectId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        CancellationToken cancellationToken) =>
        AddPhotosAsync(
            "vehicle_defect_photos",
            "defect_id",
            defectId,
            photos,
            mediaStorage.SavePhotoAsync,
            cancellationToken);

    public Task<VehicleWorkPhotoContent?> GetDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        GetPhotoAsync("vehicle_defect_photos", photoId, cancellationToken);

    public Task<bool> DeleteDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        DeletePhotoAsync("vehicle_defect_photos", photoId, cancellationToken);

    public Task<int> GetDefectVideoCountAsync(
        Guid defectId,
        CancellationToken cancellationToken) =>
        GetMediaCountAsync(
            "vehicle_defect_videos", "defect_id", defectId, cancellationToken);

    public Task<IReadOnlyList<Guid>> AddDefectVideosAsync(
        Guid defectId,
        IReadOnlyList<VehicleMediaUpload> videos,
        CancellationToken cancellationToken) =>
        AddMediaAsync(
            "vehicle_defect_videos", "defect_id", defectId, videos,
            mediaStorage.SaveVideoAsync, cancellationToken);

    public Task<VehicleWorkPhotoContent?> GetDefectVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        GetPhotoAsync("vehicle_defect_videos", videoId, cancellationToken);

    public Task<bool> DeleteDefectVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        DeletePhotoAsync("vehicle_defect_videos", videoId, cancellationToken);

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

    public Task<Guid> AddWorkAsync(
        Guid vehicleId,
        VehicleWorkRequest request,
        Guid createdBy,
        CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO vehicle_works (
                id, vehicle_id, work_date, description, engine_hours,
                performer, note, defect_id, created_by)
            VALUES (
                @id, @vehicleId, CURRENT_DATE, @description, NULL,
                '', '', @defectId, @createdBy)
            """,
            vehicleId,
            createdBy,
            cancellationToken,
            ("description", request.Description),
            ("defectId", request.DefectId));

    public async Task<Guid?> CompleteDefectAsync(
        Guid defectId,
        VehicleWorkRequest request,
        Guid performedBy,
        bool _administrator,
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
            SELECT vehicle_id
            FROM vehicle_defects
            WHERE id = @defectId
            FOR UPDATE
            """;
        select.Parameters.AddWithValue("defectId", defectId);
        Guid vehicleId;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }
            vehicleId = reader.GetGuid(0);
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO vehicle_works (
                id, vehicle_id, work_date, description, engine_hours,
                performer, note, defect_id,
                failure_cause, repair_status, required_parts, performed_by,
                completed_at, created_by)
            VALUES (
                @id, @vehicleId, @workDate, @description, NULL,
                '', '', @defectId, @failureCause, @status,
                @requiredParts, @performedBy, @completedAt, @performedBy)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        command.Parameters.AddWithValue("defectId", defectId);
        var completedAt = request.RepairDateTime ?? DateTimeOffset.UtcNow;
        command.Parameters.AddWithValue("workDate", completedAt.Date);
        command.Parameters.AddWithValue("completedAt", completedAt);
        command.Parameters.AddWithValue("description", request.Description);
        command.Parameters.AddWithValue("failureCause", request.Cause);
        command.Parameters.AddWithValue("status", request.Status!);
        command.Parameters.AddWithValue(
            "requiredParts", request.RequiredParts ?? string.Empty);
        command.Parameters.AddWithValue("performedBy", performedBy);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

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

    public async Task<bool> CanManageMediaAsync(
        string category,
        Guid mediaId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var (table, parentColumn, parentTable, ownerColumn) = category switch
        {
            "defect-photo" => (
                "vehicle_defect_photos", "defect_id",
                "vehicle_defects", "created_by"),
            "defect-video" => (
                "vehicle_defect_videos", "defect_id",
                "vehicle_defects", "created_by"),
            "work-photo" => (
                "vehicle_work_photos", "work_id",
                "vehicle_works", "performed_by"),
            "work-video" => (
                "vehicle_work_videos", "work_id",
                "vehicle_works", "performed_by"),
            _ => (null, null, null, null)
        };
        if (table is null)
        {
            return false;
        }
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT EXISTS (
                SELECT 1
                FROM {table} AS media
                JOIN {parentTable} AS parent
                  ON parent.id = media.{parentColumn}
                WHERE media.id = @mediaId
                  AND parent.{ownerColumn} = @userId)
            """);
        command.Parameters.AddWithValue("mediaId", mediaId);
        command.Parameters.AddWithValue("userId", userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<int> GetWorkPhotoCountAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT COUNT(*) FROM vehicle_work_photos WHERE work_id = @workId");
        command.Parameters.AddWithValue("workId", workId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<Guid>> AddWorkPhotosAsync(
        Guid workId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        CancellationToken cancellationToken) =>
        await AddPhotosAsync(
            "vehicle_work_photos",
            "work_id",
            workId,
            photos,
            mediaStorage.SavePhotoAsync,
            cancellationToken);

    private async Task<IReadOnlyList<Guid>> AddPhotosAsync(
        string table,
        string parentColumn,
        Guid parentId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        Func<byte[], string, CancellationToken, Task<string>> saveAsync,
        CancellationToken cancellationToken)
    {
        var media = photos
            .Select(photo => new VehicleMediaUpload(
                photo.FileName, photo.ContentType, photo.Content))
            .ToArray();
        return await AddMediaAsync(
            table, parentColumn, parentId, media, saveAsync, cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> AddMediaAsync(
        string table,
        string parentColumn,
        Guid parentId,
        IReadOnlyList<VehicleMediaUpload> media,
        Func<byte[], string, CancellationToken, Task<string>> saveAsync,
        CancellationToken cancellationToken)
    {
        var writtenPaths = new List<string>(media.Count);
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var ids = new List<Guid>(media.Count);
        try
        {
            foreach (var item in media)
            {
                var id = Guid.NewGuid();
                var extension = GetSafeExtension(item.FileName, item.ContentType);
                var path = await saveAsync(
                    item.Content, $"upload{extension}", cancellationToken);
                writtenPaths.Add(path);
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    $"""
                    INSERT INTO {table} (
                        id, {parentColumn}, file_name, content_type,
                        content, storage_path, size)
                    VALUES (
                        @id, @parentId, @fileName, @contentType,
                        NULL, @storagePath, @size)
                    """;
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("parentId", parentId);
                command.Parameters.AddWithValue(
                    "fileName", SafeFileName(item.FileName));
                command.Parameters.AddWithValue("contentType", item.ContentType);
                command.Parameters.AddWithValue("storagePath", path);
                command.Parameters.AddWithValue("size", (long)item.Content.Length);
                await command.ExecuteNonQueryAsync(cancellationToken);
                ids.Add(id);
            }
            await transaction.CommitAsync(cancellationToken);
            return ids;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            foreach (var path in writtenPaths)
            {
                DeleteFile(path);
            }
            throw;
        }
    }

    public async Task<VehicleWorkPhotoContent?> GetWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        await GetPhotoAsync("vehicle_work_photos", photoId, cancellationToken);

    private async Task<VehicleWorkPhotoContent?> GetPhotoAsync(
        string table,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT file_name, content_type, content, storage_path
            FROM {table}
            WHERE id = @photoId
            """);
        command.Parameters.AddWithValue("photoId", photoId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? await ReadMediaContentAsync(reader, cancellationToken)
            : null;
    }

    public async Task<bool> DeleteWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        await DeletePhotoAsync("vehicle_work_photos", photoId, cancellationToken);

    private async Task<bool> DeletePhotoAsync(
        string table,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"DELETE FROM {table} WHERE id = @photoId RETURNING storage_path";
        command.Parameters.AddWithValue("photoId", photoId);
        var path = await command.ExecuteScalarAsync(cancellationToken);
        if (path is null)
        {
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        if (path is string storagePath)
        {
            DeleteFile(storagePath);
        }
        return true;
    }

    public Task<int> GetWorkVideoCountAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        GetMediaCountAsync(
            "vehicle_work_videos", "work_id", workId, cancellationToken);

    public Task<IReadOnlyList<Guid>> AddWorkVideosAsync(
        Guid workId,
        IReadOnlyList<VehicleMediaUpload> videos,
        CancellationToken cancellationToken) =>
        AddMediaAsync(
            "vehicle_work_videos", "work_id", workId, videos,
            mediaStorage.SaveVideoAsync, cancellationToken);

    public Task<VehicleWorkPhotoContent?> GetWorkVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        GetPhotoAsync("vehicle_work_videos", videoId, cancellationToken);

    public Task<bool> DeleteWorkVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        DeletePhotoAsync("vehicle_work_videos", videoId, cancellationToken);

    public async Task<Guid?> AddPartsRequestAsync(
        Guid defectId,
        VehiclePartsRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO vehicle_parts_requests (
                id, defect_id, request_date, request_number, description,
                required_parts, created_by)
            SELECT @id, defects.id, @date, @number, @description,
                   @requiredParts, @createdBy
            FROM vehicle_defects AS defects
            WHERE defects.id = @defectId
              AND EXISTS (
                  SELECT 1
                  FROM vehicle_works
                  WHERE defect_id = defects.id
                    AND repair_status = 'awaiting_parts')
            RETURNING id
            """);
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("defectId", defectId);
        command.Parameters.AddWithValue("date", request.RequestDate);
        command.Parameters.AddWithValue("number", request.RequestNumber);
        command.Parameters.AddWithValue("description", request.Description);
        command.Parameters.AddWithValue("requiredParts", request.RequiredParts ?? "");
        command.Parameters.AddWithValue("createdBy", createdBy);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid value ? value : null;
    }

    public async Task<IReadOnlyList<VehiclePartsRequestResponse>> GetPartsRequestsAsync(
        Guid defectId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, defect_id, request_number, request_date, description,
                   required_parts, created_at
            FROM vehicle_parts_requests
            WHERE defect_id = @defectId
            ORDER BY created_at
            """);
        command.Parameters.AddWithValue("defectId", defectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<VehiclePartsRequestResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new VehiclePartsRequestResponse(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetFieldValue<DateOnly>(3), reader.GetString(4),
                reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }
        return result;
    }

    public async Task<bool> UpdatePartsRequestAsync(
        Guid requestId,
        VehiclePartsRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE vehicle_parts_requests
            SET request_date = @date,
                request_number = @number,
                description = @description,
                required_parts = @requiredParts
            WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", requestId);
        command.Parameters.AddWithValue("date", request.RequestDate);
        command.Parameters.AddWithValue("number", request.RequestNumber);
        command.Parameters.AddWithValue("description", request.Description);
        command.Parameters.AddWithValue("requiredParts", request.RequiredParts ?? "");
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeletePartsRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM vehicle_parts_requests WHERE id = @id");
        command.Parameters.AddWithValue("id", requestId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
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
        await using var command = dataSource.CreateCommand(
            $"DELETE FROM {table} WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (deleted)
        {
            foreach (var path in paths)
            {
                DeleteFile(path);
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

    private async Task<IReadOnlyList<VehicleDefectResponse>> GetDefectsAsync(
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
        await photoReader.DisposeAsync();
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

    private async Task<IReadOnlyList<VehicleWorkResponse>> GetWorksAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        await using var command = CreateRangeCommand(
            """
            SELECT works.id, works.defect_id, COALESCE(defects.node_name, ''),
                   works.description, works.created_at,
                   works.failure_cause, works.repair_status,
                   works.required_parts, works.performed_by,
                   COALESCE(CONCAT_WS(' ', performers.last_name,
                       performers.first_name, NULLIF(performers.middle_name, '')), ''),
                   works.completed_at
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
                reader.IsDBNull(1) ? null :                 reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                [],
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.GetString(9),
                reader.IsDBNull(10)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(10),
                []));
        }
        await reader.DisposeAsync();

        for (var index = 0; index < result.Count; index++)
        {
            if (result[index].DefectId is not Guid defectId)
            {
                continue;
            }
            result[index] = result[index] with
            {
                PartsRequests = await GetPartsRequestsAsync(
                    defectId, cancellationToken)
            };
        }

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
        await photoReader.DisposeAsync();
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

    private async Task<int> GetMediaCountAsync(
        string table,
        string parentColumn,
        Guid parentId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT COUNT(*) FROM {table} WHERE {parentColumn} = @parentId");
        command.Parameters.AddWithValue("parentId", parentId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private Task<IReadOnlyList<string>> GetDefectMediaPathsAsync(
        Guid defectId,
        CancellationToken cancellationToken) =>
        GetPathsAsync(
            """
            SELECT storage_path FROM vehicle_defect_photos
            WHERE defect_id = @id AND storage_path IS NOT NULL
            UNION ALL
            SELECT storage_path FROM vehicle_defect_videos
            WHERE defect_id = @id
            UNION ALL
            SELECT photos.storage_path
            FROM vehicle_work_photos AS photos
            JOIN vehicle_works AS works ON works.id = photos.work_id
            WHERE works.defect_id = @id AND photos.storage_path IS NOT NULL
            UNION ALL
            SELECT videos.storage_path
            FROM vehicle_work_videos AS videos
            JOIN vehicle_works AS works ON works.id = videos.work_id
            WHERE works.defect_id = @id
            """,
            defectId,
            cancellationToken);

    private Task<IReadOnlyList<string>> GetWorkMediaPathsAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        GetPathsAsync(
            """
            SELECT storage_path FROM vehicle_work_photos
            WHERE work_id = @id AND storage_path IS NOT NULL
            UNION ALL
            SELECT storage_path FROM vehicle_work_videos
            WHERE work_id = @id
            """,
            workId,
            cancellationToken);

    private async Task<IReadOnlyList<string>> GetPathsAsync(
        string sql,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private async Task<VehicleWorkPhotoContent?> ReadMediaContentAsync(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var fileName = reader.GetString(0);
        var contentType = reader.GetString(1);
        if (!reader.IsDBNull(2))
        {
            return new(
                fileName, contentType, reader.GetFieldValue<byte[]>(2));
        }
        if (reader.IsDBNull(3))
        {
            return null;
        }
        var path = reader.GetString(3);
        if (!IsManagedPath(path))
        {
            return null;
        }
        try
        {
            return new(
                fileName,
                contentType,
                await File.ReadAllBytesAsync(path, cancellationToken),
                path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
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

    private static string GetSafeExtension(
        string fileName,
        string contentType)
    {
        var allowed = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => new[] { ".jpg", ".jpeg" },
            "image/png" => new[] { ".png" },
            "image/webp" => new[] { ".webp" },
            "video/mp4" => new[] { ".mp4" },
            "video/webm" => new[] { ".webm" },
            "video/quicktime" => new[] { ".mov" },
            _ => Array.Empty<string>()
        };
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return allowed.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? extension
            : allowed.FirstOrDefault() ?? string.Empty;
    }

    private static string SafeFileName(string value)
    {
        var name = value.Replace('\\', '/').Split('/').Last();
        name = new string(name.Where(character => !char.IsControl(character)).ToArray());
        return name.Length <= 255 ? name : name[..255];
    }

    private void DeleteFile(string path)
    {
        if (!IsManagedPath(path))
        {
            return;
        }
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Metadata deletion must not be rolled back after it is committed.
        }
        catch (UnauthorizedAccessException)
        {
            // A later maintenance pass can remove an inaccessible orphan.
        }
    }

    private bool IsManagedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(
                   Path.GetFullPath(mediaStorageOptions.PhotoDirectory) + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal) ||
               fullPath.StartsWith(
                   Path.GetFullPath(mediaStorageOptions.VideoDirectory) + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal);
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
