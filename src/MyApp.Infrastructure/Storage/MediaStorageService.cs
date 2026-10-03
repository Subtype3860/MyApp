using MyApp.Application.Abstractions;
using MyApp.Application.Storage;

namespace MyApp.Infrastructure.Storage;

public sealed class MediaStorageService(MediaStorageOptions options) : IMediaStorageService
{
    public Task<string> SavePhotoAsync(
        byte[] content,
        string fileName,
        CancellationToken cancellationToken) =>
        SaveAsync(options.StagingPhotoDirectory, content, fileName, cancellationToken);

    public Task<string> SaveVideoAsync(
        byte[] content,
        string fileName,
        CancellationToken cancellationToken) =>
        SaveAsync(options.StagingVideoDirectory, content, fileName, cancellationToken);

    private static async Task<string> SaveAsync(
        string directory,
        byte[] content,
        string fileName,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);

        // A GUID-based name avoids collisions and strips any unsafe characters
        // from the original file name while keeping its extension.
        var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var filePath = Path.Combine(directory, storedFileName);

        await File.WriteAllBytesAsync(filePath, content, cancellationToken);
        return filePath;
    }
}
