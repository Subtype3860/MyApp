using System.Security.Claims;
using MyApp.Application.Abstractions;

namespace MyApp.Application.Security;

/// <summary>Идентификатор пользователя и метка сессии, извлечённые из токена.</summary>
public static class SessionIdentity
{
    /// <summary>Пытается прочитать идентификатор пользователя и метку сессии из набора утверждений.</summary>
    public static bool TryRead(ClaimsPrincipal? principal, out Guid userId, out Guid stamp)
    {
        userId = default;
        stamp = default;
        return Guid.TryParse(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal?.FindFirst("sub")?.Value, out userId) &&
            Guid.TryParse(principal?.FindFirst(UserAccess.SessionStampClaim)?.Value, out stamp) && stamp != Guid.Empty;
    }

    /// <summary>Проверяет актуальность метки сессии пользователя.</summary>
    public static Task<bool> IsCurrentAsync(ClaimsPrincipal? principal, IUserSessionStore store, CancellationToken cancellationToken) =>
        TryRead(principal, out var userId, out var stamp)
            ? store.IsCurrentAsync(userId, stamp, cancellationToken)
            : Task.FromResult(false);
}
