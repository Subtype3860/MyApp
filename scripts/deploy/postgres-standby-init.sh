#!/usr/bin/env bash
# Инициализирует резервный PostgreSQL из основного через pg_basebackup.
# Запускать на резервном сервере с правами root. Ожидает пустой том.
set -euo pipefail

: "${PRIMARY_HOST:?Set PRIMARY_HOST, e.g. 10.8.8.8}"
PRIMARY_PORT="${PRIMARY_PORT:-5432}"
REPL_USER="${REPL_USER:-replicator}"
REPL_SLOT="${REPL_SLOT:-myapp_standby}"
: "${REPL_PASSWORD:?Set REPL_PASSWORD}"
PG_IMAGE="${PG_IMAGE:-docker.io/library/postgres:17}"
PG_VOLUME="${PG_VOLUME:-myapp-pg-standby-data}"
PG_CONTAINER="${PG_CONTAINER:-myapp-postgres-standby}"
STANDBY_PORT="${STANDBY_PORT:-5433}"

if ! [[ "$REPL_SLOT" =~ ^[a-z0-9_]+$ ]]; then
    echo "REPL_SLOT must contain lowercase letters, digits and underscores only" >&2
    exit 1
fi

if podman container exists "$PG_CONTAINER"; then
    echo "Container $PG_CONTAINER already exists; refusing to overwrite it." >&2
    echo "Remove it manually only after verifying that the standby is not needed." >&2
    exit 1
fi

if ! podman volume exists "$PG_VOLUME"; then
    podman volume create "$PG_VOLUME" >/dev/null
fi

volume_entries="$(podman run --rm -v "$PG_VOLUME:/data" "$PG_IMAGE" \
    bash -c 'ls -A /data 2>/dev/null | wc -l' | tr -d '[:space:]')"
if [[ "$volume_entries" != "0" ]]; then
    echo "Volume $PG_VOLUME is not empty; use a new volume or clean it manually." >&2
    exit 1
fi

echo "Starting pg_basebackup from $PRIMARY_HOST:$PRIMARY_PORT ..."
podman run --rm \
    -e PGPASSWORD="$REPL_PASSWORD" \
    -v "$PG_VOLUME:/var/lib/postgresql/data" \
    "$PG_IMAGE" \
    pg_basebackup \
        -h "$PRIMARY_HOST" \
        -p "$PRIMARY_PORT" \
        -U "$REPL_USER" \
        -D /var/lib/postgresql/data \
        -Fp -Xs -P -R \
        -S "$REPL_SLOT"

# pg_basebackup -R создаёт standby.signal и primary_conninfo. Явно дописываем
# пароль и слот, чтобы не зависеть от формата строки, созданной утилитой.
escaped_password="$(printf '%s' "$REPL_PASSWORD" | sed "s/'/''/g")"
podman run --rm \
    -e PRIMARY_HOST="$PRIMARY_HOST" \
    -e PRIMARY_PORT="$PRIMARY_PORT" \
    -e REPL_USER="$REPL_USER" \
    -e REPL_SLOT="$REPL_SLOT" \
    -e REPL_PASSWORD="$escaped_password" \
    -v "$PG_VOLUME:/data" \
    "$PG_IMAGE" \
    bash -c '
        sed -i "/^primary_conninfo/d;/^primary_slot_name/d" /data/postgresql.auto.conf
        {
            printf "primary_conninfo = '\''host=%s port=%s user=%s password=%s'\''\n" \
                "$PRIMARY_HOST" "$PRIMARY_PORT" "$REPL_USER" "$REPL_PASSWORD"
            printf "primary_slot_name = '\''%s'\''\n" "$REPL_SLOT"
        } >> /data/postgresql.auto.conf
        touch /data/standby.signal
        chown 999:999 /data/postgresql.auto.conf /data/standby.signal
    '

podman run -d \
    --name "$PG_CONTAINER" \
    --restart=unless-stopped \
    -p "$STANDBY_PORT:5432" \
    -v "$PG_VOLUME:/var/lib/postgresql/data" \
    "$PG_IMAGE" >/dev/null

echo "Waiting for the standby to accept connections ..."
ready=0
for _ in $(seq 1 60); do
    if podman exec "$PG_CONTAINER" pg_isready -U postgres >/dev/null 2>&1; then
        ready=1
        break
    fi
    sleep 2
done
if [[ "$ready" != "1" ]]; then
    echo "Standby did not start; inspect: podman logs $PG_CONTAINER" >&2
    exit 1
fi

in_recovery="$(podman exec "$PG_CONTAINER" \
    psql -U postgres -tAc 'SELECT pg_is_in_recovery()')"
if [[ "$in_recovery" != "t" ]]; then
    echo "Standby is not in recovery; check primary_conninfo and pg_hba.conf." >&2
    exit 1
fi

echo
echo "Резервный сервер запущен в режиме recovery:"
echo "  container=$PG_CONTAINER"
echo "  host_port=$STANDBY_PORT"
echo "  slot=$REPL_SLOT"
podman exec "$PG_CONTAINER" psql -U postgres -c \
    "SELECT status, sender_host, sender_port FROM pg_stat_wal_receiver;"
