CREATE TABLE IF NOT EXISTS fleet_regions (
    id uuid PRIMARY KEY, name varchar(200) NOT NULL CHECK (length(btrim(name)) > 0),
    is_archived boolean NOT NULL DEFAULT false
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_fleet_regions_name ON fleet_regions (lower(btrim(name)));
CREATE TABLE IF NOT EXISTS fleet_region_grants (
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    region_id uuid NOT NULL REFERENCES fleet_regions(id) ON DELETE RESTRICT,
    PRIMARY KEY (user_id, region_id)
);
CREATE TABLE IF NOT EXISTS vehicle_region_assignments (
    id uuid PRIMARY KEY,
    vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE RESTRICT,
    region_id uuid NOT NULL REFERENCES fleet_regions(id) ON DELETE RESTRICT,
    started_at timestamptz NOT NULL, ended_at timestamptz,
    created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
    recorded_at timestamptz NOT NULL, note varchar(2000) NOT NULL DEFAULT '',
    CHECK (ended_at IS NULL OR ended_at > started_at)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_vehicle_region_current
    ON vehicle_region_assignments(vehicle_id) WHERE ended_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_vehicle_region_history ON vehicle_region_assignments(vehicle_id, started_at);
CREATE INDEX IF NOT EXISTS ix_vehicle_region_access ON vehicle_region_assignments(region_id, vehicle_id) WHERE ended_at IS NULL;
ALTER TABLE gto_reports ADD COLUMN IF NOT EXISTS region_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint c
        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY(c.conkey)
        WHERE c.conrelid = 'gto_reports'::regclass AND c.contype = 'p' AND a.attname = 'region_id') THEN
        ALTER TABLE gto_reports DROP CONSTRAINT gto_reports_pkey;
        ALTER TABLE gto_reports ADD PRIMARY KEY (region_id, report_date, shift);
    END IF;
END $$;
