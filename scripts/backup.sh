#!/usr/bin/env bash
# Nightly Postgres backup. Cron via systemd timer or compose service.
#
# Env vars (with defaults; tune for your deployment):
#   POSTGRES_HOST   default: localhost
#   POSTGRES_PORT   default: 5432
#   POSTGRES_DB     default: agentplatform
#   POSTGRES_USER   default: agentplatform
#   PGPASSWORD                             (required — pulled by pg_dump)
#   BACKUP_DIR      default: /var/backups/agentplatform
#   BACKUP_RETAIN   default: 14            (days; older dumps are removed)
#
# Usage: PGPASSWORD=... bash scripts/backup.sh
#
# Example systemd unit (.timer fires daily at 03:00):
#   [Service]
#   Type=oneshot
#   EnvironmentFile=/etc/agentplatform/backup.env
#   ExecStart=/usr/local/bin/agentplatform-backup.sh

set -euo pipefail

POSTGRES_HOST="${POSTGRES_HOST:-localhost}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
POSTGRES_DB="${POSTGRES_DB:-agentplatform}"
POSTGRES_USER="${POSTGRES_USER:-agentplatform}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/agentplatform}"
BACKUP_RETAIN="${BACKUP_RETAIN:-14}"

if [[ -z "${PGPASSWORD:-}" ]]; then
    echo "ERROR: PGPASSWORD must be set" >&2
    exit 1
fi

mkdir -p "${BACKUP_DIR}"

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
out="${BACKUP_DIR}/agentplatform-${timestamp}.sql.gz"

echo "[backup] dumping ${POSTGRES_DB} from ${POSTGRES_HOST}:${POSTGRES_PORT} → ${out}"

# --no-owner / --no-acl so the dump restores cleanly into a different
# environment (staging restore from prod, etc.).
pg_dump \
    --host="${POSTGRES_HOST}" \
    --port="${POSTGRES_PORT}" \
    --username="${POSTGRES_USER}" \
    --dbname="${POSTGRES_DB}" \
    --format=plain \
    --no-owner \
    --no-acl \
    | gzip -9 > "${out}"

# Sanity-check the dump landed and isn't empty (gzip header alone is ~20B).
if [[ ! -s "${out}" || $(stat -c%s "${out}" 2>/dev/null || stat -f%z "${out}") -lt 100 ]]; then
    echo "[backup] ERROR: dump file is empty or too small: ${out}" >&2
    rm -f "${out}"
    exit 2
fi

echo "[backup] done: $(du -h "${out}" | cut -f1)"

# Prune dumps older than BACKUP_RETAIN days.
find "${BACKUP_DIR}" -maxdepth 1 -type f -name 'agentplatform-*.sql.gz' -mtime "+${BACKUP_RETAIN}" -print -delete \
    | sed 's/^/[backup] pruned /' || true

echo "[backup] retention: kept dumps from the last ${BACKUP_RETAIN} day(s)"
