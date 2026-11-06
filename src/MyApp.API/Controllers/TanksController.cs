using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyApp.API.Games;
using MyApp.Application.Security;

namespace MyApp.API.Controllers;

[ApiController]
[Route("api/tanks")]
public sealed class TanksController(ITanksAccess access, TanksTickets tickets, TanksArena arena) : ControllerBase
{
    private async Task<TankIdentity?> Identity(CancellationToken ct)
    {
        if(!SessionIdentity.TryRead(User,out var id,out var stamp) || !long.TryParse(User.FindFirst("exp")?.Value,out var expiry)) return null;
        return await access.FindAsync(id,stamp,DateTimeOffset.FromUnixTimeSeconds(expiry),ct);
    }
    [Authorize]
    [HttpGet("lobby")]
    public async Task<IActionResult> Lobby(CancellationToken ct) => await Identity(ct) is null
        ? StatusCode(403,new{message="Доступ к сетевым танкам выдаёт администратор в настройках пользователя."})
        : Ok(new{online=arena.Online,capacity=TanksArena.Capacity});

    [Authorize]
    [EnableRateLimiting("tanks")]
    [HttpPost("ticket")]
    public async Task<IActionResult> Ticket(CancellationToken ct)
    {
        var identity=await Identity(ct);
        if(identity is null) return StatusCode(403,new{message="Администратор не выдал доступ к игре либо допуск отозван."});
        try { return Ok(new{ticket=tickets.Issue(identity),expiresIn=20}); }
        catch(InvalidOperationException) { return StatusCode(429); }
    }
    [AllowAnonymous]
    [EnableRateLimiting("tanks")]
    [Route("socket")]
    public async Task Socket([FromServices] TanksServer server)
    {
        if(!HttpContext.WebSockets.IsWebSocketRequest) { Response.StatusCode=400; return; }
        using var socket=await HttpContext.WebSockets.AcceptWebSocketAsync();
        await server.ConnectAsync(socket,HttpContext.RequestAborted);
    }
}
