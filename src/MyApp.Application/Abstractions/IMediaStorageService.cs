namespace MyApp.Application.Abstractions;

/// <summary>
/// Saves uploaded photo and video files to their configured storage directories,
/// renaming each file to a new GUID-based name to avoid collisions.
/// </summary>
public interface IMediaStorageService
{
    /// <summary>
    /// Saves a photo and returns the full path to the stored file, including its new name.
    /// </summary>
    Task<string> SavePhotoAsync(
        byte[] content,
        string fileName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves a video and returns the full path to the stored file, including its new name.
    /// </summary>
    Task<string> SaveVideoAsync(
        byte[] content,
        string fileName,
        CancellationToken cancellationToken);
}
