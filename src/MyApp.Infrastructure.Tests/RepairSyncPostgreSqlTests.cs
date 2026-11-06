using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.API.Controllers;
using MyApp.Application.Common;
using MyApp.Application.Services;
using MyApp.Application.Storage;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Repositories;
using MyApp.Infrastructure.Storage;
using Xunit;

namespace MyApp.Infrastructure.Tests;
public sealed class RepairSyncPostgreSqlTests
{
    private static RepairSyncController Controller(AppDbContext db, Guid actor, bool failAfterWrite = false)
    {
        var options = new MediaStorageOptions(Path.GetTempPath(), Path.GetTempPath(), Path.GetTempPath(), Path.GetTempPath());
        var repo = new VehicleRepository(db, new MediaStorageService(options), options);
        var service = DispatchProxy.Create<IVehicleService, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)service).Handler = (method, args) => {
            Assert.Equal(nameof(IVehicleService.AddWorkAsync), method.Name);
            return Write((MyApp.Application.DTO.VehicleWorkRequest)args[1]!);
        };
        async Task<ServiceResult<Guid>> Write(MyApp.Application.DTO.VehicleWorkRequest work) {
            var id = await repo.CompleteDefectAsync(work.DefectId, work, actor, false, default);
            if (failAfterWrite) throw new InvalidOperationException("Synthetic failure before receipt");
            return ServiceResult<Guid>.Success(id!.Value);
        }
        return new(db, service) { ControllerContext = new() { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "vehicles.repair"), new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "test"))
        } } };
    }
    private static async Task Prepare(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE vehicle_defects
              ADD node_name text NOT NULL DEFAULT '', ADD failure_reason text NOT NULL DEFAULT '',
              ADD error_code text NOT NULL DEFAULT '', ADD symptoms text NOT NULL DEFAULT '',
              ADD assigned_to uuid, ADD repair_started_at timestamptz, ADD created_at timestamptz NOT NULL DEFAULT now();
            ALTER TABLE vehicle_works
              ADD work_kind text NOT NULL DEFAULT 'repair', ADD title text NOT NULL DEFAULT '',
              ADD work_date date NOT NULL DEFAULT current_date, ADD description text NOT NULL DEFAULT '',
              ADD location text NOT NULL DEFAULT '', ADD engine_hours numeric, ADD performer text NOT NULL DEFAULT '',
              ADD note text NOT NULL DEFAULT '', ADD failure_cause text NOT NULL DEFAULT '',
              ADD required_parts text NOT NULL DEFAULT '', ADD performed_by uuid, ADD completed_at timestamptz,
              ADD created_at timestamptz NOT NULL DEFAULT now();
            CREATE TABLE vehicle_work_participants (
              id uuid PRIMARY KEY, work_id uuid NOT NULL, user_id uuid NOT NULL, status text NOT NULL,
              requested_at timestamptz NOT NULL, reviewed_by uuid, reviewed_at timestamptz);
            """);
        await db.Database.ExecuteSqlRawAsync(RepairSyncSchema.Sql);
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Lost_response_replay_is_once_only_and_changed_payload_owner_or_version_conflicts()
    {
        await using var fixture = await VehiclePartsPostgreSqlDatabase.CreateAsync();
        var (vehicle, defect, actor) = await fixture.SeedAsync();
        await using var db = fixture.CreateContext(); await Prepare(db);
        var original = RepairSyncTests.Command();
        var command = original with { VehicleId = vehicle, DefectId = defect, Version = await RepairSyncSchema.Version(db, vehicle, default), Work = original.Work! with { DefectId = defect } };
        var controller = Controller(db, actor);
        var before = await db.VehicleWorks.CountAsync();
        Assert.IsType<OkObjectResult>(await controller.Execute(command, default));
        await using var reconnect = fixture.CreateContext();
        Assert.IsType<OkObjectResult>(await Controller(reconnect, actor).Execute(command, default));
        Assert.Equal(before + 1, await reconnect.VehicleWorks.CountAsync());
        Assert.Single(await reconnect.RepairOperationReceipts.ToListAsync());
        Assert.IsType<ConflictObjectResult>(await Controller(reconnect, actor).Execute(command with { Work = command.Work! with { Description = "Другой текст" } }, default));
        Assert.IsType<ConflictObjectResult>(await Controller(reconnect, Guid.NewGuid()).Execute(command, default));
        Assert.IsType<ConflictObjectResult>(await Controller(reconnect, actor).Execute(command with { OperationId = Guid.NewGuid() }, default));
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Failed_command_rolls_back_work_participant_and_receipt_together()
    {
        await using var fixture = await VehiclePartsPostgreSqlDatabase.CreateAsync();
        var (vehicle, defect, actor) = await fixture.SeedAsync();
        await using var db = fixture.CreateContext(); await Prepare(db);
        var c = RepairSyncTests.Command();
        c = c with { VehicleId = vehicle, DefectId = defect, Version = await RepairSyncSchema.Version(db, vehicle, default), Work = c.Work! with { DefectId = defect } };
        var before = await db.VehicleWorks.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Controller(db, actor, true).Execute(c, default));
        await using var fresh = fixture.CreateContext();
        Assert.Equal(before, await fresh.VehicleWorks.CountAsync());
        Assert.Empty(await fresh.VehicleWorkParticipants.ToListAsync());
        Assert.Empty(await fresh.RepairOperationReceipts.ToListAsync());
    }
}
