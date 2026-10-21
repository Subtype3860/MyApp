using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции журнала требований.</summary>
public interface IRequirementJournalService
{
    /// <summary>Возвращает последние записи журнала требований с заданными фильтрами.</summary>
    Task<IReadOnlyList<RequirementJournalEntry>> GetRecentAsync(
        CancellationToken cancellationToken);

    /// <summary>Восстанавливает данные документа требования по записи журнала.</summary>
    Task<ComponentDocumentRequest?> GetDocumentAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Удаляет требование.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
