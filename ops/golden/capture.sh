#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
query="${1:-}"
if [ -z "$query" ] || [ ! -f "$here/$query" ]; then
  echo "usage: capture.sh <query.sql>   (one of: $(cd "$here" && ls *.sql | tr '\n' ' '))" >&2
  echo "Runs the query read-only against the staging database and prints tab-separated rows." >&2
  exit 2
fi

host="${GOLDEN_SSH_HOST:-api-box}"
container="${GOLDEN_CONTAINER:-nom-postgres-staging}"
seed="${GOLDEN_SEED:-golden-2026-11}"

case "$container" in
  *staging*) ;;
  *) echo "refusing: $container is not a staging container" >&2; exit 2 ;;
esac

{
  echo "BEGIN TRANSACTION READ ONLY;"
  echo "\\set seed '$seed'"
  cat "$here/$query"
  echo "ROLLBACK;"
} | ssh "$host" "docker exec -i $container psql -U nom -d nom -X -q -A -F \$'\\t' -P footer=off -v ON_ERROR_STOP=1"
