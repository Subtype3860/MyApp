using MyApp.Application.DTO;
using MyApp.Application.Common;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции импорта и экспорта табличных данных.</summary>
public interface ICsvFileService
{
    /// <summary>Проверяет импортируемый файл без сохранения изменений.</summary>
    Task<ServiceResult<CsvImportPreview>> PreviewAsync(string fileName, byte[] content, CancellationToken cancellationToken,
        string fileExtension = ".csv", string? sheetName = null);

    /// <summary>Возвращает данные табличного источника.</summary>
    Task<ServiceResult<byte[]>> GetAsync(
        string fileName,
        CancellationToken cancellationToken);

    /// <summary>Заменяет данные табличного источника содержимым проверенного импорта.</summary>
    Task<ServiceResult<CsvImportResult>> ReplaceAsync(
        string fileName,
        byte[] content,
        CancellationToken cancellationToken,
        string fileExtension = ".csv",
        string? sheetName = null);
}
