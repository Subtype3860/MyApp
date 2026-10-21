using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.Models;
using MyApp.Infrastructure.Db;

namespace MyApp.Infrastructure.Repositories;

/// <summary>Доступ к данным пользователей.</summary>
public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    /// <summary>Находит пользователя по данным для входа.</summary>
    public Task<User?> FindByLoginAsync(
        string login,
        CancellationToken cancellationToken) =>
        db.Users.Include(user => user.Profession).FirstOrDefaultAsync(
            user => user.UserName == login || user.Email == login,
            cancellationToken);

    /// <summary>Возвращает список пользователей.</summary>
    public async Task<IReadOnlyList<User>> GetAllAsync(
        CancellationToken cancellationToken) =>
        await db.Users
            .AsNoTracking()
            .Include(user => user.Profession)
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .ToArrayAsync(cancellationToken);

    /// <summary>Находит пользователя по идентификатору.</summary>
    public Task<User?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        db.Users
            .Include(user => user.Profession)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    /// <summary>Проверяет, занят ли логин пользователя.</summary>
    public Task<bool> UserNameExistsAsync(
        string userName,
        Guid? excludingId,
        CancellationToken cancellationToken) =>
        db.Users.AnyAsync(
            user =>
                (!excludingId.HasValue || user.Id != excludingId.Value) &&
                user.UserName.ToLower() == userName.ToLower(),
            cancellationToken);

    /// <summary>Добавляет пользователя в контекст хранения.</summary>
    public async Task AddAsync(
        User user,
        CancellationToken cancellationToken) =>
        await db.Users.AddAsync(user, cancellationToken);

    /// <summary>Сохраняет накопленные изменения контекста базы данных.</summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await db.SaveChangesAsync(cancellationToken);
}
