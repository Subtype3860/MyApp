using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Repositories;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

public sealed class MaterialGroupPostgreSqlTests
{
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Groups_items_mappings_and_cascade_delete_use_existing_tables()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var repository = new MaterialGroupRepository(context, database.DataSource);
        var groupId = await repository.CreateGroupAsync("Hydraulics", CancellationToken.None);
        var otherId = await repository.CreateGroupAsync("Electrics", CancellationToken.None);

        Xunit.Assert.True(await repository.GroupExistsAsync(groupId, CancellationToken.None));
        Xunit.Assert.True(await repository.GroupNameExistsAsync(
            " hydraulics ", CancellationToken.None));
        Xunit.Assert.False(await repository.GroupExistsAsync(Guid.NewGuid(), CancellationToken.None));

        var firstId = await repository.AddItemAsync(
            groupId, "v_full_ost", "Hydraulic pump", CancellationToken.None);
        await repository.AddItemAsync(
            groupId, "v_meh_ost", "O-ring", CancellationToken.None);
        await repository.AddItemAsync(
            otherId, "v_full_ost", "Alternator", CancellationToken.None);

        Xunit.Assert.True(await repository.MaterialIsAssignedAsync(
            "v_full_ost", " Hydraulic pump ", CancellationToken.None));
        Xunit.Assert.False(await repository.MaterialIsAssignedAsync(
            "v_meh_ost", "Hydraulic pump", CancellationToken.None));

        var all = await repository.GetAllAsync(CancellationToken.None);
        Xunit.Assert.Equal(new[] { "Electrics", "Hydraulics" },
            all.Select(group => group.Name).ToArray());
        var hydraulics = Xunit.Assert.Single(all, group => group.Id == groupId);
        Xunit.Assert.Equal(2, hydraulics.Items.Count);

        var mappings = await repository.GetMappingsAsync(
            "v_full_ost", CancellationToken.None);
        Xunit.Assert.Equal(
            new[] { "Alternator", "Hydraulic pump" },
            mappings.Select(item => item.MaterialName).ToArray());

        Xunit.Assert.True(await repository.DeleteItemAsync(firstId, CancellationToken.None));
        Xunit.Assert.False(await repository.DeleteItemAsync(firstId, CancellationToken.None));
        Xunit.Assert.True(await repository.DeleteGroupAsync(groupId, CancellationToken.None));
        Xunit.Assert.False(await repository.DeleteGroupAsync(groupId, CancellationToken.None));
        Xunit.Assert.DoesNotContain(
            await repository.GetAllAsync(CancellationToken.None),
            group => group.Id == groupId);
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Material_lookup_retains_safe_postgresql_view_access()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var repository = new MaterialGroupRepository(context, database.DataSource);
        await database.ExecuteAsync(
            """
            CREATE VIEW v_full_ost AS SELECT 'Hydraulic pump'::text AS "Наименование";
            CREATE VIEW v_meh_ost AS SELECT 'O-ring'::text AS "Наименование";
            """);

        Xunit.Assert.True(await repository.MaterialExistsAsync(
            "v_full_ost", " Hydraulic pump ", CancellationToken.None));
        Xunit.Assert.False(await repository.MaterialExistsAsync(
            "v_meh_ost", "Hydraulic pump", CancellationToken.None));
        Xunit.Assert.False(await repository.MaterialExistsAsync(
            "v_full_ost; DROP TABLE material_groups", "Hydraulic pump",
            CancellationToken.None));
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Normalized_duplicate_names_are_rejected_by_database()
    {
        await using var database = await PostgreSqlIntegrationDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var repository = new MaterialGroupRepository(context, database.DataSource);
        await repository.CreateGroupAsync("Hydraulics", CancellationToken.None);

        var error = await Xunit.Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.CreateGroupAsync(" hydraulics ", CancellationToken.None));
        var postgres = Xunit.Assert.IsType<PostgresException>(error.InnerException);
        Xunit.Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    }
}
