namespace MyApp.Application.Abstractions;

/// <summary>Контракт проверки и отзыва пользовательских сессий.</summary>
public interface IUserSessionStore
{
    /// <summary>Проверяет актуальность метки сессии пользователя.</summary>
    Task<bool> IsCurrentAsync(Guid userId, Guid stamp, CancellationToken cancellationToken);
    /// <summary>Отзывает ранее выданные сессии пользователя сменой метки сессии.</summary>
    Task RevokeAsync(Guid userId, Guid stamp, CancellationToken cancellationToken);
}
