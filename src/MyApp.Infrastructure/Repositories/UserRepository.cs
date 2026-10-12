using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Db;

namespace MyApp.Infrastructure.Repositories;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> FindByLoginAsync(
        string login,
        CancellationToken cancellationToken) =>
        db.Users.Include(user => user.Profession).FirstOrDefaultAsync(
            user => user.UserName == login || user.Email == login,
            cancellationToken);

    public async Task<IReadOnlyList<User>> GetAllAsync(
        CancellationToken cancellationToken) =>
        await db.Users
            .AsNoTracking()
            .Include(user => user.Profession)
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .ToArrayAsync(cancellationToken);

    public Task<User?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        db.Users
            .Include(user => user.Profession)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<bool> UserNameExistsAsync(
        string userName,
        Guid? excludingId,
        CancellationToken cancellationToken) =>
        db.Users.AnyAsync(
            user =>
                (!excludingId.HasValue || user.Id != excludingId.Value) &&
                user.UserName.ToLower() == userName.ToLower(),
            cancellationToken);

    public async Task AddAsync(
        User user,
        CancellationToken cancellationToken) =>
        await db.Users.AddAsync(user, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await db.SaveChangesAsync(cancellationToken);
}
