using MyApp.Application.Abstractions;
using MyApp.Application.Storage;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed class MediaStorageAdministrationRepository(
    NpgsqlDataSource dataSource,
    MediaStorageOptions storageOptions) : IMediaStorageAdministrationRepository
{
    public async Task<int> GetRetentionDaysAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT retention_days FROM media_storage_settings WHERE id = 1");
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int days
            ? days
            : throw new InvalidOperationException(
                "Media storage retention settings are missing.");
    }

    public async Task SetRetentionDaysAsync(
        int retentionDays,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE media_storage_settings
            SET retention_days = @retentionDays, updated_at = NOW()
            WHERE id = 1
            """);
        command.Parameters.AddWithValue("retentionDays", retentionDays);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "Media storage retention settings are missing.");
        }
    }

    public async Task<IReadOnlyList<StagedMediaFile>> GetExpiredFilesAsync(
        int retentionDays,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT table_name, id, storage_path, size
            FROM (
                SELECT 'vehicle_defect_photos' AS table_name, id, storage_path, size, created_at
                FROM vehicle_defect_photos
                WHERE LEFT(storage_path, LENGTH(@stagingPhoto)) = @stagingPhoto
                UNION ALL
                SELECT 'vehicle_work_photos', id, storage_path, size, created_at
                FROM vehicle_work_photos
                WHERE LEFT(storage_path, LENGTH(@stagingPhoto)) = @stagingPhoto
                UNION ALL
                SELECT 'vehicle_defect_videos', id, storage_path, size, created_at
                FROM vehicle_defect_videos
                WHERE LEFT(storage_path, LENGTH(@stagingVideo)) = @stagingVideo
                UNION ALL
                SELECT 'vehicle_work_videos', id, storage_path, size, created_at
                FROM vehicle_work_videos
                WHERE LEFT(storage_path, LENGTH(@stagingVideo)) = @stagingVideo
            ) AS staged
            WHERE created_at <= NOW() - make_interval(days => @retentionDays)
            ORDER BY created_at
            LIMIT @batchSize
            """);
        command.Parameters.AddWithValue(
            "stagingPhoto",
            Path.GetFullPath(storageOptions.StagingPhotoDirectory) +
                Path.DirectorySeparatorChar);
        command.Parameters.AddWithValue(
            "stagingVideo",
            Path.GetFullPath(storageOptions.StagingVideoDirectory) +
                Path.DirectorySeparatorChar);
        command.Parameters.AddWithValue("retentionDays", retentionDays);
        command.Parameters.AddWithValue("batchSize", batchSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var files = new List<StagedMediaFile>();
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new StagedMediaFile(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetInt64(3)));
        }
        return files;
    }

    public async Task<bool> UpdateStoragePathAsync(
        StagedMediaFile file,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var table = file.TableName switch
        {
            "vehicle_defect_photos" => "vehicle_defect_photos",
            "vehicle_work_photos" => "vehicle_work_photos",
            "vehicle_defect_videos" => "vehicle_defect_videos",
            "vehicle_work_videos" => "vehicle_work_videos",
            _ => throw new ArgumentException(
                "Unexpected media table name.", nameof(file))
        };
        await using var command = dataSource.CreateCommand(
            $"UPDATE {table} SET storage_path = @destinationPath " +
            "WHERE id = @id AND storage_path = @sourcePath");
        command.Parameters.AddWithValue("destinationPath", destinationPath);
        command.Parameters.AddWithValue("id", file.Id);
        command.Parameters.AddWithValue("sourcePath", file.StoragePath);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
