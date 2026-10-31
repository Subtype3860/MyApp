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

    /// <summary>Возвращает срок хранения медиа во временных каталогах в днях.</summary>
    public async Task<int> GetRetentionDaysAsync(
        CancellationToken cancellationToken) =>
        await db.MediaStorageSettings
            .Where(settings => settings.Id == SettingsId)
            .Select(settings => (int?)settings.RetentionDays)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException(
            "Media storage retention settings are missing.");

    /// <summary>Сохраняет срок хранения медиа во временных каталогах.</summary>
    public async Task SetRetentionDaysAsync(
        int retentionDays,
        CancellationToken cancellationToken)
    {
        var updated = await db.MediaStorageSettings
            .Where(settings => settings.Id == SettingsId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(settings => settings.RetentionDays, retentionDays)
                    .SetProperty(settings => settings.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException(
                "Media storage retention settings are missing.");
        }
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

    /// <summary>Данные кандидата на перенос из временного медиаархива.</summary>
    private sealed record StagedMediaCandidate(
        string TableName,
        Guid Id,
        string StoragePath,
        long Size,
        DateTimeOffset CreatedAt);
}
