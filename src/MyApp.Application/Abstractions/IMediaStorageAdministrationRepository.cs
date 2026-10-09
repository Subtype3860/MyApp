namespace MyApp.Application.Abstractions;

public interface IMediaStorageAdministrationRepository
{
    Task<int> GetRetentionDaysAsync(CancellationToken cancellationToken);

    Task SetRetentionDaysAsync(
        int retentionDays,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StagedMediaFile>> GetExpiredFilesAsync(
        int retentionDays,
        int batchSize,
        CancellationToken cancellationToken);

    Task<bool> UpdateStoragePathAsync(
        StagedMediaFile file,
        string destinationPath,
        CancellationToken cancellationToken);
}

public sealed record StagedMediaFile(
    string TableName,
    Guid Id,
    string StoragePath,
    long Size);
