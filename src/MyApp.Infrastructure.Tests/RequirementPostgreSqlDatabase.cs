using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Db;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

/// <summary>
/// Legacy CSV views are copied into real transactional database balances.
/// </summary>
internal sealed class RequirementPostgreSqlDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string schema;
    private readonly string scopedConnectionString;

    /// <summary>Подготавливает объект тестового окружения с заданными зависимостями.</summary>
    private RequirementPostgreSqlDatabase(string connectionString, string schema)
    {
        adminConnectionString = connectionString;
        this.schema = schema;
        scopedConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schema
        }.ConnectionString;
    }

    /// <summary>Создаёт контекст EF Core, подключённый к изолированной тестовой схеме.</summary>
    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(scopedConnectionString)
            .Options);

    /// <summary>Создаёт источник подключений к изолированной тестовой схеме.</summary>
    public NpgsqlDataSource CreateDataSource() => NpgsqlDataSource.Create(scopedConnectionString);

    /// <summary>Создаёт изолированную схему PostgreSQL и подготавливает данные теста.</summary>
    public static async Task<RequirementPostgreSqlDatabase> CreateAsync(
        decimal stock = 10m,
        bool numericViews = false)
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
            await fixture.InitializeAsync(stock, numericViews);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    /// <summary>Создаёт таблицы и исходные данные изолированной тестовой схемы.</summary>
    private async Task InitializeAsync(decimal quantity, bool numericViews)
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        // A production view can return numeric while older test fixtures
        // return text. Both must work with the same EF stock projections.
        var stockCast = numericViews ? "" : "::text";
        await using var command = dataSource.CreateCommand(
            $"""
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
                       quantity{stockCast} AS "Количество" FROM stock_fixture;
            CREATE VIEW v_meh_ost AS
                SELECT name AS "Наименование", 'pcs'::text AS "Ед.изм.",
                       quantity{stockCast} AS "Количество" FROM stock_fixture;
            CREATE VIEW full_ost AS
                SELECT name, quantity{stockCast} AS amount FROM stock_fixture;
            CREATE VIEW meh_ost AS
                SELECT name, quantity{stockCast} AS amount FROM stock_fixture;

            CREATE FUNCTION edit_csv_tab(
                file_name text, search_text text, new_value numeric)
            RETURNS text LANGUAGE plpgsql AS $body$
            BEGIN
                RAISE EXCEPTION 'External CSV writes must never be called';
            END;
            $body$;
            """);
        await command.ExecuteNonQueryAsync();
        await ExecuteAsync(
            "INSERT INTO stock_fixture(name, quantity) VALUES ('Filter', @qty)",
            ("qty", quantity));
        await using var db = CreateContext();
        await CsvDatabaseMigration.InitializeAsync(db);
    }

    /// <summary>Выполняет SQL-команду в изолированной тестовой схеме.</summary>
    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] values)
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Читает актуальный остаток материала для утверждений теста.</summary>
    public async Task<decimal> StockAsync(string name = "Filter", string source = "v_full_ost")
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT quantity FROM app_stock_balances WHERE name = @name AND source_table = @source");
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("source", source);
        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Читает остаток из прежнего складского источника в тестовой схеме.</summary>
    public async Task<decimal> LegacyStockAsync()
    {
        await using var source = CreateDataSource();
        await using var command = source.CreateCommand("SELECT quantity FROM stock_fixture WHERE name = 'Filter'");
        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Возвращает количество записей для проверки состояния тестовой базы.</summary>
    public async Task<long> CountAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT COUNT(*) FROM component_requirements");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Удаляет изолированную схему после завершения теста.</summary>
    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
}
