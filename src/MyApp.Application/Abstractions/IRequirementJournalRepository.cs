using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным журнала требований.</summary>
public interface IRequirementJournalRepository
{
    /// <summary>Сохраняет требование.</summary>
    Task SaveAsync(
        Guid userId,
        string authorName,
        string issuerName,
        ComponentDocumentRequest request,
        CancellationToken cancellationToken);

    /// <summary>Возвращает последние записи журнала требований с заданными фильтрами.</summary>
    Task<IReadOnlyList<RequirementJournalEntry>> GetRecentAsync(
        CancellationToken cancellationToken);

    /// <summary>Восстанавливает запрос формирования документа по сохранённому требованию.</summary>
    Task<ComponentDocumentRequest?> GetDocumentRequestAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Удаляет требование.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
