using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository
{
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
            ImageDirectory,
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
            VideoDirectory, cancellationToken);

    public Task<VehicleWorkPhotoContent?> GetDefectVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        GetPhotoAsync("vehicle_defect_videos", videoId, cancellationToken);

    public Task<bool> DeleteDefectVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        DeletePhotoAsync("vehicle_defect_videos", videoId, cancellationToken);

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
            ImageDirectory,
            cancellationToken);

    private async Task<IReadOnlyList<Guid>> AddPhotosAsync(
        string table,
        string parentColumn,
        Guid parentId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        string directory,
        CancellationToken cancellationToken)
    {
        var media = photos
            .Select(photo => new VehicleMediaUpload(
                photo.FileName, photo.ContentType, photo.Content))
            .ToArray();
        return await AddMediaAsync(
            table, parentColumn, parentId, media, directory, cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> AddMediaAsync(
        string table,
        string parentColumn,
        Guid parentId,
        IReadOnlyList<VehicleMediaUpload> media,
        string directory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
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
                var path = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
                await File.WriteAllBytesAsync(path, item.Content, cancellationToken);
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
            VideoDirectory, cancellationToken);

    public Task<VehicleWorkPhotoContent?> GetWorkVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        GetPhotoAsync("vehicle_work_videos", videoId, cancellationToken);

    public Task<bool> DeleteWorkVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        DeletePhotoAsync("vehicle_work_videos", videoId, cancellationToken);

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

    private static async Task<VehicleWorkPhotoContent?> ReadMediaContentAsync(
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

    private static void DeleteFile(string path)
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

    private static void DeleteManagedFile(string path)
    {
        if (!IsManagedPath(path))
        {
            throw new InvalidOperationException(
                "A repair media file is outside the managed storage directories.");
        }
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool IsManagedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(
                   Path.GetFullPath(ImageDirectory) + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal) ||
               fullPath.StartsWith(
                   Path.GetFullPath(VideoDirectory) + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal);
    }
}
