using Microsoft.EntityFrameworkCore;

namespace MyApp.Infrastructure.Db;

/// <summary>Добавляет таблицы графиков ТО без изменения существующих данных.</summary>
public static class MaintenancePlanSchema
{
    public static Task EnsureAsync(AppDbContext db, CancellationToken ct = default) =>
        db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS vehicle_maintenance_plans (
            vehicle_id uuid PRIMARY KEY REFERENCES number_car(id) ON DELETE RESTRICT,
            revision integer NOT NULL CHECK (revision > 0),
            snapshot jsonb NOT NULL,
            updated_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
            updated_at timestamptz NOT NULL DEFAULT NOW()
            );
            CREATE TABLE IF NOT EXISTS vehicle_maintenance_events (
            id uuid PRIMARY KEY,
            vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE RESTRICT,
            revision integer NOT NULL,
            kind text NOT NULL,
            description text NOT NULL DEFAULT '',
            event_date date NOT NULL,
            before_snapshot jsonb NOT NULL,
            after_snapshot jsonb NOT NULL,
            readings jsonb NOT NULL,
            created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
            created_at timestamptz NOT NULL DEFAULT NOW(),
            UNIQUE(vehicle_id, revision)
            );
            """, ct);
}
