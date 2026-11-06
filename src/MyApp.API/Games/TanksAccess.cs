using System.Collections.Concurrent;
using System.Security.Cryptography;
using MyApp.Application.Abstractions;
using MyApp.Application.Security;

namespace MyApp.API.Games;

public sealed record TankIdentity(Guid UserId, Guid Stamp, string Name, DateTimeOffset ExpiresAt);

/// <summary>Always reads live permissions; a stale JWT alone never grants game access.</summary>
public interface ITanksAccess
{
    Task<TankIdentity?> FindAsync(Guid id, Guid stamp, DateTimeOffset expiresAt, CancellationToken ct);
}
public sealed class TanksAccess(IUserRepository users, IUserSessionStore sessions) : ITanksAccess
{
    public async Task<TankIdentity?> FindAsync(Guid id, Guid stamp, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (expiresAt <= DateTimeOffset.UtcNow || !await sessions.IsCurrentAsync(id, stamp, ct)) return null;
        var user = await users.FindByIdAsync(id, ct);
        return user is not null && UserAccess.EffectivePermissions(user).Contains(Permissions.TanksPlay, StringComparer.OrdinalIgnoreCase)
            ? new(id, stamp, user.UserName.Length > 50 ? user.UserName[..50] : user.UserName, expiresAt)
            : null;
    }
}

/// <summary>Short-lived one-use connection tickets. JWTs never appear in WebSocket URLs.</summary>
public sealed class TanksTickets(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, (TankIdentity Identity, DateTimeOffset Deadline)> tickets = new();
    public string Issue(TankIdentity identity)
    {
        var now = clock.GetUtcNow();
        foreach (var item in tickets.Where(x => x.Value.Deadline <= now)) tickets.TryRemove(item.Key, out _);
        if (tickets.Count >= 512) throw new InvalidOperationException("Too many pending game connections.");
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        tickets[ticket] = (identity, now.AddSeconds(20));
        return ticket;
    }
    public TankIdentity? Take(string? ticket) => ticket is { Length: 64 } && tickets.TryRemove(ticket, out var entry)
        && entry.Deadline > clock.GetUtcNow() && entry.Identity.ExpiresAt > clock.GetUtcNow() ? entry.Identity : null;
}
