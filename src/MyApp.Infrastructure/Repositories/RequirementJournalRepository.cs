using System.Data.Common;
using System.Globalization;
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
        var (fileName, lockKey) = ResolveSource(request.SourceTable);
        var changes = AggregateStockChanges(request.Items);
        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);

        await AcquireSourceLockAsync(lockKey, cancellationToken);

        // Server-side stock is authoritative. The UI's AvailableQuantity is
        // display data and may be stale or deliberately manipulated.
        // Preflight ALL lines before any call that can mutate a physical CSV.
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

        // Fail on database constraints BEFORE invoking edit_csv_tab.
        await db.SaveChangesAsync(cancellationToken);
        foreach (var (name, quantity) in pendingWrites)
        {
            EnsureCsvUpdateSucceeded(await ExecuteCsvFunctionAsync(
                fileName, name, quantity, cancellationToken));
        }
        // edit_csv_tab may write external files. A PostgreSQL rollback cannot
        // undo those file writes; this is an interim guard, not full atomicity.
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

        var (fileName, lockKey) = ResolveSource(sourceTable);
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

        // Delete first; any relational failure occurs before touching CSV.
        var deleted = await db.ComponentRequirements
            .Where(row => row.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return false;
        }

        foreach (var (name, quantity) in pendingWrites)
        {
            EnsureCsvUpdateSucceeded(await ExecuteCsvFunctionAsync(
                fileName, name, quantity, cancellationToken));
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private const int StockLockNamespace = 4386202;

    private static (string FileName, int LockKey) ResolveSource(string sourceTable) =>
        sourceTable switch
        {
            "v_full_ost" or "full_ost" => ("o", 1),
            "v_meh_ost" or "meh_ost" => ("c", 2),
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
        var normalizedName = itemName.Trim();
        var quantity = sourceTable switch
        {
            "v_full_ost" => await db.FullStocks
                .Where(row => row.Name.Trim() == normalizedName)
                .Select(row => row.Quantity)
                .FirstOrDefaultAsync(cancellationToken),
            "v_meh_ost" => await db.MechanicalStocks
                .Where(row => row.Name.Trim() == normalizedName)
                .Select(row => row.Quantity)
                .FirstOrDefaultAsync(cancellationToken),
            "full_ost" => await db.FullStockLegacy
                .Where(row => row.Name.Trim() == normalizedName)
                .Select(row => row.Amount)
                .FirstOrDefaultAsync(cancellationToken),
            "meh_ost" => await db.MechanicalStockLegacy
                .Where(row => row.Name.Trim() == normalizedName)
                .Select(row => row.Amount)
                .FirstOrDefaultAsync(cancellationToken),
            _ => throw new InvalidOperationException(
                "Для требования не указан источник остатков.")
        };

        if (quantity is null)
        {
            throw new InvalidOperationException(
                $"Компонент «{itemName.Trim()}» не найден в остатках.");
        }

        if (decimal.TryParse(
                quantity.Replace(',', '.'),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsedQuantity))
        {
            return parsedQuantity;
        }

        throw new InvalidOperationException(
            $"Некорректный остаток для компонента «{itemName.Trim()}»: {quantity}.");
    }

    private async Task<string?> ExecuteCsvFunctionAsync(
        string fileName,
        string itemName,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            "SELECT edit_csv_tab(@fileName, @searchText, @newValue)";
        AddParameter(command, "fileName", fileName);
        AddParameter(command, "searchText", itemName);
        AddParameter(command, "newValue", quantity);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(result, CultureInfo.InvariantCulture);
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

    private static void EnsureCsvUpdateSucceeded(string? result)
    {
        if (string.IsNullOrWhiteSpace(result) ||
            !result.StartsWith("Успешно обновлено!", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                result ?? "Функция edit_csv_tab не вернула результат.");
        }
    }
}
