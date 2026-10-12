using MyApp.Application.DTO;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;

namespace MyApp.Application.Services;

public sealed class CsvFileService(ICsvFileRepository repository) : ICsvFileService
{
    private static readonly HashSet<string> AllowedFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "c.csv",
            "o.csv",
            "p.csv"
        };

    public async Task<ServiceResult<byte[]>> GetAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        var normalizedFileName = NormalizeFileName(fileName);
        if (normalizedFileName is null)
        {
            return ServiceResult<byte[]>.NotFound();
        }

        return ServiceResult<byte[]>.Success(
            await repository.GetAsync(normalizedFileName, cancellationToken));
    }

    public async Task<ServiceResult<CsvImportResult>> ReplaceAsync(
        string fileName,
        byte[] content,
        CancellationToken cancellationToken,
        string fileExtension = ".csv",
        string? sheetName = null)
    {
        var normalizedFileName = NormalizeFileName(fileName);
        if (normalizedFileName is null)
        {
            return ServiceResult<CsvImportResult>.NotFound();
        }

        if (content.Length == 0)
        {
            return ServiceResult<CsvImportResult>.Validation(
                new Dictionary<string, string[]>
                {
                    ["file"] = ["Выберите непустой CSV-файл."]
                });
        }

        if (fileExtension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            content = ImportWorkbook.ToCsv(content, sheetName, cancellationToken);
        else if (!fileExtension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            throw new CsvImportValidationException("Поддерживаются CSV и XLSX.");
        var result = await repository.ReplaceAsync(
            normalizedFileName,
            content,
            cancellationToken);
        return ServiceResult<CsvImportResult>.Success(result);
    }

    public async Task<ServiceResult<CsvImportPreview>> PreviewAsync(string fileName, byte[] content, CancellationToken cancellationToken,
        string fileExtension = ".csv", string? sheetName = null)
    {
        var source = NormalizeFileName(fileName);
        if (source is null) return ServiceResult<CsvImportPreview>.NotFound();
        if (fileExtension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            content = ImportWorkbook.ToCsv(content, sheetName, cancellationToken);
        else if (!fileExtension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            throw new CsvImportValidationException("Поддерживаются CSV и XLSX.");
        return ServiceResult<CsvImportPreview>.Success(await repository.PreviewAsync(source, content, cancellationToken));
    }

    private static string? NormalizeFileName(string fileName)
    {
        var normalized = fileName.Trim().ToLowerInvariant();
        normalized = normalized switch { "warehouse" => "o.csv", "mechanics" => "c.csv", "workers" => "p.csv", _ => normalized };
        return AllowedFileNames.Contains(normalized) ? normalized : null;
    }
}
