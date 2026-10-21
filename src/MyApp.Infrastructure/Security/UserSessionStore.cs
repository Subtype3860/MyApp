using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Infrastructure.Db;

namespace MyApp.Infrastructure.Security;

/// <summary>Проверка и отзыв сессий по метке, сохранённой у пользователя.</summary>
public sealed class UserSessionStore(AppDbContext db) : IUserSessionStore
{
    /// <summary>Проверяет актуальность метки сессии пользователя.</summary>
    public Task<bool> IsCurrentAsync(Guid userId, Guid stamp, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.SessionStamp == stamp, cancellationToken);

    /// <summary>Отзывает ранее выданные сессии пользователя сменой метки сессии.</summary>
    public async Task RevokeAsync(Guid userId, Guid stamp, CancellationToken cancellationToken)
    {
        // An old logout must not replace the stamp of a newer session generation.
        await db.Users.Where(user => user.Id == userId && user.SessionStamp == stamp)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.SessionStamp, Guid.NewGuid()), cancellationToken);
    }
}
