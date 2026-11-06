using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MyApp.API.Controllers;
using MyApp.API.Extensions;
using MyApp.Application.Abstractions;
using MyApp.Application.Security;
using MyApp.Application.Models;
using MyApp.Infrastructure.Security;
using Xunit;

namespace MyApp.Infrastructure.Tests;

/// <summary>Тесты проверки JWT и отзыва сессий.</summary>
public sealed class JwtRevocationTests
{
    private const string Key = "test-signing-key-with-at-least-32-bytes";

    /// <summary>Проверяет отклонение отозванных, старых токенов и токенов удалённого пользователя настоящей JWT-аутентификацией.</summary>
    [Fact]
    public async Task Real_bearer_middleware_rejects_revoked_legacy_and_deleted_user_tokens()
    {
        var user = MakeUser();
        var store = new Sessions(user);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Jwt:Key"] = Key, ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test"
        });
        builder.Services.AddSingleton<IUserSessionStore>(store);
        builder.Services.AddJwtAuthentication(builder.Configuration);
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/protected", () => "ok").RequireAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var tokens = new JwtTokenProvider(new(Key, "test", "test"));
        var oldToken = tokens.Create(user);
        client.DefaultRequestHeaders.Authorization = new("Bearer", oldToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.Create(user));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/protected")).StatusCode);
        var currentStamp = user.SessionStamp;
        await store.RevokeAsync(user.Id, Guid.NewGuid(), default);
        Assert.Equal(currentStamp, user.SessionStamp);

        var legacy = new JwtSecurityToken("test", "test", [new("sub", user.Id.ToString())], expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(legacy));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/protected")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.Create(user));
        store.User = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/protected")).StatusCode);
        await app.StopAsync();
    }

    /// <summary>Проверяет доступ механика к моточасам и совпадение метки сессии JWT с базой.</summary>
    [Fact]
    public void Mechanic_receives_hours_navigation_and_token_identity_matches_database_stamp()
    {
        var user = MakeUser();
        user.Role = "user";
        user.Profession = new() { Id = Guid.NewGuid(), Name = "Механик" };
        var token = new JwtSecurityTokenHandler().ReadJwtToken(new JwtTokenProvider(new(Key, "test", "test")).Create(user));
        Assert.Equal(user.SessionStamp.ToString(), token.Claims.Single(claim => claim.Type == UserAccess.SessionStampClaim).Value);
        Assert.Contains(token.Claims, claim => claim.Type == "permission" && claim.Value == UserAccess.HoursPermission);
        Assert.Contains(UserAccess.HoursPermission, UserAccess.EffectivePermissions(user));
        Assert.Contains(Permissions.VehiclesView, UserAccess.EffectivePermissions(user));
    }

    /// <summary>Создаёт пользователя с заданной ролью и профессией для теста.</summary>
    internal static User MakeUser() => new() { Id = Guid.NewGuid(), FirstName = "Иван", MiddleName = "", LastName = "Иванов",
        UserName = "worker", Email = "worker@local", PasswordHash = "hash", Role = "administrator",
        PositionId = Guid.NewGuid(), Profession = new() { Id = Guid.NewGuid(), Name = "Слесарь" } };

    /// <summary>Тестовое хранилище актуальности и отзыва сессий.</summary>
    private sealed class Sessions(User user) : IUserSessionStore
    {
        public User? User = user;
        /// <summary>Возвращает актуальность сессии из тестового хранилища.</summary>
        public Task<bool> IsCurrentAsync(Guid userId, Guid stamp, CancellationToken cancellationToken) =>
            Task.FromResult(User?.Id == userId && User.SessionStamp == stamp);
        /// <summary>Помечает сессию отозванной в тестовом хранилище.</summary>
        public Task RevokeAsync(Guid userId, Guid stamp, CancellationToken cancellationToken)
        {
            if (User?.Id == userId && User.SessionStamp == stamp) User.SessionStamp = Guid.NewGuid();
            return Task.CompletedTask;
        }
    }
}
