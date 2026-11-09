using System.Collections.Concurrent;

namespace MyApp.Infrastructure.Sany;

/// <summary>Короткоживущие подключения к порталу SANY: пароль хранится только до входа.</summary>
public sealed class SanySessionStore
{
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, SanyConnection> sessions = new();

    /// <summary>Создаёт новое подключение и удаляет просроченные.</summary>
    public SanyConnection Create(string account, string password)
    {
        Cleanup();
        var connection = new SanyConnection(Guid.NewGuid().ToString("N"))
        {
            Account = account.Trim(),
            Password = password,
            ExpiresAt = DateTimeOffset.UtcNow.Add(PendingLifetime),
        };
        sessions[connection.Id] = connection;
        return connection;
    }

    /// <summary>Возвращает подключение, если оно ещё живо.</summary>
    public SanyConnection? Find(string id)
    {
        if (!sessions.TryGetValue(id, out var connection)) return null;
        if (connection.ExpiresAt > DateTimeOffset.UtcNow) return connection;
        Remove(id);
        return null;
    }

    /// <summary>Удаляет подключение и стирает его секреты.</summary>
    public void Remove(string id)
    {
        if (!sessions.TryRemove(id, out var connection)) return;
        connection.Password = null;
        connection.Token = null;
        connection.Dispose();
    }

    /// <summary>Удаляет все просроченные подключения.</summary>
    public void Cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, connection) in sessions)
        {
            if (connection.ExpiresAt > now) continue;
            Remove(id);
        }
    }
}
