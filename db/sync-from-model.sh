#!/bin/bash
# Keep db/schema.sql (the deployable source of truth) in sync with the EF Core model.
#
# Schema changes are AUTHORED in C# (Nom.Data entities); db/schema.sql is the
# deployable ARTIFACT. This script rebuilds the artifact from the model:
#
#   scratch A  <- db/schema.sql                       (current artifact)
#   scratch B  <- `dotnet ef dbcontext script`        (current EF model DDL)
#                 + db/custom-objects.sql             (non-EF objects, e.g. views)
#   atlas schema diff A B                             (state-based comparison)
#
# Usage:
#   ./db/sync-from-model.sh --check    # CI guard: exit 1 if model drifted from schema.sql
#   ./db/sync-from-model.sh            # regenerate schema.sql from the model
set -euo pipefail

cd "$(dirname "$0")/.."
DB_HOST="${DB_HOST:-localhost}"; DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-nom}"; DB_PASSWORD="${DB_PASSWORD:-dev_password}"
DB_SUPERUSER="${DB_SUPERUSER:-postgres}"
CHECK=0; [ "${1:-}" = "--check" ] && CHECK=1
A="nom_sync_a_$$"; B="nom_sync_b_$$"

export PGPASSWORD="$DB_PASSWORD"
command -v atlas >/dev/null || { echo "ERROR: atlas not found on PATH."; exit 1; }

PSQL_SUPER() { psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_SUPERUSER" -d postgres -v ON_ERROR_STOP=1 -q "$@"; }
PSQL_DB()    { psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$1" -v ON_ERROR_STOP=1 -q "${@:2}"; }
URL()        { echo "postgres://${DB_USER}:${DB_PASSWORD}@${DB_HOST}:${DB_PORT}/$1?sslmode=disable"; }

PSQL_SUPER -c "CREATE DATABASE \"$A\" OWNER \"$DB_USER\";" -c "CREATE DATABASE \"$B\" OWNER \"$DB_USER\";"
cleanup() {
  PSQL_SUPER -c "DROP DATABASE IF EXISTS \"$A\" WITH (FORCE);" \
             -c "DROP DATABASE IF EXISTS \"$B\" WITH (FORCE);" >/dev/null 2>&1 || true
  rm -f /tmp/nom-model-ddl-$$.sql
}
trap cleanup EXIT

# A: current artifact
PSQL_DB "$A" -f db/schema.sql

# B: current EF model + custom objects
(cd nom-api && dotnet ef dbcontext script --no-build \
  --project Nom.Data/Nom.Data.csproj --startup-project Nom.Api/Nom.Api.csproj \
  -o /tmp/nom-model-ddl-$$.sql >/dev/null) \
  || (cd nom-api && dotnet ef dbcontext script \
  --project Nom.Data/Nom.Data.csproj --startup-project Nom.Api/Nom.Api.csproj \
  -o /tmp/nom-model-ddl-$$.sql >/dev/null)
PSQL_DB "$B" -f /tmp/nom-model-ddl-$$.sql
PSQL_DB "$B" -f db/custom-objects.sql

DIFF=$(atlas schema diff --from "$(URL "$A")" --to "$(URL "$B")" \
  --exclude 'public.__EFMigrationsHistory' 2>/dev/null | sed '/^Skipped triggers/,$d')

if [ -z "$(echo "$DIFF" | tr -d '[:space:]')" ] || echo "$DIFF" | grep -q "Schemas are synced"; then
  echo "db/schema.sql is in sync with the EF model."
  exit 0
fi

if [ "$CHECK" -eq 1 ]; then
  echo "DRIFT DETECTED between the EF model and db/schema.sql:" >&2
  echo "$DIFF" >&2
  echo "Run ./db/sync-from-model.sh to regenerate the artifact." >&2
  exit 1
fi

echo "Model drift detected — regenerating db/schema.sql from the EF model..."
pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$B" \
  --schema-only --no-owner --no-privileges \
  --exclude-table='public.__EFMigrationsHistory' > db/schema.sql
echo "db/schema.sql regenerated. Review with 'git diff db/schema.sql' and commit."
