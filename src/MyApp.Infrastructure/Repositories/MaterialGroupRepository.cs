using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed class MaterialGroupRepository(
    AppDbContext db, NpgsqlDataSource dataSource) : IMaterialGroupRepository
{
    public async Task<IReadOnlyList<MaterialGroupResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        // Two bounded queries, not one query per group (avoids N+1).
        var groups = await db.MaterialGroups.AsNoTracking()
            .OrderBy(group => group.Name)
            .Select(group => new { group.Id, group.Name })
            .ToListAsync(cancellationToken);
        var items = await db.MaterialGroupItems.AsNoTracking()
            .OrderBy(item => item.MaterialName)
            .Select(item => new
            {
                item.Id,
                item.GroupId,
                item.SourceTable,
                item.MaterialName
            })
            .ToListAsync(cancellationToken);
        var byGroup = items.ToLookup(item => item.GroupId);

        return groups.Select(group =>
            new MaterialGroupResponse(
                group.Id,
                group.Name,
                byGroup[group.Id]
                    .Select(item => new MaterialGroupItemResponse(
                        item.Id, item.SourceTable, item.MaterialName))
                    .ToArray()))
            .ToArray();
    }

    public async Task<IReadOnlyList<MaterialGroupMappingResponse>> GetMappingsAsync(
        string sourceTable, CancellationToken cancellationToken)
    {
        var query =
            from item in db.MaterialGroupItems.AsNoTracking()
            join materialGroup in db.MaterialGroups.AsNoTracking()
                on item.GroupId equals materialGroup.Id
            where item.SourceTable == sourceTable
            orderby item.MaterialName
            select new MaterialGroupMappingResponse(
                item.MaterialName, materialGroup.Id, materialGroup.Name);
        return await query.ToListAsync(cancellationToken);
    }

    public Task<bool> GroupExistsAsync(
        Guid id, CancellationToken cancellationToken) =>
        db.MaterialGroups.AsNoTracking().AnyAsync(
            group => group.Id == id, cancellationToken);

    public Task<bool> GroupNameExistsAsync(
        string name, CancellationToken cancellationToken) =>
        db.MaterialGroups.AsNoTracking().AnyAsync(
            group => group.Name.Trim().ToLower() == name.Trim().ToLower(),
            cancellationToken);

    public async Task<bool> MaterialExistsAsync(
        string sourceTable, string materialName, CancellationToken cancellationToken)
    {
        // PostgreSQL views have dynamic identifiers. Preserve a strict allowlist.
        if (sourceTable is not ("v_full_ost" or "v_meh_ost"))
        {
            return false;
        }

        await using var command = dataSource.CreateCommand(
            $"""
            SELECT EXISTS (
                SELECT 1 FROM {sourceTable}
                WHERE BTRIM("Наименование") = BTRIM(@materialName))
            """);
        command.Parameters.AddWithValue("materialName", materialName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public Task<bool> MaterialIsAssignedAsync(
        string sourceTable, string materialName, CancellationToken cancellationToken) =>
        db.MaterialGroupItems.AsNoTracking().AnyAsync(
            item => item.SourceTable == sourceTable &&
                    item.MaterialName.Trim() == materialName.Trim(),
            cancellationToken);

    public async Task<Guid> CreateGroupAsync(
        string name, CancellationToken cancellationToken)
    {
        var group = new MaterialGroupEntity { Id = Guid.NewGuid(), Name = name };
        db.MaterialGroups.Add(group);
        await db.SaveChangesAsync(cancellationToken);
        return group.Id;
    }

    public async Task<Guid> AddItemAsync(
        Guid groupId, string sourceTable, string materialName,
        CancellationToken cancellationToken)
    {
        var item = new MaterialGroupItemEntity
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            SourceTable = sourceTable,
            MaterialName = materialName
        };
        db.MaterialGroupItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    public async Task<bool> DeleteGroupAsync(
        Guid id, CancellationToken cancellationToken) =>
        await db.MaterialGroups.Where(group => group.Id == id)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<bool> DeleteItemAsync(
        Guid id, CancellationToken cancellationToken) =>
        await db.MaterialGroupItems.Where(item => item.Id == id)
            .ExecuteDeleteAsync(cancellationToken) > 0;
}
