using System.Globalization;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

/// <summary>
/// Centralizes PostgreSQL stock views and the legacy edit_csv_tab function.
/// IMPORTANT: edit_csv_tab may write external CSV files. PostgreSQL rollback
/// does not guarantee that those file changes are rolled back.
/// </summary>
public sealed class RequirementStockGateway
{
    private const int AdvisoryLockNamespace = 4203860;

    private static (string FileName, int LockKey) ResolveSource(string sourceTable) =>
        sourceTable switch
        {
            "v_full_ost" or "full_ost" => ("o", 1),
            "v_meh_ost" or "meh_ost" => ("c", 2),
            _ => throw new InvalidOperationException(
                "Неизвестный источник компонентов.")
        };

    public static void ValidateSource(string sourceTable) =>
        _ = ResolveSource(sourceTable);

    /// <summary>
    /// Serializes requirement writes made by this application for one CSV
    /// source. Other writers and filesystem writes do not honor this lock.
    /// </summary>
    public async Task AcquireSourceLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sourceTable,
        CancellationToken cancellationToken)
    {
        var (_, lockKey) = ResolveSource(sourceTable);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT pg_advisory_xact_lock(@lockNamespace, @lockKey)";
        command.Parameters.AddWithValue("lockNamespace", AdvisoryLockNamespace);
        command.Parameters.AddWithValue("lockKey", lockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<decimal> GetCurrentQuantityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sourceTable,
        string itemName,
        CancellationToken cancellationToken)
    {
        ResolveSource(sourceTable);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sourceTable switch
        {
            "v_full_ost" or "v_meh_ost" =>
                $"""
                SELECT "Количество"
                FROM {sourceTable}
                WHERE BTRIM("Наименование") = BTRIM(@name)
                LIMIT 1
                """,
            _ =>
                $"""
                SELECT amount
                FROM {sourceTable}
                WHERE BTRIM(name) = BTRIM(@name)
                LIMIT 1
                """
        };
        command.Parameters.AddWithValue("name", itemName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException(
                $"Компонент «{itemName.Trim()}» не найден в остатках.");
        }
        if (result is decimal value)
        {
            return value;
        }
        var text = Convert.ToString(result, CultureInfo.InvariantCulture);
        if (decimal.TryParse(
                text?.Replace(',', '.'),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var quantity))
        {
            return quantity;
        }
        throw new InvalidOperationException(
            $"Некорректный остаток для компонента «{itemName.Trim()}»: {text}.");
    }

    public async Task SetQuantityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sourceTable,
        string itemName,
        decimal newValue,
        CancellationToken cancellationToken)
    {
        var (fileName, _) = ResolveSource(sourceTable);
        if (newValue < 0)
        {
            throw new InvalidOperationException(
                $"Недостаточный остаток для компонента «{itemName.Trim()}».");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT edit_csv_tab(@fileName, @searchText, @newValue)";
        command.Parameters.AddWithValue("fileName", fileName);
        command.Parameters.AddWithValue("searchText", itemName);
        command.Parameters.AddWithValue("newValue", newValue);
        var result = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken));
        if (string.IsNullOrWhiteSpace(result) ||
            !result.StartsWith("Успешно обновлено!", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                result ?? "Функция edit_csv_tab не вернула результат.");
        }
    }
}
