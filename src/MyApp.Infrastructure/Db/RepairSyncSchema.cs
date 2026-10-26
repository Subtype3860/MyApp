using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MyApp.Infrastructure.Db;

public sealed class RepairOperationReceipt
{
    public Guid Id { get; set; }
    public Guid Owner { get; set; }
    public string Hash { get; set; } = "";
    public Guid ResultId { get; set; }
}

public static class RepairSyncSchema
{
    public const string Sql = """
        CREATE TABLE IF NOT EXISTS repair_operation_receipts (
            id uuid PRIMARY KEY, owner uuid NOT NULL, hash text NOT NULL, result_id uuid NOT NULL
        );
        """;
    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static async Task<string> Version(AppDbContext db, Guid vehicle, CancellationToken ct)
    {
        var defects = await db.VehicleDefects.AsNoTracking().Where(x => x.VehicleId == vehicle).OrderBy(x => x.Id).ToArrayAsync(ct);
        var works = await db.VehicleWorks.AsNoTracking().Where(x => x.VehicleId == vehicle && x.DefectId != null).OrderBy(x => x.Id).ToArrayAsync(ct);
        return Hash(new { defects, works });
    }
}
