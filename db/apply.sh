#!/bin/bash
# Declarative (DACPAC-style) schema deployment for NOM.
#
# db/schema.sql is the single source of truth for the database schema.
# This script makes any target database match it:
#   - missing database  -> created
#   - empty database    -> full provision (schema.sql + seed.sql)
#   - existing database -> state-based diff via Atlas, delta applied
#
# Usage:
#   ./db/apply.sh                 # apply to default dev DB (nom_dev)
#   ./db/apply.sh --dry-run       # show the delta without applying
#   DB_NAME=nom_test ./db/apply.sh
#
# Env (defaults): DB_HOST=localhost DB_PORT=5432 DB_NAME=nom_dev
#                 DB_USER=nom DB_PASSWORD=dev_password DB_SUPERUSER=postgres
set -euo pipefail

cd "$(dirname "$0")"

DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_NAME="${DB_NAME:-nom_dev}"
DB_USER="${DB_USER:-nom}"
DB_PASSWORD="${DB_PASSWORD:-dev_password}"
DB_SUPERUSER="${DB_SUPERUSER:-postgres}"
DRY_RUN=0
[ "${1:-}" = "--dry-run" ] && DRY_RUN=1

export PGPASSWORD="$DB_PASSWORD"
TARGET_URL="postgres://${DB_USER}:${DB_PASSWORD}@${DB_HOST}:${DB_PORT}/${DB_NAME}?sslmode=disable"

# 1. Ensure database exists
if ! psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "SELECT 1" >/dev/null 2>&1; then
  echo "Database '$DB_NAME' not reachable as $DB_USER — creating it as $DB_SUPERUSER..."
  psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_SUPERUSER" -d postgres -v ON_ERROR_STOP=1 \
    -c "CREATE DATABASE \"$DB_NAME\" OWNER \"$DB_USER\";"
fi

# 2. Empty database -> full provision
TABLE_COUNT=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -tAc \
  "SELECT count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema');")
if [ "$TABLE_COUNT" -eq 0 ]; then
  if [ "$DRY_RUN" -eq 1 ]; then
    echo "[dry-run] '$DB_NAME' is empty: would apply schema.sql + seed.sql in full."
    exit 0
  fi
  echo "Provisioning empty database '$DB_NAME' from schema.sql..."
  psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -q -f schema.sql
  echo "Seeding reference/demo data from seed.sql..."
  psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -q -f seed.sql
  echo "Done. '$DB_NAME' provisioned."
  exit 0
fi

# 3. Existing database -> state-based diff (DACPAC-style)
command -v atlas >/dev/null || { echo "ERROR: atlas not found. Install: curl -fsSL -o ~/bin/atlas https://release.ariga.io/atlas/atlas-linux-amd64-latest && chmod +x ~/bin/atlas"; exit 1; }

DESIRED_DB="nom_desired_$$"
echo "Building desired-state database '$DESIRED_DB' from schema.sql..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_SUPERUSER" -d postgres -v ON_ERROR_STOP=1 -q \
  -c "CREATE DATABASE \"$DESIRED_DB\" OWNER \"$DB_USER\";"
cleanup() {
  psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_SUPERUSER" -d postgres -q \
    -c "DROP DATABASE IF EXISTS \"$DESIRED_DB\" WITH (FORCE);" >/dev/null 2>&1 || true
}
trap cleanup EXIT
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DESIRED_DB" -v ON_ERROR_STOP=1 -q -f schema.sql

DESIRED_URL="postgres://${DB_USER}:${DB_PASSWORD}@${DB_HOST}:${DB_PORT}/${DESIRED_DB}?sslmode=disable"
DIFF=$(atlas schema diff --from "$TARGET_URL" --to "$DESIRED_URL" \
  --exclude 'public.__EFMigrationsHistory' 2>/dev/null | sed '/^Skipped triggers/,$d')

if [ -z "$(echo "$DIFF" | tr -d '[:space:]')" ] || echo "$DIFF" | grep -q "Schemas are synced"; then
  echo "'$DB_NAME' already matches schema.sql. Nothing to do."
  exit 0
fi

echo "--- Delta required to bring '$DB_NAME' up to date: ---"
echo "$DIFF"
echo "-------------------------------------------------------"
if [ "$DRY_RUN" -eq 1 ]; then
  echo "[dry-run] Delta NOT applied."
  exit 0
fi
echo "$DIFF" | psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -q
echo "Delta applied. '$DB_NAME' now matches schema.sql."
