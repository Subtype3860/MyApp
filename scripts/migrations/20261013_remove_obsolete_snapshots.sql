-- Apply only after exporting and verifying all three snapshot tables.
-- No CASCADE: unexpected database dependencies must stop the cleanup.
BEGIN;
DO $cleanup$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM app_schema_migrations WHERE migration_key = 'legacy_csv_bootstrap_v1') THEN
        RAISE EXCEPTION 'Legacy import has not been completed';
    END IF;
END
$cleanup$;
DROP TABLE IF EXISTS app_csv_migration_snapshots;
DROP TABLE IF EXISTS car_f_removed_snapshot_20261011;
DROP TABLE IF EXISTS car_f_snapshot_20261011;
INSERT INTO app_schema_migrations(migration_key) VALUES ('obsolete_snapshots_removed_20261013') ON CONFLICT DO NOTHING;
COMMIT;
