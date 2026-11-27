using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db;

public static class MaintenanceCardSchema
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task EnsureAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS maintenance_card_templates (
              "Id" uuid PRIMARY KEY, "Revision" integer NOT NULL, "Published" boolean NOT NULL,
              "Snapshot" jsonb NOT NULL, "ApprovedBy" uuid NULL, "ApprovedAt" timestamptz NULL);
            CREATE TABLE IF NOT EXISTS maintenance_cards (
              "Id" uuid PRIMARY KEY, "VehicleId" uuid NOT NULL REFERENCES number_car(id),
              "TemplateId" uuid NOT NULL REFERENCES maintenance_card_templates("Id"), "TemplateRevision" integer NOT NULL,
              "PlanRevision" integer NOT NULL, "RunInId" uuid NULL, "VehicleLabel" text NOT NULL,
              "Template" jsonb NOT NULL, "Data" jsonb NOT NULL, "Audit" jsonb NOT NULL, "Defects" jsonb NOT NULL,
              "Status" text NOT NULL CHECK ("Status" IN ('draft','submitted','accepted','cancelled')),
              "Revision" integer NOT NULL, "CreatedBy" uuid NOT NULL REFERENCES app_users(id), "CreatedAt" timestamptz NOT NULL,
              "AcceptedBy" uuid NULL REFERENCES app_users(id), "AcceptedAt" timestamptz NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_maintenance_card_active
              ON maintenance_cards ("VehicleId", COALESCE("RunInId", '00000000-0000-0000-0000-000000000000'::uuid))
              WHERE "Status" IN ('draft','submitted');
            ALTER TABLE maintenance_cards ADD COLUMN IF NOT EXISTS "Readings" jsonb NOT NULL DEFAULT '[]';
            CREATE TABLE IF NOT EXISTS maintenance_card_photos (
              "Id" uuid PRIMARY KEY, "CardId" uuid NOT NULL REFERENCES maintenance_cards("Id"),
              "OperationId" text NOT NULL, "ContentType" text NOT NULL, "Content" bytea NOT NULL,
              "CreatedBy" uuid NOT NULL REFERENCES app_users(id), "CreatedAt" timestamptz NOT NULL);
            """, ct);
        using var stream = typeof(MaintenanceCardSchema).Assembly.GetManifestResourceStream("MyApp.Infrastructure.Db.Seeds.maintenance-cards.json")!;
        var seeds = (await JsonSerializer.DeserializeAsync<MaintenanceCardTemplate[]>(stream, Json, ct))!;
        foreach (var seed in seeds)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO maintenance_card_templates (\"Id\",\"Revision\",\"Published\",\"Snapshot\") VALUES ({seed.Id},1,false,CAST({seed.Snapshot} AS jsonb)) ON CONFLICT (\"Id\") DO NOTHING", ct);
        // Only the exact old standard cycle qualifies. Custom schedules and all past events stay intact.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(721, 1)", ct);
        var old = new[] {350,700,1000,350,700,2000,350,700,3000,350,700,4000}
            .Select((v,i) => new MaintenanceStep("ТО-" + v, i % 3 == 2 ? 300 : 350)).ToArray();
        var old250 = new[] {250,500,250,1000,250,500,250,2000,250,500,250,3000,250,500,250,4000}
            .Select(v => new MaintenanceStep("ТО-" + v, 250)).ToArray();
        foreach (var vehicleId in await db.VehicleMaintenancePlans.IgnoreQueryFilters().AsNoTracking().Select(p => p.VehicleId).OrderBy(id => id).ToArrayAsync(ct))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({vehicleId.ToString()}, 227))", ct);
            var stored = await db.VehicleMaintenancePlans.IgnoreQueryFilters().SingleAsync(p => p.VehicleId == vehicleId, ct);
            await db.Entry(stored).ReloadAsync(ct);
            var plan = JsonSerializer.Deserialize<MaintenancePlan>(stored.Snapshot, Json);
            if (plan is null) continue;
            var index = plan.Steps.SequenceEqual(old) ? 8 : plan.Steps.SequenceEqual(old250) ? 11 : -1;
            if (index < 0) continue;
            var before = stored.Snapshot;
            plan.Steps[index] = plan.Steps[index] with { Name = "ТО-1000" };
            stored.Snapshot = JsonSerializer.Serialize(plan, Json); stored.Revision++; stored.UpdatedAt = DateTimeOffset.UtcNow;
            db.VehicleMaintenanceEvents.Add(new() { Id = Guid.NewGuid(), VehicleId = stored.VehicleId, Revision = stored.Revision,
                Kind = "schedule", Description = "Уточнение стандартного цикла: на отметке 3000 выполняется ТО-1000; позиция и показания сохранены.",
                Before = before, After = stored.Snapshot, Readings = "[]", Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Asia/Sakhalin").DateTime),
                CreatedBy = stored.UpdatedBy, CreatedAt = stored.UpdatedAt });
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
}
