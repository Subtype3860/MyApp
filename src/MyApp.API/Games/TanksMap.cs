using System.Text.Json;

namespace MyApp.API.Games;

public sealed record TankObstacle(double X, double Z, double W, double D, double H);

/// <summary>The renderer and server read the same map shipped with both builds.</summary>
public sealed record TanksMap(double HalfSize, TankObstacle[] Obstacles)
{
    public static TanksMap Current { get; } = Load();
    private static TanksMap Load()
    {
        var map = JsonSerializer.Deserialize<TanksMap>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Games", "tanks-arena.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if(map is null || !double.IsFinite(map.HalfSize) || map.HalfSize is < 16 or > 128 || map.Obstacles is null ||
            map.Obstacles.Any(o => !double.IsFinite(o.X) || !double.IsFinite(o.Z) || !double.IsFinite(o.W) || !double.IsFinite(o.D) || !double.IsFinite(o.H) ||
                o.W <= 0 || o.D <= 0 || o.H <= 0 || Math.Abs(o.X)+o.W/2 > map.HalfSize-2 || Math.Abs(o.Z)+o.D/2 > map.HalfSize-2))
            throw new InvalidDataException("Invalid tank arena map.");
        return map;
    }
}
