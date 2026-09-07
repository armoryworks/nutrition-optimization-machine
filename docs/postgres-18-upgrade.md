# Postgres 16 → 18 upgrade

> All four compose files now pin `postgres:18-alpine`. **Postgres 18 cannot read
> a Postgres 16 data directory** — pointing the new image at the existing
> `postgres_data` volume fails to start. Nothing is corrupted, but the stack
> stays down until you migrate. Do this before or during the deploy that picks
> up the new tag.

## 1. What changed besides the version

The upstream image moved its default data directory in 18:

| | PGDATA | Declared VOLUME |
|---|---|---|
| ≤ 17 | `/var/lib/postgresql/data` | `/var/lib/postgresql/data` |
| 18 | `/var/lib/postgresql/18/docker` | `/var/lib/postgresql` |

The compose files set `PGDATA: /var/lib/postgresql/data` explicitly so the mount
path, the `postgres_data` volume name, and the existing backup scripts keep
working. Don't drop that env var without also moving the volume mount.

Dev and test are disposable — `docker-compose.dev.yml` / `.test.yml` re-run
`db/schema.sql` + `db/seed.sql` through `docker-entrypoint-initdb.d` on a fresh
volume. For those, `docker compose down -v && docker compose up -d` is the whole
migration. Only production carries data worth moving.

## 2. Migrate production

Run on the nommeal API host, with the stack still on the **postgres:16** tag:

```bash
cd /path/to/nom        # wherever docker-compose.yml lives
PGVOL=$(docker compose ps -q postgres | xargs docker inspect \
  -f '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Name}}{{end}}{{end}}')
echo "$PGVOL"          # e.g. nom_postgres_data — sanity-check before continuing

# 1. Dump with the OLD server still running.
docker compose exec -T postgres \
  pg_dump -U "${POSTGRES_USER:-nom}" -d "${POSTGRES_DB:-nom}" \
  --format=custom --compress=9 > /tmp/nom-pre18.dump

# 2. Stop and copy the old volume aside — this is the rollback, don't delete it.
docker compose down
docker volume create "${PGVOL}_pg16_keep"
docker run --rm -v "$PGVOL":/from -v "${PGVOL}_pg16_keep":/to alpine \
  sh -c 'cd /from && cp -a . /to'
docker volume rm "$PGVOL"

# 3. Start ONLY the database so initdb runs clean on 18.
docker compose pull postgres
docker compose up -d postgres
until docker compose exec -T postgres pg_isready -U "${POSTGRES_USER:-nom}"; do sleep 2; done

# 4. Restore.
docker compose exec -T postgres \
  pg_restore -U "${POSTGRES_USER:-nom}" -d "${POSTGRES_DB:-nom}" \
  --clean --if-exists --no-owner < /tmp/nom-pre18.dump

# 5. Bring the rest up.
docker compose up -d
```

`pg_restore` reports errors for objects that didn't exist yet on the first
`--clean` pass; those are expected. Anything about a missing extension or a
failed constraint is not — stop and read it.

## 3. Verify before deleting the rollback volume

```bash
docker compose exec -T postgres psql -U nom -d nom -tAc "select version()"
docker compose exec -T postgres psql -U nom -d nom -tAc \
  "select count(*) from pg_tables where schemaname='public'"
curl -fsS http://127.0.0.1:8080/health
```

A clean `db/schema.sql` build produces **114 tables** — compare that against the
restored count and against the pre-upgrade number. Then exercise a sign-in, a
meal-plan read, and a recipe search before running
`docker volume rm "${PGVOL}_pg16_keep"`.

## 4. Rollback

```bash
docker compose down
docker volume rm "$PGVOL"
docker volume create "$PGVOL"
docker run --rm -v "${PGVOL}_pg16_keep":/from -v "$PGVOL":/to alpine \
  sh -c 'cd /from && cp -a . /to'
git checkout -- docker-compose.yml    # back to the postgres:16 tag
docker compose up -d
```

## 5. Staging first

`staging.nommeal.com` runs from a production snapshot — restore the prod dump
there onto 18 and let it soak before touching production. That also validates
the dump/restore pair itself, which is the part most likely to surprise you.

## 6. What was already verified

`db/schema.sql` and `db/seed.sql` apply cleanly to `postgres:18-alpine` from a
cold `initdb` (114 tables, no errors), with `PGDATA` overridden to the historical
path and the data directory confirmed at `/var/lib/postgresql/data`.
