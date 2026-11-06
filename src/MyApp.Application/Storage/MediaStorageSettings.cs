namespace MyApp.Application.Storage;

/// <summary>Настройки хранения медиафайлов.</summary>
public sealed record MediaStorageSettings(
    int StagingRetentionDays,
    int PermanentRetentionDays,
    long MaxStorageBytes);
