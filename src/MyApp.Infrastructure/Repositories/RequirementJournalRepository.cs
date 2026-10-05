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
        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);
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

        await db.SaveChangesAsync(cancellationToken);
        foreach (var item in request.Items)
        {
            await UpdateCsvAsync(
                request.SourceTable,
                item,
                cancellationToken);
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
        var requirement = await db.ComponentRequirements
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (requirement is null)
        {
            return false;
        }

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
        foreach (var item in items)
        {
            await RestoreCsvAsync(
                requirement.SourceTable,
                item,
                cancellationToken);
        }

        var deleted = await db.ComponentRequirements
            .Where(row => row.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted > 0;
    }

    private async Task UpdateCsvAsync(
        string sourceTable,
        ComponentDocumentItem item,
        CancellationToken cancellationToken)
    {
        var fileName = sourceTable switch
        {
            "v_full_ost" or "full_ost" => "o",
            "v_meh_ost" or "meh_ost" => "c",
            _ => throw new InvalidOperationException(
                "Неизвестный источник компонентов.")
        };
        var newValue = Math.Max(
            item.AvailableQuantity - item.Quantity,
            0);
        var result = await ExecuteCsvFunctionAsync(
            fileName,
            item.Name,
            newValue,
            cancellationToken);
        EnsureCsvUpdateSucceeded(result);
    }

    private async Task RestoreCsvAsync(
        string sourceTable,
        ComponentDocumentItem item,
        CancellationToken cancellationToken)
    {
        var fileName = sourceTable switch
        {
            "v_full_ost" or "full_ost" => "o",
            "v_meh_ost" or "meh_ost" => "c",
            _ => throw new InvalidOperationException(
                "Для требования не указан источник остатков.")
        };
        var currentQuantity = await GetCurrentQuantityAsync(
            sourceTable,
            item.Name,
            cancellationToken);
        var result = await ExecuteCsvFunctionAsync(
            fileName,
            item.Name,
            currentQuantity + item.Quantity,
            cancellationToken);
        EnsureCsvUpdateSucceeded(result);
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
