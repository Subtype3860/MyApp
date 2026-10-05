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

    public async Task<Guid> CreateEquipmentAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var sortOrder = await db.MaintenanceEquipment
            .Select(equipment => (int?)equipment.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;
        var equipment = new MaintenanceEquipmentRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = sortOrder + 1
        };
        await db.MaintenanceEquipment.AddAsync(equipment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return equipment.Id;
    }

    public async Task<Guid> CreateIntervalAsync(
        Guid equipmentId,
        string name,
        CancellationToken cancellationToken)
    {
        var sortOrder = await db.MaintenanceIntervals
            .Where(interval => interval.EquipmentId == equipmentId)
            .Select(interval => (int?)interval.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;
        var interval = new MaintenanceIntervalRecord
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            Name = name,
            SortOrder = sortOrder + 1
        };
        await db.MaintenanceIntervals.AddAsync(interval, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return interval.Id;
    }

    public async Task<Guid> AddItemAsync(
        Guid intervalId,
        string materialName,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var sortOrder = await db.MaintenanceIntervalItems
            .Where(item => item.IntervalId == intervalId)
            .Select(item => (int?)item.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;
        var item = new MaintenanceIntervalItemRecord
        {
            Id = Guid.NewGuid(),
            IntervalId = intervalId,
            MaterialName = materialName,
            Quantity = quantity,
            SortOrder = sortOrder + 1
        };
        await db.MaintenanceIntervalItems.AddAsync(item, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return item.Id;
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
