using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Db;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

/// <summary>
/// Minimal production-shaped tables for part-request CRUD regression tests.
/// Each test owns a distinct disposable PostgreSQL schema.
/// </summary>
internal sealed class VehiclePartsPostgreSqlDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string schema;
    private readonly string scopedConnectionString;

    private VehiclePartsPostgreSqlDatabase(string connectionString, string schema)
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
            .UseNpgsql(scopedConnectionString).Options);

    public static async Task<VehiclePartsPostgreSqlDatabase> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MYAPP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("MYAPP_TEST_POSTGRES is required.");

        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(settings.Database) ||
            !settings.Database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Only isolated disposable *_test PostgreSQL databases may be used.");

        var schema = "myapp_parts_it_" + Guid.NewGuid().ToString("N");
        var fixture = new VehiclePartsPostgreSqlDatabase(connectionString, schema);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"CREATE SCHEMA \"{schema}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await fixture.InitializeAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private async Task InitializeAsync()
    {
        await ExecuteAsync(
            """
            CREATE TABLE app_users (id uuid PRIMARY KEY);
            CREATE TABLE number_car (id uuid PRIMARY KEY);
            CREATE TABLE vehicle_defects (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id),
                created_by uuid NOT NULL REFERENCES app_users(id)
            );
            CREATE TABLE vehicle_works (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id),
                defect_id uuid REFERENCES vehicle_defects(id),
                repair_status varchar(30) NOT NULL,
                created_by uuid NOT NULL REFERENCES app_users(id)
            );
            CREATE TABLE vehicle_parts_requests (
                id uuid PRIMARY KEY,
                defect_id uuid NOT NULL REFERENCES vehicle_defects(id) ON DELETE CASCADE,
                request_date date NOT NULL,
                request_number varchar(100) NOT NULL DEFAULT '',
                description text NOT NULL DEFAULT '',
                required_parts text NOT NULL DEFAULT '',
                created_by uuid NOT NULL REFERENCES app_users(id),
                created_at timestamptz NOT NULL DEFAULT NOW()
            );
            CREATE UNIQUE INDEX ux_vehicle_parts_requests_legacy
                ON vehicle_parts_requests
                   (defect_id, request_number, request_date, description);
            """);
    }

    public async Task<(Guid Vehicle, Guid Defect, Guid User)> SeedAsync(
        string workStatus = "awaiting_parts")
    {
        var vehicle = Guid.NewGuid();
        var defect = Guid.NewGuid();
        var user = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", user));
        await ExecuteAsync(
            "INSERT INTO number_car (id) VALUES (@id)", ("id", vehicle));
        await ExecuteAsync(
            """
            INSERT INTO vehicle_defects (id, vehicle_id, created_by)
            VALUES (@id, @vehicle, @user)
            """,
            ("id", defect), ("vehicle", vehicle), ("user", user));
        await ExecuteAsync(
            """
            INSERT INTO vehicle_works
                (id, vehicle_id, defect_id, repair_status, created_by)
            VALUES (@id, @vehicle, @defect, @status, @user)
            """,
            ("id", Guid.NewGuid()), ("vehicle", vehicle), ("defect", defect),
            ("status", workStatus), ("user", user));
        return (vehicle, defect, user);
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> CountAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var cmd = dataSource.CreateCommand(
            "SELECT COUNT(*) FROM vehicle_parts_requests");
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
        await cmd.ExecuteNonQueryAsync();
    }
}
