using System.Reflection;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Application.Models;
using Xunit;

namespace MyApp.Infrastructure.Tests;

/// <summary>Тесты прав доступа к документам требований.</summary>
public sealed class ComponentDocumentAuthorizationTests
{
    /// <summary>Проверяет, что формировать требование может только сотрудник с профессией механика.</summary>
    [Theory]
    [InlineData("Механик", "user", true)]
    [InlineData(" механик ", "user", true)]
    [InlineData("Механик", "administrator", true)]
    [InlineData("Администратор", "administrator", false)]
    [InlineData("Старший механик", "user", false)]
    [InlineData("Слесарь", "user", false)]
    public async Task Only_mechanic_profession_can_prepare_and_create_a_requirement(string profession, string role, bool allowed)
    {
        var user = JwtRevocationTests.MakeUser();
        user.Role = role;
        user.Permissions = string.Join(',', MyApp.Application.Security.Permissions.All);
        user.Profession = new() { Id = Guid.NewGuid(), Name = profession };
        var saved = 0;
        var users = Proxy<IUserRepository>((_, _) => Task.FromResult<User?>(user));
        var groups = Proxy<IMaterialGroupService>((_, _) => {
            Assert.True(allowed, "Access must be checked before reading material groups.");
            return Task.FromResult(ServiceResult<IReadOnlyList<MaterialGroupMappingResponse>>.Success([]));
        });
        var employees = Proxy<IResponsibleEmployeeService>((_, _) => {
            Assert.True(allowed, "Access must be checked before resolving employees.");
            return Task.FromResult(ServiceResult<ResponsibleEmployeeResponse>.Success(new("Пётр", "", "Петров", "Кладовщик")));
        });
        var repository = Proxy<IRequirementJournalRepository>((method, args) => {
            Assert.True(allowed);
            Assert.Equal(nameof(IRequirementJournalRepository.SaveAsync), method.Name);
            Assert.Equal(user.Id, args[0]);
            saved++;
            return Task.CompletedTask;
        });
        var service = new ComponentDocumentService(users, employees, repository, groups);
        var request = new ComponentDocumentRequest(new(2026, 10, 12), "94", "v_full_ost", new("Пётр", "", "Петров"), [new("Фильтр", "шт", 1, 2)]);
        var preview = await service.PrepareAsync(request, user.Id, default);
        var created = await service.GenerateAsync(request, user.Id, default);
        Assert.Equal(allowed ? ServiceResultStatus.Success : ServiceResultStatus.Forbidden, preview.Status);
        Assert.Equal(allowed ? ServiceResultStatus.Success : ServiceResultStatus.Forbidden, created.Status);
        Assert.Equal(allowed ? 1 : 0, saved);
    }

    /// <summary>Проверяет отказ в создании требования для отсутствующего пользователя.</summary>
    [Fact]
    public async Task Missing_user_cannot_create_a_requirement()
    {
        var users = Proxy<IUserRepository>((_, _) => Task.FromResult<User?>(null));
        /** Проверяет, что запрещённая операция возвращает ожидаемый отказ. */
        object? Reject(MethodInfo _, object?[] __) => throw new Exception("No document data access expected.");
        var service = new ComponentDocumentService(users, Proxy<IResponsibleEmployeeService>(Reject),
            Proxy<IRequirementJournalRepository>(Reject), Proxy<IMaterialGroupService>(Reject));
        var result = await service.GenerateAsync(null!, Guid.NewGuid(), default);
        Assert.Equal(ServiceResultStatus.Unauthorized, result.Status);
    }

    /// <summary>Создаёт прокси зависимости с заданным обработчиком вызовов для теста.</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
