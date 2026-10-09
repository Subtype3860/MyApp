using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyApp.Infrastructure.Db;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

/// <summary>
/// Isolated PostgreSQL fixture. No application initializer or live database
/// tables are touched; each test creates and drops a private schema.
/// </summary>
internal sealed class VehicleHoursPostgreSqlDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string schema;
    private readonly string scopedConnectionString;

    private VehicleHoursPostgreSqlDatabase(string connectionString, string schema)
    {
        adminConnectionString = connectionString;
        this.schema = schema;
        scopedConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schema
        }.ConnectionString;
    }

    public AppDbContext CreateContext(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(scopedConnectionString);
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new AppDbContext(options.Options);
    }

    public static async Task<VehicleHoursPostgreSqlDatabase> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MYAPP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("MYAPP_TEST_POSTGRES must be set.");

        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(settings.Database) ||
            !settings.Database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Integration tests require an isolated disposable *_test PostgreSQL database.");

        var schema = "myapp_hours_it_" + Guid.NewGuid().ToString("N");
        var fixture = new VehicleHoursPostgreSqlDatabase(connectionString, schema);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection);
            await cmd.ExecuteNonQueryAsync();
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
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var cmd = dataSource.CreateCommand(
            """
            CREATE TABLE app_users (id uuid PRIMARY KEY);
            CREATE TABLE number_car (id uuid PRIMARY KEY);
            CREATE TABLE vehicle_hour_readings (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE CASCADE,
                reading_date date NOT NULL,
                engine_hours numeric NOT NULL CHECK (engine_hours >= 0),
                note text NOT NULL DEFAULT '',
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );
            CREATE INDEX ix_vehicle_hours_vehicle_date
                ON vehicle_hour_readings (vehicle_id, reading_date DESC);
            """);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] values)
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var cmd = dataSource.CreateCommand(sql);
        foreach (var (name, value) in values)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<(Guid VehicleId, DateOnly Date, decimal Hours)>>
        ReadingsAsync()
    {
        await using var db = CreateContext();
        var rows = await db.VehicleHourReadings.AsNoTracking()
            .OrderBy(row => row.VehicleId)
            .ThenBy(row => row.ReadingDate)
            .Select(row => new { row.VehicleId, row.ReadingDate, row.EngineHours })
            .ToArrayAsync();
        return rows.Select(x => (x.VehicleId, x.ReadingDate, x.EngineHours)).ToArray();
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
