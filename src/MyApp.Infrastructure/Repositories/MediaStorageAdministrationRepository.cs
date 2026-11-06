using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.Storage;
using MyApp.Infrastructure.Db;

namespace MyApp.Infrastructure.Repositories;

/// <summary>Доступ к данным настроек и переноса медиафайлов.</summary>
public sealed class MediaStorageAdministrationRepository(
    AppDbContext db,
    MediaStorageOptions storageOptions) : IMediaStorageAdministrationRepository
{
    private const short SettingsId = 1;

    /// <summary>Возвращает настройки временного и постоянного хранения медиа.</summary>
    public async Task<MediaStorageSettings> GetSettingsAsync(
        CancellationToken cancellationToken) =>
        await db.MediaStorageSettings
            .AsNoTracking()
            .Where(settings => settings.Id == SettingsId)
            .Select(settings => new MediaStorageSettings(
                settings.RetentionDays,
                settings.PermanentRetentionDays,
                settings.MaxStorageBytes))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException(
            "Media storage retention settings are missing.");

    /// <summary>Сохраняет настройки временного и постоянного хранения медиа.</summary>
    public async Task SetSettingsAsync(
        MediaStorageSettings settings,
        CancellationToken cancellationToken)
    {
        var updated = await db.MediaStorageSettings
            .Where(row => row.Id == SettingsId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        row => row.RetentionDays,
                        settings.StagingRetentionDays)
                    .SetProperty(
                        row => row.PermanentRetentionDays,
                        settings.PermanentRetentionDays)
                    .SetProperty(
                        row => row.MaxStorageBytes,
                        settings.MaxStorageBytes)
                    .SetProperty(row => row.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException(
                "Media storage retention settings are missing.");
        }
    }

    /// <summary>Возвращает медиафайлы, созданные раньше указанного момента.</summary>
    public async Task<IReadOnlyList<RetainedMediaFile>> GetExpiredMediaAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var defectPhotos = await db.VehicleDefectPhotos
            .AsNoTracking()
            .Where(file => file.CreatedAt <= cutoff)
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new RetainedMediaFile(
                "vehicle_defect_photos",
                file.Id,
                file.StoragePath,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var workPhotos = await db.VehicleWorkPhotos
            .AsNoTracking()
            .Where(file => file.CreatedAt <= cutoff)
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new RetainedMediaFile(
                "vehicle_work_photos",
                file.Id,
                file.StoragePath,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var defectVideos = await db.VehicleDefectVideos
            .AsNoTracking()
            .Where(file => file.CreatedAt <= cutoff)
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new RetainedMediaFile(
                "vehicle_defect_videos",
                file.Id,
                file.StoragePath,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var workVideos = await db.VehicleWorkVideos
            .AsNoTracking()
            .Where(file => file.CreatedAt <= cutoff)
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new RetainedMediaFile(
                "vehicle_work_videos",
                file.Id,
                file.StoragePath,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return MergeByAge(
            [.. defectPhotos, .. workPhotos, .. defectVideos, .. workVideos],
            batchSize);
    }

    /// <summary>Возвращает самые старые медиафайлы независимо от даты.</summary>
    public async Task<IReadOnlyList<RetainedMediaFile>> GetOldestMediaAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var defectPhotos = await db.VehicleDefectPhotos
            .AsNoTracking()
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new RetainedMediaFile(
                "vehicle_defect_photos",
                file.Id,
                file.StoragePath,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var workPhotos = await db.VehicleWorkPhotos
            .AsNoTracking()
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new RetainedMediaFile(
                "vehicle_work_photos",
                file.Id,
                file.StoragePath,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var defectVideos = await db.VehicleDefectVideos
            .AsNoTracking()
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new RetainedMediaFile(
                "vehicle_defect_videos",
                file.Id,
                file.StoragePath,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var workVideos = await db.VehicleWorkVideos
            .AsNoTracking()
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new RetainedMediaFile(
                "vehicle_work_videos",
                file.Id,
                file.StoragePath,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return MergeByAge(
            [.. defectPhotos, .. workPhotos, .. defectVideos, .. workVideos],
            batchSize);
    }

    /// <summary>Возвращает суммарный размер всех учтённых медиафайлов.</summary>
    public async Task<long> GetTotalMediaSizeAsync(
        CancellationToken cancellationToken)
    {
        var defectPhotos = await db.VehicleDefectPhotos
            .SumAsync(file => file.Size, cancellationToken);
        var workPhotos = await db.VehicleWorkPhotos
            .SumAsync(file => file.Size, cancellationToken);
        var defectVideos = await db.VehicleDefectVideos
            .SumAsync(file => file.Size, cancellationToken);
        var workVideos = await db.VehicleWorkVideos
            .SumAsync(file => file.Size, cancellationToken);
        return defectPhotos + workPhotos + defectVideos + workVideos;
    }

    /// <summary>Удаляет запись медиафайла из базы данных.</summary>
    public async Task<bool> DeleteMediaAsync(
        RetainedMediaFile file,
        CancellationToken cancellationToken)
    {
        var deleted = file.TableName switch
        {
            "vehicle_defect_photos" => await db.VehicleDefectPhotos
                .Where(row =>
                    row.Id == file.Id &&
                    row.StoragePath == file.StoragePath)
                .ExecuteDeleteAsync(cancellationToken),
            "vehicle_work_photos" => await db.VehicleWorkPhotos
                .Where(row =>
                    row.Id == file.Id &&
                    row.StoragePath == file.StoragePath)
                .ExecuteDeleteAsync(cancellationToken),
            "vehicle_defect_videos" => await db.VehicleDefectVideos
                .Where(row =>
                    row.Id == file.Id &&
                    row.StoragePath == file.StoragePath)
                .ExecuteDeleteAsync(cancellationToken),
            "vehicle_work_videos" => await db.VehicleWorkVideos
                .Where(row =>
                    row.Id == file.Id &&
                    row.StoragePath == file.StoragePath)
                .ExecuteDeleteAsync(cancellationToken),
            _ => throw new ArgumentException(
                "Unexpected media table name.", nameof(file))
        };
        return deleted == 1;
    }

    /// <summary>Находит временные медиафайлы, готовые к переносу в постоянное хранилище.</summary>
    public async Task<IReadOnlyList<StagedMediaFile>> GetExpiredFilesAsync(
        int retentionDays,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var stagingPhotoPrefix = Path.GetFullPath(
            storageOptions.StagingPhotoDirectory) + Path.DirectorySeparatorChar;
        var stagingVideoPrefix = Path.GetFullPath(
            storageOptions.StagingVideoDirectory) + Path.DirectorySeparatorChar;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        var defectPhotos = await db.VehicleDefectPhotos
            .AsNoTracking()
            .Where(file =>
                file.StoragePath != null &&
                file.StoragePath.StartsWith(stagingPhotoPrefix) &&
                file.CreatedAt <= cutoff)
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new StagedMediaCandidate(
                "vehicle_defect_photos",
                file.Id,
                file.StoragePath!,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var workPhotos = await db.VehicleWorkPhotos
            .AsNoTracking()
            .Where(file =>
                file.StoragePath != null &&
                file.StoragePath.StartsWith(stagingPhotoPrefix) &&
                file.CreatedAt <= cutoff)
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new StagedMediaCandidate(
                "vehicle_work_photos",
                file.Id,
                file.StoragePath!,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var defectVideos = await db.VehicleDefectVideos
            .AsNoTracking()
            .Where(file =>
                file.StoragePath.StartsWith(stagingVideoPrefix) &&
                file.CreatedAt <= cutoff)
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new StagedMediaCandidate(
                "vehicle_defect_videos",
                file.Id,
                file.StoragePath,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var workVideos = await db.VehicleWorkVideos
            .AsNoTracking()
            .Where(file =>
                file.StoragePath.StartsWith(stagingVideoPrefix) &&
                file.CreatedAt <= cutoff)
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new StagedMediaCandidate(
                "vehicle_work_videos",
                file.Id,
                file.StoragePath,
                file.Size,
                file.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return defectPhotos
            .Concat(workPhotos)
            .Concat(defectVideos)
            .Concat(workVideos)
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .Select(file => new StagedMediaFile(
                file.TableName,
                file.Id,
                file.StoragePath,
                file.Size))
            .ToArray();
    }

    /// <summary>Обновляет путь медиафайла после его переноса.</summary>
    public async Task<bool> UpdateStoragePathAsync(
        StagedMediaFile file,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var updated = file.TableName switch
        {
            "vehicle_defect_photos" => await db.VehicleDefectPhotos
                    .Where(row =>
                        row.Id == file.Id &&
                        row.StoragePath == file.StoragePath)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            row => row.StoragePath,
                            destinationPath),
                        cancellationToken),
            "vehicle_work_photos" => await db.VehicleWorkPhotos
                    .Where(row =>
                        row.Id == file.Id &&
                        row.StoragePath == file.StoragePath)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            row => row.StoragePath,
                            destinationPath),
                        cancellationToken),
            "vehicle_defect_videos" => await db.VehicleDefectVideos
                    .Where(row =>
                        row.Id == file.Id &&
                        row.StoragePath == file.StoragePath)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            row => row.StoragePath,
                            destinationPath),
                        cancellationToken),
            "vehicle_work_videos" => await db.VehicleWorkVideos
                    .Where(row =>
                        row.Id == file.Id &&
                        row.StoragePath == file.StoragePath)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            row => row.StoragePath,
                            destinationPath),
                        cancellationToken),
            _ => throw new ArgumentException(
                "Unexpected media table name.", nameof(file))
        };
        return updated == 1;
    }

    /// <summary>Сортирует файлы из разных таблиц по возрасту и ограничивает выборку.</summary>
    private static IReadOnlyList<RetainedMediaFile> MergeByAge(
        IReadOnlyList<RetainedMediaFile> files,
        int batchSize) =>
        files
            .OrderBy(file => file.CreatedAt)
            .Take(batchSize)
            .ToArray();

    /// <summary>Данные кандидата на перенос из временного медиаархива.</summary>
    private sealed record StagedMediaCandidate(
        string TableName,
        Guid Id,
        string StoragePath,
        long Size,
        DateTimeOffset CreatedAt);
}
