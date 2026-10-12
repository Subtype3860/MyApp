namespace MyApp.Application.Abstractions;

public interface IUserSessionStore
{
    Task<bool> IsCurrentAsync(Guid userId, Guid stamp, CancellationToken cancellationToken);
    Task RevokeAsync(Guid userId, Guid stamp, CancellationToken cancellationToken);
}
