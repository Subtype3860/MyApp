using MyApp.Application.DTO;
using MyApp.Application.Common;

namespace MyApp.Application.Services;

public interface ICsvFileService
{
    Task<ServiceResult<CsvImportPreview>> PreviewAsync(string fileName, byte[] content, CancellationToken cancellationToken,
        string fileExtension = ".csv", string? sheetName = null);

    Task<ServiceResult<byte[]>> GetAsync(
        string fileName,
        CancellationToken cancellationToken);

    Task<ServiceResult<CsvImportResult>> ReplaceAsync(
        string fileName,
        byte[] content,
        CancellationToken cancellationToken,
        string fileExtension = ".csv",
        string? sheetName = null);
}
