using System.Reflection;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class UserSessionRevocationTests
{
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

    [Fact]
    public async Task Avatar_change_does_not_revoke_tokens()
    {
        var user = JwtRevocationTests.MakeUser();
        var stamp = user.SessionStamp;
        Assert.Equal(ServiceResultStatus.Success, (await Service(user).UpdateAvatarAsync(user.Id, [1, 2], "image/png", default)).Status);
        Assert.Equal(stamp, user.SessionStamp);
    }

    private sealed class Passwords : IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string encodedHash) => password == encodedHash;
    }
}
