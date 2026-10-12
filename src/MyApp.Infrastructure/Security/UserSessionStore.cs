using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Infrastructure.Db;

namespace MyApp.Infrastructure.Security;

public sealed class UserSessionStore(AppDbContext db) : IUserSessionStore
{
    public Task<bool> IsCurrentAsync(Guid userId, Guid stamp, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.SessionStamp == stamp, cancellationToken);

    public async Task RevokeAsync(Guid userId, Guid stamp, CancellationToken cancellationToken)
    {
        // An old logout must not replace the stamp of a newer session generation.
        await db.Users.Where(user => user.Id == userId && user.SessionStamp == stamp)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.SessionStamp, Guid.NewGuid()), cancellationToken);
    }
}
