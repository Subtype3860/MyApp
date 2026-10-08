using Microsoft.EntityFrameworkCore;
using MyApp.Application.DTO;
using MyApp.Application.Storage;
using MyApp.Infrastructure.Repositories;
using MyApp.Infrastructure.Storage;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

/// <summary>
/// Protects edit/delete parts-request functionality that was added to master
/// independently of auth-navigation-shell and already exists in this API.
/// </summary>
public sealed class VehiclePartsCurrentModelPostgreSqlTests
{
    private static VehicleRepository Repo(MyApp.Infrastructure.Db.AppDbContext db)
    {
        var options = new MediaStorageOptions(
            Path.GetTempPath(), Path.GetTempPath(),
            Path.GetTempPath(), Path.GetTempPath());
        return new VehicleRepository(db, new MediaStorageService(options), options);
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Awaiting_parts_allows_create_update_read_and_delete()
    {
        await using var fixture = await VehiclePartsPostgreSqlDatabase.CreateAsync();
        var (_, defect, user) = await fixture.SeedAsync();
        await using var db = fixture.CreateContext();
        var repository = Repo(db);
        var request = new VehiclePartsRequest(
            "PR-100", new DateOnly(2026, 10, 8), "Hydraulic filter", "2 pcs");

        var id = await repository.AddPartsRequestAsync(
            defect, request, user, CancellationToken.None);
        Xunit.Assert.NotNull(id);
        var created = Xunit.Assert.Single(await repository.GetPartsRequestsAsync(
            defect, CancellationToken.None));
        Xunit.Assert.Equal(id.Value, created.Id);
        Xunit.Assert.Equal("PR-100", created.RequestNumber);
        Xunit.Assert.Equal("Hydraulic filter", created.Description);

        var revised = request with {
            RequestNumber = "PR-101",
            Description = "Two hydraulic filters",
            RequiredParts = "3 pcs"
        };
        Xunit.Assert.True(await repository.UpdatePartsRequestAsync(
            id.Value, revised, CancellationToken.None));
        var changed = Xunit.Assert.Single(await repository.GetPartsRequestsAsync(
            defect, CancellationToken.None));
        Xunit.Assert.Equal("PR-101", changed.RequestNumber);
        Xunit.Assert.Equal("3 pcs", changed.RequiredParts);

        Xunit.Assert.True(await repository.DeletePartsRequestAsync(
            id.Value, CancellationToken.None));
        Xunit.Assert.False(await repository.DeletePartsRequestAsync(
            id.Value, CancellationToken.None));
        Xunit.Assert.Empty(await repository.GetPartsRequestsAsync(
            defect, CancellationToken.None));
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Parts_request_requires_awaiting_parts_repair_status()
    {
        await using var fixture = await VehiclePartsPostgreSqlDatabase.CreateAsync();
        var (_, defect, user) = await fixture.SeedAsync("repaired");
        await using var db = fixture.CreateContext();
        var repository = Repo(db);
        var request = new VehiclePartsRequest(
            "PR-200", new DateOnly(2026, 10, 8), "Belts", "2 pcs");

        Xunit.Assert.Null(await repository.AddPartsRequestAsync(
            defect, request, user, CancellationToken.None));
        Xunit.Assert.Equal(0L, await fixture.CountAsync());

        await fixture.ExecuteAsync(
            "UPDATE vehicle_works SET repair_status = 'awaiting_parts' WHERE defect_id = @defect",
            ("defect", defect));
        Xunit.Assert.NotNull(await repository.AddPartsRequestAsync(
            defect, request, user, CancellationToken.None));
        Xunit.Assert.Equal(1L, await fixture.CountAsync());
    }

    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Duplicate_parts_request_preserves_existing_uniqueness_rule()
    {
        await using var fixture = await VehiclePartsPostgreSqlDatabase.CreateAsync();
        var (_, defect, user) = await fixture.SeedAsync();
        var request = new VehiclePartsRequest(
            "PR-300", new DateOnly(2026, 10, 8), "Bearing", "1 pc");

        await using (var db = fixture.CreateContext())
        {
            Xunit.Assert.NotNull(await Repo(db).AddPartsRequestAsync(
                defect, request, user, CancellationToken.None));
        }

        await using (var db = fixture.CreateContext())
        {
            var error = await Xunit.Assert.ThrowsAsync<DbUpdateException>(() =>
                Repo(db).AddPartsRequestAsync(
                    defect, request, user, CancellationToken.None));
            var pg = Xunit.Assert.IsType<PostgresException>(error.InnerException);
            Xunit.Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
        }
        Xunit.Assert.Equal(1L, await fixture.CountAsync());
    }
}
