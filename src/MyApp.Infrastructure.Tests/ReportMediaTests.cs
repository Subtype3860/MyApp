using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.API.Extensions;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyApp.API.Controllers;
using MyApp.Application.DTO;
using MyApp.Application.Security;
using MyApp.Application.Services;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class ReportMediaTests
{
    [Fact]
    public async Task Http_anonymous_media_supports_ranges_but_cannot_issue_links()
    {
        var id = Guid.NewGuid();
        var service = DispatchProxy.Create<IVehicleService, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)service).Handler = (_, _) =>
            Task.FromResult<VehicleMediaStream?>(new("video.mp4", "video/mp4", new MemoryStream("0000ftyp0000"u8.ToArray())));
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Jwt:Key"] = Options.Key, ["Jwt:Issuer"] = Options.Issuer, ["Jwt:Audience"] = Options.Audience
        });
        builder.Services.AddJwtAuthentication(builder.Configuration);
        builder.Services.AddAuthorization(options => options.AddPolicy(Permissions.ShiftReport,
            policy => policy.RequireAuthenticatedUser().RequireClaim("permission", Permissions.ShiftReport)));
        builder.Services.AddSingleton(Options);
        builder.Services.AddSingleton(service);
        builder.Services.AddControllers().AddApplicationPart(typeof(ReportMediaController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.Single()) };
        var signature = new ReportMediaController(Options, service).Signature("video", id);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/report-media/video/{id}/{signature}");
        request.Headers.Range = new(0, 3);
        using var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("0000", await response.Content.ReadAsStringAsync());
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/report-media/links", new[] { new { id, type = "video" } })).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync($"/api/report-media/video/{Guid.NewGuid()}/{signature}")).StatusCode);
        await app.StopAsync();
    }
    private static readonly JwtOptions Options = new("synthetic-report-key-for-tests-only-123456789", "test", "test");
    private static ReportMediaController Controller(Func<MethodInfo, object?[], object?> handler, JwtOptions? options = null)
    {
        var service = DispatchProxy.Create<IVehicleService, WorkParticipationAuthorizationTests.InvocationProxy>();
        ((WorkParticipationAuthorizationTests.InvocationProxy)(object)service).Handler = handler;
        return new(options ?? Options, service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
    }

    [Theory]
    [InlineData("image")]
    [InlineData("video")]
    public async Task Link_survives_new_controller_and_opens_inline_without_identity(string type)
    {
        var id = Guid.NewGuid();
        var issuer = Controller((_, _) => throw new Exception());
        var signature = issuer.Signature(type, id);
        var reader = Controller((method, args) => {
            Assert.Equal(id, args[0]);
            if (method.Name == nameof(IVehicleService.GetWorkPhotoAsync))
                return Task.FromResult<VehicleWorkPhotoContent?>(new("photo.webp", "image/webp", "RIFF0000WEBP"u8.ToArray()));
            return Task.FromResult<VehicleMediaStream?>(new("video.mp4", "video/mp4", new MemoryStream("0000ftyp0000"u8.ToArray())));
        });
        Assert.False(reader.User.Identity?.IsAuthenticated ?? false);
        var result = Assert.IsAssignableFrom<FileResult>(await reader.Open(type, id, signature, default));
        Assert.True(result.EnableRangeProcessing);
        Assert.True(string.IsNullOrEmpty(result.FileDownloadName));
        Assert.Equal("no-referrer", reader.Response.Headers["Referrer-Policy"]);
        if (result is FileStreamResult stream) await stream.FileStream.DisposeAsync();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("type")]
    [InlineData("signature")]
    [InlineData("key")]
    public async Task Altered_links_never_read_storage(string change)
    {
        var id = Guid.NewGuid();
        var controller = Controller((_, _) => throw new Exception("Storage must not be read"));
        var signature = controller.Signature("image", id);
        if (change == "id") id = Guid.NewGuid();
        if (change == "signature") signature = new string('x', 43);
        if (change == "key") controller = Controller((_, _) => throw new Exception(), Options with { Key = "different-test-key" });
        Assert.IsType<NotFoundResult>(await controller.Open(change == "type" ? "video" : "image", id, signature, default));
    }

    [Fact]
    public async Task Deleted_file_returns_not_found()
    {
        var id = Guid.NewGuid();
        var controller = Controller((_, _) => Task.FromResult<VehicleWorkPhotoContent?>(null));
        Assert.IsType<NotFoundResult>(await controller.Open("image", id, controller.Signature("image", id), default));
    }

    [Fact]
    public void Only_reading_a_signed_link_is_anonymous()
    {
        Assert.Equal(Permissions.ShiftReport, typeof(ReportMediaController).GetMethod("Links")!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.NotNull(typeof(ReportMediaController).GetMethod("Open")!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(typeof(ReportMediaController).GetMethod("Signature")!.GetCustomAttribute<NonActionAttribute>());
    }
}
