using MyApp.Application.Storage;

namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным настроек и переноса медиафайлов.</summary>
public interface IMediaStorageAdministrationRepository
{
    /// <summary>Возвращает настройки временного и постоянного хранения медиа.</summary>
    Task<MediaStorageSettings> GetSettingsAsync(CancellationToken cancellationToken);

    /// <summary>Сохраняет настройки временного и постоянного хранения медиа.</summary>
    Task SetSettingsAsync(
        MediaStorageSettings settings,
        CancellationToken cancellationToken);

    /// <summary>Возвращает медиафайлы, созданные раньше указанного момента.</summary>
    Task<IReadOnlyList<RetainedMediaFile>> GetExpiredMediaAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Возвращает самые старые медиафайлы независимо от даты.</summary>
    Task<IReadOnlyList<RetainedMediaFile>> GetOldestMediaAsync(
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Возвращает суммарный размер всех учтённых медиафайлов.</summary>
    Task<long> GetTotalMediaSizeAsync(CancellationToken cancellationToken);

    /// <summary>Удаляет запись медиафайла из базы данных.</summary>
    Task<bool> DeleteMediaAsync(
        RetainedMediaFile file,
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

/// <summary>Медиафайл, участвующий в очистке по сроку хранения и объёму.</summary>
public sealed record RetainedMediaFile(
    string TableName,
    Guid Id,
    string? StoragePath,
    long Size,
    DateTimeOffset CreatedAt);
