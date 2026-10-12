using System.Text;
using System.Text.Json;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Repositories;
using MyApp.Infrastructure.Storage;
using Xunit;

namespace MyApp.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class CsvDatabaseImportTests
{
    [Fact]
    public async Task Snapshot_zeroes_absent_preserves_ids_and_records_actual_changes()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await using var source = fixture.CreateDataSource();
        var repository = new CsvFileRepository(source);
        var original = CsvImportCodec.Parse("o.csv", await repository.GetAsync("o.csv", default));
        var id = Assert.Single(original.Rows).Data["ID"];
        var preview = await repository.PreviewAsync("o.csv", Encoding.UTF8.GetBytes("name;unit;amount\nNew;pcs;3"), default);
        Assert.Equal(1, preview.Added);
        Assert.Equal(1, preview.Zeroed);
        Assert.Equal(10m, await fixture.StockAsync());
        Assert.Equal(new CsvImportResult(1, 0, 1), await repository.ReplaceAsync("o.csv", Encoding.UTF8.GetBytes("name;unit;amount\nNew;pcs;3"), default));
        Assert.Equal(0m, await fixture.StockAsync());
        Assert.Equal(10m, await fixture.StockAsync("Filter", "v_meh_ost"));
        var result = await repository.ReplaceAsync("o.csv", Encoding.UTF8.GetBytes($"id;name;unit;amount\n{id};Renamed;pcs;4"), default);
        Assert.Equal(new CsvImportResult(0, 1, 1), result);
        Assert.Equal(4m, await fixture.StockAsync("Renamed"));
        var exported = CsvImportCodec.Parse("o.csv", await repository.GetAsync("o.csv", default));
        Assert.Equal(id, exported.Rows.Single(row => row.Key == "Renamed").Data["ID"]);
        await using var history = source.CreateCommand("SELECT COUNT(*) FROM app_import_history WHERE source_key = 'warehouse'");
        Assert.Equal(2L, (long)(await history.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Unknown_id_and_empty_snapshot_leave_all_balances_unchanged()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await using var source = fixture.CreateDataSource();
        var repository = new CsvFileRepository(source);
        await Assert.ThrowsAsync<CsvImportValidationException>(() => repository.ReplaceAsync("o.csv", Encoding.UTF8.GetBytes($"id;name;unit;amount\n{Guid.NewGuid()};Filter;pcs;1"), default));
        await Assert.ThrowsAsync<CsvImportValidationException>(() => repository.ReplaceAsync("o.csv", Encoding.UTF8.GetBytes("name;unit;amount"), default));
        Assert.Equal(10m, await fixture.StockAsync());
    }

    [Fact]
    public async Task Worker_rename_keeps_id_and_absent_workers()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await using var source = fixture.CreateDataSource();
        var repository = new CsvFileRepository(source);
        await repository.ReplaceAsync("p.csv", Encoding.UTF8.GetBytes("ФИО;Профессия\nИванов Иван;Механик\nПетров Петр;Кладовщик"), default);
        var export = CsvImportCodec.Parse("p.csv", await repository.GetAsync("p.csv", default));
        var id = export.Rows.Single(row => row.Key == "Иванов Иван").Data["ID"];
        Assert.Equal(new CsvImportResult(0, 1), await repository.ReplaceAsync("p.csv", Encoding.UTF8.GetBytes($"ID;ФИО;Профессия\n{id};Сидоров Иван;Механик"), default));
        var updated = CsvImportCodec.Parse("p.csv", await repository.GetAsync("p.csv", default));
        Assert.Equal(2, updated.Rows.Count);
        Assert.Equal(id, updated.Rows.Single(row => row.Key == "Сидоров Иван").Data["ID"]);
    }

    [Fact]
    public async Task Bootstrap_treats_absent_legacy_quantities_as_zero()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await fixture.ExecuteAsync("""
            DROP VIEW v_full_ost;
            CREATE TABLE v_full_ost ("Наименование" text, "Ед.изм." text, "Количество" text);
            INSERT INTO v_full_ost VALUES ('Null', 'pcs', NULL), ('Empty', 'pcs', '  '), ('Comma', 'pcs', ' 2,5 ');
            DELETE FROM app_schema_migrations WHERE migration_key = 'legacy_csv_bootstrap_v1';
            UPDATE app_import_sources SET initialized = false WHERE source_key = 'warehouse';
            """);
        await using var db = fixture.CreateContext();
        await CsvDatabaseMigration.InitializeAsync(db);
        Assert.Equal(0m, await fixture.StockAsync("Null"));
        Assert.Equal(0m, await fixture.StockAsync("Empty"));
        Assert.Equal(2.5m, await fixture.StockAsync("Comma"));
    }

    [Fact]
    public async Task Bootstrap_sums_matching_duplicates_before_clamping_without_persistent_snapshots()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await fixture.ExecuteAsync("""
            ALTER TABLE stock_fixture DROP CONSTRAINT stock_fixture_pkey;
            ALTER TABLE stock_fixture DROP CONSTRAINT stock_fixture_quantity_check;
            INSERT INTO stock_fixture VALUES (' Filter ', -2.5), ('Filter', 1);
            DELETE FROM app_stock_balances;
            DELETE FROM app_schema_migrations WHERE migration_key = 'legacy_csv_bootstrap_v1';
            UPDATE app_import_sources SET initialized = false WHERE source_key IN ('warehouse', 'mechanics');
            """);
        await using var db = fixture.CreateContext();
        await CsvDatabaseMigration.InitializeAsync(db);
        Assert.Equal(8.5m, await fixture.StockAsync());
        Assert.Equal(8.5m, await fixture.StockAsync("Filter", "v_meh_ost"));
        await using var source = fixture.CreateDataSource();
        await using var snapshot = source.CreateCommand("SELECT to_regclass('csv_bootstrap_snapshot') IS NULL");
        Assert.True((bool)(await snapshot.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("details")]
    public async Task Bootstrap_rejects_conflicting_duplicates_and_rolls_back(string conflict)
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await fixture.ExecuteAsync("""
            DROP VIEW v_full_ost;
            CREATE TABLE v_full_ost ("Наименование" text, "Ед.изм." text, "Количество" text, extra text);
            INSERT INTO v_full_ost VALUES ('Filter', 'pcs', '2', 'a');
            DELETE FROM app_schema_migrations WHERE migration_key = 'legacy_csv_bootstrap_v1';
            UPDATE app_import_sources SET initialized = false WHERE source_key = 'warehouse';
            """);
        await fixture.ExecuteAsync(conflict == "unit"
            ? "INSERT INTO v_full_ost VALUES (' Filter ', 'kg', '3', 'a')"
            : "INSERT INTO v_full_ost VALUES (' Filter ', 'pcs', '3', 'b')");
        await using var db = fixture.CreateContext();
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => CsvDatabaseMigration.InitializeAsync(db));
        Assert.Contains("Conflicting duplicate stock positions", error.MessageText);
        Assert.Equal(10m, await fixture.StockAsync());
        await using var source = fixture.CreateDataSource();
        await using var initialized = source.CreateCommand("SELECT initialized FROM app_import_sources WHERE source_key = 'warehouse'");
        Assert.False((bool)(await initialized.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bootstrap_clamps_negative_legacy_balances_and_does_not_reimport(bool numericViews)
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync(numericViews: numericViews);
        await fixture.ExecuteAsync("""
            ALTER TABLE stock_fixture DROP CONSTRAINT stock_fixture_quantity_check;
            UPDATE stock_fixture SET quantity = -2.5 WHERE name = 'Filter';
            INSERT INTO stock_fixture VALUES ('Zero', 0), ('Positive', 4.5);
            DELETE FROM app_stock_balances;
            DELETE FROM app_schema_migrations WHERE migration_key = 'legacy_csv_bootstrap_v1';
            UPDATE app_import_sources SET initialized = false WHERE source_key IN ('warehouse', 'mechanics');
            """);
        await using var db = fixture.CreateContext();
        await CsvDatabaseMigration.InitializeAsync(db);
        foreach (var sourceTable in new[] { "v_full_ost", "v_meh_ost" })
        {
            Assert.Equal(0m, await fixture.StockAsync("Filter", sourceTable));
            Assert.Equal(0m, await fixture.StockAsync("Zero", sourceTable));
            Assert.Equal(4.5m, await fixture.StockAsync("Positive", sourceTable));
        }
        Assert.Equal(-2.5m, await fixture.LegacyStockAsync());
        await using var source = fixture.CreateDataSource();
        await using var snapshot = source.CreateCommand("SELECT to_regclass('csv_bootstrap_snapshot') IS NULL");
        Assert.True((bool)(await snapshot.ExecuteScalarAsync())!);
        await fixture.ExecuteAsync("UPDATE app_stock_balances SET quantity = 3 WHERE name = 'Filter'");
        await CsvDatabaseMigration.InitializeAsync(db);
        Assert.Equal(3m, await fixture.StockAsync());
        Assert.Equal(3m, await fixture.StockAsync("Filter", "v_meh_ost"));
        Assert.True((bool)(await snapshot.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Worker_table_preserves_signature_status_join_and_search()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await fixture.ExecuteAsync("""
            CREATE TABLE employee_signatures (last_name text, first_name text, patronymic text);
            INSERT INTO employee_signatures VALUES ('Иванов', 'Иван', '');
            """);
        await using var source = fixture.CreateDataSource();
        await new CsvFileRepository(source).ReplaceAsync("p.csv", Encoding.UTF8.GetBytes("ФИО;Профессия\nИванов Иван;Кладовщик"), default);
        var table = await new TableViewRepository(source).QueryAsync(new("v_workers", 1, "20", "Иванов", null, "Иван", null, "Кладовщик", true), default);
        Assert.Contains("Роспись", table.Columns);
        Assert.True(((JsonElement)Assert.Single(table.Rows)["Роспись"]!).GetBoolean());
    }

    [Fact]
    public async Task Worker_bootstrap_accepts_legacy_employees_view_and_preserves_a_snapshot()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await fixture.ExecuteAsync("""
            CREATE TABLE employees ("FullName" text, "Profession" text);
            INSERT INTO employees VALUES ('Иванов Иван Иванович', 'Кладовщик');
            DELETE FROM app_schema_migrations WHERE migration_key = 'legacy_csv_bootstrap_v1';
            UPDATE app_import_sources SET initialized = false WHERE source_key = 'workers';
            """);
        await using var db = fixture.CreateContext();
        await CsvDatabaseMigration.InitializeAsync(db);
        var employees = await new ResponsibleEmployeeRepository(db).GetByProfessionAsync("Кладовщик", default);
        Assert.Equal("Иванович", Assert.Single(employees).Patronymic);
    }

    [Fact]
    public async Task Requirement_failure_after_first_balance_update_rolls_back_all_balances_and_document()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        var user = Guid.NewGuid();
        await fixture.ExecuteAsync("INSERT INTO app_users(id) VALUES (@id)", ("id", user));
        await fixture.ExecuteAsync("""
            INSERT INTO app_stock_balances(source_table, name, unit, quantity) VALUES ('v_full_ost', 'Second', 'pcs', 10);
            ALTER TABLE app_stock_balances ADD CONSTRAINT fail_second_write CHECK (name <> 'Second' OR quantity >= 5);
            """);
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => new RequirementJournalRepository(db).SaveAsync(user, "Author", "Issuer",
            new(new(2026, 10, 12), "94", "v_full_ost", new("Иван", "", "Иванов"), [new("Filter", "pcs", 2, 999), new("Second", "pcs", 7, 999)]), default));
        Assert.Equal(10m, await fixture.StockAsync());
        Assert.Equal(10m, await fixture.StockAsync("Second"));
        Assert.Equal(0L, await fixture.CountAsync());
    }

    [Fact]
    public async Task Snapshot_updates_existing_adds_new_zeroes_absent_and_is_repeatable()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await using var source = fixture.CreateDataSource();
        var repository = new CsvFileRepository(source);
        var content = Encoding.UTF8.GetBytes("name;unit;amount;price\nFilter;pcs;4.5;10\nNew;pcs;2;20");
        Assert.Equal(new CsvImportResult(1, 1), await repository.ReplaceAsync("o.csv", content, default));
        Assert.Equal(new CsvImportResult(0, 2), await repository.ReplaceAsync("o.csv", content, default));
        await repository.ReplaceAsync("o.csv", Encoding.UTF8.GetBytes("name;unit;amount\nNew;pcs;3"), default);
        var exported = CsvImportCodec.Parse("o.csv", await repository.GetAsync("o.csv", default));
        Assert.Equal(2, exported.Rows.Count);
        Assert.Equal(0m, exported.Rows.Single(row => row.Key == "Filter").Data["Количество"]);
        Assert.Equal("20", exported.Rows.Single(row => row.Key == "New").Data["price"]);
        Assert.Equal(3m, await fixture.StockAsync("New"));
        Assert.Equal(10m, await fixture.StockAsync("Filter", "v_meh_ost"));
        await using var db = fixture.CreateContext();
        await CsvDatabaseMigration.InitializeAsync(db);
        Assert.Equal(0m, await fixture.StockAsync());
        Assert.Equal(10m, await fixture.LegacyStockAsync());
        var table = await new TableViewRepository(source).QueryAsync(new("v_full_ost", 1, "20", null, null, null, null, null, false), default);
        Assert.Contains("price", table.Columns);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(0m, ((JsonElement)table.Rows.Single(row => ((JsonElement)row["Наименование"]!).GetString() == "Filter")["Количество"]!).GetDecimal());
    }

    [Fact]
    public async Task Import_rolls_back_all_rows_when_metadata_update_fails()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await fixture.ExecuteAsync("ALTER TABLE app_import_sources ADD CONSTRAINT fail_import CHECK (imported_at IS NULL)");
        await using var source = fixture.CreateDataSource();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => new CsvFileRepository(source).ReplaceAsync("o.csv",
            Encoding.UTF8.GetBytes("name;unit;amount\nFilter;pcs;4\nNew;pcs;2"), default));
        Assert.Equal(10m, await fixture.StockAsync());
        var exported = CsvImportCodec.Parse("o.csv", await new CsvFileRepository(source).GetAsync("o.csv", default));
        Assert.Single(exported.Rows);
    }

    [Fact]
    public async Task Worker_import_updates_profession_and_responsible_employee_directory()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await using var source = fixture.CreateDataSource();
        var repository = new CsvFileRepository(source);
        Assert.Equal(new CsvImportResult(1, 0), await repository.ReplaceAsync("p.csv", Encoding.UTF8.GetBytes("ФИО;Профессия\nИванов Иван;Механик"), default));
        Assert.Equal(new CsvImportResult(0, 1), await repository.ReplaceAsync("p.csv", Encoding.UTF8.GetBytes("ФИО;Профессия\nИванов Иван;Кладовщик"), default));
        await using var db = fixture.CreateContext();
        var employees = await new ResponsibleEmployeeRepository(db).GetByProfessionAsync("Кладовщик", default);
        Assert.Equal("Иванов", Assert.Single(employees).LastName);
        var table = await new TableViewRepository(source).QueryAsync(new("v_workers", 1, "20", null, null, null, null, "Кладовщик", false), default);
        Assert.Single(table.Rows);
    }

    [Fact]
    public async Task Concurrent_import_and_issue_are_serialized_and_export_contains_database_balance()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        await fixture.ExecuteAsync("INSERT INTO app_users(id) VALUES (@id)", ("id", userId));
        await using var source = fixture.CreateDataSource();
        var csv = new CsvFileRepository(source);
        async Task Issue() {
            await using var db = fixture.CreateContext();
            await new RequirementJournalRepository(db).SaveAsync(userId, "Author", "Issuer",
                new(new(2026, 10, 12), "94", "v_full_ost", new("Иван", "", "Иванов"), [new("Filter", "pcs", 2, 999)]), default);
        }
        await Task.WhenAll(Issue(), csv.ReplaceAsync("o.csv", Encoding.UTF8.GetBytes("name;unit;amount\nFilter;pcs;20"), default));
        var balance = await fixture.StockAsync();
        Assert.True(balance is 18m or 20m);
        var export = CsvImportCodec.Parse("o.csv", await csv.GetAsync("o.csv", default));
        Assert.Equal(balance, Assert.Single(export.Rows).Data["Количество"]);
        Assert.Equal(10m, await fixture.LegacyStockAsync());
        Assert.Equal(1L, await fixture.CountAsync());
    }
}
