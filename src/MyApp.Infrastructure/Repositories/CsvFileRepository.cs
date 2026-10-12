using System.Text.Json;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Storage;
using Npgsql;
using NpgsqlTypes;

namespace MyApp.Infrastructure.Repositories;

public sealed class CsvFileRepository(NpgsqlDataSource dataSource) : ICsvFileRepository
{
    internal static string DataQuery(string fileName) => fileName switch
    {
        "o.csv" or "c.csv" => """
            SELECT details || jsonb_build_object('ID', id, 'Наименование', name, 'Ед.изм.', unit, 'Количество', quantity) AS data
            FROM app_stock_balances WHERE source_table = @source
            """,
        "p.csv" => """
            SELECT details || jsonb_build_object('ID', id, 'FullName', CONCAT_WS(' ', last_name, first_name, NULLIF(patronymic, '')), 'LastName', last_name, 'FirstName', first_name,
                'Patronymic', patronymic, 'Profession', profession) AS data FROM app_csv_workers
            """,
        _ => throw new ArgumentException("Unsupported CSV file.", nameof(fileName))
    };

    internal static string SourceKey(string fileName) => fileName switch
    {
        "o.csv" => "warehouse", "c.csv" => "mechanics", "p.csv" => "workers",
        _ => throw new ArgumentException("Unsupported import source.", nameof(fileName))
    };

    internal static string StockSource(string fileName) => fileName == "o.csv" ? "v_full_ost" : "v_meh_ost";

    public async Task<byte[]> GetAsync(string fileName, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        await using var metadata = new NpgsqlCommand("SELECT columns FROM app_import_sources WHERE source_key = @file", connection, transaction);
        metadata.Parameters.AddWithValue("file", SourceKey(fileName));
        var columns = (string[]?)await metadata.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("CSV storage is not initialized.");
        await using var command = new NpgsqlCommand($"SELECT data FROM ({DataQuery(fileName)}) AS t ORDER BY data::text", connection, transaction);
        if (fileName != "p.csv") command.Parameters.AddWithValue("source", StockSource(fileName));
        var rows = new List<Dictionary<string, object?>>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.GetString(0))!);
        }
        await transaction.CommitAsync(cancellationToken);
        return CsvImportCodec.Export(new[] { "ID" }.Concat(columns.Where(column => column != "ID")).ToArray(), rows);
    }

    public async Task<CsvImportPreview> PreviewAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var csv = CsvImportCodec.Parse(fileName, content, cancellationToken);
        var stock = fileName != "p.csv";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(stock
            ? "SELECT id, name, quantity FROM app_stock_balances WHERE source_table = @source"
            : "SELECT id, CONCAT_WS(' ', last_name, first_name, NULLIF(patronymic, '')), 0::numeric FROM app_csv_workers", connection);
        if (stock) command.Parameters.AddWithValue("source", StockSource(fileName));
        var records = new Dictionary<Guid, (string Name, decimal Quantity)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) records.Add(reader.GetGuid(0), (reader.GetString(1), reader.GetDecimal(2)));
        var names = records.GroupBy(record => record.Value.Name).ToDictionary(group => group.Key, group => group.Select(record => record.Key).ToArray());
        var matched = new HashSet<Guid>();
        var added = 0;
        foreach (var row in csv.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid? match = null;
            if (row.Data.TryGetValue("ID", out var suppliedId) && Guid.TryParse((string?)suppliedId, out var id))
            {
                if (!records.ContainsKey(id)) throw new CsvImportValidationException("ID не найден в выбранном источнике. Для новых записей оставьте ID пустым.");
                match = id;
            }
            else if (names.TryGetValue(row.Key, out var candidates))
            {
                if (candidates.Length != 1) throw new CsvImportValidationException("В базе есть работники с одинаковым ФИО. Укажите ID из выгрузки.");
                match = candidates[0];
            }
            if (match.HasValue && !matched.Add(match.Value)) throw new CsvImportValidationException("Одна запись указана в файле несколько раз.");
            if (!match.HasValue) added++;
        }
        return new(added, matched.Count, stock ? records.Count(record => record.Value.Quantity != 0 && !matched.Contains(record.Key)) : 0,
            csv.Rows.Count, csv.Columns, csv.Rows.Take(10).Select(row => row.Data).ToArray());
    }

    public async Task<CsvImportResult> ReplaceAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var csv = CsvImportCodec.Parse(fileName, content, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sourceKey = fileName switch { "o.csv" => 1, "c.csv" => 2, "p.csv" => 3, _ => throw new ArgumentException("Unsupported CSV file.") };
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(4386202, @key)", connection, transaction))
        {
            command.Parameters.AddWithValue("key", sourceKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var command = new NpgsqlCommand("CREATE TEMP TABLE csv_import_stage (key text NOT NULL, data jsonb NOT NULL) ON COMMIT DROP", connection, transaction))
            await command.ExecuteNonQueryAsync(cancellationToken);
        await using (var writer = await connection.BeginBinaryImportAsync("COPY csv_import_stage(key, data) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var row in csv.Rows)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(row.Key, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(JsonSerializer.Serialize(row.Data), NpgsqlDbType.Jsonb, cancellationToken);
            }
            await writer.CompleteAsync(cancellationToken);
        }
        var stock = fileName != "p.csv";
        if (!stock)
        {
            await using var ambiguous = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM csv_import_stage s WHERE NULLIF(s.data->>'ID', '') IS NULL
                    AND (SELECT COUNT(*) FROM app_csv_workers w WHERE CONCAT_WS(' ', w.last_name, w.first_name, NULLIF(w.patronymic, '')) = s.key) > 1)
                """, connection, transaction);
            if ((bool)(await ambiguous.ExecuteScalarAsync(cancellationToken))!)
                throw new CsvImportValidationException("В базе есть работники с одинаковым ФИО. Укажите ID из выгрузки для однозначного сопоставления.");
        }
        await using (var resolve = new NpgsqlCommand(stock ? """
            ALTER TABLE csv_import_stage ADD COLUMN record_id uuid;
            UPDATE csv_import_stage s SET record_id = COALESCE(NULLIF(s.data->>'ID', '')::uuid,
                (SELECT b.id FROM app_stock_balances b WHERE b.source_table = @source AND b.name = s.key), gen_random_uuid());
            """ : """
            ALTER TABLE csv_import_stage ADD COLUMN record_id uuid;
            UPDATE csv_import_stage s SET record_id = COALESCE(NULLIF(s.data->>'ID', '')::uuid,
                (SELECT w.id FROM app_csv_workers w WHERE CONCAT_WS(' ', w.last_name, w.first_name, NULLIF(w.patronymic, '')) = s.key), gen_random_uuid());
            """, connection, transaction))
        {
            if (stock) resolve.Parameters.AddWithValue("source", StockSource(fileName));
            await resolve.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var validate = new NpgsqlCommand(stock ? """
            SELECT EXISTS (SELECT 1 FROM csv_import_stage s WHERE NULLIF(s.data->>'ID', '') IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM app_stock_balances b WHERE b.id = s.record_id AND b.source_table = @source))
                OR EXISTS (SELECT 1 FROM csv_import_stage GROUP BY record_id HAVING COUNT(*) > 1)
            """ : """
            SELECT EXISTS (SELECT 1 FROM csv_import_stage s WHERE NULLIF(s.data->>'ID', '') IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM app_csv_workers w WHERE w.id = s.record_id))
                OR EXISTS (SELECT 1 FROM csv_import_stage GROUP BY record_id HAVING COUNT(*) > 1)
            """, connection, transaction))
        {
            if (stock) validate.Parameters.AddWithValue("source", StockSource(fileName));
            if ((bool)(await validate.ExecuteScalarAsync(cancellationToken))!)
                throw new CsvImportValidationException("ID не найден в выбранном источнике или повторяется в файле. Для новых записей оставьте ID пустым.");
        }
        await using var count = new NpgsqlCommand(stock
            ? "SELECT COUNT(*) FROM csv_import_stage s JOIN app_stock_balances b ON b.id = s.record_id AND b.source_table = @source"
            : "SELECT COUNT(*) FROM csv_import_stage s JOIN app_csv_workers w ON w.id = s.record_id", connection, transaction);
        if (stock) count.Parameters.AddWithValue("source", StockSource(fileName));
        var updated = checked((int)(long)(await count.ExecuteScalarAsync(cancellationToken))!);
        await using var upsert = new NpgsqlCommand(stock ? """
            INSERT INTO app_stock_balances(id, source_table, name, unit, quantity, details)
            SELECT record_id, @source, key, data->>'Ед.изм.', (data->>'Количество')::numeric,
                data - ARRAY['ID', 'Наименование', 'Ед.изм.', 'Количество'] FROM csv_import_stage
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name, unit = EXCLUDED.unit, quantity = EXCLUDED.quantity,
                details = app_stock_balances.details || EXCLUDED.details, updated_at = NOW()
            """ : """
            INSERT INTO app_csv_workers(id, last_name, first_name, patronymic, profession, details)
            SELECT record_id, data->>'LastName', data->>'FirstName', data->>'Patronymic', data->>'Profession',
                data - ARRAY['ID', 'FullName', 'LastName', 'FirstName', 'Patronymic', 'Profession'] FROM csv_import_stage
            ON CONFLICT (id) DO UPDATE SET
                last_name = EXCLUDED.last_name, first_name = EXCLUDED.first_name, patronymic = EXCLUDED.patronymic,
                profession = EXCLUDED.profession, details = app_csv_workers.details || EXCLUDED.details, updated_at = NOW()
            """, connection, transaction);
        if (stock) upsert.Parameters.AddWithValue("source", StockSource(fileName));
        await upsert.ExecuteNonQueryAsync(cancellationToken);
        var zeroed = 0;
        if (stock)
        {
            await using var zero = new NpgsqlCommand("""
                UPDATE app_stock_balances b SET quantity = 0, updated_at = NOW()
                WHERE source_table = @source AND quantity <> 0
                  AND NOT EXISTS (SELECT 1 FROM csv_import_stage s WHERE s.record_id = b.id)
                """, connection, transaction);
            zero.Parameters.AddWithValue("source", StockSource(fileName));
            zeroed = await zero.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var metadataUpdate = new NpgsqlCommand("""
            UPDATE app_import_sources SET columns = ARRAY(
                SELECT col FROM unnest(columns || @columns) WITH ORDINALITY AS c(col, position)
                GROUP BY col ORDER BY MIN(position)), initialized = true, imported_at = NOW()
            WHERE source_key = @file
            """, connection, transaction);
        metadataUpdate.Parameters.AddWithValue("columns", csv.Columns);
        metadataUpdate.Parameters.AddWithValue("file", SourceKey(fileName));
        if (await metadataUpdate.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("CSV storage is not initialized.");
        await using var history = new NpgsqlCommand("""
            INSERT INTO app_import_history(source_key, added, updated, zeroed) VALUES (@source, @added, @updated, @zeroed)
            """, connection, transaction);
        history.Parameters.AddWithValue("source", SourceKey(fileName));
        history.Parameters.AddWithValue("added", csv.Rows.Count - updated);
        history.Parameters.AddWithValue("updated", updated);
        history.Parameters.AddWithValue("zeroed", zeroed);
        await history.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(csv.Rows.Count - updated, updated, zeroed);
    }
}
