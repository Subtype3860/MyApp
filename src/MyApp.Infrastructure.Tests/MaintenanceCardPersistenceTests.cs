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
using MyApp.Infrastructure.Db.Entities;
using Xunit;

namespace MyApp.Infrastructure.Tests;
public sealed class MaintenanceCardPersistenceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Actor=Guid.NewGuid();
    private static MaintenancePlansController Controller(AppDbContext db, Guid vehicle, bool manage=true)
    {
        var proxy=DispatchProxy.Create<IVehicleService,WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)proxy).Handler=(method,args)=>method.Name switch {
            nameof(IVehicleService.GetVehiclesAsync)=>Task.FromResult<IReadOnlyList<VehicleResponse>>([new(vehicle,"Группа","Экскаватор","SANY SY1250H",98,"","")]),
            nameof(IVehicleService.GetVehicleCatalogOptionsAsync)=>Task.FromResult(new VehicleCatalogOptions(manage,[],[],[])),
            _=>throw new InvalidOperationException(method.Name)
        };
        return new(db,proxy) {ControllerContext=new() {HttpContext=new DefaultHttpContext {User=new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,Actor.ToString())],"test"))}}};
    }
    [Fact] public async Task Viewers_cannot_mutate_cards_or_templates()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var controller=Controller(db,Guid.NewGuid(),false);
        Assert.IsType<ForbidResult>(await controller.StartCard(Guid.NewGuid(),new(Guid.NewGuid(),Guid.NewGuid(),1,1),default));
        Assert.IsType<ForbidResult>(await controller.SaveCard(Guid.NewGuid(),new(1,null!),default));
        Assert.IsType<ForbidResult>(await controller.ReturnCard(Guid.NewGuid(),new(1,"reason"),default));
    }
    [Fact,Trait("Category","Integration")]
    public async Task Accept_is_atomic_idempotent_and_keeps_template_and_reading_snapshots()
    {
        await using var fixture=await VehicleHoursPostgreSqlDatabase.CreateAsync();
        var vehicle=Guid.NewGuid();var id=Guid.NewGuid();var templateId=Guid.NewGuid();var date=new DateOnly(2026,1,1);
        await fixture.ExecuteAsync("INSERT INTO app_users(id) VALUES(@actor); INSERT INTO number_car(id) VALUES(@vehicle);",("actor",Actor),("vehicle",vehicle));
        await fixture.ExecuteAsync("""
            CREATE TABLE maintenance_equipment(id uuid PRIMARY KEY,name text NOT NULL,sort_order integer NOT NULL DEFAULT 0);
            CREATE TABLE maintenance_intervals(id uuid PRIMARY KEY,equipment_id uuid NOT NULL,name text NOT NULL,sort_order integer NOT NULL DEFAULT 0);
            CREATE TABLE maintenance_interval_items(id uuid PRIMARY KEY,interval_id uuid NOT NULL,material_name text NOT NULL,quantity numeric NOT NULL,sort_order integer NOT NULL DEFAULT 0);
            INSERT INTO maintenance_equipment(id,name) VALUES('10000000-0000-0000-0000-000000000001','Экскаваторы');
            INSERT INTO maintenance_intervals(id,equipment_id,name) VALUES('10000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001','ТО-1000');
            """);
        var template=new CardTemplate("SANY SY1250H","ТО-1000","test","",[new("check","Контроль","Проверить",Control:true)],[]);
        await using(var db=fixture.CreateContext()){
            await MaintenancePlanSchema.EnsureAsync(db);
            db.MaintenanceCardTemplates.Add(new(){Id=templateId,Revision=1,Published=true,Snapshot=JsonSerializer.Serialize(template,Json)});
            db.VehicleMaintenancePlans.Add(new(){VehicleId=vehicle,Revision=1,UpdatedBy=Actor,UpdatedAt=DateTimeOffset.UtcNow,Snapshot=JsonSerializer.Serialize(new MaintenancePlan("Цикл",[new("ТО-1000",300),new("ТО-350",350)],0,date,"ТО-700",[new(1,700)],[]),Json)});
            await db.SaveChangesAsync();
        }
        await fixture.ExecuteAsync("INSERT INTO vehicle_hour_readings(id,vehicle_id,reading_date,engine_hours,engine_number,created_by) VALUES(@id,@vehicle,@date,1000,1,@actor)",("id",Guid.NewGuid()),("vehicle",vehicle),("date",date.AddDays(1)),("actor",Actor));
        await using(var db=fixture.CreateContext()){
            var ctl=Controller(db,vehicle);
            Assert.IsType<OkObjectResult>(await ctl.StartCard(vehicle,new(id,templateId,1,1),default));
            Assert.IsType<OkObjectResult>(await ctl.StartCard(vehicle,new(id,templateId,1,1),default));
            Assert.IsType<ConflictObjectResult>(await ctl.StartCard(vehicle,new(Guid.NewGuid(),templateId,1,1),default));
            Assert.IsType<ConflictObjectResult>(await ctl.Complete(vehicle,new(1,date.AddDays(1),[],"Обойти карту"),default));
        }
        CardData data;
        await using(var db=fixture.CreateContext()){
            var card=await db.MaintenanceCards.SingleAsync();
            data=JsonSerializer.Deserialize<CardData>(card.Data,Json)! with {Date=date.AddDays(1),StartedAt=new DateTimeOffset(2026,1,2,0,0,0,TimeSpan.Zero),FinishedAt=new DateTimeOffset(2026,1,2,1,0,0,TimeSpan.Zero),Answers=[new("check","done","ok","","Иванов","")]};
            Assert.IsType<ConflictObjectResult>(await Controller(db,vehicle).SaveCard(id,new(0,data),default));
            Assert.IsType<OkObjectResult>(await Controller(db,vehicle).SaveCard(id,new(1,data,"submit"),default));
        }
        await using(var db=fixture.CreateContext()){
            Assert.IsType<OkObjectResult>(await Controller(db,vehicle).SaveCardTemplate(templateId,new(1,true,template with {Operations=[new("new","ДВС","Другая работа")]}),default));
            Assert.Contains("check",(await db.MaintenanceCards.SingleAsync()).Template);
            Assert.IsType<OkObjectResult>(await Controller(db,vehicle).AcceptCard(id,new(2),default));
        }
        await using(var db=fixture.CreateContext()){
            Assert.IsType<OkObjectResult>(await Controller(db,vehicle).AcceptCard(id,new(2),default));
            Assert.Single(await db.VehicleMaintenanceEvents.ToArrayAsync());
            var card=await db.MaintenanceCards.SingleAsync();Assert.Equal("accepted",card.Status);
            Assert.Equal(1000,JsonSerializer.Deserialize<MaintenanceEngine[]>(card.Readings,Json)![0].Hours);
            Assert.Equal(1,JsonSerializer.Deserialize<MaintenancePlan>((await db.VehicleMaintenancePlans.SingleAsync()).Snapshot,Json)!.NextStepIndex);
            Assert.IsType<BadRequestObjectResult>(await Controller(db,vehicle).SaveCard(id,new(card.Revision,data),default));
        }
        await using(var db=fixture.CreateContext()){
            db.RestrictFleet=true;db.AccessibleVehicleIds=[];
            Assert.Empty(await db.MaintenanceCards.ToArrayAsync());
            Assert.IsType<NotFoundResult>(await Controller(db,vehicle).CardPhotos(id,default));
        }
    }
    [Fact,Trait("Category","Integration")]
    public async Task Standard_cycle_migration_is_idempotent_and_preserves_position()
    {
        await using var fixture=await VehicleHoursPostgreSqlDatabase.CreateAsync();var vehicle=Guid.NewGuid();
        await fixture.ExecuteAsync("INSERT INTO app_users(id) VALUES(@actor); INSERT INTO number_car(id) VALUES(@vehicle);",("actor",Actor),("vehicle",vehicle));
        await using(var db=fixture.CreateContext()){
            await MaintenancePlanSchema.EnsureAsync(db);
            int[] names=[350,700,1000,350,700,2000,350,700,3000,350,700,4000];
            var plan=new MaintenancePlan("Стандарт",names.Select((n,i)=>new MaintenanceStep("ТО-"+n,i%3==2?300:350)).ToArray(),8,new(2026,1,1),"ТО-700",[new(1,2700)],[]);
            db.VehicleMaintenancePlans.Add(new(){VehicleId=vehicle,Revision=1,UpdatedBy=Actor,UpdatedAt=DateTimeOffset.UtcNow,Snapshot=JsonSerializer.Serialize(plan,Json)});await db.SaveChangesAsync();
            await MaintenancePlanSchema.EnsureAsync(db);await MaintenancePlanSchema.EnsureAsync(db);
            var stored=await db.VehicleMaintenancePlans.SingleAsync();var after=JsonSerializer.Deserialize<MaintenancePlan>(stored.Snapshot,Json)!;
            Assert.Equal(2,stored.Revision);Assert.Equal(8,after.NextStepIndex);Assert.Equal(2700,after.Engines[0].Hours);Assert.Equal("ТО-1000",after.Steps[8].Name);
            Assert.Single(await db.VehicleMaintenanceEvents.ToArrayAsync());
        }
    }
}
