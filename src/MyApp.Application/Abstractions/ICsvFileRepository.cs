using MyApp.Application.DTO;
namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным импорта и экспорта табличных данных.</summary>
public interface ICsvFileRepository
{
    /// <summary>Проверяет импортируемые данные без изменения сохранённого состояния.</summary>
    Task<CsvImportPreview> PreviewAsync(string fileName, byte[] content, CancellationToken cancellationToken);

    /// <summary>Возвращает данные табличного источника.</summary>
    Task<byte[]> GetAsync(
        string fileName,
        CancellationToken cancellationToken);

    /// <summary>Заменяет данные табличного источника содержимым проверенного импорта.</summary>
    Task<CsvImportResult> ReplaceAsync(
        string fileName,
        byte[] content,
        CancellationToken cancellationToken);
}
