namespace MyApp.Application.Storage;

/// <summary>Пути временного и постоянного хранения фотографий и видео.</summary>
public sealed record MediaStorageOptions(
    string PhotoDirectory,
    string VideoDirectory,
    string StagingPhotoDirectory,
    string StagingVideoDirectory);
