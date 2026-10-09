using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Db;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

/// <summary>
/// Disposable fixture that never touches the deployed schema. CI provides
/// the PostgreSQL database; each test uses its own randomly named schema.
/// </summary>
internal sealed class MaintenancePostgreSqlDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string schema;
    private readonly string scopedConnectionString;

    private MaintenancePostgreSqlDatabase(string connectionString, string schema)
    {
        adminConnectionString = connectionString;
        this.schema = schema;
        scopedConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schema
        }.ConnectionString;
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(scopedConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public static async Task<MaintenancePostgreSqlDatabase> CreateAsync(
        bool numericStockView = false)
    {
        var connectionString = Environment.GetEnvironmentVariable("MYAPP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "MYAPP_TEST_POSTGRES is required for Category=Integration tests.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database) ||
            !builder.Database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Integration tests require a disposable PostgreSQL database ending in _test.");
        }

        var schema = "myapp_maintenance_it_" + Guid.NewGuid().ToString("N");
        var result = new MaintenancePostgreSqlDatabase(connectionString, schema);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand(
                $"CREATE SCHEMA \"{schema}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await result.InitializeAsync(numericStockView);
            return result;
        }
        catch
        {
            await result.DisposeAsync();
            throw;
        }
    }

    private async Task InitializeAsync(bool numericStockView)
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        var stockColumn = numericStockView ? "'8'::numeric" : "'8'::text";
        await using var cmd = dataSource.CreateCommand(
            $"""
            CREATE TABLE maintenance_equipment (
                id uuid PRIMARY KEY,
                name varchar(100) NOT NULL,
                sort_order integer NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX ux_maintenance_equipment_name
                ON maintenance_equipment (LOWER(BTRIM(name)));

            CREATE TABLE maintenance_intervals (
                id uuid PRIMARY KEY,
                equipment_id uuid NOT NULL REFERENCES maintenance_equipment(id)
                    ON DELETE CASCADE,
                name varchar(100) NOT NULL,
                sort_order integer NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX ux_maintenance_intervals_name
                ON maintenance_intervals (equipment_id, LOWER(BTRIM(name)));

            CREATE TABLE maintenance_interval_items (
                id uuid PRIMARY KEY,
                interval_id uuid NOT NULL REFERENCES maintenance_intervals(id)
                    ON DELETE CASCADE,
                material_name text NOT NULL,
                quantity numeric NOT NULL CHECK (quantity > 0),
                sort_order integer NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX ux_maintenance_items_material
                ON maintenance_interval_items (interval_id, BTRIM(material_name));

            CREATE VIEW v_full_ost AS
                SELECT 'Filter'::text AS "Наименование",
                       'pcs'::text AS "Ед.изм.",
                       {{stockColumn}} AS "Количество";
            """);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<long> ScalarLongAsync(string commandText)
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var cmd = dataSource.CreateCommand(commandText);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task<int[]> SortOrdersAsync(string table, string keyColumn = "",
        Guid? keyValue = null)
    {
        // The table and key are chosen exclusively from fixed test constants.
        if (table is not ("maintenance_equipment" or "maintenance_intervals" or "maintenance_interval_items"))
            throw new ArgumentOutOfRangeException(nameof(table));
        if (keyColumn is not ("" or "equipment_id" or "interval_id"))
            throw new ArgumentOutOfRangeException(nameof(keyColumn));

        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var cmd = dataSource.CreateCommand(
            $"SELECT ARRAY_AGG(sort_order ORDER BY sort_order) FROM {table}" +
            (keyValue.HasValue ? $" WHERE {keyColumn} = @key" : string.Empty));
        if (keyValue.HasValue)
            cmd.Parameters.AddWithValue("key", keyValue.Value);
        return (int[]?)await cmd.ExecuteScalarAsync() ?? [];
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
