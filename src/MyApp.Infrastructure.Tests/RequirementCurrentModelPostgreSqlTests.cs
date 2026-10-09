using Microsoft.EntityFrameworkCore;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Repositories;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

public sealed class RequirementCurrentModelPostgreSqlTests
{
    private static ComponentDocumentRequest Request(
        params ComponentDocumentItem[] items) =>
        new(
            new DateOnly(2026, 10, 8),
            "Truck A",
            "v_full_ost",
            new ResponsibleEmployeeSelection("Ivan", "", "Ivanov"),
            items);

    private static ComponentDocumentItem Item(
        string name, decimal quantity, decimal clientBalance = 999m) =>
        new(name, "pcs", quantity, clientBalance);

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Uses_actual_stock_aggregates_duplicates_and_restores_once()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        var user = Guid.NewGuid();
        await fixture.ExecuteAsync(
            "INSERT INTO app_users(id) VALUES (@id)", ("id", user));
        await using var db = fixture.CreateContext();
        var repository = new RequirementJournalRepository(db);

        await repository.SaveAsync(
            user, "Author", "Issuer",
            Request(Item("Filter", 2m), Item(" Filter ", 3m)),
            CancellationToken.None);

        Xunit.Assert.Equal(5m, await fixture.StockAsync());
        Xunit.Assert.Equal(1L, await fixture.CountAsync());
        var entry = Xunit.Assert.Single(
            await repository.GetRecentAsync(CancellationToken.None));
        Xunit.Assert.Equal(2, entry.Items.Count);

        Xunit.Assert.True(await repository.DeleteAsync(entry.Id, CancellationToken.None));
        Xunit.Assert.Equal(10m, await fixture.StockAsync());
        Xunit.Assert.False(await repository.DeleteAsync(entry.Id, CancellationToken.None));
        Xunit.Assert.Equal(10m, await fixture.StockAsync());
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Numeric_stock_views_are_accepted_when_issuing_and_restoring()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync(
            numericViews: true);
        var user = Guid.NewGuid();
        await fixture.ExecuteAsync(
            "INSERT INTO app_users(id) VALUES (@id)", ("id", user));

        Guid id;
        await using (var db = fixture.CreateContext())
        {
            var repository = new RequirementJournalRepository(db);
            await repository.SaveAsync(
                user, "Author", "Issuer",
                Request(Item("Filter", 2m)), CancellationToken.None);
            id = Xunit.Assert.Single(
                await repository.GetRecentAsync(CancellationToken.None)).Id;
        }
        Xunit.Assert.Equal(8m, await fixture.StockAsync());

        await using (var db = fixture.CreateContext())
        {
            var repository = new RequirementJournalRepository(db);
            Xunit.Assert.True(await repository.DeleteAsync(
                id, CancellationToken.None));
        }
        Xunit.Assert.Equal(10m, await fixture.StockAsync());
        Xunit.Assert.Equal(0L, await fixture.CountAsync());
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Numeric_stock_views_reject_overdraw_before_writing()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync(
            stock: 2m, numericViews: true);
        var user = Guid.NewGuid();
        await fixture.ExecuteAsync(
            "INSERT INTO app_users(id) VALUES (@id)", ("id", user));
        await using var db = fixture.CreateContext();
        var repository = new RequirementJournalRepository(db);

        var error = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(
                user, "Author", "Issuer", Request(Item("Filter", 3m)),
                CancellationToken.None));
        Xunit.Assert.Contains("Недостаточный остаток", error.Message);
        Xunit.Assert.Equal(2m, await fixture.StockAsync());
        Xunit.Assert.Equal(0L, await fixture.CountAsync());
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Stale_client_balance_cannot_overdraw_stock()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync(2m);
        var user = Guid.NewGuid();
        await fixture.ExecuteAsync(
            "INSERT INTO app_users(id) VALUES (@id)", ("id", user));
        await using var db = fixture.CreateContext();
        var repository = new RequirementJournalRepository(db);

        var error = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(
                user, "Author", "Issuer", Request(Item("Filter", 3m, 500m)),
                CancellationToken.None));
        Xunit.Assert.Contains("Недостаточный остаток", error.Message);
        Xunit.Assert.Equal(2m, await fixture.StockAsync());
        Xunit.Assert.Equal(0L, await fixture.CountAsync());
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Missing_second_material_fails_before_mutating_any_stock()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        var user = Guid.NewGuid();
        await fixture.ExecuteAsync(
            "INSERT INTO app_users(id) VALUES (@id)", ("id", user));
        await using var db = fixture.CreateContext();
        var repository = new RequirementJournalRepository(db);

        await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.SaveAsync(
                user, "Author", "Issuer",
                Request(Item("Filter", 2m), Item("Unknown", 1m)),
                CancellationToken.None));
        Xunit.Assert.Equal(10m, await fixture.StockAsync());
        Xunit.Assert.Equal(0L, await fixture.CountAsync());
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Invalid_author_fails_on_database_constraint_before_csv_update()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var repository = new RequirementJournalRepository(db);

        var error = await Xunit.Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.SaveAsync(
                Guid.NewGuid(), "Author", "Issuer",
                Request(Item("Filter", 3m)),
                CancellationToken.None));
        var databaseError = Xunit.Assert.IsType<PostgresException>(error.InnerException);
        Xunit.Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, databaseError.SqlState);
        Xunit.Assert.Equal(10m, await fixture.StockAsync());
        Xunit.Assert.Equal(0L, await fixture.CountAsync());
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Concurrent_issues_cannot_overdraw_one_stock_source()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        var user = Guid.NewGuid();
        await fixture.ExecuteAsync(
            "INSERT INTO app_users(id) VALUES (@id)", ("id", user));
        var request = Request(Item("Filter", 7m));

        async Task<bool> TryIssueAsync()
        {
            await using var db = fixture.CreateContext();
            try
            {
                await new RequirementJournalRepository(db).SaveAsync(
                    user, "Author", "Issuer", request, CancellationToken.None);
                return true;
            }
            catch (InvalidOperationException e)
                when (e.Message.Contains("Недостаточный остаток"))
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryIssueAsync(), TryIssueAsync());
        Xunit.Assert.Single(results, result => result);
        Xunit.Assert.Single(results, result => !result);
        Xunit.Assert.Equal(3m, await fixture.StockAsync());
        Xunit.Assert.Equal(1L, await fixture.CountAsync());
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Concurrent_deletions_do_not_restore_stock_twice()
    {
        await using var fixture = await RequirementPostgreSqlDatabase.CreateAsync();
        var user = Guid.NewGuid();
        await fixture.ExecuteAsync(
            "INSERT INTO app_users(id) VALUES (@id)", ("id", user));
        Guid id;
        await using (var db = fixture.CreateContext())
        {
            var repo = new RequirementJournalRepository(db);
            await repo.SaveAsync(
                user, "Author", "Issuer", Request(Item("Filter", 4m)),
                CancellationToken.None);
            id = Xunit.Assert.Single(
                await repo.GetRecentAsync(CancellationToken.None)).Id;
        }

        async Task<bool> DeleteAsync()
        {
            await using var db = fixture.CreateContext();
            return await new RequirementJournalRepository(db).DeleteAsync(
                id, CancellationToken.None);
        }
        var results = await Task.WhenAll(DeleteAsync(), DeleteAsync());
        Xunit.Assert.Single(results, result => result);
        Xunit.Assert.Single(results, result => !result);
        Xunit.Assert.Equal(10m, await fixture.StockAsync());
        Xunit.Assert.Equal(0L, await fixture.CountAsync());
    }
}
