#!/usr/bin/env bash
# Готовит основной PostgreSQL (podman-контейнер) к потоковой репликации.
# Запускать на основном сервере с правами root. Скрипт идемпотентен.
set -euo pipefail

PG_CONTAINER="${PG_CONTAINER:-myapp-postgres}"
PGDATA="${PGDATA:-/var/lib/postgresql/data}"
REPL_USER="${REPL_USER:-replicator}"
REPL_SLOT="${REPL_SLOT:-myapp_standby}"
STANDBY_CIDR="${STANDBY_CIDR:?Set STANDBY_CIDR, e.g. 10.8.8.9/32}"
: "${REPL_PASSWORD:?Set REPL_PASSWORD}"

if ! [[ "$REPL_USER" =~ ^[a-z_][a-z0-9_]*$ ]]; then
    echo "REPL_USER must be a lowercase PostgreSQL identifier" >&2
    exit 1
fi
if ! [[ "$REPL_SLOT" =~ ^[a-z0-9_]+$ ]]; then
    echo "REPL_SLOT must contain lowercase letters, digits and underscores only" >&2
    exit 1
fi
if ! [[ "$STANDBY_CIDR" =~ ^[0-9a-fA-F:.]+/[0-9]{1,3}$ ]]; then
    echo "STANDBY_CIDR must look like 10.8.8.9/32 or 2001:db8::9/128" >&2
    exit 1
fi
if ! podman container exists "$PG_CONTAINER"; then
    echo "Container $PG_CONTAINER does not exist" >&2
    exit 1
fi

psql() {
    podman exec -i "$PG_CONTAINER" \
        psql -U postgres -X -v ON_ERROR_STOP=1 "$@"
}

wal_level="$(psql -tAc 'SHOW wal_level')"
max_wal_senders="$(psql -tAc 'SHOW max_wal_senders')"
need_restart=0
if [[ "$wal_level" == "minimal" ]]; then
    psql -c "ALTER SYSTEM SET wal_level = 'replica';"
    need_restart=1
fi
if [[ "$max_wal_senders" -lt 10 ]]; then
    psql -c "ALTER SYSTEM SET max_wal_senders = 10;"
    need_restart=1
fi
psql -c "ALTER SYSTEM SET wal_keep_size = '1GB';"
psql -c "ALTER SYSTEM SET hot_standby = on;"

psql -v repl_user="$REPL_USER" -v repl_password="$REPL_PASSWORD" <<'SQL'
SELECT format(
    'ALTER ROLE %I WITH LOGIN REPLICATION PASSWORD %L',
    :'repl_user',
    :'repl_password')
WHERE EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'repl_user')
\gexec

SELECT format(
    'CREATE ROLE %I WITH LOGIN REPLICATION PASSWORD %L',
    :'repl_user',
    :'repl_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'repl_user')
\gexec
SQL

slot_exists="$(psql -tAc \
    "SELECT 1 FROM pg_replication_slots WHERE slot_name = '$REPL_SLOT'")"
if [[ "$slot_exists" != "1" ]]; then
    psql -c "SELECT pg_create_physical_replication_slot('$REPL_SLOT');"
fi

hba_line="host replication $REPL_USER $STANDBY_CIDR scram-sha-256"
podman exec "$PG_CONTAINER" bash -c \
    "grep -qF '$hba_line' '$PGDATA/pg_hba.conf' || printf '%s\n' '$hba_line' >> '$PGDATA/pg_hba.conf'"

psql -c "SELECT pg_reload_conf();"

if [[ "$need_restart" == "1" ]]; then
    echo
    echo "Требуется перезапуск PostgreSQL, чтобы применить wal_level/max_wal_senders:"
    echo "  podman restart $PG_CONTAINER"
    echo "Перезапустите контейнер в согласованное окно, затем повторите проверку."
fi

echo
echo "Основной сервер подготовлен:"
echo "  wal_level=$(psql -tAc 'SHOW wal_level')"
echo "  max_wal_senders=$(psql -tAc 'SHOW max_wal_senders')"
echo "  slot=$REPL_SLOT"
echo "  role=$REPL_USER"
echo "  standby_cidr=$STANDBY_CIDR"
