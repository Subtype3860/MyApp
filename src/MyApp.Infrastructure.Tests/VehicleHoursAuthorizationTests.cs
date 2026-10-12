using System.Reflection;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class VehicleHoursAuthorizationTests
{
    [Theory]
    [InlineData("Механик", "user", "", true)]
    [InlineData("Старший механик", "user", "", true)]
    [InlineData("Слесарь", "administrator", "", true)]
    [InlineData("Слесарь", "user", "vehicles.hours", true)]
    [InlineData("Слесарь", "user", " VEHICLES.HOURS ", true)]
    [InlineData("Слесарь", "user", "menu.vehicles", false)]
    [InlineData("Слесарь", "user", "vehicles.report", false)]
    [InlineData("Слесарь", "user", "", false)]
    public async Task Writes_imports_and_deletes_require_hours_access(string profession, string role, string permissions, bool allowed)
    {
        var user = new User { Id = Guid.NewGuid(), FirstName = "Иван", MiddleName = "", LastName = "Иванов",
            UserName = "worker", Email = "worker@local", PasswordHash = "test", Role = role, Permissions = permissions,
            Profession = new() { Id = Guid.NewGuid(), Name = profession } };
        var writes = 0;
        var users = DispatchProxy.Create<IUserRepository, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)users).Handler = (_, _) => Task.FromResult<User?>(user);
        var vehicles = DispatchProxy.Create<IVehicleRepository, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)vehicles).Handler = (method, _) => {
            if (!allowed) throw new Exception("Forbidden operations must not query or mutate vehicles.");
            return method.Name switch {
                nameof(IVehicleRepository.VehicleExistsAsync) => Task.FromResult(true),
                nameof(IVehicleRepository.AddHoursAsync) => Save(),
                nameof(IVehicleRepository.GetVehiclesAsync) => Task.FromResult<IReadOnlyList<VehicleResponse>>([]),
                nameof(IVehicleRepository.DeleteEntryAsync) => Delete(),
                _ => throw new Exception(method.Name)
            };
        };
        Task<Guid> Save() { writes++; return Task.FromResult(Guid.NewGuid()); }
        Task<bool> Delete() { writes++; return Task.FromResult(true); }
        var service = new VehicleService(vehicles, users);
        var result = await service.AddHoursAsync(Guid.NewGuid(), new(new(2026, 10, 12), 100, "", 1), user.Id, default);
        Assert.Equal(allowed ? ServiceResultStatus.Success : ServiceResultStatus.Forbidden, result.Status);
        using var stream = new MemoryStream();
        var import = await service.ImportHoursAsync(stream, ".csv", user.Id, new(2026, 10, 12), default);
        Assert.Equal(allowed ? ServiceResultStatus.Success : ServiceResultStatus.Forbidden, import.Status);
        Assert.Equal(allowed, await service.DeleteEntryAsync("hours", Guid.NewGuid(), user.Id, default));
        Assert.Equal(allowed ? 2 : 0, writes);
    }

    [Fact]
    public async Task Missing_user_cannot_modify_hours()
    {
        var users = DispatchProxy.Create<IUserRepository, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)users).Handler = (_, _) => Task.FromResult<User?>(null);
        var vehicles = DispatchProxy.Create<IVehicleRepository, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)vehicles).Handler = (_, _) => throw new Exception("No vehicle access expected");
        var service = new VehicleService(vehicles, users);
        Assert.False(await service.CanManageHoursAsync(Guid.NewGuid(), default));
        Assert.Equal(ServiceResultStatus.Forbidden, (await service.AddHoursAsync(Guid.NewGuid(), new(new(2026, 10, 12), 100, "", 1), Guid.NewGuid(), default)).Status);
    }
}
