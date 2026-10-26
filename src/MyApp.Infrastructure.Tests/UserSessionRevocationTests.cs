using System.Reflection;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Application.Models;
using MyApp.Application.Security;
using Xunit;

namespace MyApp.Infrastructure.Tests;

/// <summary>Тесты отзыва сессий при изменении пользовательских данных.</summary>
public sealed class UserSessionRevocationTests
{
    [Fact]
    public async Task New_user_has_no_game_admission_and_grant_or_revoke_changes_session_stamp()
    {
        User? created = null;
        var users = DispatchProxy.Create<IUserRepository, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)users).Handler = (method, args) => method.Name switch {
            nameof(IUserRepository.UserNameExistsAsync) => Task.FromResult(false),
            nameof(IUserRepository.AddAsync) => Capture((User)args![0]!),
            nameof(IUserRepository.SaveChangesAsync) => Task.CompletedTask,
            _ => throw new Exception(method.Name)
        };
        Task Capture(User user) { created = user; return Task.CompletedTask; }
        var professions = DispatchProxy.Create<IProfessionRepository, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)professions).Handler = (_, _) => Task.FromResult(true);
        var result = await new UserService(users, professions, new Passwords()).CreateAsync(new("Test", "Test", "Test", "unused", "unused", Guid.NewGuid()), default);
        Assert.Equal(ServiceResultStatus.Success, result.Status);
        Assert.NotNull(created);
        Assert.DoesNotContain(Permissions.TanksPlay, UserAccess.EffectivePermissions(created));
        var stamp = created.SessionStamp;
        var grant = new UpdateUserRequest(created.FirstName, created.MiddleName, created.LastName, created.UserName, null, created.PositionId, "user", [Permissions.TanksPlay]);
        Assert.Equal(ServiceResultStatus.Success, (await Service(created).UpdateAsync(created.Id, grant, default)).Status);
        Assert.Contains(Permissions.TanksPlay, UserAccess.EffectivePermissions(created));
        Assert.NotEqual(stamp, created.SessionStamp);
        stamp = created.SessionStamp;
        await Service(created).UpdateAsync(created.Id, grant with { Permissions = [] }, default);
        Assert.DoesNotContain(Permissions.TanksPlay, UserAccess.EffectivePermissions(created));
        Assert.NotEqual(stamp, created.SessionStamp);
    }
    /// <summary>Собирает сервис пользователей с подменёнными зависимостями для теста.</summary>
    private static UserService Service(User user)
    {
        var users = DispatchProxy.Create<IUserRepository, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)users).Handler = (method, _) => method.Name switch {
            nameof(IUserRepository.FindByIdAsync) => Task.FromResult<User?>(user),
            nameof(IUserRepository.UserNameExistsAsync) => Task.FromResult(false),
            nameof(IUserRepository.SaveChangesAsync) => Task.CompletedTask,
            _ => throw new Exception(method.Name)
        };
        var professions = DispatchProxy.Create<IProfessionRepository, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)professions).Handler = (_, _) => Task.FromResult(true);
        return new UserService(users, professions, new Passwords());
    }

    /// <summary>Проверяет отзыв токенов при изменении настроек безопасности и сохранение сессии при смене имени.</summary>
    [Theory]
    [InlineData("password")]
    [InlineData("role")]
    [InlineData("permissions")]
    [InlineData("profession")]
    [InlineData("login")]
    [InlineData("name")]
    public async Task Administrative_security_changes_revoke_tokens_but_name_change_does_not(string change)
    {
        var user = JwtRevocationTests.MakeUser();
        user.Role = "user";
        user.Permissions = "menu.tables";
        var stamp = user.SessionStamp;
        var request = new UpdateUserRequest(change == "name" ? "Пётр" : user.FirstName, user.MiddleName, user.LastName,
            change == "login" ? "newlogin" : user.UserName, change == "password" ? "newpassword" : null,
            change == "profession" ? Guid.NewGuid() : user.PositionId,
            change == "role" ? "administrator" : user.Role,
            change == "permissions" ? ["vehicles.hours"] : ["menu.tables"]);
        Assert.Equal(ServiceResultStatus.Success, (await Service(user).UpdateAsync(user.Id, request, default)).Status);
        Assert.Equal(change == "name", user.SessionStamp == stamp);
    }

    /// <summary>Проверяет отзыв сессий при смене пароля только после проверки текущего пароля.</summary>
    [Theory]
    [InlineData("oldpassword", true)]
    [InlineData("wrongpassword", false)]
    public async Task Own_password_change_revokes_only_after_verifying_current_password(string password, bool valid)
    {
        var user = JwtRevocationTests.MakeUser();
        user.PasswordHash = "oldpassword";
        var stamp = user.SessionStamp;
        var result = await Service(user).UpdateProfileAsync(user.Id, new(user.UserName, password, "newpassword"), default);
        Assert.Equal(valid ? ServiceResultStatus.Success : ServiceResultStatus.ValidationError, result.Status);
        Assert.Equal(!valid, user.SessionStamp == stamp);
        Assert.Equal(valid ? "newpassword" : "oldpassword", user.PasswordHash);
    }

    /// <summary>Проверяет сохранение сессий при изменении аватара.</summary>
    [Fact]
    public async Task Avatar_change_does_not_revoke_tokens()
    {
        var user = JwtRevocationTests.MakeUser();
        var stamp = user.SessionStamp;
        Assert.Equal(ServiceResultStatus.Success, (await Service(user).UpdateAvatarAsync(user.Id, [1, 2], "image/png", default)).Status);
        Assert.Equal(stamp, user.SessionStamp);
    }

    /// <summary>Тестовая реализация хеширования и проверки паролей.</summary>
    private sealed class Passwords : IPasswordHasher
    {
        /// <summary>Возвращает тестовое представление хеша пароля.</summary>
        public string Hash(string password) => password;
        /// <summary>Проверяет пароль с помощью тестовой реализации хеширования.</summary>
        public bool Verify(string password, string encodedHash) => password == encodedHash;
    }
}
