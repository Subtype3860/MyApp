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
    /// <summary>Создаёт репозиторий на контексте тестовой схемы.</summary>
    private static VehicleRepository Repo(MyApp.Infrastructure.Db.AppDbContext db)
    {
        var options = new MediaStorageOptions(
            Path.GetTempPath(), Path.GetTempPath(),
            Path.GetTempPath(), Path.GetTempPath());
        return new VehicleRepository(db, new MediaStorageService(options), options);
    }


    /// <summary>Проверяет создание, изменение, чтение и удаление заявки, прикреплённой к технике.</summary>
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Vehicle_parts_request_allows_create_update_read_and_delete()
    {
        await using var fixture = await VehiclePartsPostgreSqlDatabase.CreateAsync();
        var (vehicle, _, user) = await fixture.SeedAsync();
        await using var db = fixture.CreateContext();
        var repository = Repo(db);
        var request = new VehiclePartsRequest(
            "PR-100", new DateOnly(2026, 10, 8), "Hydraulic filter", "2 pcs");

        var id = await repository.AddPartsRequestAsync(
            vehicle, request, user, CancellationToken.None);
        Xunit.Assert.NotNull(id);
        var created = await db.VehiclePartsRequests.AsNoTracking()
            .SingleAsync(item => item.Id == id, CancellationToken.None);
        Xunit.Assert.Equal(vehicle, created.VehicleId);
        Xunit.Assert.Equal("PR-100", created.RequestNumber);
        Xunit.Assert.Equal("Hydraulic filter", created.Description);

        var revised = request with {
            RequestNumber = "PR-101",
            Description = "Two hydraulic filters",
            RequiredParts = "3 pcs",
            CompletedDate = new DateOnly(2026, 10, 10)
        };
        Xunit.Assert.True(await repository.UpdatePartsRequestAsync(
            id.Value, revised, CancellationToken.None));
        var changed = await db.VehiclePartsRequests.AsNoTracking()
            .SingleAsync(item => item.Id == id, CancellationToken.None);
        Xunit.Assert.Equal("PR-101", changed.RequestNumber);
        Xunit.Assert.Equal("3 pcs", changed.RequiredParts);
        Xunit.Assert.Equal(new DateOnly(2026, 10, 10), changed.CompletedDate);

        Xunit.Assert.True(await repository.DeletePartsRequestAsync(
            id.Value, CancellationToken.None));
        Xunit.Assert.False(await repository.DeletePartsRequestAsync(
            id.Value, CancellationToken.None));
    }

    /// <summary>Проверяет, что заявку можно создать без этапа ремонта и статуса ожидания.</summary>
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Parts_request_does_not_require_repair_stage()
    {
        await using var fixture = await VehiclePartsPostgreSqlDatabase.CreateAsync();
        var (vehicle, _, user) = await fixture.SeedAsync("repaired");
        await using var db = fixture.CreateContext();
        var repository = Repo(db);
        var request = new VehiclePartsRequest(
            "PR-200", new DateOnly(2026, 10, 8), "Belts", "2 pcs");

        Xunit.Assert.NotNull(await repository.AddPartsRequestAsync(
            vehicle, request, user, CancellationToken.None));
        Xunit.Assert.Equal(1L, await fixture.CountAsync());
    }

    /// <summary>Проверяет, что одинаковые заявки для одной техники допускаются.</summary>
    [Xunit.Fact]
    [Xunit.Trait("Category", "Integration")]
    public async Task Duplicate_vehicle_parts_requests_are_allowed()
    {
        await using var fixture = await VehiclePartsPostgreSqlDatabase.CreateAsync();
        var (vehicle, _, user) = await fixture.SeedAsync("repaired");
        var request = new VehiclePartsRequest(
            "PR-300", new DateOnly(2026, 10, 8), "Bearing", "1 pc");

        await using (var db = fixture.CreateContext())
        {
            Xunit.Assert.NotNull(await Repo(db).AddPartsRequestAsync(
                vehicle, request, user, CancellationToken.None));
        }

        await using (var db = fixture.CreateContext())
        {
            Xunit.Assert.NotNull(await Repo(db).AddPartsRequestAsync(
                vehicle, request, user, CancellationToken.None));
        }
        Xunit.Assert.Equal(2L, await fixture.CountAsync());
    }
}
