using MyApp.Application.Abstractions;
using MyApp.Application.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MyApp.API.BackgroundServices;

/// <summary>
/// Удаляет медиафайлы, срок хранения которых истёк, и освобождает место,
/// если суммарный объём превышает настроенный лимит.
/// </summary>
public sealed class MediaRetentionService(
    IServiceScopeFactory scopeFactory,
    MediaStorageOptions options,
    ILogger<MediaRetentionService> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(6);

    /// <summary>Выполняет периодическую очистку медиа до отмены работы приложения.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var timer = new PeriodicTimer(ScanInterval);
        do
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to clean up media storage; will retry on the next cycle.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Удаляет медиа по сроку хранения, затем по превышению объёма.</summary>
    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IMediaStorageAdministrationRepository>();
        var settings = await repository.GetSettingsAsync(cancellationToken);

        var expired = await DeleteExpiredAsync(
            repository,
            settings.PermanentRetentionDays,
            cancellationToken);
        var overLimit = await DeleteOverLimitAsync(
            repository,
            settings.MaxStorageBytes,
            cancellationToken);

        if (expired > 0 || overLimit > 0)
        {
            logger.LogInformation(
                "Media retention removed {ExpiredCount} expired and {OverLimitCount} over-limit files.",
                expired,
                overLimit);
        }
    }

    /// <summary>Удаляет файлы старше настроенного срока хранения.</summary>
    private async Task<int> DeleteExpiredAsync(
        IMediaStorageAdministrationRepository repository,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var skipped = new HashSet<Guid>();
        var deletedTotal = 0;
        while (true)
        {
            var batch = await repository.GetExpiredMediaAsync(
                cutoff, BatchSize, cancellationToken);
            var candidates = batch
                .Where(file => !skipped.Contains(file.Id))
                .ToArray();
            if (candidates.Length == 0)
            {
                return deletedTotal;
            }

            foreach (var file in candidates)
            {
                if (await TryDeleteAsync(repository, file, cancellationToken))
                {
                    deletedTotal++;
                }
                else
                {
                    skipped.Add(file.Id);
                }
            }

            if (batch.Count < BatchSize)
            {
                return deletedTotal;
            }
        }
    }

    /// <summary>Удаляет самые старые файлы, пока объём не опустится до лимита.</summary>
    private async Task<int> DeleteOverLimitAsync(
        IMediaStorageAdministrationRepository repository,
        long maxStorageBytes,
        CancellationToken cancellationToken)
    {
        var total = await repository.GetTotalMediaSizeAsync(cancellationToken);
        if (total <= maxStorageBytes)
        {
            return 0;
        }

        var skipped = new HashSet<Guid>();
        var deletedTotal = 0;
        while (total > maxStorageBytes)
        {
            var batch = await repository.GetOldestMediaAsync(
                BatchSize, cancellationToken);
            var candidates = batch
                .Where(file => !skipped.Contains(file.Id))
                .ToArray();
            if (candidates.Length == 0)
            {
                logger.LogWarning(
                    "Media storage uses {TotalBytes} bytes and could not be reduced to {MaxBytes} bytes.",
                    total,
                    maxStorageBytes);
                return deletedTotal;
            }

            var deletedInBatch = 0;
            foreach (var file in candidates)
            {
                if (await TryDeleteAsync(repository, file, cancellationToken))
                {
                    deletedInBatch++;
                    deletedTotal++;
                }
                else
                {
                    skipped.Add(file.Id);
                }
            }

            if (deletedInBatch == 0)
            {
                return deletedTotal;
            }

            total = await repository.GetTotalMediaSizeAsync(cancellationToken);
        }

        return deletedTotal;
    }

    /// <summary>Удаляет файл с диска и связанную запись в базе данных.</summary>
    private async Task<bool> TryDeleteAsync(
        IMediaStorageAdministrationRepository repository,
        RetainedMediaFile file,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file.StoragePath) &&
            !TryDeleteFile(file))
        {
            return false;
        }

        var deleted = await repository.DeleteMediaAsync(file, cancellationToken);
        if (!deleted)
        {
            logger.LogWarning(
                "Media row {MediaId} in {TableName} changed before deletion; skipping.",
                file.Id,
                file.TableName);
        }

        return deleted;
    }

    /// <summary>Удаляет физический файл, если он находится в разрешённом каталоге.</summary>
    private bool TryDeleteFile(RetainedMediaFile file)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(file.StoragePath!);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            logger.LogError(
                exception,
                "Media file {MediaId} has an invalid path and was skipped.",
                file.Id);
            return false;
        }

        if (!IsWithinConfiguredDirectory(fullPath))
        {
            logger.LogError(
                "Media file {MediaId} points outside the configured storage directories and was skipped: {StoragePath}.",
                file.Id,
                fullPath);
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return true;
        }

        try
        {
            File.Delete(fullPath);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Failed to delete media file {MediaId} at {StoragePath}.",
                file.Id,
                fullPath);
            return false;
        }
    }

    /// <summary>Проверяет, что файл расположен внутри одного из настроенных каталогов.</summary>
    private bool IsWithinConfiguredDirectory(string filePath)
    {
        foreach (var directory in new[]
                 {
                     options.PhotoDirectory,
                     options.VideoDirectory,
                     options.StagingPhotoDirectory,
                     options.StagingVideoDirectory
                 })
        {
            try
            {
                var prefix = Path.GetFullPath(directory) +
                    Path.DirectorySeparatorChar;
                if (filePath.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                logger.LogError(
                    exception,
                    "Configured media directory {Directory} is invalid.",
                    directory);
            }
        }

        return false;
    }
}
