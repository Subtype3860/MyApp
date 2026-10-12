using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MyApp.API.Controllers;
using MyApp.Application.Abstractions;
using MyApp.Application.Security;
using MyApp.Application.Services;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class VideoSessionRevocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Vehicle_video_ticket_is_rejected_and_removed_after_session_revocation(bool work)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new VehicleController { ControllerContext = Identity() };
        var videoId = Guid.NewGuid();
        var result = work ? controller.CreateWorkVideoStreamTicket(videoId, cache) : controller.CreateDefectVideoStreamTicket(videoId, cache);
        var ticket = Ticket(result);
        var service = DispatchProxy.Create<IVehicleService, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)service).Handler = (_, _) => throw new Exception("Revoked ticket must not read media.");
        Assert.IsType<UnauthorizedResult>(await controller.StreamVideo(ticket, RevokedSessions(), cache, service, default));
        Assert.IsType<NotFoundResult>(await controller.StreamVideo(ticket, RevokedSessions(), cache, service, default));
    }

    [Fact]
    public async Task Auxiliary_video_ticket_is_rejected_after_session_revocation()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = DispatchProxy.Create<IVehicleService, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)service).Handler = (method, _) =>
            method.Name == nameof(IVehicleService.AuxiliaryMediaExistsAsync) ? Task.FromResult(true) : throw new Exception("Revoked ticket must not read media.");
        var controller = new AuxiliaryWorkController(service, cache) { ControllerContext = Identity() };
        var ticket = Ticket(await controller.VideoTicket(Guid.NewGuid(), default));
        Assert.IsType<UnauthorizedResult>(await controller.VideoStream(ticket, RevokedSessions(), default));
        Assert.IsType<NotFoundResult>(await controller.VideoStream(ticket, RevokedSessions(), default));
    }

    private static IUserSessionStore RevokedSessions()
    {
        var store = DispatchProxy.Create<IUserSessionStore, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)store).Handler = (_, _) => Task.FromResult(false);
        return store;
    }

    private static ControllerContext Identity() => new() { HttpContext = new DefaultHttpContext {
        User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(UserAccess.SessionStampClaim, Guid.NewGuid().ToString())], "Bearer"))
    } };

    private static string Ticket(IActionResult result)
    {
        var value = Assert.IsType<OkObjectResult>(result).Value!;
        return ((string)value.GetType().GetProperty("url")!.GetValue(value)!).Split('/').Last();
    }
}
