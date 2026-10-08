using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Db;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

/// <summary>
/// Stock fixture uses PostgreSQL table-backed edit_csv_tab instead of a real
/// filesystem writer. Database transaction tests cannot prove file rollback.
/// </summary>
internal sealed class RequirementPostgreSqlDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string schema;
    private readonly string scopedConnectionString;

    private RequirementPostgreSqlDatabase(string connectionString, string schema)
    {
        adminConnectionString = connectionString;
        this.schema = schema;
        scopedConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schema
        }.ConnectionString;
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(scopedConnectionString)
            .Options);

    public static async Task<RequirementPostgreSqlDatabase> CreateAsync(
        decimal stock = 10m)
    {
        var connectionString = Environment.GetEnvironmentVariable("MYAPP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("MYAPP_TEST_POSTGRES must be set.");

        var b = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(b.Database) ||
            !b.Database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Integration tests may only run against a disposable *_test database.");

        var schema = "myapp_stock_it_" + Guid.NewGuid().ToString("N");
        var fixture = new RequirementPostgreSqlDatabase(connectionString, schema);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"CREATE SCHEMA \"{schema}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await fixture.InitializeAsync(stock);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private async Task InitializeAsync(decimal quantity)
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var command = dataSource.CreateCommand(
            """
            CREATE TABLE app_users (id uuid PRIMARY KEY);
            CREATE TABLE component_requirements (
                id uuid PRIMARY KEY,
                created_at timestamptz NOT NULL DEFAULT NOW(),
                created_by uuid NOT NULL REFERENCES app_users(id),
                author_name varchar(300) NOT NULL,
                issuer_name varchar(300) NOT NULL,
                vehicle_number varchar(100) NOT NULL,
                source_table varchar(20) NOT NULL,
                form_data jsonb NOT NULL
            );
            CREATE TABLE component_requirement_items (
                requirement_id uuid NOT NULL REFERENCES component_requirements(id)
                    ON DELETE CASCADE,
                position integer NOT NULL,
                name text NOT NULL,
                unit varchar(100) NOT NULL,
                quantity numeric NOT NULL,
                PRIMARY KEY (requirement_id, position)
            );

            CREATE TABLE stock_fixture (
                name text PRIMARY KEY,
                quantity numeric NOT NULL CHECK (quantity >= 0)
            );
            CREATE VIEW v_full_ost AS
                SELECT name AS "Наименование", 'pcs'::text AS "Ед.изм.",
                       quantity::text AS "Количество" FROM stock_fixture;
            CREATE VIEW v_meh_ost AS
                SELECT name AS "Наименование", 'pcs'::text AS "Ед.изм.",
                       quantity::text AS "Количество" FROM stock_fixture;
            CREATE VIEW full_ost AS
                SELECT name, quantity::text AS amount FROM stock_fixture;
            CREATE VIEW meh_ost AS
                SELECT name, quantity::text AS amount FROM stock_fixture;

            CREATE FUNCTION edit_csv_tab(
                file_name text, search_text text, new_value numeric)
            RETURNS text LANGUAGE plpgsql AS $body$
            BEGIN
                IF file_name NOT IN ('o', 'c') THEN
                    RETURN 'Unknown stock source';
                END IF;
                UPDATE stock_fixture SET quantity = new_value
                WHERE BTRIM(name) = BTRIM(search_text);
                IF NOT FOUND THEN
                    RETURN 'Material missing';
                END IF;
                RETURN 'Успешно обновлено!';
            END;
            $body$;
            """);
        await command.ExecuteNonQueryAsync();
        await ExecuteAsync(
            "INSERT INTO stock_fixture(name, quantity) VALUES ('Filter', @qty)",
            ("qty", quantity));
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] values)
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<decimal> StockAsync(string name = "Filter")
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT quantity FROM stock_fixture WHERE name = @name");
        command.Parameters.AddWithValue("name", name);
        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    public async Task<long> CountAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT COUNT(*) FROM component_requirements");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
}
