using MyApp.Application.Abstractions;
using MyApp.Application.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MyApp.API.BackgroundServices;

public sealed class StagedMediaTransferService(
    IServiceScopeFactory scopeFactory,
    MediaStorageOptions options,
    ILogger<StagedMediaTransferService> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(25);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        do
        {
            try
            {
                await TransferExpiredFilesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to scan staged media files; will retry on the next cycle.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TransferExpiredFilesAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IMediaStorageAdministrationRepository>();
        var retentionDays = await repository.GetRetentionDaysAsync(cancellationToken);
        while (true)
        {
            var files = await repository.GetExpiredFilesAsync(
                retentionDays, BatchSize, cancellationToken);
            foreach (var file in files)
            {
                try
                {
                    await TransferFileAsync(
                        repository,
                        file,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Failed to transfer staged media file {MediaId} from {StoragePath}.",
                        file.Id,
                        file.StoragePath);
                }
            }

            if (files.Count < BatchSize)
            {
                return;
            }
        }
    }

    private async Task TransferFileAsync(
        IMediaStorageAdministrationRepository repository,
        StagedMediaFile file,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(file.StoragePath);
        var destinationDirectory = GetDestinationDirectory(sourcePath);
        var destinationPath = Path.Combine(
            destinationDirectory, Path.GetFileName(sourcePath));
        var temporaryDestination = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(sourcePath)}.{Guid.NewGuid():N}.moving");

        Directory.CreateDirectory(destinationDirectory);
        try
        {
            await using (var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true))
            await using (var destination = new FileStream(
                temporaryDestination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                if (destination.Length != file.Size)
                {
                    throw new IOException(
                        $"Transferred media size mismatch for {file.Id}.");
                }
            }

            File.Move(temporaryDestination, destinationPath, overwrite: true);
            var updated = await repository.UpdateStoragePathAsync(
                file, destinationPath, cancellationToken);
            if (!updated)
            {
                File.Delete(destinationPath);
                return;
            }

            File.Delete(sourcePath);
            logger.LogInformation(
                "Transferred staged media file {MediaId} to permanent storage.",
                file.Id);
        }
        finally
        {
            if (File.Exists(temporaryDestination))
            {
                File.Delete(temporaryDestination);
            }
        }
    }

    private string GetDestinationDirectory(string sourcePath)
    {
        if (IsWithin(sourcePath, options.StagingPhotoDirectory))
        {
            return options.PhotoDirectory;
        }
        if (IsWithin(sourcePath, options.StagingVideoDirectory))
        {
            return options.VideoDirectory;
        }

        throw new InvalidOperationException(
            $"Staged media path '{sourcePath}' is outside configured staging directories.");
    }

    private static bool IsWithin(string filePath, string directory)
    {
        var normalizedDirectory = Path.GetFullPath(directory) +
            Path.DirectorySeparatorChar;
        return filePath.StartsWith(normalizedDirectory, StringComparison.Ordinal);
    }
}
