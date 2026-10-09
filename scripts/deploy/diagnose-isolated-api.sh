#!/usr/bin/env bash
# Safe database-initialization diagnostic using a restored, isolated PostgreSQL.
# No production database, credentials, service restart or remote CIFS mounts.
set -euo pipefail
umask 077

BACKUP="/var/backups/myapp/postgres-20261009-050613"
RELEASE="/var/lib/myapp-releases/d9cb41b796930e9d885b7ac2e094f63554d60781"
PG_IMAGE="localhost/myapp-postgres17-restore"
API_IMAGE="mcr.microsoft.com/dotnet/aspnet:10.0"

test -s "$BACKUP/job.dump"
test -x "$RELEASE/api/MyApp.API"
podman image exists "$PG_IMAGE" || { echo "Missing tested PostgreSQL PL/Python image"; exit 1; }
podman image exists "$API_IMAGE" || { echo "Missing previously pulled ASP.NET image"; exit 1; }

(cd "$BACKUP" && sha256sum -c job.dump.sha256)

WORK=$(mktemp -d /var/tmp/myapp-iso-api-XXXXXXXX)
POD="myapp-iso-api-$$"
PG="$POD-pg"
API="$POD-api"

cleanup() {
  podman pod rm -f "$POD" >/dev/null 2>&1 || true
  rm -rf -- "$WORK"
}
trap cleanup EXIT

# Copy the release into a disposable location so :Z cannot relabel
# any files in the release archive or currently deployed directories.
mkdir -p "$WORK/api"
cp -a "$RELEASE/api/." "$WORK/api/"

# PostgreSQL's restored file_fdw tables refer to /mnt/shara/data/*.csv,
# a path on the ORIGINAL DATABASE HOST (not the Fedora VPS). Copy only
# readable regular CSV files from the Fedora CIFS mount into temporary,
# private, disposable storage. NEVER bind mount the production share into
# the database: its extensions/functions can have filesystem side effects.
CSV_SOURCE="${MYAPP_CSV_SNAPSHOT_SOURCE:-/mnt/dietpi/data}"
test -r "$CSV_SOURCE/p.csv" || {
  echo "Required employee CSV is missing or unreadable on Fedora: $CSV_SOURCE/p.csv"
  echo "Check the read-only CIFS mount; do not connect the live share to the test container."
  exit 1
}
mkdir -p "$WORK/pg-csv"
chmod 755 "$WORK/pg-csv"
shopt -s nullglob
csv_count=0
for source in "$CSV_SOURCE"/*.csv; do
  # Avoid following symlinks into unrelated parts of the shared filesystem.
  if [[ ! -f "$source" || -L "$source" || ! -r "$source" ]]; then
    echo "Skipping nonregular, symlinked or unreadable CSV: $(basename "$source")"
    continue
  fi
  cp -- "$source" "$WORK/pg-csv/$(basename "$source")"
  chmod 444 "$WORK/pg-csv/$(basename "$source")"
  csv_count=$((csv_count + 1))
done
shopt -u nullglob
test "$csv_count" -gt 0
echo "=== Staged $csv_count local read-only CSV copies for isolated PostgreSQL ==="
echo "The live CIFS share is not mounted in the test pod."

echo "=== Start isolated PostgreSQL 17 (no external network) ==="
podman pod create --name "$POD" --network none >/dev/null
podman run -d --pod "$POD" --name "$PG" \
  --tmpfs /var/lib/postgresql/data:rw,size=1g \
  -v "$WORK/pg-csv:/mnt/shara/data:ro,Z" \
  -e POSTGRES_HOST_AUTH_METHOD=trust \
  -e POSTGRES_DB=restore_job \
  -e POSTGRES_INITDB_ARGS=--locale=ru_RU.UTF-8 \
  "$PG_IMAGE" >/dev/null

ready=0
for i in $(seq 1 90); do
  if podman exec "$PG" pg_isready -U postgres -d restore_job >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 1
done
test "$ready" -eq 1 || { echo "PostgreSQL did not start"; exit 1; }

echo "=== Restore PostgreSQL backup (in disposable pod only) ==="
podman cp "$BACKUP/job.dump" "$PG:/tmp/job.dump"
podman exec "$PG" pg_restore -U postgres -d restore_job \
  --no-owner --no-acl --exit-on-error --single-transaction /tmp/job.dump

echo "=== Restored file_fdw CSV references (metadata only) ==="
podman exec "$PG" psql -U postgres -d restore_job -v ON_ERROR_STOP=1 -c \
  "SELECT n.nspname AS schema, c.relname AS foreign_table,
          opt.option_value AS configured_filename
   FROM pg_foreign_table ft
   JOIN pg_class c ON c.oid=ft.ftrelid
   JOIN pg_namespace n ON n.oid=c.relnamespace
   CROSS JOIN LATERAL pg_options_to_table(ft.ftoptions) opt
   WHERE opt.option_name='filename'
   ORDER BY n.nspname,c.relname;" 

echo "=== Restored component requirements column types ==="
podman exec "$PG" psql -U postgres -d restore_job -v ON_ERROR_STOP=1 -c \
  "SELECT table_name, column_name, data_type
   FROM information_schema.columns
   WHERE table_schema='public'
     AND table_name IN ('component_requirements','component_requirement_items','employees')
     AND column_name IN ('form_data','requirement_id','FullName','Profession','source_table','issuer_name')
   ORDER BY table_name, column_name;"

echo "=== Start isolated release API ==="
JWT_KEY=$(od -An -N32 -tx1 /dev/urandom | tr -d '[:space:]')
podman run -d --pod "$POD" --name "$API" \
  --read-only --user 1654:1654 \
  --tmpfs /tmp:rw,size=128m,mode=1777 \
  -v "$WORK/api:/app:ro,Z" --workdir /app \
  --entrypoint /app/MyApp.API \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_URLS=http://127.0.0.1:5296 \
  -e "ConnectionStrings__DefaultConnection=Host=127.0.0.1;Port=5432;Database=restore_job;Username=postgres" \
  -e "Jwt__Key=$JWT_KEY" -e Jwt__Issuer=MyApp -e Jwt__Audience=MyAppUsers \
  -e MediaStorage__PhotoDirectory=/tmp/myapp/photos \
  -e MediaStorage__VideoDirectory=/tmp/myapp/videos \
  -e MediaStorage__StagingPhotoDirectory=/tmp/myapp/staging/photos \
  -e MediaStorage__StagingVideoDirectory=/tmp/myapp/staging/videos \
  -e DocumentTemplates__ComponentIssuePath=/tmp/nonexistent-test.pdf \
  "$API_IMAGE" >/dev/null
unset JWT_KEY

echo "=== Wait for startup or first failure ==="
STATUS=""
for i in $(seq 1 90); do
  STATUS=$(podman exec "$PG" bash -c \
    'exec 3<>/dev/tcp/127.0.0.1/5296 || exit 1
     printf "GET /api/documents/components/template HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3
     IFS= read -r line <&3
     printf "%s" "$line"' 2>/dev/null || true)

  if [[ "$STATUS" == HTTP/* ]]; then break; fi
  if [[ "$(podman inspect --format '{{.State.Running}}' "$API" 2>/dev/null)" == false ]]; then
    break
  fi
  sleep 1
done

if [[ "$STATUS" == *" 401 "* ]]; then
  echo "=== ISOLATED API STARTUP PASSED ==="
  exit 0
fi

echo "=== ISOLATED API DID NOT REACH HTTP 401 ==="
echo "HTTP status: ${STATUS:-none}"
podman inspect "$API" --format 'API running={{.State.Running}} exit-code={{.State.ExitCode}}' || true
echo "=== FIRST ERROR DETAILS FROM API LOG ==="
# Print exception summaries, not full SQL or any payload rows.
podman logs "$API" 2>&1 |
  grep -E -m 45 '(Unhandled exception|PostgresException|DbUpdateException|InvalidOperationException|[[:space:]][0-9]{5}:|SqlState:|MessageText:|SchemaName:|TableName:|ColumnName:|ConstraintName:|Detail:|Hint:|Failed to initialize|Unable to start|Failed executing DbCommand)' || true

echo "=== DATABASE OBJECT CHECK AFTER FAILED START ==="
podman exec "$PG" psql -U postgres -d restore_job -v ON_ERROR_STOP=1 -c \
  "SELECT count(*) AS tables_after_failed_start
   FROM information_schema.tables
   WHERE table_schema='public' AND table_type='BASE TABLE';" || true

echo "=== ISOLATED API STARTUP FAILED: share exception summary above ==="
exit 1
