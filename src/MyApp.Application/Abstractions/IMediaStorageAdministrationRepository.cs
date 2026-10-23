namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным настроек и переноса медиафайлов.</summary>
public interface IMediaStorageAdministrationRepository
{
    /// <summary>Возвращает срок хранения медиа во временных каталогах в днях.</summary>
    Task<int> GetRetentionDaysAsync(CancellationToken cancellationToken);

    /// <summary>Сохраняет срок хранения медиа во временных каталогах.</summary>
    Task SetRetentionDaysAsync(
        int retentionDays,
        CancellationToken cancellationToken);

    /// <summary>Находит временные медиафайлы, готовые к переносу в постоянное хранилище.</summary>
    Task<IReadOnlyList<StagedMediaFile>> GetExpiredFilesAsync(
        int retentionDays,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Обновляет путь медиафайла после его переноса.</summary>
    Task<bool> UpdateStoragePathAsync(
        StagedMediaFile file,
        string destinationPath,
        CancellationToken cancellationToken);
}

/// <summary>Временный медиафайл, подлежащий переносу в постоянное хранилище.</summary>
public sealed record StagedMediaFile(
    string TableName,
    Guid Id,
    string StoragePath,
    long Size);
