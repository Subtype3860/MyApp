using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Repositories;

public sealed class MaintenanceTemplateRepository(
    AppDbContext db) : IMaintenanceTemplateRepository
{
    private const int SortLockNamespace = 4386200;

    public async Task<IReadOnlyList<MaintenanceEquipmentResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var equipment = await db.MaintenanceEquipment
            .AsNoTracking()
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Name)
            .ToArrayAsync(cancellationToken);
        var equipmentIds = equipment.Select(row => row.Id).ToArray();
        var intervals = await db.MaintenanceIntervals
            .AsNoTracking()
            .Where(row => equipmentIds.Contains(row.EquipmentId))
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Name)
            .ToArrayAsync(cancellationToken);
        var intervalIds = intervals.Select(row => row.Id).ToArray();
        var items = await db.MaintenanceIntervalItems
            .AsNoTracking()
            .Where(row => intervalIds.Contains(row.IntervalId))
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.MaterialName)
            .ToArrayAsync(cancellationToken);
        var materialNames = items
            .Select(item => item.MaterialName.Trim())
            .Distinct()
            .ToArray();
        var stocks = materialNames.Length == 0
            ? []
            : await db.FullStocks
                .AsNoTracking()
                .Where(stock => materialNames.Contains(stock.Name.Trim()))
                .Select(stock => new {
                    Name = stock.Name.Trim(),
                    stock.Unit,
                    stock.Quantity
                })
                .ToArrayAsync(cancellationToken);
        var stockByName = stocks
            .GroupBy(stock => stock.Name)
            .ToDictionary(group => group.Key, group => group.First());
        var itemsByInterval = items
            .GroupBy(item => item.IntervalId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var intervalsByEquipment = intervals
            .GroupBy(interval => interval.EquipmentId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return equipment
            .Select(row => new MaintenanceEquipmentResponse(
                row.Id,
                row.Name,
                intervalsByEquipment.GetValueOrDefault(row.Id, [])
                    .Select(interval => new MaintenanceIntervalResponse(
                        interval.Id,
                        interval.Name,
                        itemsByInterval.GetValueOrDefault(interval.Id, [])
                            .Select(item => {
                                stockByName.TryGetValue(
                                    item.MaterialName.Trim(),
                                    out var stock);
                                return new MaintenanceItemResponse(
                                    item.Id,
                                    item.MaterialName,
                                    stock?.Unit ?? string.Empty,
                                    item.Quantity,
                                    ParseQuantity(stock?.Quantity, item.MaterialName));
                            })
                            .ToArray()))
                    .ToArray()))
            .ToArray();
    }

    public Task<bool> EquipmentExistsAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        db.MaintenanceEquipment.AnyAsync(
            equipment => equipment.Id == id,
            cancellationToken);

    public Task<bool> IntervalExistsAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        db.MaintenanceIntervals.AnyAsync(
            interval => interval.Id == id,
            cancellationToken);

    public Task<bool> EquipmentNameExistsAsync(
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToLower();
        return db.MaintenanceEquipment.AnyAsync(
            equipment =>
                equipment.Name.Trim().ToLower() == normalizedName &&
                (!exceptId.HasValue || equipment.Id != exceptId.Value),
            cancellationToken);
    }

    public Task<bool> IntervalNameExistsAsync(
        Guid equipmentId,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToLower();
        return db.MaintenanceIntervals.AnyAsync(
            interval =>
                interval.EquipmentId == equipmentId &&
                interval.Name.Trim().ToLower() == normalizedName &&
                (!exceptId.HasValue || interval.Id != exceptId.Value),
            cancellationToken);
    }

    public Task<bool> MaterialExistsAsync(
        string materialName,
        CancellationToken cancellationToken)
    {
        var normalizedName = materialName.Trim();
        return db.FullStocks.AnyAsync(
            stock => stock.Name.Trim() == normalizedName,
            cancellationToken);
    }

    public Task<bool> ItemExistsAsync(
        Guid intervalId,
        string materialName,
        CancellationToken cancellationToken)
    {
        var normalizedName = materialName.Trim();
        return db.MaintenanceIntervalItems.AnyAsync(
            item =>
                item.IntervalId == intervalId &&
                item.MaterialName.Trim() == normalizedName,
            cancellationToken);
    }

    public Task<Guid> CreateEquipmentAsync(
        string name,
        CancellationToken cancellationToken) =>
        CreateOrderedAsync<MaintenanceEquipmentRecord>(
            db.MaintenanceEquipment.Select(equipment => (int?)equipment.SortOrder),
            "equipment",
            order => new MaintenanceEquipmentRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                SortOrder = order
            },
            equipment => equipment.Id,
            cancellationToken);

    public Task<Guid> CreateIntervalAsync(
        Guid equipmentId,
        string name,
        CancellationToken cancellationToken) =>
        CreateOrderedAsync<MaintenanceIntervalRecord>(
            db.MaintenanceIntervals
                .Where(interval => interval.EquipmentId == equipmentId)
                .Select(interval => (int?)interval.SortOrder),
            "interval/" + equipmentId.ToString("N"),
            order => new MaintenanceIntervalRecord
            {
                Id = Guid.NewGuid(),
                EquipmentId = equipmentId,
                Name = name,
                SortOrder = order
            },
            interval => interval.Id,
            cancellationToken);

    public Task<Guid> AddItemAsync(
        Guid intervalId,
        string materialName,
        decimal quantity,
        CancellationToken cancellationToken) =>
        CreateOrderedAsync<MaintenanceIntervalItemRecord>(
            db.MaintenanceIntervalItems
                .Where(item => item.IntervalId == intervalId)
                .Select(item => (int?)item.SortOrder),
            "item/" + intervalId.ToString("N"),
            order => new MaintenanceIntervalItemRecord
            {
                Id = Guid.NewGuid(),
                IntervalId = intervalId,
                MaterialName = materialName,
                Quantity = quantity,
                SortOrder = order
            },
            item => item.Id,
            cancellationToken);

    /// <summary>
    /// Read MAX(sort_order) and INSERT under the same PostgreSQL transaction.
    /// Only repository writers using this lock participate; out-of-band SQL
    /// still needs the same policy or a database-level uniqueness constraint.
    /// </summary>
    private async Task<Guid> CreateOrderedAsync<TEntity>(
        IQueryable<int?> sortOrders,
        string lockScope,
        Func<int, TEntity> buildEntity,
        Func<TEntity, Guid> getId,
        CancellationToken cancellationToken) where TEntity : class
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({SortLockNamespace}, hashtext({lockScope}))",
            cancellationToken);

        var maxOrder = await sortOrders.MaxAsync(cancellationToken) ?? -1;
        var entity = buildEntity(checked(maxOrder + 1));
        await db.Set<TEntity>().AddAsync(entity, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return getId(entity);
    }

    public async Task<bool> RenameEquipmentAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken) =>
        await db.MaintenanceEquipment
            .Where(equipment => equipment.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(equipment => equipment.Name, name),
                cancellationToken) > 0;

    public async Task<bool> RenameIntervalAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToLower();
        return await db.MaintenanceIntervals
            .Where(current =>
                current.Id == id &&
                !db.MaintenanceIntervals.Any(duplicate =>
                    duplicate.EquipmentId == current.EquipmentId &&
                    duplicate.Id != current.Id &&
                    duplicate.Name.Trim().ToLower() == normalizedName))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(interval => interval.Name, name),
                cancellationToken) > 0;
    }

    public async Task<bool> UpdateItemAsync(
        Guid id,
        decimal quantity,
        CancellationToken cancellationToken) =>
        await db.MaintenanceIntervalItems
            .Where(item => item.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.Quantity, quantity),
                cancellationToken) > 0;

    public async Task<bool> DeleteEquipmentAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await db.MaintenanceEquipment
            .Where(equipment => equipment.Id == id)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<bool> DeleteIntervalAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await db.MaintenanceIntervals
            .Where(interval => interval.Id == id)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<bool> DeleteItemAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await db.MaintenanceIntervalItems
            .Where(item => item.Id == id)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    private static decimal ParseQuantity(string? value, string materialName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var quantity))
        {
            return quantity;
        }

        throw new InvalidOperationException(
            $"Некорректный остаток для компонента «{materialName.Trim()}»: {value}.");
    }
}
