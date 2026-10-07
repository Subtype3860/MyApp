using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed class TableViewRepository(
    NpgsqlDataSource dataSource) : ITableViewRepository
{
    private static readonly HashSet<string> AllowedViews =
        new(StringComparer.Ordinal)
        {
            "v_full_ost",
            "v_meh_ost",
            "v_workers"
        };

    public async Task<TableViewResponse> QueryAsync(
        TableViewRequest request,
        CancellationToken cancellationToken)
    {
        if (!AllowedViews.Contains(request.TableName))
        {
            throw new ArgumentException("The requested view is not allowed.", nameof(request));
        }

        var filters = new List<Filter>();
        AddFilter(
            filters,
            "row_to_json(t)::text ILIKE @search",
            "search",
            request.Search);

        if (request.TableName == "v_workers")
        {
            AddFilter(
                filters,
                """t."LastName" ILIKE @lastName""",
                "lastName",
                request.LastName);
            AddFilter(
                filters,
                """t."FirstName" ILIKE @firstName""",
                "firstName",
                request.FirstName);
            AddFilter(
                filters,
                """t."Patronymic" ILIKE @patronymic""",
                "patronymic",
                request.Patronymic);
            AddFilter(
                filters,
                """t."Profession" ILIKE @profession""",
                "profession",
                request.Profession);
        }

        var whereClause = filters.Count == 0
            ? string.Empty
            : $" WHERE {string.Join(
                " AND ",
                filters.Select(filter => filter.Clause))}";

        await using var countCommand = dataSource.CreateCommand(
            $"SELECT COUNT(*) FROM {request.TableName} AS t{whereClause}");
        AddParameters(countCommand, filters);
        var scalar = await countCommand.ExecuteScalarAsync(cancellationToken);
        var total = Convert.ToInt64(scalar);

        var pagingClause = request.PageSize.Equals(
            "all",
            StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : " LIMIT @limit OFFSET @offset";
        var selectClause = request.TableName == "v_workers" &&
                           request.IncludeSignatures
            ? """
              SELECT t.*,
                     employees."Id" AS "EmployeeId",
                     (signatures.employee_id IS NOT NULL) AS "Роспись"
              FROM v_workers AS t
              LEFT JOIN (
                  SELECT
                      "Id",
                      LOWER(REGEXP_REPLACE(
                          BTRIM("FullName"),
                          '\s+',
                          ' ',
                          'g')) AS normalized_name,
                      COUNT(*) OVER (PARTITION BY LOWER(REGEXP_REPLACE(
                          BTRIM("FullName"),
                          '\s+',
                          ' ',
                          'g'))) AS match_count
                  FROM v_employee
              ) AS employees
                ON employees.normalized_name = LOWER(REGEXP_REPLACE(
                    CONCAT_WS(
                        ' ',
                        NULLIF(BTRIM(t."LastName"), ''),
                        NULLIF(BTRIM(t."FirstName"), ''),
                        NULLIF(BTRIM(t."Patronymic"), '')),
                    '\s+',
                    ' ',
                    'g'))
               AND employees.match_count = 1
              LEFT JOIN employee_signatures AS signatures
                ON signatures.employee_id = employees."Id"
              """
            : $"SELECT * FROM {request.TableName} AS t";
        await using var command = dataSource.CreateCommand(
            $"{selectClause}{whereClause}" +
            $" ORDER BY row_to_json(t)::text{pagingClause}");
        AddParameters(command, filters);

        if (pagingClause.Length > 0)
        {
            var limit = int.Parse(request.PageSize);
            command.Parameters.AddWithValue("limit", limit);
            command.Parameters.AddWithValue(
                "offset",
                (request.Page - 1) * limit);
        }

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();
        var rows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(columns.Length);
            for (var index = 0; index < columns.Length; index++)
            {
                row[columns[index]] = await reader.IsDBNullAsync(
                    index,
                    cancellationToken)
                    ? null
                    : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return new TableViewResponse(
            columns,
            rows,
            total,
            request.Page,
            request.PageSize);
    }

    private static void AddFilter(
        ICollection<Filter> filters,
        string clause,
        string parameter,
        string? value)
    {
        if (value is not null)
        {
            filters.Add(new Filter(clause, parameter, value));
        }
    }

    private static void AddParameters(
        NpgsqlCommand command,
        IEnumerable<Filter> filters)
    {
        foreach (var filter in filters)
        {
            command.Parameters.AddWithValue(
                filter.Parameter,
                $"%{filter.Value}%");
        }
    }

    private sealed record Filter(string Clause, string Parameter, string Value);
}
