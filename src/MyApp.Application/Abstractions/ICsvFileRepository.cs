using MyApp.Application.DTO;
namespace MyApp.Application.Abstractions;

public interface ICsvFileRepository
{
    Task<CsvImportPreview> PreviewAsync(string fileName, byte[] content, CancellationToken cancellationToken);

    Task<byte[]> GetAsync(
        string fileName,
        CancellationToken cancellationToken);

    Task<CsvImportResult> ReplaceAsync(
        string fileName,
        byte[] content,
        CancellationToken cancellationToken);
}
