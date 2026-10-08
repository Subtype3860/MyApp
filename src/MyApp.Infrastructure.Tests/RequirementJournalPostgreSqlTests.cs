using MyApp.Application.DTO;
using MyApp.Infrastructure.Repositories;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

public sealed class RequirementJournalPostgreSqlTests
{
    // This substitute edit_csv_tab changes a PostgreSQL table, NOT an external
    // CSV file. Rollback assertions therefore cover DB transactions only.
    private static async Task SetupTransactionalStockStubAsync(
        PostgreSqlIntegrationDatabase database, decimal initialQuantity)
    {
        await database.ExecuteAsync(
            """
            CREATE TABLE stock_fixture (
                name text PRIMARY KEY,
                quantity numeric NOT NULL
            );
            CREATE VIEW v_full_ost AS
                SELECT name AS "Наименование", quantity AS "Количество"
                FROM stock_fixture;
            CREATE VIEW full_ost AS
                SELECT name, quantity AS amount FROM stock_fixture;
            CREATE FUNCTION edit_csv_tab(
                file_name text, search_text text, new_value numeric)
            RETURNS text LANGUAGE plpgsql AS $body$
            BEGIN
                IF file_name <> 'o' THEN
                    RETURN 'Неизвестный источник';
                END IF;
                UPDATE stock_fixture
                SET quantity = new_value
                WHERE BTRIM(name) = BTRIM(search_text);
                IF NOT FOUND THEN
                    RETURN 'Компонент не найден';
                END IF;
                RETURN 'Успешно обновлено!';
            END;
            $body$;
            """);
        await database.ExecuteAsync(
            "INSERT INTO stock_fixture (name, quantity) VALUES ('Filter', @quantity)",
            ("quantity", initialQuantity));
    }

    private static ComponentDocumentRequest Request(
        params ComponentDocumentItem[] items) =>
        new(
            new DateOnly(2026, 10, 8),
            "A-21",
            "v_full_ost",
            new ResponsibleEmployeeSelection("Ivan", "", "Ivanov"),
            items);

    private static RequirementJournalRepository CreateRepository(
        PostgreSqlIntegrationDatabase database) =>
        new(database.DataSource, new RequirementStockGateway());

    private static async Task<decimal> GetStockAsync(
        PostgreSqlIntegrationDatabase database)
    {
        await using var command = database.DataSource.CreateCommand(
            "SELECT quantity FROM stock_fixture WHERE name = 'Filter'");
        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountRequirementsAsync(
        PostgreSqlIntegrationDatabase database)
    {
        await using var command = database.DataSource.CreateCommand(
            "SELECT COUNT(*) FROM component_requirements");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Save_uses_actual_stock_and_aggregates_duplicate_positions()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await SetupTransactionalStockStubAsync(database, 10m);
        var userId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", userId));
        var repository = CreateRepository(database);

        // Both stale client-side values are deliberately much larger than stock.
        await repository.SaveAsync(
            userId, "Author", "Issuer",
            Request(
                new ComponentDocumentItem("Filter", "pcs", 2m, 999m),
                new ComponentDocumentItem(" Filter ", "pcs", 3m, 999m)),
            CancellationToken.None);

        Xunit.Assert.Equal(5m, await GetStockAsync(database));
        Xunit.Assert.Equal(1L, await CountRequirementsAsync(database));

        var entries = await repository.GetRecentAsync(CancellationToken.None);
        var entry = Xunit.Assert.Single(entries);
        Xunit.Assert.Equal(2, entry.Items.Count);
        var storedRequest = await repository.GetDocumentRequestAsync(
            entry.Id, CancellationToken.None);
        Xunit.Assert.NotNull(storedRequest);
        Xunit.Assert.Equal(2, storedRequest.Items.Count);

        Xunit.Assert.True(await repository.DeleteAsync(
            entry.Id, CancellationToken.None));
        Xunit.Assert.Equal(10m, await GetStockAsync(database));
        Xunit.Assert.False(await repository.DeleteAsync(
            entry.Id, CancellationToken.None));
        Xunit.Assert.Equal(10m, await GetStockAsync(database));
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Save_rejects_insufficient_stock_before_writing_anything()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await SetupTransactionalStockStubAsync(database, 2m);
        var userId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", userId));
        var repository = CreateRepository(database);

        var error = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(
                userId, "Author", "Issuer",
                Request(new ComponentDocumentItem("Filter", "pcs", 5m, 500m)),
                CancellationToken.None));
        Xunit.Assert.Contains("Недостаточный остаток", error.Message);
        Xunit.Assert.Equal(2m, await GetStockAsync(database));
        Xunit.Assert.Equal(0L, await CountRequirementsAsync(database));
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Invalid_author_fails_before_legacy_stock_function_is_called()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await SetupTransactionalStockStubAsync(database, 10m);
        var repository = CreateRepository(database);

        var exception = await Xunit.Assert.ThrowsAsync<PostgresException>(
            () => repository.SaveAsync(
                Guid.NewGuid(), "Author", "Issuer",
                Request(new ComponentDocumentItem("Filter", "pcs", 3m, 10m)),
                CancellationToken.None));
        Xunit.Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Xunit.Assert.Equal(10m, await GetStockAsync(database));
        Xunit.Assert.Equal(0L, await CountRequirementsAsync(database));
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Concurrent_requests_cannot_overdraw_the_same_stock_source()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await SetupTransactionalStockStubAsync(database, 10m);
        var userId = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO app_users (id) VALUES (@id)", ("id", userId));
        var repository = CreateRepository(database);
        var request = Request(
            new ComponentDocumentItem("Filter", "pcs", 7m, 100m));

        async Task<bool> TrySaveAsync()
        {
            try
            {
                await repository.SaveAsync(
                    userId, "Author", "Issuer", request, CancellationToken.None);
                return true;
            }
            catch (InvalidOperationException error)
                when (error.Message.Contains("Недостаточный остаток"))
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TrySaveAsync(), TrySaveAsync());
        Xunit.Assert.Single(results, succeeded => succeeded);
        Xunit.Assert.Single(results, succeeded => !succeeded);
        Xunit.Assert.Equal(3m, await GetStockAsync(database));
        Xunit.Assert.Equal(1L, await CountRequirementsAsync(database));
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Unknown_stock_source_is_rejected_without_database_mutation()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        var repository = CreateRepository(database);

        var request = Request(new ComponentDocumentItem(
            "Filter", "pcs", 1m, 10m)) with
        {
            SourceTable = "v_full_ost; DROP TABLE component_requirements"
        };
        await Xunit.Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(
                Guid.NewGuid(), "Author", "Issuer",
                request, CancellationToken.None));
        Xunit.Assert.Equal(0L, await CountRequirementsAsync(database));
    }
}
