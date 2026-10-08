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
            CREATE TABLE app_users (
                id uuid PRIMARY KEY,
                first_name text NOT NULL DEFAULT '',
                middle_name text NOT NULL DEFAULT '',
                last_name text NOT NULL DEFAULT ''
            );

            CREATE TABLE component_requirements (
                id uuid PRIMARY KEY,
                created_at timestamptz NOT NULL DEFAULT NOW(),
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                author_name varchar(300) NOT NULL,
                issuer_name varchar(300) NOT NULL,
                vehicle_number varchar(100) NOT NULL,
                source_table varchar(20) NOT NULL,
                form_data jsonb NOT NULL
            );
            CREATE TABLE component_requirement_items (
                requirement_id uuid NOT NULL
                    REFERENCES component_requirements(id) ON DELETE CASCADE,
                position integer NOT NULL,
                name text NOT NULL,
                unit varchar(100) NOT NULL,
                quantity numeric NOT NULL,
                PRIMARY KEY (requirement_id, position)
            );
            CREATE INDEX ix_component_requirements_created_at
                ON component_requirements (created_at DESC);

            CREATE TABLE material_groups (
                id uuid PRIMARY KEY,
                name varchar(100) NOT NULL
            );
            CREATE UNIQUE INDEX ux_material_groups_name
                ON material_groups (LOWER(BTRIM(name)));

            CREATE TABLE material_group_items (
                id uuid PRIMARY KEY,
                group_id uuid NOT NULL REFERENCES material_groups(id) ON DELETE CASCADE,
                source_table varchar(20) NOT NULL
                    CHECK (source_table IN ('v_full_ost', 'v_meh_ost')),
                material_name text NOT NULL
            );
            CREATE UNIQUE INDEX ux_material_group_items_material
                ON material_group_items (source_table, BTRIM(material_name));

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
            CREATE TABLE vehicle_defect_photos (
                id uuid PRIMARY KEY,
                defect_id uuid NOT NULL REFERENCES vehicle_defects(id) ON DELETE CASCADE,
                file_name text NOT NULL,
                content_type text NOT NULL,
                content bytea,
                size integer NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );
            CREATE TABLE vehicle_defect_videos (
                id uuid PRIMARY KEY,
                defect_id uuid NOT NULL REFERENCES vehicle_defects(id) ON DELETE CASCADE,
                file_name text NOT NULL,
                content_type text NOT NULL,
                size bigint NOT NULL,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );
            CREATE TABLE vehicle_work_photos (
                id uuid PRIMARY KEY,
                work_id uuid NOT NULL REFERENCES vehicle_works(id) ON DELETE CASCADE,
                file_name text NOT NULL,
                content_type text NOT NULL,
                content bytea,
                size integer NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );
            CREATE TABLE vehicle_work_videos (
                id uuid PRIMARY KEY,
                work_id uuid NOT NULL REFERENCES vehicle_works(id) ON DELETE CASCADE,
                file_name text NOT NULL,
                content_type text NOT NULL,
                size bigint NOT NULL,
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
