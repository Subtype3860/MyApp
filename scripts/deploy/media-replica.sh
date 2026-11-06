#!/usr/bin/env bash
# Копирует новые фото и видео MyApp на резервную площадку.
# Запускается из systemd-таймера myapp-media-replica.timer.
set -euo pipefail

ENV_FILE="${MYAPP_MEDIA_ENV:-/etc/myapp/media-replica.env}"
if [[ -r "$ENV_FILE" ]]; then
    # shellcheck disable=SC1090
    source "$ENV_FILE"
fi

: "${TARGET:?TARGET is required, e.g. backup@standby:/srv/myapp-media or /mnt/backup/myapp-media}"
SOURCE_PHOTO="${SOURCE_PHOTO:-/mnt/dietpi/img}"
SOURCE_VIDEO="${SOURCE_VIDEO:-/mnt/dietpi/video}"
SOURCE_STAGING_PHOTO="${SOURCE_STAGING_PHOTO:-/opt/myapp/media-staging/img}"
SOURCE_STAGING_VIDEO="${SOURCE_STAGING_VIDEO:-/opt/myapp/media-staging/video}"
REQUIRE_MOUNT="${REQUIRE_MOUNT:-1}"
ALLOW_DELETE="${ALLOW_DELETE:-1}"
DRY_RUN="${DRY_RUN:-0}"
LOCK_FILE="${LOCK_FILE:-${RUNTIME_DIRECTORY:-/tmp}/myapp-media-replica.lock}"

command -v rsync >/dev/null 2>&1 || {
    echo "rsync is required" >&2
    exit 1
}

exec 9>"$LOCK_FILE"
if ! flock -n 9; then
    echo "Another media replication run is already active; skipping."
    exit 0
fi

RSYNC_OPTIONS=(-a --partial --human-readable --stats --mkpath)
if [[ "$ALLOW_DELETE" == "1" ]]; then
    RSYNC_OPTIONS+=(--delete-delay)
fi
if [[ "$DRY_RUN" == "1" ]]; then
    RSYNC_OPTIONS+=(--dry-run)
fi
if [[ -n "${RSYNC_SSH:-}" ]]; then
    RSYNC_OPTIONS+=(-e "$RSYNC_SSH")
fi

failures=0

sync_directory() {
    local source="$1"
    local destination="$2"
    local check_mount="$3"
    local allow_empty="${4:-0}"

    if [[ ! -d "$source" ]]; then
        echo "Source directory is missing: $source" >&2
        failures=$((failures + 1))
        return
    fi

    if [[ "$check_mount" == "1" && "$REQUIRE_MOUNT" == "1" ]]; then
        if ! mountpoint -q "$source"; then
            echo "Source directory is not a mount point: $source" >&2
            failures=$((failures + 1))
            return
        fi
    fi

    if [[ "$allow_empty" != "1" ]] && \
        [[ -z "$(find "$source" -type f -print -quit 2>/dev/null)" ]]; then
        echo "Source contains no files; refusing to replicate deletions: $source" >&2
        failures=$((failures + 1))
        return
    fi

    if ! rsync "${RSYNC_OPTIONS[@]}" "$source/" "$TARGET/$destination/"; then
        echo "rsync failed for $source -> $TARGET/$destination/" >&2
        failures=$((failures + 1))
    fi
}

sync_directory "$SOURCE_PHOTO" "img" 1 0
sync_directory "$SOURCE_VIDEO" "video" 1 0
sync_directory "$SOURCE_STAGING_PHOTO" "staging/img" 0 1
sync_directory "$SOURCE_STAGING_VIDEO" "staging/video" 0 1

if [[ "$failures" -gt 0 ]]; then
    echo "Media replication completed with $failures error(s)." >&2
    exit 1
fi

echo "Media replication completed: $TARGET"
