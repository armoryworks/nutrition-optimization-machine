# Declarative database workflow (DACPAC-style)

This directory replaces EF Core's migration history with a **state-based**
schema workflow, analogous to SQL Server DACPAC: the repository holds the
*desired end state*, and deployment computes and applies the delta.

## Files

| File | Role |
|---|---|
| `schema.sql` | **Source of truth** for the database schema (all 12 domain schemas, 89 tables, views, indexes, FKs). Generated artifact — do not hand-edit. |
| `seed.sql` | Idempotent reference + demo data (`ON CONFLICT DO NOTHING`), including the System person and default groups. Applied once on fresh databases. |
| `custom-objects.sql` | DDL not represented in the EF model (currently the `reference."ReferenceGroupView"` view). Input to `sync-from-model.sh`. |
| `apply.sh` | **Deploy**: creates/provisions an empty database, or diffs an existing one against `schema.sql` (via `pgdiff.py`) and applies only the delta. `--dry-run` prints the delta. |
| `sync-from-model.sh` | **Build**: re-derives `schema.sql` after C# entity changes (EF `dbcontext script` + custom objects → dump). `--check` mode is a CI drift guard. |
| `pgdiff.py` | **Internal schema differ** (stdlib Python + `psql`/`pg_dump`, zero third-party dependencies). Compares system catalogs of two live databases and emits delta DDL: schemas, tables, columns (type/null/default/identity), PK/FK/UNIQUE/CHECK constraints, indexes, views. |

## Workflows

**Fresh environment**
```bash
./db/apply.sh                # creates DB, applies schema.sql + seed.sql
```

**Deploying schema changes to an existing database**
```bash
./db/apply.sh --dry-run      # review the computed delta
./db/apply.sh                # apply it
```

**After changing an entity in Nom.Data**
```bash
./db/sync-from-model.sh      # regenerates schema.sql from the model
git diff db/schema.sql       # review, then commit
```

**CI guard** (fails the build if someone changed entities without regenerating)
```bash
./db/sync-from-model.sh --check
```

## Requirements

- `psql`/`pg_dump` (PostgreSQL 18 client) and `python3` — nothing else.
  All tooling is open source (PostgreSQL license) or internal (`pgdiff.py`).
- A superuser (default `postgres`) for database creation and for `seed.sql`
  (it uses `SET LOCAL session_replication_role = replica` to load data with
  circular FKs).
- Connection defaults match dev (`nom`/`dev_password`@`localhost:5432/nom_dev`);
  override via `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `DB_PASSWORD`, `DB_SUPERUSER`.

## Notes & caveats

- **EF migrations are gone.** `Nom.Data/Migrations/` is deleted; the EF model
  remains the *authoring* surface, but deployment never uses
  `dotnet ef database update`. The legacy `refresh_db_and_migration.sh` is
  superseded by `apply.sh` (it still works — it regenerates a migration from
  scratch — but produces nothing that gets committed).
- `pgdiff.py` diffs schemas, tables, columns, constraints, indexes, and views.
  It does **not** yet handle functions, triggers, procedures, standalone
  sequences, or RLS policies (NOM currently has none of these); if they're
  ever added, put them in `custom-objects.sql` and extend `pgdiff.py`.
  Renames are expressed as drop + add; identity-mode changes emit a warning
  comment instead of DDL.
- `seed.sql` intentionally contains demo recipes (144) alongside reference
  data. If you want production seeds without demo content, split it.
