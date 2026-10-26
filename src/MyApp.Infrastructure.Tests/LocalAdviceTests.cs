using System.Security.Claims;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.API.Controllers;
using MyApp.Application.Services;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class LocalAdviceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-20T00:00:00Z");
    private static AdviceDefect Defect(Guid id, DateTimeOffset time, string node = "Насос") => new(id, node, time, "stopped", time);
    [Fact]
    public void Readiness_abstains_without_labels_even_when_no_open_defects()
    {
        var result = LocalAdvice.Readiness([], [], Now, 0);
        Assert.Equal("insufficient_data", result.Status);
        Assert.Null(result.Confidence);
        Assert.Equal(Now.AddMinutes(5), result.ExpiresAt);
    }
    [Fact]
    public void Readiness_deduplicates_ids_and_ignores_future_repairs_and_defects()
    {
        var id = Guid.NewGuid(); var defect = Defect(id, Now.AddDays(-1));
        var futureRepair = new AdviceRepair(Guid.NewGuid(), id, Now.AddDays(1), Now.AddDays(-1), "repaired");
        var result = LocalAdvice.Readiness([defect, defect, Defect(Guid.NewGuid(), Now.AddDays(1))], [futureRepair], Now, 2);
        Assert.Contains("Незакрытых неисправностей: 1.", result.Factors);
        Assert.Contains("Требуется проверка", result.Recommendation);
        var completed = futureRepair with { CreatedAt = Now, CompletedAt = Now };
        Assert.Contains("Незакрытых неисправностей: 0.", LocalAdvice.Readiness([defect], [completed], Now, 2).Factors);
    }
    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    [InlineData(-1, false)]
    public void Repeat_window_counts_distinct_defects_only_after_repair(int days, bool expected)
    {
        var current = Defect(Guid.NewGuid(), Now, " НАСОС ");
        var previous = Defect(Guid.NewGuid(), Now.AddDays(-60));
        var repair = new AdviceRepair(Guid.NewGuid(), previous.Id, Now.AddDays(-40), Now.AddDays(-days), "repaired");
        var result = LocalAdvice.Recurrence(current, [current, previous, previous], [repair, repair], Now);
        Assert.Contains($"Подходящих предыдущих неисправностей с ремонтом: {(expected ? 1 : 0)}.", result.Factors);
        Assert.Null(result.Confidence);
    }
    [Fact]
    public void Repeat_does_not_use_self_updates_unknown_nodes_or_backfilled_future_records()
    {
        var current = Defect(Guid.NewGuid(), Now);
        var previous = Defect(Guid.NewGuid(), Now.AddDays(-10));
        var repair = new AdviceRepair(Guid.NewGuid(), previous.Id, Now.AddDays(1), Now.AddDays(-2), "repaired");
        Assert.Contains("Подходящих предыдущих неисправностей с ремонтом: 0.", LocalAdvice.Recurrence(current, [previous], [repair], Now).Factors);
        Assert.Contains("Подходящих предыдущих неисправностей с ремонтом: 0.", LocalAdvice.Recurrence(current, [current], [repair with { DefectId = current.Id }], Now).Factors);
        Assert.Contains("Узел не указан. Начните фиксировать узел при регистрации неисправностей.", LocalAdvice.Recurrence(current with { Node = "" }, [], [], Now).Factors);
    }
    [Theory]
    [InlineData("repair", "Симптомы: шум; Узел: насос", "nodeName", "насос")]
    [InlineData("parts", "Описание: плановая замена; Запчасти: фильтр 2 шт", "requiredParts", "фильтр 2 шт")]
    [InlineData("repair", "Не включается", "symptoms", "Не включается")]
    public void Parser_extracts_only_explicit_values(string kind, string text, string name, string expected)
    {
        var result = LocalAdvice.Parse(text, kind);
        Assert.Contains(result.Fields, field => field.Name == name && field.Value == expected);
        Assert.DoesNotContain(result.Fields, field => field.Name == "failureReason");
    }
    [Fact]
    public void Parser_abstains_on_empty_and_ambiguous_fields_and_limits_input()
    {
        Assert.Empty(LocalAdvice.Parse("  ", "repair").Fields);
        Assert.Empty(LocalAdvice.Parse("Узел: насос; Узел: двигатель", "repair").Fields);
        Assert.Throws<ArgumentException>(() => LocalAdvice.Parse(new string('x', 5001), "parts"));
        Assert.Throws<ArgumentException>(() => LocalAdvice.Parse("", "invalid"));
    }
    private static LocalAdviceController Controller(AppDbContext db, params string[] rights) => new(db, null!) {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity(rights.Select(x => new Claim("permission", x)), "test"))
        } }
    };
    private static AppDbContext OfflineDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=advice_test;Username=test;Password=test;Timeout=1;Pooling=false").Options);
    [Fact]
    public async Task Api_preserves_permissions_before_database_access()
    {
        using var db = OfflineDb(); var controller = Controller(db);
        Assert.IsType<ForbidResult>(await controller.Readiness(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddHours(11)), 1, default));
        Assert.IsType<ForbidResult>(await controller.Recurrence(Guid.NewGuid(), default));
        Assert.IsType<ForbidResult>(controller.Parse(new("test", "repair")));
        Assert.IsType<ForbidResult>(await controller.Quality(default));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(LocalAdviceController), typeof(AuthorizeAttribute)));
    }
    [Fact]
    public async Task Read_endpoints_fail_closed_when_database_is_unavailable()
    {
        using var db = OfflineDb(); var controller = Controller(db, "reports.gto", "vehicles.repair");
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.Readiness(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddHours(11)), 1, default)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.Recurrence(Guid.NewGuid(), default)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.Quality(default)).StatusCode);
        Assert.IsType<BadRequestObjectResult>(await controller.Readiness(Guid.NewGuid(), new DateOnly(2000, 1, 1), 1, default));
    }
    [Fact]
    public void Parser_api_handles_missing_text_and_invalid_kind_without_writes()
    {
        using var db = OfflineDb(); var controller = Controller(db, "vehicles.repair_request");
        var response = Assert.IsType<OkObjectResult>(controller.Parse(new(null, "repair")));
        Assert.Equal("insufficient_data", Assert.IsType<LocalDraft>(response.Value).Status);
        Assert.IsType<BadRequestResult>(controller.Parse(new("", "invalid")));
        Assert.IsType<ForbidResult>(controller.Parse(new("", "parts")));
        Assert.Empty(db.ChangeTracker.Entries());
    }
    [Fact]
    public void Temporal_evaluation_excludes_training_rows_late_snapshots_and_duplicate_ids()
    {
        var expected = new Dictionary<string, string> { ["symptoms"] = "шум" };
        var sample = new ExtractionSample("new", Now, Now, "repair", "Симптомы: шум", expected);
        var report = LocalAdviceEvaluation.Evaluate([
            sample,
            sample with { Id = "old", CreatedAt = Now.AddDays(-2), CapturedAt = Now.AddDays(-2), Expected = [] },
            sample with { Id = "edited", CapturedAt = Now.AddDays(1) },
            sample with { Id = "duplicate" }, sample with { Id = "duplicate" }
        ], Now.AddDays(-1));
        Assert.Equal(1, report.Holdout); Assert.Equal(1, report.BeforeCutoff); Assert.Equal(3, report.Rejected);
        Assert.Equal(1d, report.F1); Assert.Equal(1d, report.ExactMatch);
        Assert.Null(LocalAdviceEvaluation.Evaluate([], Now).F1);
    }
    internal static LocalAdviceController OutcomeController(AppDbContext db, Guid actor, bool canManage)
    {
        var service = DispatchProxy.Create<IVehicleService, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)service).Handler = (method, _) => {
            Assert.Equal(nameof(IVehicleService.GetVehicleCatalogOptionsAsync), method.Name);
            return Task.FromResult(new VehicleCatalogOptions(canManage, [], [], []));
        };
        return new LocalAdviceController(db, service) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "reports.gto"), new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "test"))
            } }
        };
    }
    [Fact]
    public async Task Outcome_requires_mechanic_explicit_confirmation_value_and_past_date()
    {
        using var db = OfflineDb(); var actor = Guid.NewGuid();
        var request = new LocalAdviceController.OutcomeRequest(new DateOnly(2026, 1, 1), 1, true, true);
        Assert.IsType<ForbidResult>(await OutcomeController(db, actor, false).ConfirmOutcome(Guid.NewGuid(), request, default));
        var controller = OutcomeController(db, actor, true);
        foreach (var invalid in new[] { request with { Confirmed = false }, request with { ReadyWholeShift = null }, request with { Shift = 3 }, request with { Date = default }, request with { Date = DateOnly.MaxValue } })
            Assert.IsType<BadRequestObjectResult>(await controller.ConfirmOutcome(Guid.NewGuid(), invalid, default));
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
