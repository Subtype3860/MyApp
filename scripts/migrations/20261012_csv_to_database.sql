-- Run as one transaction. Legacy views/files are preserved and never written.
SELECT pg_advisory_xact_lock(4386202, 0);

DO $upgrade$
BEGIN
    IF to_regclass('app_csv_sources') IS NOT NULL AND to_regclass('app_import_sources') IS NULL THEN
        ALTER TABLE app_csv_sources RENAME TO app_import_sources;
        ALTER TABLE app_import_sources DROP CONSTRAINT IF EXISTS app_csv_sources_file_name_check;
        ALTER TABLE app_import_sources RENAME COLUMN file_name TO source_key;
        UPDATE app_import_sources SET source_key = CASE source_key
            WHEN 'o.csv' THEN 'warehouse' WHEN 'c.csv' THEN 'mechanics' WHEN 'p.csv' THEN 'workers' ELSE source_key END;
    END IF;
    IF to_regclass('app_import_sources') IS NOT NULL AND EXISTS (
        SELECT 1 FROM pg_constraint WHERE conrelid = to_regclass('app_import_sources') AND conname = 'app_csv_sources_pkey'
    ) THEN
        ALTER TABLE app_import_sources RENAME CONSTRAINT app_csv_sources_pkey TO app_import_sources_pkey;
    END IF;
END
$upgrade$;
CREATE TABLE IF NOT EXISTS app_import_sources (
    source_key text PRIMARY KEY CHECK (source_key IN ('warehouse', 'mechanics', 'workers')),
    columns text[] NOT NULL,
    initialized boolean NOT NULL DEFAULT false,
    imported_at timestamptz
);
CREATE TABLE IF NOT EXISTS app_stock_balances (
    source_table text NOT NULL CHECK (source_table IN ('v_full_ost', 'v_meh_ost')),
    name text NOT NULL CHECK (name = BTRIM(name) AND name <> ''),
    unit text NOT NULL CHECK (BTRIM(unit) <> ''),
    quantity numeric NOT NULL CHECK (quantity >= 0),
    details jsonb NOT NULL DEFAULT '{}'::jsonb,
    updated_at timestamptz NOT NULL DEFAULT NOW(),
    PRIMARY KEY (source_table, name)
);
CREATE TABLE IF NOT EXISTS app_csv_workers (
    id uuid NOT NULL DEFAULT gen_random_uuid() CONSTRAINT app_csv_workers_id_pkey PRIMARY KEY,
    last_name text NOT NULL CHECK (last_name <> ''),
    first_name text NOT NULL CHECK (first_name <> ''),
    patronymic text NOT NULL DEFAULT '',
    profession text NOT NULL,
    details jsonb NOT NULL DEFAULT '{}'::jsonb,
    updated_at timestamptz NOT NULL DEFAULT NOW()
);
-- Stable primary keys; names are fallback import attributes.
ALTER TABLE app_stock_balances ADD COLUMN IF NOT EXISTS id uuid NOT NULL DEFAULT gen_random_uuid();
ALTER TABLE app_csv_workers ADD COLUMN IF NOT EXISTS id uuid NOT NULL DEFAULT gen_random_uuid();
DO $keys$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'app_stock_balances'::regclass AND conname = 'app_stock_balances_id_pkey') THEN
        ALTER TABLE app_stock_balances DROP CONSTRAINT app_stock_balances_pkey;
        ALTER TABLE app_stock_balances ADD CONSTRAINT app_stock_balances_id_pkey PRIMARY KEY (id);
        ALTER TABLE app_stock_balances ADD CONSTRAINT app_stock_balances_source_name_key UNIQUE (source_table, name);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'app_csv_workers'::regclass AND conname = 'app_csv_workers_id_pkey') THEN
        ALTER TABLE app_csv_workers DROP CONSTRAINT app_csv_workers_pkey;
        ALTER TABLE app_csv_workers ADD CONSTRAINT app_csv_workers_id_pkey PRIMARY KEY (id);

    END IF;
END
$keys$;
DO $names$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = 'app_csv_workers'::regclass AND attname = 'full_name' AND NOT attisdropped) THEN
        IF EXISTS (SELECT 1 FROM app_csv_workers WHERE full_name IS DISTINCT FROM CONCAT_WS(' ', last_name, first_name, NULLIF(patronymic, ''))) THEN
            RAISE EXCEPTION 'Worker full names differ from individual name fields; reconcile before dropping full_name';
        END IF;
    END IF;
END
$names$;
ALTER TABLE app_csv_workers DROP COLUMN IF EXISTS full_name;
CREATE INDEX IF NOT EXISTS ix_app_csv_workers_name_parts ON app_csv_workers(last_name, first_name, patronymic);


CREATE TEMP TABLE csv_bootstrap_snapshot (
    file_name text NOT NULL,
    data jsonb NOT NULL
) ON COMMIT DROP;

INSERT INTO app_import_sources(source_key, columns) VALUES
    ('warehouse', ARRAY['Наименование', 'Ед.изм.', 'Количество']),
    ('mechanics', ARRAY['Наименование', 'Ед.изм.', 'Количество']),
    ('workers', ARRAY['LastName', 'FirstName', 'Patronymic', 'Profession'])
ON CONFLICT (source_key) DO NOTHING;

DO $migration$
DECLARE
    src record;
    source_oid regclass;
BEGIN
    IF to_regclass('app_schema_migrations') IS NOT NULL THEN
        IF EXISTS (SELECT 1 FROM app_schema_migrations WHERE migration_key = 'legacy_csv_bootstrap_v1') THEN
            RETURN;
        END IF;
    END IF;
    FOR src IN SELECT * FROM (VALUES
        ('o.csv', 'v_full_ost', 1, 'warehouse'), ('c.csv', 'v_meh_ost', 2, 'mechanics')
    ) AS s(file_name, view_name, lock_key, source_key)
    LOOP
        IF (SELECT initialized FROM app_import_sources WHERE source_key = src.source_key FOR UPDATE) THEN
            CONTINUE;
        END IF;
        PERFORM pg_advisory_xact_lock(4386202, src.lock_key);
        source_oid := to_regclass(src.view_name);
        IF source_oid IS NOT NULL THEN
            -- Materialize once so validation and import see the exact same CSV snapshot.
            EXECUTE format('INSERT INTO csv_bootstrap_snapshot SELECT %L, row_to_json(t)::jsonb FROM %I AS t', src.file_name, src.view_name);
            -- Legacy balances may be negative or absent. Keep the original value in the
            -- snapshot, but expose no available stock for these positions.
            -- Merge duplicate positions only when their unit and details agree.
            IF EXISTS (
                SELECT 1 FROM csv_bootstrap_snapshot
                WHERE file_name = src.file_name
                GROUP BY BTRIM(data->>'Наименование')
                HAVING COUNT(DISTINCT BTRIM(data->>'Ед.изм.')) > 1
                    OR COUNT(DISTINCT (data - ARRAY['Наименование', 'Ед.изм.', 'Количество'])) > 1
            ) THEN
                RAISE EXCEPTION 'Conflicting duplicate stock positions in %', src.file_name;
            END IF;
            INSERT INTO app_stock_balances(source_table, name, unit, quantity, details)
            SELECT src.view_name, BTRIM(data->>'Наименование'), BTRIM(data->>'Ед.изм.'),
                   GREATEST(COALESCE(SUM(REPLACE(NULLIF(BTRIM(data->>'Количество'), ''), ',', '.')::numeric), 0), 0),
                   data - ARRAY['Наименование', 'Ед.изм.', 'Количество']
            FROM csv_bootstrap_snapshot WHERE file_name = src.file_name
            GROUP BY BTRIM(data->>'Наименование'), BTRIM(data->>'Ед.изм.'),
                     data - ARRAY['Наименование', 'Ед.изм.', 'Количество'];
            UPDATE app_import_sources SET columns = (
                SELECT array_agg(attname::text ORDER BY attnum) FROM pg_attribute
                WHERE attrelid = source_oid AND attnum > 0 AND NOT attisdropped
            ) WHERE source_key = src.source_key;
        END IF;
        UPDATE app_import_sources SET initialized = true WHERE source_key = src.source_key;
    END LOOP;

    IF NOT (SELECT initialized FROM app_import_sources WHERE source_key = 'workers' FOR UPDATE) THEN
        PERFORM pg_advisory_xact_lock(4386202, 3);
        source_oid := to_regclass('v_workers');
        IF source_oid IS NOT NULL THEN
            INSERT INTO csv_bootstrap_snapshot SELECT 'p.csv', row_to_json(t)::jsonb FROM v_workers t;
            INSERT INTO app_csv_workers(last_name, first_name, patronymic, profession, details)
            SELECT BTRIM(data->>'LastName'), BTRIM(data->>'FirstName'), COALESCE(BTRIM(data->>'Patronymic'), ''),
                   COALESCE(BTRIM(data->>'Profession'), ''),
                   data - ARRAY['LastName', 'FirstName', 'Patronymic', 'Profession', 'Роспись']
            FROM csv_bootstrap_snapshot WHERE file_name = 'p.csv';
            UPDATE app_import_sources SET columns = (
                SELECT array_agg(attname::text ORDER BY attnum) FROM pg_attribute
                WHERE attrelid = source_oid AND attnum > 0 AND NOT attisdropped AND attname <> 'Роспись'
            ) WHERE source_key = 'workers';
        ELSIF to_regclass('employees') IS NOT NULL THEN
            INSERT INTO csv_bootstrap_snapshot SELECT 'p.csv', row_to_json(t)::jsonb FROM employees t;
            INSERT INTO app_csv_workers(last_name, first_name, patronymic, profession)
            SELECT parts[1], parts[2], COALESCE(array_to_string(parts[3:cardinality(parts)], ' '), ''),
                   COALESCE(BTRIM(data->>'Profession'), '')
            FROM csv_bootstrap_snapshot,
                 LATERAL regexp_split_to_array(BTRIM(data->>'FullName'), '\s+') AS parts
            WHERE file_name = 'p.csv';
        END IF;
        UPDATE app_import_sources SET initialized = true WHERE source_key = 'workers';
    END IF;
END
$migration$;

CREATE TABLE IF NOT EXISTS app_schema_migrations (
    migration_key text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT NOW()
);
INSERT INTO app_schema_migrations(migration_key) VALUES ('legacy_csv_bootstrap_v1') ON CONFLICT DO NOTHING;
CREATE TABLE IF NOT EXISTS app_import_history (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    source_key text NOT NULL REFERENCES app_import_sources(source_key),
    added integer NOT NULL,
    updated integer NOT NULL,
    zeroed integer NOT NULL DEFAULT 0,
    imported_at timestamptz NOT NULL DEFAULT NOW()
);
