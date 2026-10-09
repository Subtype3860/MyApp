namespace MyApp.Application.Storage;

public sealed record MediaStorageOptions(
    string PhotoDirectory,
    string VideoDirectory,
    string StagingPhotoDirectory,
    string StagingVideoDirectory);
