using System.Text.Json;
using System.Threading.Channels;

namespace MyApp.API.Games;

public sealed record TankInput(double X, double Z, double? AimX, double? AimZ, bool Fire, long Seq = 0)
{
    public static readonly TankInput Idle = new(0, 0, null, null, false);
    public bool IsValid => Seq is >= 0 and <= 9007199254740991 && double.IsFinite(X) && double.IsFinite(Z) && Math.Abs(X) <= 1 && Math.Abs(Z) <= 1 &&
        (AimX is null && AimZ is null || AimX is double x && AimZ is double z && double.IsFinite(x) && double.IsFinite(z) && Math.Abs(x) <= 100 && Math.Abs(z) <= 100);
}

public sealed class ArenaTank(Guid id, string name)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public double X, Z, Yaw, Turret, Hp = 100, Cooldown, RespawnAt, ProtectedUntil, LastInputAt, DisconnectedAt;
    public int Score, Kills, Collected;
    public long LastSequence, AckSeq;
    public int SpawnId;
    public bool Connected;
    public TankInput Input = TankInput.Idle;
    public Dictionary<string, double> Skills { get; } = [];
}

public sealed class ArenaConnection(TankIdentity identity)
{
    public TankIdentity Identity { get; } = identity;
    public Channel<byte[]> Frames { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
}

public sealed record ArenaPickup(int Id, double X, double Z, string Type, double Phase);
public sealed class ArenaBullet(Guid owner, double x, double z, double vx, double vz, double damage)
{
    public Guid Owner = owner;
    public double X = x, Z = z, Vx = vx, Vz = vz, Damage = damage, Life = 2.8;
}

/// <summary>One authoritative PvP arena. All mutations and snapshot publication share one lock.</summary>
public sealed class TanksArena
{
    public const int Capacity = 8;
    public static double HalfSize => TanksMap.Current.HalfSize;
    public static TankObstacle[] Obstacles => TanksMap.Current.Obstacles;
    private static readonly string[] skillTypes = ["speed", "shield", "damage", "double", "repair"];
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private readonly object gate = new();
    private readonly Dictionary<Guid, ArenaTank> tanks = [];
    private readonly Dictionary<Guid, ArenaConnection> connections = [];
    private readonly List<ArenaBullet> bullets = [];
    private readonly List<ArenaPickup> pickups = [];
    private readonly Random random = new();
    private double time, spawnDelay;
    private int nextId;

    public TanksArena() { for (var i = 0; i < 28; i++) SpawnPickup(); }
    public int Online { get { lock(gate) return connections.Count; } }
    public ArenaConnection? Join(TankIdentity identity, out string? reason)
    {
        lock (gate)
        {
            reason = connections.ContainsKey(identity.UserId) ? "already_connected" : connections.Count >= Capacity ? "arena_full" : null;
            if (reason is not null) return null;
            if (!tanks.TryGetValue(identity.UserId, out var tank))
            {
                tank = new(identity.UserId, identity.Name); tanks.Add(identity.UserId, tank); Respawn(tank);
            }
            tank.Connected = true; tank.Input = TankInput.Idle; tank.LastInputAt = time; tank.LastSequence = 0; tank.AckSeq = 0;
            var connection = new ArenaConnection(identity); connections.Add(identity.UserId, connection); return connection;
        }
    }
    public void Leave(ArenaConnection connection)
    {
        lock (gate)
        {
            if (!connections.TryGetValue(connection.Identity.UserId, out var active) || !ReferenceEquals(active, connection)) return;
            connections.Remove(connection.Identity.UserId); connection.Frames.Writer.TryComplete();
            var tank = tanks[connection.Identity.UserId]; tank.Connected = false; tank.Input = TankInput.Idle; tank.DisconnectedAt = time;
            bullets.RemoveAll(b => b.Owner == tank.Id);
        }
    }
    public bool SetInput(ArenaConnection connection, TankInput input)
    {
        if (!input.IsValid) return false;
        lock(gate)
        {
            if (!connections.TryGetValue(connection.Identity.UserId, out var active) || !ReferenceEquals(active, connection)) return false;
            var tank = tanks[connection.Identity.UserId];
            if(input.Seq < tank.LastSequence || input.Seq > 0 && input.Seq == tank.LastSequence) return false;
            tank.LastSequence = input.Seq; tank.Input = input; tank.LastInputAt = time; return true;
        }
    }
    public static bool Blocked(double x, double z, double radius = .85) => Math.Abs(x) > HalfSize-1-radius || Math.Abs(z) > HalfSize-1-radius ||
        Obstacles.Any(o => Math.Abs(x-o.X) < o.W/2+radius && Math.Abs(z-o.Z) < o.D/2+radius);
    private static double Distance(double x, double z) => Math.Sqrt(x*x+z*z);
    private void Move(ArenaTank tank, double dx, double dz)
    {
        if (!Blocked(tank.X+dx,tank.Z) && !tanks.Values.Any(t => t != tank && t.Connected && t.Hp>0 && Distance(t.X-tank.X-dx,t.Z-tank.Z)<1.7)) tank.X+=dx;
        if (!Blocked(tank.X,tank.Z+dz) && !tanks.Values.Any(t => t != tank && t.Connected && t.Hp>0 && Distance(t.X-tank.X,t.Z-tank.Z-dz)<1.7)) tank.Z+=dz;
    }
    private void Respawn(ArenaTank tank)
    {
        for(var i=0;i<100;i++) { tank.X=(random.NextDouble()-.5)*(HalfSize-6)*2; tank.Z=(random.NextDouble()-.5)*(HalfSize-6)*2; if(!Blocked(tank.X,tank.Z,2) && !tanks.Values.Any(t=>t!=tank && t.Connected && t.Hp>0 && Distance(t.X-tank.X,t.Z-tank.Z)<8)) break; }
        tank.Hp=100; tank.Skills.Clear(); tank.ProtectedUntil=time+3; tank.Cooldown=1; tank.Input=TankInput.Idle; tank.SpawnId++;
    }
    private void SpawnPickup()
    {
        double x=0,z=0; var found=false;
        for(var i=0;i<50;i++) { x=(random.NextDouble()-.5)*(HalfSize-5)*2; z=(random.NextDouble()-.5)*(HalfSize-5)*2; if(!Blocked(x,z,2) && !pickups.Any(p=>Distance(p.X-x,p.Z-z)<3)) { found=true; break; } }
        if(found) pickups.Add(new(++nextId,x,z,skillTypes[random.Next(skillTypes.Length)],random.NextDouble()*Math.PI*2));
    }
    public void Tick()
    {
        lock(gate)
        {
            const double dt=.05; time+=dt;
            foreach(var tank in tanks.Values.Where(t=>t.Connected))
            {
                if(tank.Hp<=0) { if(time>=tank.RespawnAt) Respawn(tank); else continue; }
                foreach(var key in tank.Skills.Keys.ToArray()) { tank.Skills[key]-=dt; if(tank.Skills[key]<=0) tank.Skills.Remove(key); }
                tank.Cooldown-=dt; var input=time-tank.LastInputAt<=.5?tank.Input:TankInput.Idle;
                if(input.Seq>0) tank.AckSeq=input.Seq;
                var length=Math.Max(1,Distance(input.X,input.Z)); var speed=tank.Skills.ContainsKey("speed")?10.5:6;
                Move(tank,input.X/length*speed*dt,input.Z/length*speed*dt);
                if(Math.Abs(input.X)+Math.Abs(input.Z)>.01) tank.Yaw=Math.Atan2(input.X,input.Z);
                if(input.AimX is double ax && input.AimZ is double az) tank.Turret=Math.Atan2(ax-tank.X,az-tank.Z);
                else { var target=tanks.Values.Where(t=>t!=tank&&t.Connected&&t.Hp>0).MinBy(t=>Distance(t.X-tank.X,t.Z-tank.Z)); tank.Turret=target is null?tank.Yaw:Math.Atan2(target.X-tank.X,target.Z-tank.Z); }
                if(input.Fire && tank.Cooldown<=0)
                {
                    // Firing cancels spawn protection; it cannot be used to attack invulnerably.
                    tank.ProtectedUntil=0;
                    foreach(var offset in tank.Skills.ContainsKey("double")?new[]{-.09,.09}:new[]{0d}) { var a=tank.Turret+offset; bullets.Add(new(tank.Id,tank.X+Math.Sin(a)*1.55,tank.Z+Math.Cos(a)*1.55,Math.Sin(a)*25,Math.Cos(a)*25,tank.Skills.ContainsKey("damage")?40:20)); }
                    tank.Cooldown=.4;
                }
                foreach(var pickup in pickups.Where(p=>Distance(p.X-tank.X,p.Z-tank.Z)<1.5).ToArray())
                { pickups.Remove(pickup); tank.Collected++; tank.Score+=25; if(pickup.Type=="repair") tank.Hp=Math.Min(100,tank.Hp+35); else tank.Skills[pickup.Type]=pickup.Type=="shield"?20:18; }
            }
            foreach(var bullet in bullets)
            {
                bullet.X+=bullet.Vx*dt; bullet.Z+=bullet.Vz*dt; bullet.Life-=dt;
                if(Blocked(bullet.X,bullet.Z,.08)) { bullet.Life=0; continue; }
                var target=tanks.Values.FirstOrDefault(t=>t.Id!=bullet.Owner&&t.Connected&&t.Hp>0&&Distance(t.X-bullet.X,t.Z-bullet.Z)<1.05);
                if(target is null) continue; bullet.Life=0;
                if(target.ProtectedUntil>time) continue;
                target.Hp=Math.Max(0,target.Hp-bullet.Damage*(target.Skills.ContainsKey("shield")?.2:1));
                if(target.Hp<=0) { target.RespawnAt=time+3; target.Skills.Clear(); if(tanks.TryGetValue(bullet.Owner,out var killer)) { killer.Kills++; killer.Score+=100; } }
            }
            bullets.RemoveAll(b=>b.Life<=0);
            spawnDelay-=dt; if(spawnDelay<=0) { spawnDelay=2; if(pickups.Count<36) SpawnPickup(); }
            foreach(var id in tanks.Where(t=>!t.Value.Connected&&time-t.Value.DisconnectedAt>60).Select(t=>t.Key).ToArray()) tanks.Remove(id);
            var snapshot=JsonSerializer.SerializeToUtf8Bytes(new { type="state",time,players=tanks.Values.Where(t=>t.Connected).Select(t=>new{t.Id,t.Name,t.X,t.Z,t.Yaw,t.Turret,t.Hp,t.Score,t.Kills,t.Collected,t.AckSeq,t.SpawnId,skills=t.Skills,respawnIn=Math.Max(0,t.RespawnAt-time),protectedSpawn=t.ProtectedUntil>time}),bullets=bullets.Select(b=>new{b.X,b.Z,b.Vx,b.Vz,owner=b.Owner}),pickups },json);
            foreach(var connection in connections.Values) connection.Frames.Writer.TryWrite(snapshot);
        }
    }
}
