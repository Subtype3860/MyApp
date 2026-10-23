using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.Models;
using MyApp.Infrastructure.Db;

namespace MyApp.Infrastructure.Repositories;

/// <summary>Доступ к данным справочника профессий.</summary>
public sealed class ProfessionRepository(AppDbContext db) : IProfessionRepository
{
    /// <summary>Возвращает список профессий.</summary>
    public async Task<IReadOnlyList<Profession>> GetAllAsync(
        CancellationToken cancellationToken) =>
        await db.Professions
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ToArrayAsync(cancellationToken);

    /// <summary>Находит профессию по идентификатору.</summary>
    public Task<Profession?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        db.Professions.FindAsync([id], cancellationToken).AsTask();

    /// <summary>Проверяет наличие профессии.</summary>
    public Task<bool> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        db.Professions.AnyAsync(
            profession => profession.Id == id,
            cancellationToken);

    /// <summary>Проверяет, назначена ли профессия хотя бы одному пользователю.</summary>
    public Task<bool> IsAssignedAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        db.Users.AnyAsync(user => user.PositionId == id, cancellationToken);

    /// <summary>Добавляет профессию в контекст хранения.</summary>
    public async Task AddAsync(
        Profession profession,
        CancellationToken cancellationToken) =>
        await db.Professions.AddAsync(profession, cancellationToken);

    /// <summary>Помечает профессию для удаления.</summary>
    public void Remove(Profession profession) => db.Professions.Remove(profession);

    /// <summary>Сохраняет накопленные изменения контекста базы данных.</summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await db.SaveChangesAsync(cancellationToken);
}
