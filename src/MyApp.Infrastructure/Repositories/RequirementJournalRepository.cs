using System.Text.Json;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;
using NpgsqlTypes;

namespace MyApp.Infrastructure.Repositories;

public sealed class RequirementJournalRepository(
    NpgsqlDataSource dataSource,
    RequirementStockGateway stockGateway) : IRequirementJournalRepository
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
        RequirementStockGateway.ValidateSource(request.SourceTable);
        var changes = BuildStockChanges(request.Items);
        if (changes.Count == 0)
        {
            throw new InvalidOperationException(
                "Требование должно содержать хотя бы один компонент.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await stockGateway.AcquireSourceLockAsync(
            connection, transaction, request.SourceTable, cancellationToken);

        // Read actual stock, not client-provided AvailableQuantity. Aggregate
        // identical material names so repeated lines cannot overwrite each other.
        // Validate every line before any potentially external file mutation.
        var updatedQuantities = new List<(string Name, decimal Value)>();
        foreach (var change in changes)
        {
            var available = await stockGateway.GetCurrentQuantityAsync(
                connection, transaction, request.SourceTable,
                change.Name, cancellationToken);
            if (available < change.Quantity)
            {
                throw new InvalidOperationException(
                    $"Недостаточный остаток для компонента «{change.Name}»: " +
                    $"доступно {available}, требуется {change.Quantity}.");
            }
            updatedQuantities.Add((change.Name, available - change.Quantity));
        }

        var requirementId = Guid.NewGuid();
        await using (var headerCommand = connection.CreateCommand())
        {
            headerCommand.Transaction = transaction;
            headerCommand.CommandText =
                """
                INSERT INTO component_requirements (
                    id,
                    created_by,
                    author_name,
                    issuer_name,
                    vehicle_number,
                    source_table,
                    form_data)
                VALUES (
                    @id,
                    @createdBy,
                    @authorName,
                    @issuerName,
                    @vehicleNumber,
                    @sourceTable,
                    @formData)
                """;
            headerCommand.Parameters.AddWithValue("id", requirementId);
            headerCommand.Parameters.AddWithValue("createdBy", userId);
            headerCommand.Parameters.AddWithValue("authorName", authorName);
            headerCommand.Parameters.AddWithValue("issuerName", issuerName);
            headerCommand.Parameters.AddWithValue(
                "vehicleNumber", request.VehicleNumber);
            headerCommand.Parameters.AddWithValue(
                "sourceTable", request.SourceTable);
            headerCommand.Parameters.AddWithValue(
                "formData", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(request, JsonOptions));
            await headerCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        // Database inserts (including their constraints) precede CSV writes.
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            await using var itemCommand = connection.CreateCommand();
            itemCommand.Transaction = transaction;
            itemCommand.CommandText =
                """
                INSERT INTO component_requirement_items (
                    requirement_id, position, name, unit, quantity)
                VALUES (
                    @requirementId, @position, @name, @unit, @quantity)
                """;
            itemCommand.Parameters.AddWithValue("requirementId", requirementId);
            itemCommand.Parameters.AddWithValue("position", index + 1);
            itemCommand.Parameters.AddWithValue("name", item.Name);
            itemCommand.Parameters.AddWithValue("unit", item.Unit);
            itemCommand.Parameters.AddWithValue("quantity", item.Quantity);
            await itemCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        // CAUTION: the legacy edit_csv_tab may modify files on disk. An error
        // here can still leave external side effects despite DB rollback.
        foreach (var (name, quantity) in updatedQuantities)
        {
            await stockGateway.SetQuantityAsync(
                connection, transaction, request.SourceTable,
                name, quantity, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RequirementJournalEntry>> GetRecentAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            WITH recent AS (
                SELECT id, created_at, author_name, issuer_name, vehicle_number
                FROM component_requirements
                ORDER BY created_at DESC
                LIMIT 200
            )
            SELECT
                recent.id,
                recent.created_at,
                recent.author_name,
                recent.issuer_name,
                recent.vehicle_number,
                items.name,
                items.unit,
                items.quantity
            FROM recent
            LEFT JOIN component_requirement_items AS items
                ON items.requirement_id = recent.id
            ORDER BY recent.created_at DESC, items.position
            """);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<RequirementJournalEntry>();
        var entryIndexes = new Dictionary<Guid, int>();
        var entryItems = new Dictionary<Guid, List<ComponentDocumentItem>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            if (!entryIndexes.ContainsKey(id))
            {
                entryIndexes[id] = entries.Count;
                entryItems[id] = [];
                entries.Add(new RequirementJournalEntry(
                    id,
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    entryItems[id]));
            }

            if (!reader.IsDBNull(5))
            {
                entryItems[id].Add(new ComponentDocumentItem(
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(7)));
            }
        }

        return entries;
    }

    public async Task<ComponentDocumentRequest?> GetDocumentRequestAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT form_data
            FROM component_requirements
            WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ComponentDocumentRequest>(
            reader.GetString(0),
            JsonOptions);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);

        string sourceTable;
        await using (var sourceCommand = connection.CreateCommand())
        {
            sourceCommand.Transaction = transaction;
            // Prevent concurrent deletions from restoring the same stock twice.
            sourceCommand.CommandText =
                "SELECT source_table FROM component_requirements WHERE id = @id FOR UPDATE";
            sourceCommand.Parameters.AddWithValue("id", id);
            var source = await sourceCommand.ExecuteScalarAsync(cancellationToken);
            if (source is null)
            {
                return false;
            }
            sourceTable = Convert.ToString(source) ?? string.Empty;
        }

        await stockGateway.AcquireSourceLockAsync(
            connection, transaction, sourceTable, cancellationToken);
        var items = new List<ComponentDocumentItem>();
        await using (var itemsCommand = connection.CreateCommand())
        {
            itemsCommand.Transaction = transaction;
            itemsCommand.CommandText =
                """
                SELECT name, unit, quantity
                FROM component_requirement_items
                WHERE requirement_id = @id
                ORDER BY position
                """;
            itemsCommand.Parameters.AddWithValue("id", id);
            await using var reader = await itemsCommand.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ComponentDocumentItem(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetDecimal(2),
                    0));
            }
        }

        var changes = BuildStockChanges(items);
        var restoredQuantities = new List<(string Name, decimal Value)>();
        foreach (var change in changes)
        {
            var available = await stockGateway.GetCurrentQuantityAsync(
                connection, transaction, sourceTable,
                change.Name, cancellationToken);
            restoredQuantities.Add(
                (change.Name, checked(available + change.Quantity)));
        }

        // Delete within the transaction before potentially changing CSV files.
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText =
            "DELETE FROM component_requirements WHERE id = @id";
        deleteCommand.Parameters.AddWithValue("id", id);
        var deleted = await deleteCommand.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (!deleted)
        {
            return false;
        }

        foreach (var (name, quantity) in restoredQuantities)
        {
            await stockGateway.SetQuantityAsync(
                connection, transaction, sourceTable,
                name, quantity, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static IReadOnlyList<(string Name, decimal Quantity)> BuildStockChanges(
        IReadOnlyList<ComponentDocumentItem> items)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.Quantity <= 0)
            {
                throw new InvalidOperationException(
                    "Компонент должен иметь название и положительное количество.");
            }
            var name = item.Name.Trim();
            totals[name] = checked(totals.GetValueOrDefault(name) + item.Quantity);
        }
        return totals.Select(pair => (pair.Key, pair.Value)).ToArray();
    }
}
