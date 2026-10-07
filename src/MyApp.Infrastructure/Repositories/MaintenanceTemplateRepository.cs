using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed class MaintenanceTemplateRepository(
    NpgsqlDataSource dataSource) : IMaintenanceTemplateRepository
{
    public async Task<IReadOnlyList<MaintenanceEquipmentResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT
                equipment.id,
                equipment.name,
                intervals.id,
                intervals.name,
                items.id,
                items.material_name,
                items.quantity,
                COALESCE(stock."Ед.изм."::text, ''),
                COALESCE(stock."Количество"::numeric, 0)
            FROM maintenance_equipment AS equipment
            LEFT JOIN maintenance_intervals AS intervals
                ON intervals.equipment_id = equipment.id
            LEFT JOIN maintenance_interval_items AS items
                ON items.interval_id = intervals.id
            LEFT JOIN LATERAL (
                SELECT source."Ед.изм.", source."Количество"
                FROM v_full_ost AS source
                WHERE BTRIM(source."Наименование") =
                      BTRIM(items.material_name)
                LIMIT 1
            ) AS stock ON TRUE
            ORDER BY
                equipment.sort_order,
                equipment.name,
                intervals.sort_order,
                intervals.name,
                items.sort_order,
                items.material_name
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var equipment = new List<MutableEquipment>();
        var equipmentById = new Dictionary<Guid, MutableEquipment>();
        var intervalsById = new Dictionary<Guid, MutableInterval>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var equipmentId = reader.GetGuid(0);
            if (!equipmentById.TryGetValue(equipmentId, out var equipmentEntry))
            {
                equipmentEntry = new MutableEquipment(
                    equipmentId, reader.GetString(1), []);
                equipmentById[equipmentId] = equipmentEntry;
                equipment.Add(equipmentEntry);
            }
            if (reader.IsDBNull(2))
            {
                continue;
            }

            var intervalId = reader.GetGuid(2);
            if (!intervalsById.TryGetValue(intervalId, out var intervalEntry))
            {
                intervalEntry = new MutableInterval(
                    intervalId, reader.GetString(3), []);
                intervalsById[intervalId] = intervalEntry;
                equipmentEntry.Intervals.Add(intervalEntry);
            }
            if (reader.IsDBNull(4))
            {
                continue;
            }

            intervalEntry.Items.Add(new MaintenanceItemResponse(
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(7),
                reader.GetDecimal(6),
                reader.GetDecimal(8)));
        }

        return equipment
            .Select(entry => new MaintenanceEquipmentResponse(
                entry.Id,
                entry.Name,
                entry.Intervals
                    .Select(interval => new MaintenanceIntervalResponse(
                        interval.Id, interval.Name, interval.Items))
                    .ToArray()))
            .ToArray();
    }

    public Task<bool> EquipmentExistsAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        ExistsAsync(
            "SELECT EXISTS (SELECT 1 FROM maintenance_equipment WHERE id = @id)",
            ("id", id),
            cancellationToken);

    public Task<bool> IntervalExistsAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        ExistsAsync(
            "SELECT EXISTS (SELECT 1 FROM maintenance_intervals WHERE id = @id)",
            ("id", id),
            cancellationToken);

    public Task<bool> EquipmentNameExistsAsync(
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken) =>
        ExistsAsync(
            """
            SELECT EXISTS (
                SELECT 1 FROM maintenance_equipment
                WHERE LOWER(BTRIM(name)) = LOWER(BTRIM(@name))
                  AND id <> @exceptId)
            """,
            ("name", name),
            ("exceptId", exceptId ?? Guid.Empty),
            cancellationToken);

    public Task<bool> IntervalNameExistsAsync(
        Guid equipmentId,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken) =>
        ExistsAsync(
            """
            SELECT EXISTS (
                SELECT 1 FROM maintenance_intervals
                WHERE equipment_id = @equipmentId
                  AND LOWER(BTRIM(name)) = LOWER(BTRIM(@name))
                  AND id <> @exceptId)
            """,
            ("equipmentId", equipmentId),
            ("name", name),
            ("exceptId", exceptId ?? Guid.Empty),
            cancellationToken);

    public Task<bool> IntervalNameConflictExistsAsync(
        Guid intervalId,
        string name,
        CancellationToken cancellationToken) =>
        ExistsAsync(
            """
            SELECT EXISTS (
                SELECT 1
                FROM maintenance_intervals AS current
                JOIN maintenance_intervals AS duplicate
                  ON duplicate.equipment_id = current.equipment_id
                 AND duplicate.id <> current.id
                WHERE current.id = @intervalId
                  AND LOWER(BTRIM(duplicate.name)) = LOWER(BTRIM(@name)))
            """,
            ("intervalId", intervalId),
            ("name", name),
            cancellationToken);

    public Task<bool> MaterialExistsAsync(
        string materialName,
        CancellationToken cancellationToken) =>
        ExistsAsync(
            """
            SELECT EXISTS (
                SELECT 1 FROM v_full_ost
                WHERE BTRIM("Наименование") = BTRIM(@materialName))
            """,
            ("materialName", materialName),
            cancellationToken);

    public Task<bool> ItemExistsAsync(
        Guid intervalId,
        string materialName,
        CancellationToken cancellationToken) =>
        ExistsAsync(
            """
            SELECT EXISTS (
                SELECT 1 FROM maintenance_interval_items
                WHERE interval_id = @intervalId
                  AND BTRIM(material_name) = BTRIM(@materialName))
            """,
            ("intervalId", intervalId),
            ("materialName", materialName),
            cancellationToken);

    public Task<Guid> CreateEquipmentAsync(
        string name,
        CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO maintenance_equipment (id, name, sort_order)
            VALUES (@id, @name,
                COALESCE((SELECT MAX(sort_order) + 1 FROM maintenance_equipment), 0))
            """,
            cancellationToken,
            ("name", name));

    public Task<Guid> CreateIntervalAsync(
        Guid equipmentId,
        string name,
        CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO maintenance_intervals (
                id, equipment_id, name, sort_order)
            VALUES (
                @id, @equipmentId, @name,
                COALESCE((
                    SELECT MAX(sort_order) + 1
                    FROM maintenance_intervals
                    WHERE equipment_id = @equipmentId), 0))
            """,
            cancellationToken,
            ("equipmentId", equipmentId),
            ("name", name));

    public Task<Guid> AddItemAsync(
        Guid intervalId,
        string materialName,
        decimal quantity,
        CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO maintenance_interval_items (
                id, interval_id, material_name, quantity, sort_order)
            VALUES (
                @id, @intervalId, @materialName, @quantity,
                COALESCE((
                    SELECT MAX(sort_order) + 1
                    FROM maintenance_interval_items
                    WHERE interval_id = @intervalId), 0))
            """,
            cancellationToken,
            ("intervalId", intervalId),
            ("materialName", materialName),
            ("quantity", quantity));

    public Task<bool> RenameEquipmentAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE maintenance_equipment SET name = @name WHERE id = @id",
            cancellationToken,
            ("id", id),
            ("name", name));

    public Task<bool> RenameIntervalAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE maintenance_intervals AS current
            SET name = @name
            WHERE current.id = @id
              AND NOT EXISTS (
                  SELECT 1 FROM maintenance_intervals AS duplicate
                  WHERE duplicate.equipment_id = current.equipment_id
                    AND duplicate.id <> current.id
                    AND LOWER(BTRIM(duplicate.name)) =
                        LOWER(BTRIM(@name)))
            """,
            cancellationToken,
            ("id", id),
            ("name", name));

    public Task<bool> UpdateItemAsync(
        Guid id,
        decimal quantity,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE maintenance_interval_items
            SET quantity = @quantity
            WHERE id = @id
            """,
            cancellationToken,
            ("id", id),
            ("quantity", quantity));

    public Task<bool> DeleteEquipmentAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        DeleteAsync("maintenance_equipment", id, cancellationToken);

    public Task<bool> DeleteIntervalAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        DeleteAsync("maintenance_intervals", id, cancellationToken);

    public Task<bool> DeleteItemAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        DeleteAsync("maintenance_interval_items", id, cancellationToken);

    private async Task<bool> ExistsAsync(
        string sql,
        (string Name, object? Value) parameter,
        CancellationToken cancellationToken) =>
        await ExistsAsync(sql, [parameter], cancellationToken);

    private async Task<bool> ExistsAsync(
        string sql,
        (string Name, object? Value) first,
        (string Name, object? Value) second,
        CancellationToken cancellationToken) =>
        await ExistsAsync(sql, [first, second], cancellationToken);

    private async Task<bool> ExistsAsync(
        string sql,
        (string Name, object? Value) first,
        (string Name, object? Value) second,
        (string Name, object? Value) third,
        CancellationToken cancellationToken) =>
        await ExistsAsync(sql, [first, second, third], cancellationToken);

    private async Task<bool> ExistsAsync(
        string sql,
        IReadOnlyList<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        AddParameters(command, parameters);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task<Guid> InsertAsync(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        var id = Guid.NewGuid();
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private async Task<bool> ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = dataSource.CreateCommand(sql);
        AddParameters(command, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private Task<bool> DeleteAsync(
        string table,
        Guid id,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            $"DELETE FROM {table} WHERE id = @id",
            cancellationToken,
            ("id", id));

    private static void AddParameters(
        NpgsqlCommand command,
        IEnumerable<(string Name, object? Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private sealed record MutableEquipment(
        Guid Id,
        string Name,
        List<MutableInterval> Intervals);

    private sealed record MutableInterval(
        Guid Id,
        string Name,
        List<MaintenanceItemResponse> Items);
}
