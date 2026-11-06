using MyApp.Application.Models;

namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным пользователей.</summary>
public interface IUserRepository
{
    /// <summary>Находит пользователя по данным для входа.</summary>
    Task<User?> FindByLoginAsync(
        string login,
        CancellationToken cancellationToken);

    /// <summary>Возвращает список пользователей.</summary>
    Task<IReadOnlyList<User>> GetAllAsync(
        CancellationToken cancellationToken);

    /// <summary>Находит пользователя по идентификатору.</summary>
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Проверяет, занят ли логин пользователя.</summary>
    Task<bool> UserNameExistsAsync(
        string userName,
        Guid? excludingId,
        CancellationToken cancellationToken);

    /// <summary>Добавляет пользователя в контекст хранения.</summary>
    Task AddAsync(User user, CancellationToken cancellationToken);

    /// <summary>Сохраняет накопленные изменения контекста базы данных.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
