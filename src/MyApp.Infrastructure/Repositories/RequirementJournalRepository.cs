using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Repositories;

public sealed class RequirementJournalRepository(
    AppDbContext db) : IRequirementJournalRepository
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(
        Guid userId,
        string authorName,
        string issuerName,
        ComponentDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var (source, lockKey) = ResolveSource(request.SourceTable);
        var changes = AggregateStockChanges(request.Items);
        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);

        await AcquireSourceLockAsync(lockKey, cancellationToken);

        // Server-side stock is authoritative. The UI's AvailableQuantity is
        // display data and may be stale or deliberately manipulated.
        // Check all lines before updating the requirement and database balances.
        var pendingWrites = new List<(string Name, decimal NewQuantity)>();
        foreach (var change in changes)
        {
            var available = await GetCurrentQuantityAsync(
                request.SourceTable, change.Name, cancellationToken);
            if (available < change.Quantity)
            {
                throw new InvalidOperationException(
                    $"Недостаточный остаток для компонента «{change.Name}»: " +
                    $"доступно {available}, требуется {change.Quantity}.");
            }
            pendingWrites.Add((change.Name, available - change.Quantity));
        }

        var requirementId = Guid.NewGuid();
        await db.ComponentRequirements.AddAsync(
            new ComponentRequirementRecord
            {
                Id = requirementId,
                CreatedBy = userId,
                AuthorName = authorName,
                IssuerName = issuerName,
                VehicleNumber = request.VehicleNumber,
                SourceTable = request.SourceTable,
                FormData = JsonSerializer.Serialize(request, JsonOptions)
            },
            cancellationToken);

        // EF does not model a navigation/foreign key between these records.
        // Persist the parent first so children cannot be inserted before it.
        // Both SaveChanges calls remain inside the same transaction.
        await db.SaveChangesAsync(cancellationToken);

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            db.ComponentRequirementItems.Add(new ComponentRequirementItemRecord
            {
                RequirementId = requirementId,
                Position = index + 1,
                Name = item.Name,
                Unit = item.Unit,
                Quantity = item.Quantity
            });
        }

        // Requirement rows and balances are committed in the same transaction.
        await db.SaveChangesAsync(cancellationToken);
        foreach (var (name, quantity) in pendingWrites)
        {
            await UpdateQuantityAsync(source, name, quantity, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RequirementJournalEntry>> GetRecentAsync(
        CancellationToken cancellationToken)
    {
        var requirements = await db.ComponentRequirements
            .AsNoTracking()
            .OrderByDescending(requirement => requirement.CreatedAt)
            .Take(200)
            .Select(requirement => new {
                requirement.Id,
                requirement.CreatedAt,
                requirement.AuthorName,
                requirement.IssuerName,
                requirement.VehicleNumber
            })
            .ToArrayAsync(cancellationToken);
        var requirementIds = requirements
            .Select(requirement => requirement.Id)
            .ToArray();
        var items = await db.ComponentRequirementItems
            .AsNoTracking()
            .Where(item => requirementIds.Contains(item.RequirementId))
            .OrderBy(item => item.Position)
            .Select(item => new {
                item.RequirementId,
                item.Name,
                item.Unit,
                item.Quantity
            })
            .ToArrayAsync(cancellationToken);
        var itemsByRequirement = items
            .GroupBy(item => item.RequirementId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ComponentDocumentItem>)group
                    .Select(item => new ComponentDocumentItem(
                        item.Name,
                        item.Unit,
                        item.Quantity,
                        item.Quantity))
                    .ToArray());

        return requirements
            .Select(requirement => new RequirementJournalEntry(
                requirement.Id,
                requirement.CreatedAt,
                requirement.AuthorName,
                requirement.IssuerName,
                requirement.VehicleNumber,
                itemsByRequirement.GetValueOrDefault(
                    requirement.Id,
                    Array.Empty<ComponentDocumentItem>())))
            .ToArray();
    }

    public async Task<ComponentDocumentRequest?> GetDocumentRequestAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var formData = await db.ComponentRequirements
            .AsNoTracking()
            .Where(requirement => requirement.Id == id)
            .Select(requirement => requirement.FormData)
            .FirstOrDefaultAsync(cancellationToken);
        return formData is null
            ? null
            : JsonSerializer.Deserialize<ComponentDocumentRequest>(
                formData,
                JsonOptions);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);

        // Lock the requirement row BEFORE reading items and restoring stock:
        // a second concurrent deletion must not restore the same stock twice.
        string? sourceTable;
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText =
                "SELECT source_table FROM component_requirements WHERE id = @id FOR UPDATE";
            AddParameter(command, "id", id);
            sourceTable = Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken));
        }
        if (string.IsNullOrEmpty(sourceTable))
        {
            return false;
        }

        var (source, lockKey) = ResolveSource(sourceTable);
        await AcquireSourceLockAsync(lockKey, cancellationToken);

        var items = await db.ComponentRequirementItems
            .AsNoTracking()
            .Where(item => item.RequirementId == id)
            .OrderBy(item => item.Position)
            .Select(item => new ComponentDocumentItem(
                item.Name,
                item.Unit,
                item.Quantity,
                0))
            .ToArrayAsync(cancellationToken);

        var changes = AggregateStockChanges(items);
        var pendingWrites = new List<(string Name, decimal NewQuantity)>();
        foreach (var change in changes)
        {
            var available = await GetCurrentQuantityAsync(
                sourceTable, change.Name, cancellationToken);
            pendingWrites.Add((change.Name, checked(available + change.Quantity)));
        }

        // Delete and restore balances atomically in this transaction.
        var deleted = await db.ComponentRequirements
            .Where(row => row.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return false;
        }

        foreach (var (name, quantity) in pendingWrites)
        {
            await UpdateQuantityAsync(source, name, quantity, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private const int StockLockNamespace = 4386202;

    private static (string Source, int LockKey) ResolveSource(string sourceTable) =>
        sourceTable switch
        {
            "v_full_ost" or "full_ost" => ("v_full_ost", 1),
            "v_meh_ost" or "meh_ost" => ("v_meh_ost", 2),
            _ => throw new InvalidOperationException(
                "Неизвестный источник компонентов.")
        };

    private async Task AcquireSourceLockAsync(
        int sourceKey,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({StockLockNamespace}, {sourceKey})",
            cancellationToken);
    }

    private static IReadOnlyList<(string Name, decimal Quantity)> AggregateStockChanges(
        IReadOnlyList<ComponentDocumentItem> items)
    {
        if (items.Count == 0)
            throw new InvalidOperationException(
                "Требование должно содержать хотя бы один компонент.");

        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.Quantity <= 0)
                throw new InvalidOperationException(
                    "Компонент должен иметь название и положительное количество.");

            var name = item.Name.Trim();
            totals[name] = checked(totals.GetValueOrDefault(name) + item.Quantity);
        }
        return totals.Select(pair => (pair.Key, pair.Value)).ToArray();
    }

    private async Task<decimal> GetCurrentQuantityAsync(
        string sourceTable,
        string itemName,
        CancellationToken cancellationToken)
    {
        var (source, _) = ResolveSource(sourceTable);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT quantity FROM app_stock_balances WHERE source_table = @source AND name = @name FOR UPDATE";
        AddParameter(command, "source", source);
        AddParameter(command, "name", itemName.Trim());
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is decimal quantity ? quantity
            : throw new InvalidOperationException($"Компонент «{itemName.Trim()}» не найден в остатках.");
    }

    private async Task UpdateQuantityAsync(string source, string name, decimal quantity, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "UPDATE app_stock_balances SET quantity = @quantity, updated_at = NOW() WHERE source_table = @source AND name = @name";
        AddParameter(command, "source", source);
        AddParameter(command, "name", name);
        AddParameter(command, "quantity", quantity);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Компонент «{name}» не найден в остатках.");
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

}
