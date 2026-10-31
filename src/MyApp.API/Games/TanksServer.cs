using System.Net.WebSockets;
using System.Text.Json;

namespace MyApp.API.Games;

public sealed class TanksTicker(TanksArena arena) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while(await timer.WaitForNextTickAsync(stoppingToken)) arena.Tick();
    }
}

/// <summary>Bounded messages, one writer per socket, live session checks and no client-owned physics.</summary>
public sealed class TanksServer(TanksArena arena, TanksTickets tickets, IServiceScopeFactory scopes)
{
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    public async Task ConnectAsync(WebSocket socket, CancellationToken requestAborted)
    {
        ArenaConnection? connection = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        var closeCode = 1000; var closeReason = "disconnected";
        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            var hello = await ReceiveAsync(socket, helloTimeout.Token);
            if(hello is null || !hello.Value.TryGetProperty("ticket",out var ticket) || ticket.ValueKind!=JsonValueKind.String) { closeCode=4401; return; }
            var identity = tickets.Take(ticket.GetString());
            if(identity is null) { closeCode=4401; return; }
            using(var scope=scopes.CreateScope())
            {
                if(await scope.ServiceProvider.GetRequiredService<ITanksAccess>().FindAsync(identity.UserId,identity.Stamp,identity.ExpiresAt,stop.Token) is null) { closeCode=4403; return; }
            }
            connection=arena.Join(identity,out var reason);
            if(connection is null) { closeCode=reason=="arena_full"?4429:4409; closeReason=reason!; return; }
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new{type="joined",selfId=identity.UserId,capacity=TanksArena.Capacity},json),WebSocketMessageType.Text,true,stop.Token);
            var receive=ReadInputs(socket,connection,stop.Token);
            var send=SendFrames(socket,connection,stop.Token);
            var check=CheckAccess(connection,stop.Token);
            try
            {
                var done=await Task.WhenAny(receive,send,check);
                if(done==check) { closeCode=await check; closeReason=closeCode==4401?"session_expired":"access_revoked"; }
                else await done;
            }
            catch(JsonException) { closeCode=1008; closeReason="invalid_input"; }
            catch(InvalidDataException) { closeCode=1008; closeReason="invalid_input"; }
            catch(OperationCanceledException) { closeCode=1001; closeReason="timeout"; }
            catch(WebSocketException) { closeCode=1001; }
            catch(Exception) { closeCode=1011; closeReason="server_error"; }
            finally
            {
                // Finish the single sender before sending a close frame. Cancelling a pending
                // ReceiveAsync first aborts the socket and loses the access-revoked close code.
                arena.Leave(connection);
                try { await send; } catch { }
                if(socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var closeTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try { await socket.CloseOutputAsync((WebSocketCloseStatus)closeCode,closeReason,closeTimeout.Token); } catch { }
                }
                try { await receive.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
                stop.Cancel();
                try { await Task.WhenAll(receive,check); } catch { }
            }
        }
        catch(OperationCanceledException) { closeCode=4401; }
        catch(WebSocketException) { }
        catch(JsonException) { closeCode=1008; }
        catch(InvalidDataException) { closeCode=1008; }
        catch(Exception) { closeCode=1011; closeReason="server_error"; }
        finally
        {
            stop.Cancel(); if(connection is not null) arena.Leave(connection);
            if(socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await socket.CloseOutputAsync((WebSocketCloseStatus)closeCode,closeReason,closeTimeout.Token); } catch { socket.Abort(); }
            }
        }
    }
    private async Task<int> CheckAccess(ArenaConnection connection,CancellationToken ct)
    {
        while(true)
        {
            await Task.Delay(TimeSpan.FromSeconds(3),ct);
            var identity=connection.Identity;
            if(identity.ExpiresAt<=DateTimeOffset.UtcNow) return 4401;
            using var scope=scopes.CreateScope();
            if(await scope.ServiceProvider.GetRequiredService<ITanksAccess>().FindAsync(identity.UserId,identity.Stamp,identity.ExpiresAt,ct) is null) return 4403;
        }
    }
    private static async Task SendFrames(WebSocket socket,ArenaConnection connection,CancellationToken ct)
    {
        await foreach(var frame in connection.Frames.Reader.ReadAllAsync(ct))
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await socket.SendAsync(frame,WebSocketMessageType.Text,true,timeout.Token);
        }
    }
    private async Task ReadInputs(WebSocket socket,ArenaConnection connection,CancellationToken ct)
    {
        var window=DateTimeOffset.UtcNow; var count=0;
        while(!ct.IsCancellationRequested)
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var payload=await ReceiveAsync(socket,timeout.Token); if(payload is null) return;
            if(DateTimeOffset.UtcNow-window>TimeSpan.FromSeconds(1)) { window=DateTimeOffset.UtcNow; count=0; }
            if(++count>60) throw new InvalidDataException("Input rate exceeded.");
            var input=payload.Value.Deserialize<TankInput>(json);
            if(input is null || !arena.SetInput(connection,input)) throw new InvalidDataException("Invalid input.");
        }
    }
    public static async Task<JsonElement?> ReceiveAsync(WebSocket socket,CancellationToken ct)
    {
        var buffer=new byte[1024]; var length=0;
        while(true)
        {
            if(length==buffer.Length) throw new InvalidDataException("Message too large.");
            var received=await socket.ReceiveAsync(buffer.AsMemory(length),ct);
            if(received.MessageType==WebSocketMessageType.Close) return null;
            if(received.MessageType!=WebSocketMessageType.Text) throw new InvalidDataException("Text messages required.");
            length+=received.Count;
            if(received.EndOfMessage) { using var document=JsonDocument.Parse(buffer.AsMemory(0,length),new JsonDocumentOptions{MaxDepth=6}); return document.RootElement.Clone(); }
        }
    }
}
