using System.Text.Json;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed class TableViewRepository(NpgsqlDataSource dataSource) : ITableViewRepository
{
    public async Task<TableViewResponse> QueryAsync(TableViewRequest request, CancellationToken cancellationToken)
    {
        var fileName = request.TableName switch {
            "v_full_ost" => "o.csv", "v_meh_ost" => "c.csv", "v_workers" => "p.csv",
            _ => throw new ArgumentException("The requested view is not allowed.", nameof(request))
        };
        var filters = new List<(string Clause, string Parameter, string Value)>();
        if (request.Search is not null) filters.Add(("t.data::text ILIKE @search", "search", request.Search));
        if (fileName == "p.csv")
        {
            void Filter(string column, string parameter, string? value) {
                if (value is not null) filters.Add(($"t.data->>'{column}' ILIKE @{parameter}", parameter, value));
            }
            Filter("LastName", "lastName", request.LastName);
            Filter("FirstName", "firstName", request.FirstName);
            Filter("Patronymic", "patronymic", request.Patronymic);
            Filter("Profession", "profession", request.Profession);
        }
        var where = filters.Count == 0 ? "" : " WHERE " + string.Join(" AND ", filters.Select(filter => filter.Clause));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        await using var metadata = new NpgsqlCommand("SELECT columns FROM app_import_sources WHERE source_key = @file", connection, transaction);
        metadata.Parameters.AddWithValue("file", CsvFileRepository.SourceKey(fileName));
        var columns = (string[]?)await metadata.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("CSV storage is not initialized.");
        var common = $"WITH t AS ({CsvFileRepository.DataQuery(fileName)}) ";
        void Parameters(NpgsqlCommand command) {
            if (fileName != "p.csv") command.Parameters.AddWithValue("source", CsvFileRepository.StockSource(fileName));
            foreach (var filter in filters) command.Parameters.AddWithValue(filter.Parameter, $"%{filter.Value}%");
        }
        await using var count = new NpgsqlCommand(common + "SELECT COUNT(*) FROM t" + where, connection, transaction);
        Parameters(count);
        var total = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        var signatures = fileName == "p.csv" && request.IncludeSignatures;
        var select = signatures ? """
            SELECT t.data || jsonb_build_object('Роспись', signatures.last_name IS NOT NULL) FROM t
            LEFT JOIN employee_signatures AS signatures
              ON signatures.last_name = t.data->>'LastName'
             AND signatures.first_name = t.data->>'FirstName'
             AND signatures.patronymic = COALESCE(t.data->>'Patronymic', '')
            """ : "SELECT t.data FROM t";
        if (signatures) columns = columns.Append("Роспись").ToArray();
        var all = request.PageSize.Equals("all", StringComparison.OrdinalIgnoreCase);
        await using var query = new NpgsqlCommand(common + select + where + " ORDER BY t.data::text" + (all ? "" : " LIMIT @limit OFFSET @offset"), connection, transaction);
        Parameters(query);
        if (!all)
        {
            var limit = int.Parse(request.PageSize);
            query.Parameters.AddWithValue("limit", limit);
            query.Parameters.AddWithValue("offset", ((long)request.Page - 1) * limit);
        }
        var rows = new List<Dictionary<string, object?>>();
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.GetString(0))!;
                rows.Add(columns.ToDictionary(column => column, column => data.GetValueOrDefault(column)));
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return new(columns, rows, total, request.Page, request.PageSize);
    }
}
