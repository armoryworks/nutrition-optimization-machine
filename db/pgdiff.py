#!/usr/bin/env python3
"""Internal state-based schema differ for NOM (no third-party dependencies).

Compares two live PostgreSQL databases -- a TARGET and a DESIRED state
(typically materialized from db/schema.sql) -- and prints the DDL delta
that makes TARGET match DESIRED. Prints nothing when they are in sync.

Usage:
    pgdiff.py --target DBNAME --desired DBNAME
              [--host H] [--port P] [--user U] [--password PW]
              [--exclude-table public.__EFMigrationsHistory] ...

Covered: schemas, tables, columns (type / null / default / identity),
constraints (PK, FK, UNIQUE, CHECK), indexes, views.
Not covered (none exist in NOM today; see db/README.md): functions,
triggers, procedures, standalone sequences, row-level security policies.
Renames are expressed as drop + add.

Exit codes: 0 = ok (delta on stdout, possibly empty), 2 = error.
"""
import argparse
import os
import subprocess
import sys

SYSTEM_SCHEMAS = ("pg_catalog", "information_schema")


def psql(db, query, args):
    env = dict(os.environ, PGPASSWORD=args.password)
    cmd = [
        "psql", "-h", args.host, "-p", str(args.port), "-U", args.user,
        "-d", db, "-tA", "-F", "\t", "-v", "ON_ERROR_STOP=1", "-c", query,
    ]
    res = subprocess.run(cmd, capture_output=True, text=True, env=env)
    if res.returncode != 0:
        sys.stderr.write(res.stderr)
        sys.exit(2)
    return [line.split("\t") for line in res.stdout.splitlines() if line]


def pg_dump_table(db, qualified, args):
    """Full CREATE DDL (table + constraints + indexes) for one table."""
    env = dict(os.environ, PGPASSWORD=args.password)
    cmd = [
        "pg_dump", "-h", args.host, "-p", str(args.port), "-U", args.user,
        "-d", db, "--schema-only", "--no-owner", "--no-privileges",
        "-t", qualified,
    ]
    res = subprocess.run(cmd, capture_output=True, text=True, env=env)
    if res.returncode != 0:
        sys.stderr.write(res.stderr)
        sys.exit(2)
    keep = []
    for line in res.stdout.splitlines():
        s = line.strip()
        if not s or s.startswith("--") or s.startswith("SET ") \
           or s.startswith("SELECT pg_catalog") \
           or s.startswith("\\restrict") or s.startswith("\\unrestrict"):
            continue
        keep.append(line)
    return "\n".join(keep)


def q(ident):
    return '"' + ident.replace('"', '""') + '"'


def qual(schema, name):
    return f"{q(schema)}.{q(name)}"


# ---------- catalog snapshots ----------

def get_schemas(db, args):
    rows = psql(db, f"""
        SELECT nspname FROM pg_namespace
        WHERE nspname NOT IN {SYSTEM_SCHEMAS!r} AND nspname NOT LIKE 'pg_%'
    """.replace("(", "(", 1), args)
    return {r[0] for r in rows}


def get_tables(db, args):
    rows = psql(db, f"""
        SELECT schemaname, tablename FROM pg_tables
        WHERE schemaname NOT IN ('pg_catalog','information_schema')
    """, args)
    return {(r[0], r[1]) for r in rows}


def get_columns(db, args):
    rows = psql(db, """
        SELECT n.nspname, c.relname, a.attname,
               format_type(a.atttypid, a.atttypmod),
               a.attnotnull::text,
               COALESCE(pg_get_expr(d.adbin, d.adrelid), ''),
               a.attidentity
        FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid AND c.relkind = 'r'
        JOIN pg_namespace n ON n.oid = c.relnamespace
        LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
        WHERE a.attnum > 0 AND NOT a.attisdropped
          AND n.nspname NOT IN ('pg_catalog','information_schema')
        ORDER BY n.nspname, c.relname, a.attnum
    """, args)
    cols = {}
    for schema, table, col, typ, notnull, default, identity in rows:
        cols[(schema, table, col)] = {
            "type": typ, "notnull": notnull == "true",
            "default": default, "identity": identity,
        }
    return cols


def get_constraints(db, args):
    rows = psql(db, """
        SELECT n.nspname, c.relname, con.conname, con.contype,
               pg_get_constraintdef(con.oid)
        FROM pg_constraint con
        JOIN pg_class c ON c.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname NOT IN ('pg_catalog','information_schema')
    """, args)
    return {(r[0], r[1], r[2]): {"type": r[3], "def": r[4]} for r in rows}


def get_indexes(db, args):
    # Exclude indexes backing constraints (handled via pg_constraint).
    rows = psql(db, """
        SELECT s.schemaname, s.tablename, s.indexname, s.indexdef
        FROM pg_indexes s
        WHERE s.schemaname NOT IN ('pg_catalog','information_schema')
          AND NOT EXISTS (
            SELECT 1 FROM pg_constraint con
            JOIN pg_class i ON i.oid = con.conindid
            JOIN pg_namespace n2 ON n2.oid = i.relnamespace
            WHERE i.relname = s.indexname AND n2.nspname = s.schemaname)
    """, args)
    return {(r[0], r[1], r[2]): r[3] for r in rows}


def get_views(db, args):
    rows = psql(db, """
        SELECT n.nspname, c.relname,
               regexp_replace(pg_get_viewdef(c.oid, true), '\\s+', ' ', 'g')
        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'v'
          AND n.nspname NOT IN ('pg_catalog','information_schema')
    """, args)
    return {(r[0], r[1]): r[2] for r in rows}


# ---------- diff ----------

def column_add_ddl(schema, table, col, meta):
    ddl = f"ALTER TABLE {qual(schema, table)} ADD COLUMN {q(col)} {meta['type']}"
    if meta["identity"] == "d":
        ddl += " GENERATED BY DEFAULT AS IDENTITY"
    elif meta["identity"] == "a":
        ddl += " GENERATED ALWAYS AS IDENTITY"
    if meta["default"] and not meta["identity"]:
        ddl += f" DEFAULT {meta['default']}"
    if meta["notnull"]:
        ddl += " NOT NULL"
    return ddl + ";"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--target", required=True)
    ap.add_argument("--desired", required=True)
    ap.add_argument("--host", default=os.environ.get("DB_HOST", "localhost"))
    ap.add_argument("--port", default=os.environ.get("DB_PORT", "5432"))
    ap.add_argument("--user", default=os.environ.get("DB_USER", "nom"))
    ap.add_argument("--password", default=os.environ.get("DB_PASSWORD", "dev_password"))
    ap.add_argument("--exclude-table", action="append", default=[],
                    help="schema.table to ignore entirely (repeatable)")
    args = ap.parse_args()

    excluded = {tuple(x.split(".", 1)) for x in args.exclude_table}

    def keep(st):
        return st not in excluded

    t_schemas, d_schemas = get_schemas(args.target, args), get_schemas(args.desired, args)
    t_tables = {t for t in get_tables(args.target, args) if keep(t)}
    d_tables = {t for t in get_tables(args.desired, args) if keep(t)}
    t_cols = {k: v for k, v in get_columns(args.target, args).items() if keep(k[:2])}
    d_cols = {k: v for k, v in get_columns(args.desired, args).items() if keep(k[:2])}
    t_cons = {k: v for k, v in get_constraints(args.target, args).items() if keep(k[:2])}
    d_cons = {k: v for k, v in get_constraints(args.desired, args).items() if keep(k[:2])}
    t_idx = {k: v for k, v in get_indexes(args.target, args).items() if keep(k[:2])}
    d_idx = {k: v for k, v in get_indexes(args.desired, args).items() if keep(k[:2])}
    t_views, d_views = get_views(args.target, args), get_views(args.desired, args)

    new_tables = d_tables - t_tables
    gone_tables = t_tables - d_tables
    common_tables = d_tables & t_tables

    out = []

    # 1. Schemas
    for s in sorted(d_schemas - t_schemas):
        out.append(f"CREATE SCHEMA {q(s)};")
    # (extra schemas in target are left alone -- report only)
    for s in sorted(t_schemas - d_schemas):
        out.append(f"-- NOTE: schema {q(s)} exists in target but not in desired state (left untouched)")

    # 2. Drop constraints that changed or disappeared (common tables only)
    cons_to_drop, cons_to_add = [], []
    for key in sorted(set(t_cons) | set(d_cons)):
        schema, table, name = key
        if (schema, table) in new_tables or (schema, table) in gone_tables:
            continue
        tdef, ddef = t_cons.get(key), d_cons.get(key)
        if tdef and (not ddef or tdef["def"] != ddef["def"]):
            cons_to_drop.append(key)
        if ddef and (not tdef or tdef["def"] != ddef["def"]):
            cons_to_add.append(key)
    for schema, table, name in cons_to_drop:
        out.append(f"ALTER TABLE {qual(schema, table)} DROP CONSTRAINT {q(name)};")

    # 3. Drop indexes that changed or disappeared
    idx_to_add = []
    for key in sorted(set(t_idx) | set(d_idx)):
        schema, table, name = key
        if (schema, table) in new_tables or (schema, table) in gone_tables:
            continue
        tdef, ddef = t_idx.get(key), d_idx.get(key)
        if tdef and (not ddef or tdef != ddef):
            out.append(f"DROP INDEX {qual(schema, name)};")
        if ddef and (not tdef or tdef != ddef):
            idx_to_add.append(ddef)

    # 4. Drop removed views
    for key in sorted(set(t_views) - set(d_views)):
        out.append(f"DROP VIEW {qual(*key)};")

    # 5. Drop removed tables
    for schema, table in sorted(gone_tables):
        out.append(f"DROP TABLE {qual(schema, table)} CASCADE;")

    # 6. Create new tables (full pg_dump DDL: includes their constraints/indexes)
    for schema, table in sorted(new_tables):
        out.append(pg_dump_table(args.desired, f'"{schema}"."{table}"', args))

    # 7. Column-level changes on common tables
    for key in sorted(set(t_cols) | set(d_cols)):
        schema, table, col = key
        if (schema, table) not in common_tables:
            continue
        tmeta, dmeta = t_cols.get(key), d_cols.get(key)
        tq = qual(schema, table)
        if dmeta and not tmeta:
            out.append(column_add_ddl(schema, table, col, dmeta))
        elif tmeta and not dmeta:
            out.append(f"ALTER TABLE {tq} DROP COLUMN {q(col)};")
        elif tmeta != dmeta:
            if tmeta["type"] != dmeta["type"]:
                out.append(f"ALTER TABLE {tq} ALTER COLUMN {q(col)} TYPE {dmeta['type']} USING {q(col)}::{dmeta['type']};")
            if tmeta["notnull"] != dmeta["notnull"]:
                verb = "SET" if dmeta["notnull"] else "DROP"
                out.append(f"ALTER TABLE {tq} ALTER COLUMN {q(col)} {verb} NOT NULL;")
            if tmeta["default"] != dmeta["default"] and not dmeta["identity"]:
                if dmeta["default"]:
                    out.append(f"ALTER TABLE {tq} ALTER COLUMN {q(col)} SET DEFAULT {dmeta['default']};")
                else:
                    out.append(f"ALTER TABLE {tq} ALTER COLUMN {q(col)} DROP DEFAULT;")
            if tmeta["identity"] != dmeta["identity"]:
                out.append(f"-- WARNING: identity change on {tq}.{q(col)} "
                           f"('{tmeta['identity']}' -> '{dmeta['identity']}') requires manual migration")

    # 8. Add constraints (PK/UNIQUE/CHECK before FK so FKs can reference them)
    order = {"p": 0, "u": 1, "c": 2, "f": 3}
    for schema, table, name in sorted(cons_to_add, key=lambda k: order.get(d_cons[k]["type"], 9)):
        out.append(f"ALTER TABLE {qual(schema, table)} ADD CONSTRAINT {q(name)} {d_cons[(schema, table, name)]['def']};")

    # 9. Recreate indexes
    for ddef in idx_to_add:
        out.append(ddef + ";")

    # 10. Create or update views
    for key in sorted(set(d_views)):
        if t_views.get(key) != d_views[key]:
            out.append(f"CREATE OR REPLACE VIEW {qual(*key)} AS\n{d_views[key]}")

    print("\n".join(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
