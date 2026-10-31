using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.API.Controllers;
using MyApp.Infrastructure.Db;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class LocalAdvicePostgreSqlTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Explicit_outcome_is_persisted_once_and_never_overwrites_existing_fact()
    {
        await using var fixture = await VehiclePartsPostgreSqlDatabase.CreateAsync();
        var (vehicle, _, actor) = await fixture.SeedAsync();
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync(LocalAdviceSchema.Sql);
        await db.Database.ExecuteSqlRawAsync(LocalAdviceSchema.Sql); // idempotent initialization
        var controller = LocalAdviceTests.OutcomeController(db, actor, true);
        var request = new LocalAdviceController.OutcomeRequest(new DateOnly(2026, 1, 1), 1, true, true);
        Assert.IsType<OkObjectResult>(await controller.ConfirmOutcome(vehicle, request, default));
        Assert.IsType<ConflictObjectResult>(await controller.ConfirmOutcome(vehicle, request with { ReadyWholeShift = false }, default));
        var saved = await db.VehicleShiftOutcomes.SingleAsync();
        Assert.True(saved.ReadyWholeShift); Assert.Equal(actor, saved.ConfirmedBy);
        Assert.IsType<NotFoundResult>(await controller.ConfirmOutcome(Guid.NewGuid(), request, default));
    }
}
