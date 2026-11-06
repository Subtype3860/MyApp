using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyApp.API.Controllers;
using MyApp.API.Extensions;
using MyApp.API.Games;
using MyApp.Application.Abstractions;
using MyApp.Application.Models;
using MyApp.Application.Security;
using MyApp.Infrastructure.Security;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class TanksOnlineTests
{
    [Fact]
    public void Tickets_are_single_use_expire_and_require_a_live_identity()
    {
        var clock=new Clock(); var tickets=new TanksTickets(clock);
        var identity=new TankIdentity(Guid.NewGuid(),Guid.NewGuid(),"synthetic",clock.GetUtcNow().AddHours(1));
        var ticket=tickets.Issue(identity);
        Assert.Equal(identity,tickets.Take(ticket)); Assert.Null(tickets.Take(ticket)); Assert.Null(tickets.Take("invalid"));
        ticket=tickets.Issue(identity);clock.Now=clock.Now.AddSeconds(21);Assert.Null(tickets.Take(ticket));
    }

    [Fact]
    public void Server_acknowledges_only_simulated_input_and_rejects_replayed_sequences()
    {
        var arena=new TanksArena();var identity=new TankIdentity(Guid.NewGuid(),Guid.NewGuid(),"synthetic",DateTimeOffset.UtcNow.AddHours(1));
        var connection=arena.Join(identity,out _)!;
        Assert.True(arena.SetInput(connection,new(1,0,null,null,false,7)));
        Assert.False(arena.SetInput(connection,new(1,0,null,null,false,7)));
        Assert.False(arena.SetInput(connection,new(1,0,null,null,false,6)));
        Assert.False(arena.SetInput(connection,new(1,0,null,null,false,-1)));
        arena.Tick();Assert.True(connection.Frames.Reader.TryRead(out var frame));
        var player=Player(frame!,identity.UserId);Assert.Equal(7,player.GetProperty("ackSeq").GetInt64());Assert.Equal(1,player.GetProperty("spawnId").GetInt32());
        arena.Leave(connection);connection=arena.Join(identity,out _)!;
        Assert.True(arena.SetInput(connection,new(0,0,null,null,false,1)));
    }

    [Fact]
    public void Arena_enforces_capacity_unique_users_and_server_movement_limits()
    {
        var arena=new TanksArena(); var identity=new TankIdentity(Guid.NewGuid(),Guid.NewGuid(),"synthetic",DateTimeOffset.UtcNow.AddHours(1));
        var player=arena.Join(identity,out _)!;
        Assert.Null(arena.Join(identity,out var duplicate));Assert.Equal("already_connected",duplicate);
        for(var i=0;i<7;i++) Assert.NotNull(arena.Join(identity with{UserId=Guid.NewGuid()},out _));
        Assert.Null(arena.Join(identity with{UserId=Guid.NewGuid()},out var full));Assert.Equal("arena_full",full);
        Assert.False(arena.SetInput(player,new(double.NaN,0,null,null,false)));
        Assert.False(arena.SetInput(player,new(50,0,null,null,false)));
        arena.Tick(); Assert.True(player.Frames.Reader.TryRead(out var initial));
        var before=Player(initial!,identity.UserId);
        arena.SetInput(player,new(1,0,null,null,false));
        for(var i=0;i<60;i++)arena.Tick();
        Assert.True(player.Frames.Reader.TryRead(out var afterFrame));
        var after=Player(afterFrame!,identity.UserId);
        // A held input expires in 0.5 seconds rather than moving forever after a disconnect.
        Assert.InRange(Math.Abs(after.GetProperty("x").GetDouble()-before.GetProperty("x").GetDouble()),0,3.31);
        arena.Leave(player);Assert.Equal(7,arena.Online);
        Assert.NotNull(arena.Join(identity,out _));
        Assert.True(TanksArena.Blocked(-12,-9));Assert.False(TanksArena.Blocked(32,0));Assert.True(TanksArena.Blocked(64,0));
        Assert.Equal(64,TanksArena.HalfSize);Assert.Equal(40,TanksArena.Obstacles.Length);
        foreach(var obstacle in TanksArena.Obstacles) Assert.True(TanksArena.Blocked(obstacle.X,obstacle.Z));
    }

    [Fact]
    public async Task Live_permissions_protect_http_and_websocket_two_players_share_a_room_and_revocation_disconnects()
    {
        await using var host=await Host.Start();
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Http.PostAsync("/api/tanks/ticket",null)).StatusCode);
        host.Authorize(host.Users.Items[0]);
        Assert.Equal(HttpStatusCode.Forbidden,(await host.Http.PutAsJsonAsync("/api/admin/users/"+host.Users.Items[0].Id,new {permissions=new[]{Permissions.TanksPlay}})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await host.Http.GetAsync("/api/tanks/lobby")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await host.Http.PostAsync("/api/tanks/ticket",null)).StatusCode);
        host.Users.Items[0].Permissions=Permissions.TanksPlay;
        Assert.Equal(HttpStatusCode.OK,(await host.Http.GetAsync("/api/tanks/lobby")).StatusCode);
        var ticket=await host.Ticket();
        using var first=await host.Connect(ticket);await Message(first,"joined");
        using(var replay=await host.Connect(ticket)) { var denied=await Read(replay);Assert.Null(denied);Assert.Equal(4401,(int)replay.CloseStatus!); }
        host.Authorize(host.Users.Items[1]);host.Users.Items[1].Permissions=Permissions.TanksPlay;
        using var second=await host.Connect(await host.Ticket());await Message(second,"joined");
        var snapshot=await Message(first,"state",r=>r.GetProperty("players").GetArrayLength()==2);
        Assert.Equal(2,snapshot.GetProperty("players").GetArrayLength());
        await Send(first,new{x=0,z=0,aimX=0,aimZ=0,fire=true});
        host.Users.Items[0].Permissions="";
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        {
            while(await Read(first,timeout.Token) is not null) { }
        }
        Assert.Equal(4403,(int)first.CloseStatus!);
        // A different session change also invalidates an already-issued ticket.
        var secondTicket=await host.Ticket();host.Users.Items[1].SessionStamp=Guid.NewGuid();
        using var revoked=await host.Connect(secondTicket);Assert.Null(await Read(revoked));Assert.Equal(4403,(int)revoked.CloseStatus!);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_and_cannot_set_position_or_health()
    {
        await using var host=await Host.Start();var user=host.Users.Items[0];user.Permissions=Permissions.TanksPlay;host.Authorize(user);
        using var socket=await host.Connect(await host.Ticket());await Message(socket,"joined");
        await Send(socket,new{x=9999,z=0,hp=9999,fire=true});
        while(await Read(socket) is not null) { }
        Assert.Equal(WebSocketCloseStatus.PolicyViolation,socket.CloseStatus);
    }

    private static JsonElement Player(byte[] frame,Guid id) { using var d=JsonDocument.Parse(frame);return d.RootElement.GetProperty("players").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==id).Clone(); }
    private static async Task Send(ClientWebSocket socket,object data) => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(data),WebSocketMessageType.Text,true,CancellationToken.None);
    private static async Task<JsonElement?> Read(ClientWebSocket socket,CancellationToken ct=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var buffer=new byte[65536];var offset=0;
        while(true) { var received=await socket.ReceiveAsync(buffer.AsMemory(offset),timeout.Token);if(received.MessageType==WebSocketMessageType.Close)return null;offset+=received.Count;if(received.EndOfMessage){using var d=JsonDocument.Parse(buffer.AsMemory(0,offset));return d.RootElement.Clone();} }
    }
    private static async Task<JsonElement> Message(ClientWebSocket socket,string type,Func<JsonElement,bool>? predicate=null)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while(await Read(socket,timeout.Token) is JsonElement message)if(message.GetProperty("type").GetString()==type&&(predicate?.Invoke(message)??true))return message;
        throw new InvalidOperationException("Socket closed before expected message.");
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class Users : IUserRepository,IUserSessionStore
    {
        public User[] Items=[Make("synthetic-one"),Make("synthetic-two")];
        static User Make(string name)=>new(){Id=Guid.NewGuid(),FirstName="Test",MiddleName="",LastName="Test",UserName=name,Email=name+"@example.invalid",PasswordHash="unused",Role="user"};
        public Task<User?> FindByIdAsync(Guid id,CancellationToken ct)=>Task.FromResult(Items.FirstOrDefault(u=>u.Id==id));
        public Task<bool> IsCurrentAsync(Guid id,Guid stamp,CancellationToken ct)=>Task.FromResult(Items.Any(u=>u.Id==id&&u.SessionStamp==stamp));
        public Task RevokeAsync(Guid id,Guid stamp,CancellationToken ct){var u=Items.FirstOrDefault(u=>u.Id==id&&u.SessionStamp==stamp);if(u is not null)u.SessionStamp=Guid.NewGuid();return Task.CompletedTask;}
        public Task<User?> FindByLoginAsync(string login,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct)=>Task.FromResult<IReadOnlyList<User>>(Items);
        public Task<bool> UserNameExistsAsync(string name,Guid? id,CancellationToken ct)=>Task.FromResult(false);
        public Task AddAsync(User user,CancellationToken ct)=>throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct)=>Task.CompletedTask;
    }
    private sealed class Host(WebApplication app,HttpClient http,Users users) : IAsyncDisposable
    {
        private const string Key="synthetic-tanks-key-at-least-thirty-two-bytes";
        public HttpClient Http= http;public Users Users=users;
        public void Authorize(User user)=>Http.DefaultRequestHeaders.Authorization=new("Bearer",new JwtTokenProvider(new(Key,"test","test")).Create(user));
        public async Task<string> Ticket(){var response=await Http.PostAsync("/api/tanks/ticket",null);response.EnsureSuccessStatusCode();var payload=await response.Content.ReadFromJsonAsync<JsonElement>();return payload.GetProperty("ticket").GetString()!;}
        public async Task<ClientWebSocket> Connect(string ticket){var socket=new ClientWebSocket();var uri=new UriBuilder(Http.BaseAddress!){Scheme="ws",Path="/api/tanks/socket"}.Uri;await socket.ConnectAsync(uri,CancellationToken.None);await Send(socket,new{ticket});return socket;}
        public static async Task<Host> Start()
        {
            var users=new Users();var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,0));
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Jwt:Key"]=Key,["Jwt:Issuer"]="test",["Jwt:Audience"]="test"});
            builder.Services.AddSingleton<IUserRepository>(users);builder.Services.AddSingleton<IUserSessionStore>(users);builder.Services.AddScoped<ITanksAccess,TanksAccess>();
            builder.Services.AddSingleton(TimeProvider.System);builder.Services.AddSingleton<TanksTickets>();builder.Services.AddSingleton<TanksArena>();builder.Services.AddSingleton<TanksServer>();builder.Services.AddHostedService<TanksTicker>();
            builder.Services.AddJwtAuthentication(builder.Configuration);builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(o=>o.AddPolicy("tanks",_=>System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
            builder.Services.AddControllers().AddApplicationPart(typeof(TanksController).Assembly);
            var app=builder.Build();app.UseAuthentication();app.UseRateLimiter();app.UseAuthorization();app.UseWebSockets();app.MapControllers();await app.StartAsync();
            var uri=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();return new(app,new HttpClient{BaseAddress=new Uri(uri)},users);
        }
        public async ValueTask DisposeAsync(){Http.Dispose();await app.StopAsync();await app.DisposeAsync();}
    }
}
