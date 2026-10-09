using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Repositories;

public sealed class MaterialGroupRepository(
    AppDbContext db) : IMaterialGroupRepository
{
    public async Task<IReadOnlyList<MaterialGroupResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var groups = await db.MaterialGroups
            .AsNoTracking()
            .OrderBy(group => group.Name)
            .Select(group => new {
                group.Id,
                group.Name
            })
            .ToArrayAsync(cancellationToken);
        var groupIds = groups.Select(group => group.Id).ToArray();
        var items = await db.MaterialGroupItems
            .AsNoTracking()
            .Where(item => groupIds.Contains(item.GroupId))
            .OrderBy(item => item.MaterialName)
            .Select(item => new {
                item.Id,
                item.GroupId,
                item.SourceTable,
                item.MaterialName
            })
            .ToArrayAsync(cancellationToken);
        var itemsByGroup = items
            .GroupBy(item => item.GroupId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MaterialGroupItemResponse>)group
                    .Select(item => new MaterialGroupItemResponse(
                        item.Id,
                        item.SourceTable,
                        item.MaterialName))
                    .ToArray());

        return groups
            .Select(group => new MaterialGroupResponse(
                group.Id,
                group.Name,
                itemsByGroup.GetValueOrDefault(
                    group.Id,
                    Array.Empty<MaterialGroupItemResponse>())))
            .ToArray();
    }

    public async Task<IReadOnlyList<MaterialGroupMappingResponse>> GetMappingsAsync(
        string sourceTable,
        CancellationToken cancellationToken) =>
        await db.MaterialGroupItems
            .AsNoTracking()
            .Where(item => item.SourceTable == sourceTable)
            .Join(
                db.MaterialGroups.AsNoTracking(),
                item => item.GroupId,
                group => group.Id,
                (item, group) => new {
                    item.MaterialName,
                    GroupId = group.Id,
                    GroupName = group.Name
                })
            .OrderBy(item => item.MaterialName)
            .Select(item => new MaterialGroupMappingResponse(
                item.MaterialName,
                item.GroupId,
                item.GroupName))
            .ToArrayAsync(cancellationToken);

    public Task<bool> GroupExistsAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        db.MaterialGroups.AnyAsync(
            group => group.Id == id,
            cancellationToken);

    public Task<bool> GroupNameExistsAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToLower();
        return db.MaterialGroups.AnyAsync(
            group => group.Name.Trim().ToLower() == normalizedName,
            cancellationToken);
    }

    public Task<bool> MaterialExistsAsync(
        string sourceTable,
        string materialName,
        CancellationToken cancellationToken)
    {
        var normalizedName = materialName.Trim();
        return sourceTable switch
        {
            "v_full_ost" => db.FullStocks.AnyAsync(
                row => row.Name.Trim() == normalizedName,
                cancellationToken),
            "v_meh_ost" => db.MechanicalStocks.AnyAsync(
                row => row.Name.Trim() == normalizedName,
                cancellationToken),
            _ => Task.FromResult(false)
        };
    }

    public Task<bool> MaterialIsAssignedAsync(
        string sourceTable,
        string materialName,
        CancellationToken cancellationToken)
    {
        var normalizedName = materialName.Trim();
        return db.MaterialGroupItems.AnyAsync(
            item =>
                item.SourceTable == sourceTable &&
                item.MaterialName.Trim() == normalizedName,
            cancellationToken);
    }

    public async Task<Guid> CreateGroupAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var group = new MaterialGroupRecord
        {
            Id = Guid.NewGuid(),
            Name = name
        };
        await db.MaterialGroups.AddAsync(group, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return group.Id;
    }

    public async Task<Guid> AddItemAsync(
        Guid groupId,
        string sourceTable,
        string materialName,
        CancellationToken cancellationToken)
    {
        var item = new MaterialGroupItemRecord
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            SourceTable = sourceTable,
            MaterialName = materialName
        };
        await db.MaterialGroupItems.AddAsync(item, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    public async Task<bool> DeleteGroupAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await db.MaterialGroups
            .Where(group => group.Id == id)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<bool> DeleteItemAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await db.MaterialGroupItems
            .Where(item => item.Id == id)
            .ExecuteDeleteAsync(cancellationToken) > 0;
}
