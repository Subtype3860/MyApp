using MyApp.Application.Models;

namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным справочника профессий.</summary>
public interface IProfessionRepository
{
    /// <summary>Возвращает список профессий.</summary>
    Task<IReadOnlyList<Profession>> GetAllAsync(
        CancellationToken cancellationToken);

    /// <summary>Находит профессию по идентификатору.</summary>
    Task<Profession?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Проверяет наличие профессии.</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Проверяет, назначена ли профессия хотя бы одному пользователю.</summary>
    Task<bool> IsAssignedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Добавляет профессию в контекст хранения.</summary>
    Task AddAsync(Profession profession, CancellationToken cancellationToken);

    /// <summary>Помечает профессию для удаления.</summary>
    void Remove(Profession profession);

    /// <summary>Сохраняет накопленные изменения контекста базы данных.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
