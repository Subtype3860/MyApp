#!/usr/bin/env bash
# Переводит резервный PostgreSQL в основной режим (failover).
# Запускать на резервном сервере после остановки приложения.
set -euo pipefail

PG_CONTAINER="${PG_CONTAINER:-myapp-postgres-standby}"
STANDBY_PORT="${STANDBY_PORT:-5433}"

if ! podman container exists "$PG_CONTAINER"; then
    echo "Container $PG_CONTAINER does not exist" >&2
    exit 1
fi

in_recovery="$(podman exec "$PG_CONTAINER" \
    psql -U postgres -tAc 'SELECT pg_is_in_recovery()')"
if [[ "$in_recovery" != "t" ]]; then
    echo "Database is already primary (pg_is_in_recovery = $in_recovery)." >&2
    exit 0
fi

echo "Promoting $PG_CONTAINER ..."
podman exec "$PG_CONTAINER" psql -U postgres -v ON_ERROR_STOP=1 \
    -c 'SELECT pg_promote(wait => true, wait_seconds => 120);'

for _ in $(seq 1 30); do
    if [[ "$(podman exec "$PG_CONTAINER" \
        psql -U postgres -tAc 'SELECT pg_is_in_recovery()')" == "f" ]]; then
        break
    fi
    sleep 2
done

if [[ "$(podman exec "$PG_CONTAINER" \
    psql -U postgres -tAc 'SELECT pg_is_in_recovery()')" != "f" ]]; then
    echo "Promotion did not finish; inspect: podman logs $PG_CONTAINER" >&2
    exit 1
fi

echo
echo "Резервный сервер повышен до основного:"
echo "  container=$PG_CONTAINER"
echo "  host_port=$STANDBY_PORT"
echo
echo "Дальше:"
echo "  1. Обновите ConnectionStrings__DefaultConnection (хост/порт) и перезапустите myapp."
echo "  2. Проверьте вход, отчёты и загрузку фото."
echo "  3. Старый основной не включайте без pg_rewind — см. docs/deployment/postgres-standby.md."
