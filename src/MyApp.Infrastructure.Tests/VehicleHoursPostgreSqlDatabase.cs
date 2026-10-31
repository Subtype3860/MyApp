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

    /// <summary>Подготавливает объект тестового окружения с заданными зависимостями.</summary>
    private VehicleHoursPostgreSqlDatabase(string connectionString, string schema)
    {
        adminConnectionString = connectionString;
        this.schema = schema;
        scopedConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schema
        }.ConnectionString;
    }

    /// <summary>Создаёт контекст EF Core, подключённый к изолированной тестовой схеме.</summary>
    public AppDbContext CreateContext(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(scopedConnectionString);
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new AppDbContext(options.Options);
    }

    /// <summary>Создаёт изолированную схему PostgreSQL и подготавливает данные теста.</summary>
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

    /// <summary>Создаёт таблицы и исходные данные изолированной тестовой схемы.</summary>
    private async Task InitializeAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var cmd = dataSource.CreateCommand(
            """
            CREATE TABLE app_users (id uuid PRIMARY KEY);
            CREATE TABLE number_car (id uuid PRIMARY KEY, is_archived boolean NOT NULL DEFAULT false);
            CREATE TABLE vehicle_hour_readings (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE CASCADE,
                reading_date date NOT NULL,
                reading_time time without time zone,
                engine_hours numeric NOT NULL CHECK (engine_hours >= 0),
                engine_number integer NOT NULL DEFAULT 0,
                note text NOT NULL DEFAULT '',
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );
            CREATE INDEX ix_vehicle_hours_vehicle_date
                ON vehicle_hour_readings (vehicle_id, reading_date DESC);
            """);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Выполняет SQL-команду в изолированной тестовой схеме.</summary>
    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] values)
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var cmd = dataSource.CreateCommand(sql);
        foreach (var (name, value) in values)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Читает показания моточасов для проверки результатов теста.</summary>
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

    /// <summary>Удаляет изолированную схему после завершения теста.</summary>
    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
        await cmd.ExecuteNonQueryAsync();
    }
}
