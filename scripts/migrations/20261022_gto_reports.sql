CREATE TABLE IF NOT EXISTS gto_reports (
    report_date date NOT NULL,
    shift integer NOT NULL CHECK (shift IN (1, 2)),
    revision integer NOT NULL CHECK (revision > 0),
    snapshot jsonb NOT NULL,
    updated_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
    updated_at timestamptz NOT NULL DEFAULT NOW(),
    PRIMARY KEY (report_date, shift)
);
