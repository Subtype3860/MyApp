using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.API.Controllers;
using MyApp.Application.Models;
using MyApp.Application.Services;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;
using Npgsql;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class FleetReportScopeTests
{
    [Fact] public void Filters_foreign_rows_and_preserves_them_when_saving()
    {
        var mine = Guid.NewGuid(); var other = Guid.NewGuid();
        var original = JsonSerializer.Serialize(new { rows = new[] { new { vehicle = new { id = mine }, note = "mine" }, new { vehicle = new { id = other }, note = "secret" } } });
        var visible = FleetReportScope.Filter(original, new HashSet<Guid> { mine });
        Assert.Single(visible["rows"]!.AsArray()); Assert.DoesNotContain("secret", visible.ToJsonString());
        visible["rows"]![0]!["note"] = "edited";
        var merged = FleetReportScope.Merge(visible.ToJsonString(), original, new HashSet<Guid> { mine });
        Assert.Contains("edited", merged.ToJsonString()); Assert.Contains("secret", merged.ToJsonString());
        Assert.Throws<UnauthorizedAccessException>(() => FleetReportScope.Merge(original, null, new HashSet<Guid> { mine }));
    }
}

public sealed class FleetRegionPostgreSqlTests
{
    [Fact] public async Task Forecasts_are_separate_by_region_and_reject_foreign_vehicles()
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Context();
        var service = System.Reflection.DispatchProxy.Create<IVehicleService, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)service).Handler = (_, _) =>
            Task.FromResult(new MyApp.Application.DTO.VehicleCatalogOptions(true, [], [], []));
        var api = new GtoReportsController(db, service) { ControllerContext = fixture.Controller(db).ControllerContext };
        var date = new DateOnly(2099,1,1);
        GtoReportsController.SaveRequest Request(bool vehicle) => new(0, JsonSerializer.SerializeToElement(new {
            version=1, date="2099-01-01", shift="1", organisation="Wrong name", site="Wrong site", mechanic="Test",
            rows=vehicle ? new[] { new { vehicle=new {id=fixture.Vehicle.ToString()}, forecast="healthy", ktg=50, maintenance="", note="" } } : []
        }));
        Assert.IsType<OkObjectResult>(await api.Save(date,1,fixture.RegionA,Request(true),default));
        Assert.IsType<BadRequestObjectResult>(await api.Save(date,1,fixture.RegionB,Request(true),default));
        Assert.IsType<OkObjectResult>(await api.Save(date,1,fixture.RegionB,Request(false),default));
        Assert.Equal(2,await db.GtoReports.CountAsync());
        var saved = await db.GtoReports.SingleAsync(r=>r.RegionId==fixture.RegionA);
        Assert.Equal("A",JsonDocument.Parse(saved.Snapshot).RootElement.GetProperty("organisation").GetString());
        Assert.IsType<ConflictObjectResult>(await api.Save(date,1,fixture.RegionA,Request(true),default));
        api.HttpContext.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,fixture.UserB.ToString())],"test"));
        await db.SetFleetScopeAsync(fixture.UserB,default);
        Assert.IsType<NotFoundResult>(await api.Get(date,1,fixture.RegionA,default));
        Assert.IsType<NotFoundResult>(await api.Save(date,1,fixture.RegionA,Request(true),default));
        Assert.IsType<OkObjectResult>(await api.Get(date,1,fixture.RegionB,default));
    }

    [Theory]
    [InlineData("Главный механик")]
    [InlineData("Старший механик")]
    public async Task Region_managers_can_manage_assignments_without_unrestricted_work_access(string profession)
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Context();
        var api = fixture.Controller(db);
        Assert.IsType<OkObjectResult>(await api.Create(new("Admin site"), default));
        api.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, fixture.UserA.ToString())], "test"));
        await db.SetFleetScopeAsync(fixture.UserA, default);
        Assert.IsType<ForbidResult>(await api.Create(new("Denied"), default));
        Assert.IsType<ForbidResult>(await api.Catalog(default));
        Assert.IsType<ForbidResult>(await api.Management(default));
        Assert.IsType<ForbidResult>(await api.SetAccess(fixture.UserB, new([]), default));
        Assert.IsType<ForbidResult>(await api.Transfer(fixture.Vehicle, new(fixture.RegionB,fixture.Assignment,null,""),default));
        var user = await db.Users.Include(u => u.Profession).SingleAsync(u => u.Id == fixture.UserA);
        user.Profession.Name = profession;
        await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await api.Create(new("Chief site"), default));
        Assert.IsType<OkObjectResult>(await api.Catalog(default));
        Assert.IsType<OkObjectResult>(await api.Management(default));
        Assert.IsType<NoContentResult>(await api.SetAccess(fixture.UserB, new([fixture.RegionB]), default));
        Assert.IsType<OkObjectResult>(await api.Transfer(fixture.Vehicle,new(fixture.RegionB,fixture.Assignment,null,"Переезд"),default));
        await db.SetFleetScopeAsync(fixture.UserA,default);
        Assert.Empty(await db.VehicleWorks.ToArrayAsync());
        Assert.IsType<OkObjectResult>(await api.History(fixture.Vehicle,default));
        Assert.IsType<ConflictObjectResult>(await api.Update(fixture.RegionB,new("B",true),default));
        Assert.True(db.RestrictFleet); // Creating sites does not grant unrestricted vehicle access.
        Assert.IsType<ConflictObjectResult>(await api.Create(new("Chief site"), default));
        user.Profession.Name = "Механик";
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await api.Create(new("Revoked"), default));
        Assert.IsType<ForbidResult>(await api.Management(default));
    }

    [Fact] public async Task Public_signature_opens_only_its_file_and_restores_region_scope()
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Context();
        var photoId = await db.VehicleWorkPhotos.Select(p => p.Id).SingleAsync();
        await db.SetFleetScopeAsync(fixture.UserB, default);
        var service = System.Reflection.DispatchProxy.Create<IVehicleService, WorkParticipationAuthorizationTests.InvocationProxy>();
        var reads = 0;
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)service).Handler = (_, args) => Read((Guid)args[0]!);
        async Task<MyApp.Application.DTO.VehicleWorkPhotoContent?> Read(Guid id) {
            reads++;
            return await db.VehicleWorkPhotos.AnyAsync(p => p.Id == id)
                ? new("photo.webp", "image/webp", "RIFF0000WEBP"u8.ToArray()) : null;
        }
        using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var api = new ReportMediaController(new("synthetic-region-test-key-1234567890", "test", "test"), service) {
            ControllerContext = new() { HttpContext = new DefaultHttpContext { RequestServices = services } }
        };
        Assert.IsType<NotFoundResult>(await api.Links([new(photoId, "image")], db, default));
        Assert.IsType<NotFoundResult>(await api.Open("image", photoId, new string('x',43), default));
        Assert.Equal(0, reads);
        Assert.IsType<FileContentResult>(await api.Open("image", photoId, api.Signature("image", photoId), default));
        Assert.Equal(1, reads);
        Assert.True(db.RestrictFleet);
        Assert.Empty(await db.VehicleWorkPhotos.ToArrayAsync());
    }

    [Fact] public async Task Http_requests_enforce_region_scope_and_administrator_only_mutations()
    {
        await using var fixture = await Fixture.Create();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer();
        builder.Services.AddScoped(_ => fixture.Context());
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(FleetRegionsController).Assembly);
        await using var app = builder.Build();
        // Test-only identities; real authentication remains the existing JWT middleware.
        app.Use(async (context, next) => {
            var id = Guid.Parse(context.Request.Headers["Test-User"].ToString());
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,id.ToString()),
                new Claim(ClaimTypes.Role,id==fixture.Admin ? "administrator" : "user")], "test"));
            await next();
        });
        app.UseAuthorization();
        app.UseMiddleware<MyApp.API.FleetRegionMiddleware>();
        app.MapControllers();
        app.MapGet("/test/vehicles", async (AppDbContext db) => await db.Vehicles.Select(v => v.Id).ToArrayAsync());
        app.Urls.Add("http://127.0.0.1:0"); await app.StartAsync();
        try {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            client.DefaultRequestHeaders.Add("Test-User", fixture.UserB.ToString());
            Assert.Equal("[]", await client.GetStringAsync("/test/vehicles"));
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync($"/api/fleet-regions/vehicles/{fixture.Vehicle}/history")).StatusCode);
            var payload = new StringContent("{\"name\":\"Forbidden\"}", System.Text.Encoding.UTF8,"application/json");
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.PostAsync("/api/fleet-regions", payload)).StatusCode);
            client.DefaultRequestHeaders.Remove("Test-User"); client.DefaultRequestHeaders.Add("Test-User", fixture.UserA.ToString());
            Assert.Contains(fixture.Vehicle.ToString(), await client.GetStringAsync("/test/vehicles"));
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync($"/api/fleet-regions/vehicles/{fixture.Vehicle}/history")).StatusCode);
        } finally { await app.StopAsync(); }
    }

    [Fact] public async Task Concurrent_moves_cannot_create_two_current_assignments()
    {
        await using var fixture = await Fixture.Create();
        async Task<IActionResult> Move() {
            await using var db = fixture.Context();
            return await fixture.Controller(db).Transfer(fixture.Vehicle, new(fixture.RegionB, fixture.Assignment, null, ""), default);
        }
        var results = await Task.WhenAll(Move(), Move());
        Assert.Single(results.OfType<OkObjectResult>()); Assert.Single(results.OfType<ConflictObjectResult>());
        await using var check = fixture.Context();
        Assert.Single(await check.VehicleRegionAssignments.Where(a => a.EndedAt == null).ToArrayAsync());
        Assert.Equal(2, await check.VehicleRegionAssignments.CountAsync());
    }

    [Fact] public async Task Transfer_preserves_history_and_changes_access_including_media_and_reports()
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Context();
        await db.SetFleetScopeAsync(fixture.UserA, default);
        Assert.Equal(fixture.Vehicle, Assert.Single(await db.Vehicles.ToArrayAsync()).Id);
        Assert.Single(await db.VehicleWorks.ToArrayAsync());
        Assert.Single(await db.VehicleWorkPhotos.ToArrayAsync());
        Assert.Single(await db.VehicleHourReadings.ToArrayAsync());
        await db.SetFleetScopeAsync(fixture.UserB, default);
        Assert.Empty(await db.Vehicles.ToArrayAsync()); Assert.Empty(await db.VehicleWorks.ToArrayAsync());
        Assert.Empty(await db.VehicleDefectPhotos.ToArrayAsync()); Assert.Empty(await db.VehicleWorkPhotos.ToArrayAsync());
        Assert.Empty(await db.VehicleEngineSettings.ToArrayAsync()); Assert.Empty(await db.GtoDefectSettings.ToArrayAsync());
        await db.SetFleetScopeAsync(fixture.Admin, default);
        Assert.Equal(2, await db.Vehicles.CountAsync()); // includes unassigned vehicle
        var api = fixture.Controller(db);
        var transfer = new FleetRegionsController.TransferRequest(fixture.RegionB, fixture.Assignment, DateTimeOffset.UtcNow.AddMinutes(-1), "Переезд");
        Assert.IsType<OkObjectResult>(await api.Transfer(fixture.Vehicle, transfer, default));
        Assert.IsType<ConflictObjectResult>(await api.Transfer(fixture.Vehicle, transfer, default));
        Assert.Equal(2, await db.VehicleRegionAssignments.CountAsync());
        Assert.Single(await db.VehicleRegionAssignments.Where(a => a.EndedAt == null).ToArrayAsync());
        Assert.Single(await db.VehicleWorks.ToArrayAsync());
        await db.SetFleetScopeAsync(fixture.UserA, default);
        Assert.Empty(await db.Vehicles.ToArrayAsync()); Assert.Empty(await db.VehicleWorks.ToArrayAsync());
        Assert.Empty(await db.VehicleWorkPhotos.ToArrayAsync()); Assert.Empty(await db.VehicleHourReadings.ToArrayAsync());
        api.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, fixture.UserA.ToString())], "test"));
        Assert.IsType<NotFoundResult>(await api.History(fixture.Vehicle, default));
        await db.SetFleetScopeAsync(fixture.UserB, default);
        Assert.Single(await db.Vehicles.ToArrayAsync()); Assert.Single(await db.VehicleWorks.ToArrayAsync());
        Assert.Single(await db.VehicleWorkPhotos.ToArrayAsync()); Assert.Equal(2, await db.VehicleRegionAssignments.CountAsync());
        Assert.Single(await db.VehicleEngineSettings.ToArrayAsync()); Assert.Single(await db.GtoDefectSettings.ToArrayAsync());
        await db.SetFleetScopeAsync(fixture.Admin, default);
        api.HttpContext.User = fixture.Controller(db).User;
        Assert.IsType<NoContentResult>(await api.SetAccess(fixture.UserB, new([]), default));
        await db.SetFleetScopeAsync(fixture.UserB, default);
        Assert.Empty(await db.Vehicles.ToArrayAsync()); Assert.Empty(await db.VehicleWorkVideos.ToArrayAsync());
    }

    [Fact] public async Task Rejects_invalid_transfer_and_archiving_occupied_region()
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Context();
        var api = fixture.Controller(db);
        Assert.IsType<ConflictObjectResult>(await api.Update(fixture.RegionA, new("A", true), default));
        Assert.IsType<BadRequestObjectResult>(await api.Transfer(fixture.Vehicle,
            new(fixture.RegionB, fixture.Assignment, DateTimeOffset.UtcNow.AddDays(1), ""), default));
        Assert.IsType<BadRequestObjectResult>(await api.Transfer(fixture.Vehicle,
            new(fixture.RegionB, fixture.Assignment, DateTimeOffset.UtcNow.AddDays(-10), ""), default));
        Assert.Single(await db.VehicleRegionAssignments.ToArrayAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string connection;
        private readonly string schema = "fleet_it_" + Guid.NewGuid().ToString("N");
        public Guid Admin { get; } = Guid.NewGuid();
        public Guid UserA { get; } = Guid.NewGuid();
        public Guid UserB { get; } = Guid.NewGuid();
        public Guid RegionA { get; } = Guid.NewGuid();
        public Guid RegionB { get; } = Guid.NewGuid();
        public Guid Vehicle { get; } = Guid.NewGuid();
        public Guid Assignment { get; } = Guid.NewGuid();
        private Fixture(string connection) { this.connection = connection; }
        public AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString).Options);
        public FleetRegionsController Controller(AppDbContext db) => new(db) { ControllerContext = new() {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, Admin.ToString()), new Claim(ClaimTypes.Role, "administrator")], "test")) } } };
        public static async Task<Fixture> Create()
        {
            var value = Environment.GetEnvironmentVariable("MYAPP_TEST_POSTGRES") ?? throw new InvalidOperationException("Use an isolated *_test database.");
            if (!new NpgsqlConnectionStringBuilder(value).Database!.EndsWith("_test")) throw new InvalidOperationException("Use an isolated *_test database.");
            var fixture = new Fixture(value);
            await using var connection = new NpgsqlConnection(value); await connection.OpenAsync();
            await new NpgsqlCommand($"CREATE SCHEMA {fixture.schema}", connection).ExecuteNonQueryAsync();
            try { await fixture.Seed(); return fixture; } catch { await fixture.DisposeAsync(); throw; }
        }
        private async Task Seed()
        {
            await using var db = Context();
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            await FleetRegionSchema.EnsureAsync(db, default);
            await FleetRegionSchema.EnsureAsync(db, default); // schema migration must be repeatable
            var profession = new Profession { Id = Guid.NewGuid(), Name = "Механик" };
            db.Professions.Add(profession);
            foreach (var id in new[] {Admin, UserA, UserB}) db.Users.Add(new User {
                Id=id, FirstName="Test", MiddleName="", LastName="User", UserName=id.ToString(), Email=id+"@test.invalid",
                PasswordHash="unused", PositionId=profession.Id, Role=id==Admin ? "administrator" : "user", CreatedAt=DateTime.UtcNow });
            db.FleetRegions.AddRange(new FleetRegion { Id=RegionA, Name="A" }, new FleetRegion { Id=RegionB, Name="B" });
            db.Vehicles.AddRange(new NumberCarRecord { Id=Vehicle, GarageNumber=911 }, new NumberCarRecord { Id=Guid.NewGuid(), GarageNumber=912 });
            await db.SaveChangesAsync();
            db.FleetRegionGrants.AddRange(new FleetRegionGrant { UserId=UserA, RegionId=RegionA }, new FleetRegionGrant { UserId=UserB, RegionId=RegionB });
            db.VehicleRegionAssignments.Add(new() { Id=Assignment, VehicleId=Vehicle, RegionId=RegionA, StartedAt=DateTimeOffset.UtcNow.AddDays(-1), RecordedAt=DateTimeOffset.UtcNow, CreatedBy=Admin });
            var defect = Guid.NewGuid(); var work = Guid.NewGuid();
            db.VehicleDefects.Add(new() { Id=defect, VehicleId=Vehicle, CreatedBy=Admin });
            db.VehicleWorks.Add(new() { Id=work, VehicleId=Vehicle, CreatedBy=Admin, DefectId=defect, WorkDate=new(2026,1,1) });
            await db.SaveChangesAsync();
            db.VehicleWorkPhotos.Add(new() { Id=Guid.NewGuid(), WorkId=work, FileName="secret.jpg", ContentType="image/jpeg", Size=1 });
            db.VehicleWorkVideos.Add(new() { Id=Guid.NewGuid(), WorkId=work, FileName="secret.mp4", ContentType="video/mp4", Size=1 });
            db.VehicleDefectPhotos.Add(new() { Id=Guid.NewGuid(), DefectId=defect, FileName="secret.jpg", ContentType="image/jpeg", Size=1 });
            db.VehicleHourReadings.Add(new() { Id=Guid.NewGuid(), VehicleId=Vehicle, ReadingDate=new(2026,1,1), EngineHours=100, CreatedBy=Admin });
            db.VehicleEngineSettings.Add(new() { VehicleId=Vehicle, EngineNumber=1 });
            db.GtoDefectSettings.Add(new() { DefectId=defect, PlannedRelease="secret" });
            await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = new NpgsqlConnection(connection); await db.OpenAsync();
            await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", db).ExecuteNonQueryAsync();
        }
    }
}
