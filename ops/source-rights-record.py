#!/usr/bin/env python3
"""
Record rights classes by hand in nom-api from a TSV, so a host whose own words already forbid or restrict
automated access is classed without a request to it (staging checkpoint 5, Q22).

Each row goes to POST /api/ScrapingSource/rights/by-hand, the admin route that records a class with the
site's deciding words, the page they stand on and the date they were read. nom-api journals each record;
its journal entry's undo (Admin -> Scraping Sources, or POST /api/ScrapingSource/journal/{id}/undo) withdraws
it. Design: docs/architecture/source-rights.md ("A class recorded by hand").

RULES
-----
1. Nothing is fetched from any host. The script reads the TSV only; its one network peer is nom-api,
   authenticated as a curation admin (X-Api-Key or Bearer).
2. A dry run unless --apply: every row is sent with apply=false and nom-api answers what it would do (the
   sources of the host, the candidate, whether discovery excludes the host) or why it refuses the row.
   --bodies prints the request bodies and calls nothing.
3. The TSV has the columns host, class, quote, page, date (YYYY-MM-DD, the day the words were read). A
   quote is the site's own words, under 25 words; a row that breaks a column rule is refused before any
   call, and the run stops.
4. nom-api's rule decides the rest: a class may be as strict as the class a source's check gave it or
   stricter, never laxer, and clear only beside a written permission; forbidden needs a quote NOM's
   sentence reader reads as forbidding automated access or reuse. A refused row (HTTP 409) is named with
   its reason and the run goes on; the exit status is 2 when any row was refused.
5. A row already recorded with the same class, quote and page is reported as such and changes nothing,
   so the script may be run again.

USAGE
-----
    export NOM_API=https://nom.example.com NOM_API_KEY=...
    ./ops/source-rights-record.py                      # dry run of ops/source-rights/forbidding-hosts.tsv
    ./ops/source-rights-record.py --apply
    ./ops/source-rights-record.py other.tsv --bodies
"""

from __future__ import annotations

import argparse
import csv
import importlib.util
import json
import os
import re
import sys
from datetime import date
from typing import Callable

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_TSV = os.path.join(HERE, "source-rights", "forbidding-hosts.tsv")
PATH = "/api/ScrapingSource/rights/by-hand"
COLUMNS = ["host", "class", "quote", "page", "date"]
CLASSES = ("facts-only", "permission-needed", "forbidden", "clear")
MAX_WORDS = 25
HOST = re.compile(r"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")


def _load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, file))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


attach = _load("recipe_source_attach", "recipe-source-attach.py")


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


def row_problem(row: dict) -> str | None:
    """Why a TSV row may not be sent, or None."""
    host = (row.get("host") or "").strip().lower()
    if not HOST.match(host):
        return f"host {host!r} is not a host name"
    if row.get("class") not in CLASSES:
        return f"class {row.get('class')!r} is not one of {', '.join(CLASSES)}"
    quote = (row.get("quote") or "").strip()
    if not quote:
        return "the quote is empty"
    if len(quote.split()) >= MAX_WORDS:
        return f"the quote has {len(quote.split())} words; keep it under {MAX_WORDS}"
    page = (row.get("page") or "").strip()
    if not re.match(r"^https?://[^\s/]+", page):
        return f"page {page!r} is not an http or https address"
    try:
        read = date.fromisoformat((row.get("date") or "").strip())
    except ValueError:
        return f"date {row.get('date')!r} is not YYYY-MM-DD"
    if read > date.today():
        return f"date {read} is in the future"
    return None


def load_rows(path: str) -> list[dict]:
    """The TSV's rows; raises SystemExit naming the first row that breaks a column rule."""
    with open(path, encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE)
        if reader.fieldnames != COLUMNS:
            raise SystemExit(f"{path}: the columns must be {' '.join(COLUMNS)}, not {reader.fieldnames}")
        rows = [dict(row) for row in reader]
    seen: set[str] = set()
    for number, row in enumerate(rows, start=2):
        problem = row_problem(row)
        if problem:
            raise SystemExit(f"{path} line {number}: {problem}")
        key = row["host"].strip().lower().removeprefix("www.")
        if key in seen:
            raise SystemExit(f"{path} line {number}: host {row['host']} is listed twice")
        seen.add(key)
    return rows


def body(row: dict) -> dict:
    return {
        "host": row["host"].strip().lower(),
        "rightsClass": row["class"],
        "quote": row["quote"].strip(),
        "quoteUrl": row["page"].strip(),
        "readOn": row["date"].strip(),
    }


def describe(answer: dict) -> str:
    sources = answer.get("sources") or []
    parts = [f"{answer.get('host')}: {answer.get('rightsClass')}"]
    if answer.get("alreadyRecorded"):
        parts.append("already recorded")
    elif answer.get("applied"):
        parts.append(f"recorded (record {((answer.get('record') or {}).get('id'))})")
    else:
        parts.append("would be recorded")
    parts.append(f"{len(sources)} source(s)" + (": " + ", ".join(f"{s.get('domain')} {s.get('oldClass') or 'unclassed'} -> {s.get('newClass')}" for s in sources) if sources else ""))
    if answer.get("candidate"):
        parts.append(f"candidate {answer['candidate']}")
    parts.append("excluded from discovery" if answer.get("excluded") else "not excluded")
    return "; ".join(parts)


def run(rows: list[dict], post: Callable, apply: bool) -> dict:
    """Sends each row; returns the counts and the refusals."""
    report = {"apply": apply, "rows": len(rows), "recorded": 0, "wouldRecord": 0, "alreadyRecorded": 0, "refused": []}
    for row in rows:
        try:
            answer = post(PATH, {"apply": str(apply).lower()}, body(row)) or {}
        except attach.ApiError as exc:
            reason = exc.body
            try:
                reason = (json.loads(exc.body) or {}).get("refused") or reason
            except ValueError:
                pass
            if exc.status != 409:
                raise
            report["refused"].append({"host": row["host"], "reason": reason})
            print(f"{row['host']}: refused: {reason}")
            continue
        if answer.get("alreadyRecorded"):
            report["alreadyRecorded"] += 1
        elif answer.get("applied"):
            report["recorded"] += 1
        else:
            report["wouldRecord"] += 1
        print(describe(answer))
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Record rights classes by hand from a TSV (see the module docstring).")
    parser.add_argument("tsv", nargs="?", default=DEFAULT_TSV)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--bodies", action="store_true", help="print the request bodies and call nothing")
    parser.add_argument("--per-minute", type=int, default=attach.PER_MINUTE)
    parser.add_argument("--per-hour", type=int, default=attach.PER_HOUR)
    args = parser.parse_args(argv)

    rows = load_rows(args.tsv)
    if args.bodies:
        print(json.dumps([body(r) for r in rows], indent=2, ensure_ascii=False))
        return 0
    base = os.environ.get("NOM_API")
    if not base:
        raise SystemExit("NOM_API is not set")
    client = attach.NomApi(base, os.environ.get("NOM_API_KEY"), os.environ.get("NOM_API_TOKEN"))
    post = attach.paced(client.post, attach.Pacer(per_minute=args.per_minute, per_hour=args.per_hour))
    report = run(rows, post, args.apply)
    log(json.dumps({k: (len(v) if isinstance(v, list) else v) for k, v in report.items()}))
    return 2 if report["refused"] else 0


if __name__ == "__main__":
    sys.exit(main())
