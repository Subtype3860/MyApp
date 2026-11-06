using System.Reflection;
using MyApp.Application.Abstractions;
using MyApp.Application.Models;
using MyApp.Application.Services;
using Xunit;
namespace MyApp.Infrastructure.Tests;

public sealed class RepairHistoryAuthorizationTests
{
    [Theory]
    [InlineData("Механик", "user", false, "defects", true)]
    [InlineData("Старший механик", "user", false, "defects", true)]
    [InlineData("Слесарь", "user", true, "defects", true)]
    [InlineData("Слесарь", "user", false, "defects", false)]
    [InlineData("Слесарь", "administrator", false, "defects", true)]
    [InlineData("Механик", "user", true, "works", false)]
    [InlineData("Слесарь", "administrator", false, "works", true)]
    [InlineData("Механик", "administrator", true, "invalid", false)]
    public async Task Only_author_mechanic_or_admin_can_edit_requests(string profession, string role, bool owner, string category, bool allowed)
    {
        var user = new User { Id = Guid.NewGuid(), FirstName = "Иван", MiddleName = "", LastName = "Иванов", UserName = "worker", Email = "worker@local", PasswordHash = "test", Role = role, Permissions = "menu.vehicles", Profession = new() { Id = Guid.NewGuid(), Name = profession } };
        var users = DispatchProxy.Create<IUserRepository, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)users).Handler = (_, _) => Task.FromResult<User?>(user);
        var writes = 0;
        var vehicles = DispatchProxy.Create<IVehicleRepository, WorkParticipationAuthorizationTests.InvocationProxy>();
        var id = Guid.NewGuid();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)vehicles).Handler = (method, args) => {
            if (method.Name == nameof(IVehicleRepository.IsDefectCreatorAsync)) {
                Assert.Equal(id, args[0]); Assert.Equal(user.Id, args[1]); return Task.FromResult(owner);
            }
            Assert.Equal(nameof(IVehicleRepository.UpdateHistoryEntryAsync), method.Name);
            Assert.True(allowed); Assert.Equal("Исправленный текст", args[2]); writes++;
            return Task.FromResult(true);
        };
        var service = new VehicleService(vehicles, users);
        Assert.Equal(allowed, await service.UpdateHistoryEntryAsync(category, id, " Исправленный текст ", user.Id, default));
        Assert.Equal(allowed ? 1 : 0, writes);
        Assert.False(await service.UpdateHistoryEntryAsync(category, id, "  ", user.Id, default));
    }
}
