using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.API.Controllers;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using Xunit;

namespace MyApp.Infrastructure.Tests;
public sealed class RepairSyncTests
{
    public static RepairSyncController.Command Command() {
        var defect = Guid.NewGuid();
        return new(Guid.NewGuid(), Guid.NewGuid(), defect, new string('A',64), "work",
            new VehicleWorkRequest(defect, "Износ", "Замена", "awaiting_parts", "Фильтр", DateTimeOffset.Parse("2026-01-01T11:00:00+11:00"), "operating", null, "РММ"));
    }
    [Fact]
    public void Commands_require_stable_identity_expected_version_and_matching_defect()
    {
        var c = Command(); Assert.True(RepairSyncController.Valid(c));
        foreach (var invalid in new[] { c with { OperationId = Guid.Empty }, c with { Version = "" }, c with { Kind = "unknown" },
            c with { Work = c.Work! with { DefectId = Guid.NewGuid() } }, c with { Work = c.Work! with { RepairDateTime = null } }, c with { Work = c.Work! with { Location = "" } } })
            Assert.False(RepairSyncController.Valid(invalid));
        Assert.True(RepairSyncController.Valid(c with { Kind = "claim", Work = null }));
        Assert.Equal(RepairSyncSchema.Hash(c), RepairSyncSchema.Hash(c with { }));
        Assert.NotEqual(RepairSyncSchema.Hash(c), RepairSyncSchema.Hash(c with { Work = c.Work! with { Description = "Другая работа" } }));
    }
    [Fact]
    public async Task Missing_repair_permission_is_rejected_before_database_access()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=unused_test").Options);
        var controller = new RepairSyncController(db, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "menu.vehicles")], "test"))
        } } };
        Assert.IsType<ForbidResult>(await controller.Snapshot(default));
        Assert.IsType<ForbidResult>(await controller.Execute(Command(), default));
    }
}
