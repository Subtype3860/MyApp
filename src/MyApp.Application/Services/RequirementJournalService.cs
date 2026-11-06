using MyApp.Application.Abstractions;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции журнала требований.</summary>
public sealed class RequirementJournalService(
    IRequirementJournalRepository repository) : IRequirementJournalService
{
    /// <summary>Возвращает последние записи журнала требований с заданными фильтрами.</summary>
    public Task<IReadOnlyList<RequirementJournalEntry>> GetRecentAsync(
        CancellationToken cancellationToken) =>
        repository.GetRecentAsync(cancellationToken);

    /// <summary>Восстанавливает данные документа требования по записи журнала.</summary>
    public Task<ComponentDocumentRequest?> GetDocumentAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        repository.GetDocumentRequestAsync(id, cancellationToken);

    /// <summary>Удаляет требование.</summary>
    public Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        repository.DeleteAsync(id, cancellationToken);
}
