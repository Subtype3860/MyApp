using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.API.Controllers;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Infrastructure.Db;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class MaintenancePlanPersistenceTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static IVehicleService Service(Guid id, bool allowed)
    {
        var proxy=DispatchProxy.Create<IVehicleService,WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)proxy).Handler=(method,args)=>method.Name switch {
            nameof(IVehicleService.GetVehiclesAsync)=>Task.FromResult<IReadOnlyList<VehicleResponse>>([new(id,"Группа","Экскаватор","SET",901,"","")]),
            nameof(IVehicleService.GetVehicleCatalogOptionsAsync)=>Task.FromResult(new VehicleCatalogOptions(allowed,[],[],[])),
            _=>throw new InvalidOperationException(method.Name)
        };
        return proxy;
    }
    private static MaintenancePlansController Controller(AppDbContext db, Guid id, bool allowed=true) =>
        new(db,Service(id,allowed)) {
            ControllerContext=new ControllerContext { HttpContext=new DefaultHttpContext {
                User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,Actor.ToString())],"test"))
            }}
        };

    [Fact]
    public async Task Viewer_cannot_save_or_complete_even_with_an_editable_payload()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var controller=Controller(db,Guid.NewGuid(),false);
        Assert.IsType<ForbidResult>(await controller.Save(Guid.NewGuid(),new(0,null!),default));
        Assert.IsType<ForbidResult>(await controller.Complete(Guid.NewGuid(),new(0,default,[],""),default));
    }

    [Fact, Trait("Category","Integration")]
    public async Task Persistence_preserves_history_checks_versions_and_reads_latest_engine_hours()
    {
        await using var fixture=await VehicleHoursPostgreSqlDatabase.CreateAsync();
        var id=Guid.NewGuid();var date=new DateOnly(2026,1,1);
        await fixture.ExecuteAsync("INSERT INTO app_users(id) VALUES(@actor); INSERT INTO number_car(id) VALUES(@vehicle);",("actor",Actor),("vehicle",id));
        await using(var db=fixture.CreateContext()){
            await MaintenancePlanSchema.EnsureAsync(db);
            await MaintenancePlanSchema.EnsureAsync(db);
            await fixture.ExecuteAsync("INSERT INTO vehicle_hour_readings(id,vehicle_id,reading_date,engine_hours,engine_number,created_by) VALUES(@id,@vehicle,@date,250,1,@actor);", ("id",Guid.NewGuid()),("vehicle",id),("date",date),("actor",Actor));
            await fixture.ExecuteAsync("INSERT INTO vehicle_hour_readings(id,vehicle_id,reading_date,reading_time,engine_hours,engine_number,created_by) VALUES(@id,@vehicle,@date,'10:00',530,1,@actor),(@older,@vehicle,@date,'08:00',520,1,@actor);", ("id",Guid.NewGuid()),("older",Guid.NewGuid()),("vehicle",id),("date",date.AddDays(1)),("actor",Actor));
            var plan=new MaintenancePlan("График",[new("ТО-250",250),new("ТО-500",250)],1,date,"ТО-250",[new(1,250)],[]);
            Assert.IsType<OkObjectResult>(await Controller(db,id).Save(id,new(0,plan),default));
        }
        await using(var db=fixture.CreateContext()){
            var plan=JsonSerializer.Deserialize<MaintenancePlan>((await db.VehicleMaintenancePlans.SingleAsync()).Snapshot,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.IsType<ConflictObjectResult>(await Controller(db,id).Save(id,new(0,plan),default));
            Assert.Equal(1,await db.VehicleMaintenanceEvents.CountAsync());
        }
        await using(var db=fixture.CreateContext()){
            Assert.IsType<BadRequestObjectResult>(await Controller(db,id).Complete(id,new(1,date.AddDays(-1),[new(1,600)],"Нет показаний за дату"),default));
            var operationId = Guid.NewGuid();
            var completion = new MaintenanceCompletion(1,date.AddDays(1),[new(1,99999)],"Масла заменены",OperationId:operationId);
            Assert.IsType<OkObjectResult>(await Controller(db,id).Complete(id,completion,default));
            // A lost response followed by a retry must not advance the cycle twice.
            var retried = Assert.IsType<OkObjectResult>(await Controller(db,id).Complete(id,completion,default));
            Assert.Equal(2,JsonSerializer.SerializeToElement(retried.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("revision").GetInt32());
            Assert.Equal(2,await db.VehicleMaintenanceEvents.CountAsync());
            Assert.IsType<ConflictObjectResult>(await Controller(db,id).Complete(id,completion with { Description="Изменённый запрос" },default));
            Assert.IsType<ConflictObjectResult>(await Controller(db,id).Complete(id,completion with { OperationId=Guid.NewGuid() },default));
        }
        await fixture.ExecuteAsync("INSERT INTO vehicle_hour_readings(id,vehicle_id,reading_date,engine_hours,engine_number,created_by) VALUES(@id,@vehicle,@date,560,1,@actor);",
            ("id",Guid.NewGuid()),("vehicle",id),("date",date.AddDays(2)),("actor",Actor));
        await using(var db=fixture.CreateContext()){
            var history=await db.VehicleMaintenanceEvents.OrderBy(e=>e.Revision).ToArrayAsync();
            Assert.Equal(2,history.Length);
            Assert.Contains("ТО-250",history[0].After);
            Assert.Equal(history[0].After,history[1].Before);
            var response=Assert.IsType<OkObjectResult>(await Controller(db,id).List(default));
            var json=JsonSerializer.SerializeToElement(response.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var due=json.GetProperty("rows")[0].GetProperty("due")[0];
            Assert.Equal(780,due.GetProperty("dueHours").GetDecimal());
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Sakhalin")));
            var estimated = MaintenancePlanner.Estimate(1,560,date.AddDays(2),today);
            Assert.Equal(780-estimated.Hours,due.GetProperty("remaining").GetDecimal());
            Assert.True(due.GetProperty("isEstimated").GetBoolean());
        }
    }
}

