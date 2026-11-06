-- User-requested removal. Run inside a transaction after a verified backup.
-- Keeps previous snapshots and the original external source; no CASCADE.
DO $removal$
DECLARE
    expected_fingerprint text := current_setting('myapp.car_f_delete_fingerprint', true);
    actual_fingerprint text;
BEGIN
    IF to_regclass('public.car_f') IS NULL THEN
        RAISE NOTICE 'car_f is already absent';
        RETURN;
    END IF;
    IF (SELECT relkind FROM pg_class WHERE oid = 'public.car_f'::regclass) <> 'r' THEN
        RAISE EXCEPTION 'Expected the migrated regular car_f table';
    END IF;
    IF to_regclass('public.car_f_removed_snapshot_20261011') IS NOT NULL THEN
        RAISE EXCEPTION 'Removal snapshot already exists; inspect it before retrying';
    END IF;
    LOCK TABLE public.car_f IN ACCESS EXCLUSIVE MODE;
    SELECT md5(COALESCE(string_agg(row_to_json(t)::text, E'\n' ORDER BY id), ''))
      INTO actual_fingerprint FROM public.car_f AS t;
    IF expected_fingerprint IS NULL OR expected_fingerprint <> actual_fingerprint THEN
        RAISE EXCEPTION 'Take and verify a current backup before deleting car_f';
    END IF;
    CREATE TABLE public.car_f_removed_snapshot_20261011 (LIKE public.car_f INCLUDING ALL);
    INSERT INTO public.car_f_removed_snapshot_20261011 SELECT * FROM public.car_f;
    PERFORM setval(pg_get_serial_sequence('public.car_f_removed_snapshot_20261011', 'id'),
        GREATEST(COALESCE(max(id), 1), 1), COALESCE(max(id), 0) >= 1) FROM public.car_f_removed_snapshot_20261011;
    IF EXISTS (
        (SELECT * FROM public.car_f EXCEPT ALL SELECT * FROM public.car_f_removed_snapshot_20261011)
        UNION ALL
        (SELECT * FROM public.car_f_removed_snapshot_20261011 EXCEPT ALL SELECT * FROM public.car_f)
    ) THEN RAISE EXCEPTION 'Removal snapshot verification failed'; END IF;
    DROP TABLE public.car_f;
    COMMENT ON TABLE public.car_f_removed_snapshot_20261011 IS 'Verified snapshot before user-requested car_f removal on 2026-10-11';
END
$removal$;
