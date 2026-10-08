using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Db;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

// Isolated schema per test prevents data leaking between runs and protects other databases.
internal sealed class PostgreSqlIntegrationDatabase : IAsyncDisposable
{
    private readonly string connectionString;
    private readonly string schema;

    private PostgreSqlIntegrationDatabase(
        string connectionString, string schema, NpgsqlDataSource dataSource)
    {
        this.connectionString = connectionString;
        this.schema = schema;
        DataSource = dataSource;
    }

    public NpgsqlDataSource DataSource { get; }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(connectionString)
            {
                SearchPath = schema
            }.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public static async Task<PostgreSqlIntegrationDatabase> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MYAPP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set MYAPP_TEST_POSTGRES to a disposable PostgreSQL database " +
                "before running tests with Category=Integration.");
        }

        var schema = "myapp_it_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var createSchema = new NpgsqlCommand(
                $"CREATE SCHEMA \"{schema}\"", connection);
            await createSchema.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schema
        };
        var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        var database = new PostgreSqlIntegrationDatabase(
            connectionString, schema, dataSource);
        try
        {
            await database.CreateTablesAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private async Task CreateTablesAsync()
    {
        // A deliberately minimal fixture reflecting the columns, constraints and
        // defaults used by the repositories; it is NOT a migration test.
        await using var command = DataSource.CreateCommand(
            """
            CREATE TABLE number_car (id uuid PRIMARY KEY);
            CREATE TABLE app_users (id uuid PRIMARY KEY);

            CREATE TABLE vehicle_purchase_requests (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE CASCADE,
                request_date date NOT NULL,
                request_number varchar(100) NOT NULL DEFAULT '',
                item_name varchar(500) NOT NULL,
                quantity numeric NOT NULL CHECK (quantity > 0),
                status varchar(100) NOT NULL DEFAULT '',
                note text NOT NULL DEFAULT '',
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE TABLE vehicle_defects (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE CASCADE,
                node_name text NOT NULL DEFAULT '',
                failure_reason text NOT NULL DEFAULT '',
                error_code varchar(100) NOT NULL DEFAULT '',
                symptoms text NOT NULL DEFAULT '',
                downtime_started_at timestamptz NOT NULL DEFAULT NOW(),
                assigned_to uuid REFERENCES app_users(id) ON DELETE RESTRICT,
                repair_started_at timestamptz,
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE TABLE vehicle_hour_readings (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE CASCADE,
                reading_date date NOT NULL,
                engine_hours numeric NOT NULL CHECK (engine_hours >= 0),
                note text NOT NULL DEFAULT '',
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE TABLE vehicle_works (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE CASCADE,
                work_date date NOT NULL,
                description text NOT NULL,
                engine_hours numeric CHECK (engine_hours >= 0),
                performer varchar(300) NOT NULL DEFAULT '',
                note text NOT NULL DEFAULT '',
                defect_id uuid REFERENCES vehicle_defects(id) ON DELETE CASCADE,
                purchase_request_number varchar(100) NOT NULL DEFAULT '',
                purchase_request_date date,
                purchase_request_file_name varchar(255),
                purchase_request_content_type varchar(100),
                purchase_request_content bytea,
                failure_cause text NOT NULL DEFAULT '',
                repair_status varchar(30) NOT NULL DEFAULT 'repaired',
                required_parts text NOT NULL DEFAULT '',
                performed_by uuid REFERENCES app_users(id) ON DELETE RESTRICT,
                completed_at timestamptz,
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );
            """);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] values)
    {
        await using var command = DataSource.CreateCommand(sql);
        foreach (var (name, value) in values)
        {
            command.Parameters.AddWithValue(name, value);
        }
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
