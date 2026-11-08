using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.API.Controllers;
using MyApp.Application.Models;
using MyApp.Application.Security;
using MyApp.Infrastructure.Db;
using Xunit;

namespace MyApp.Infrastructure.Tests;
public sealed class ProfessionAccessTests
{
    private static User Employee(string? policy = null) => new() {
        FirstName = "Иван", MiddleName = "Иванович", LastName = "Иванов", UserName = "test",
        Email = "test@local", PasswordHash = "unused", Role = "user", Permissions = "vehicles.repair",
        Profession = new() { Name = "Механик", PermissionOverride = policy }
    };
    [Fact] public void Profession_replaces_individual_permissions_and_can_hide_blocks()
    {
        var user = Employee(Permissions.ShiftReport);
        user.Profession.HiddenPageBlocks = "shift_report.media";
        var actual = UserAccess.EffectivePermissions(user);
        Assert.Contains(Permissions.ShiftReport, actual);
        Assert.Contains(Permissions.VehiclesView, actual);
        Assert.Contains("ui.hidden.shift_report.media", actual);
        Assert.DoesNotContain("vehicles.repair", actual);
        Assert.False(UserAccess.CanManageHours(user));
    }
    [Fact] public void Empty_policy_grants_no_sections_and_reset_restores_individual_defaults()
    {
        var user = Employee("");
        Assert.Empty(UserAccess.EffectivePermissions(user));
        user.Profession.PermissionOverride = null;
        Assert.Contains("vehicles.repair", UserAccess.EffectivePermissions(user));
        Assert.True(UserAccess.CanManageHours(user));
    }
    [Fact] public void Administrators_ignore_profession_restrictions()
    {
        var user = Employee(""); user.Role = "administrator";
        user.Profession.HiddenPageBlocks = "shift_report.media";
        Assert.Equal(Permissions.All, UserAccess.EffectivePermissions(user));
    }
    [Fact] public async Task Invalid_or_empty_batches_are_rejected_before_database_access()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var controller = new ProfessionAccessController(db);
        Assert.IsType<BadRequestObjectResult>(await controller.Save(new([], [], []), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Save(new([new(Guid.NewGuid(), 0)], ["unknown"], []), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Save(new([new(Guid.NewGuid(), 0)], [], ["unknown"]), default));
        var authorization = (AuthorizeAttribute)Attribute.GetCustomAttribute(typeof(ProfessionAccessController), typeof(AuthorizeAttribute))!;
        Assert.Equal("administrator", authorization.Roles);
    }
}
