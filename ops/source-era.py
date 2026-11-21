#!/usr/bin/env python3
"""
Read the publication year of each cookbook from its own title page and store it in nom-api.

An unsized can in an old book is weighed at the can size of its time, so nom-api needs each book
source's year (recipe.RecipeSourceEra; design: containers.md 2.5 and the plan's correction C2.7).
The books' Project Gutenberg texts are already on the harvest host (~/cookbook-work/pg*.txt). This
script reads them there and posts the years to
POST /api/FoodCatalog/container-sizes/source-eras.

RULES
-----
1. Nothing is fetched. The script reads local files only; its one network peer is nom-api,
   authenticated as a curation admin (X-Api-Key or Bearer). Without NOM_API it only prints.
2. The year, in order: the "Original publication:" line of the Project Gutenberg header, when it
   names one year; else the title page, the first 200 lines after the "*** START OF" marker (the
   whole file's first 200 lines when there is none), leaving out lines about the e-text itself
   (Project Gutenberg, produced by, transcriber, release or posting date, web addresses), when
   exactly one year from 1600 to this year is found there. Otherwise the book stays unknown.
   A year in an attribution ("Stevenson Memorial Cook Book (Hurlbut, 1865)") is the author's and
   is never read.
3. A file is matched to its attribution by --attribution-csv (the promoter's books.csv: columns
   file and attribution). A row for pgNNNN.txt or for cookbook-NNNN.jsonl names the book of
   pgNNNN.txt. A file holding more than one attribution (two editions in one text) is not sent:
   one title page cannot date two books.
4. A dry run unless --apply; nom-api refuses an attribution no live book recipe carries and a year
   out of range, and journals every write (SourceEraSet) under the stamp it returns.
   --undo STAMP takes that run back (a dry run unless --apply).

USAGE
-----
    ./ops/source-era.py ~/cookbook-work/pg*.txt --attribution-csv books.csv
    export NOM_API=https://nom.example.com NOM_API_KEY=...
    ./ops/source-era.py ~/cookbook-work/pg*.txt --attribution-csv books.csv            # dry run in nom-api
    ./ops/source-era.py ~/cookbook-work/pg*.txt --attribution-csv books.csv --apply
    ./ops/source-era.py --undo 2026-11-21T10:00:00.000Z [--apply]
"""

from __future__ import annotations

import argparse
import csv
import datetime
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from collections import defaultdict
from dataclasses import dataclass

SET_PATH = "/api/FoodCatalog/container-sizes/source-eras"
UNDO_PATH = "/api/FoodCatalog/container-sizes/source-eras/undo"
USER_AGENT = "nom-ops-source-era/1"
TITLE_LINES = 200
EARLIEST = 1600
MODERN_FROM = 1960

START = re.compile(r"^\*\*\*\s*START OF (?:THE|THIS) PROJECT GUTENBERG", re.IGNORECASE)
ORIGINAL = re.compile(r"^\s*Original publication\s*:", re.IGNORECASE)
YEAR = re.compile(r"(?<![\d/.-])(1[6-9]\d\d|20\d\d)(?![\d/])")
ABOUT_THE_TEXT = re.compile(
    r"project gutenberg|gutenberg\.org|produced by|transcriber|release date|posting date|first posted|last updated|"
    r"updated editions|e-?text|e-?book|https?://|www\.|copyright laws|distributed proofread",
    re.IGNORECASE)
NUMBER = re.compile(r"(\d+)")


@dataclass
class Reading:
    year: int | None
    source: str


def years_in(line: str, latest: int) -> list[int]:
    """The years from 1600 to the latest a line names."""
    return [int(y) for y in YEAR.findall(line) if EARLIEST <= int(y) <= latest]


def read_year(text: str, latest: int | None = None) -> Reading:
    """The publication year a Project Gutenberg text gives (rule 2), with where it was read, or None and why not."""
    latest = latest or datetime.date.today().year
    lines = text.splitlines()
    start = next((i for i, line in enumerate(lines) if START.match(line)), None)
    for i, line in enumerate(lines[: start if start is not None else 0]):
        if ORIGINAL.match(line):
            found = sorted(set(years_in(line, latest)))
            if len(found) == 1:
                return Reading(found[0], "pg-header:original-publication")
    first = start + 1 if start is not None else 0
    found: dict[int, int] = {}
    for offset, line in enumerate(lines[first:first + TITLE_LINES]):
        if ABOUT_THE_TEXT.search(line):
            continue
        for year in years_in(line, latest):
            found.setdefault(year, first + offset + 1)
    if len(found) == 1:
        year, at = next(iter(found.items()))
        return Reading(year, f"title-page:line {at}")
    if not found:
        return Reading(None, "title-page: no year")
    return Reading(None, "title-page: " + ", ".join(str(y) for y in sorted(found)))


def era(year: int | None) -> str:
    return "unknown" if year is None else "historic" if year < MODERN_FROM else "modern"


def load_books(path: str | None) -> dict[str, list[str]]:
    """The attributions of each text, keyed by its pgNNNN number (or its base name when it has none)."""
    books: dict[str, list[str]] = defaultdict(list)
    if not path:
        return books
    with open(path, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            name = os.path.basename((row.get("file") or "").strip())
            attribution = (row.get("attribution") or "").strip()
            if name and attribution and attribution not in books[key(name)]:
                books[key(name)].append(attribution)
    return books


def key(name: str) -> str:
    """pgNNNN.txt and cookbook-NNNN.jsonl share the key NNNN; any other file is its own base name."""
    base = os.path.basename(name)
    if re.match(r"^(pg|cookbook-)\d+\.", base):
        return NUMBER.search(base).group(1)
    return base


def plan(files: list[str], books: dict[str, list[str]], latest: int | None = None) -> tuple[list[dict], list[dict]]:
    """The rows to send, one per attribution, and a line per file saying what was read and why a file is not sent."""
    rows, report = [], []
    for path in files:
        names = books.get(key(path), [])
        with open(path, encoding="utf-8", errors="replace") as handle:
            reading = read_year(handle.read(), latest)
        entry = {"file": os.path.basename(path), "attributions": names, "year": reading.year, "era": era(reading.year), "source": reading.source}
        if not names:
            entry["sent"] = "no: no attribution in the CSV"
        elif len(names) > 1:
            entry["sent"] = "no: the file holds more than one attribution"
        else:
            entry["sent"] = "yes"
            rows.append({"sourceAttribution": names[0], "publishedYear": reading.year, "yearSource": reading.source})
        report.append(entry)
    return rows, report


class NomApi:
    def __init__(self, base: str, api_key: str | None, token: str | None, timeout: int = 300):
        self.base = base.rstrip("/")
        self.headers = {"Content-Type": "application/json", "Accept": "application/json", "User-Agent": USER_AGENT}
        if api_key:
            self.headers["X-Api-Key"] = api_key
        elif token:
            self.headers["Authorization"] = f"Bearer {token}"
        self.timeout = timeout

    def post(self, path: str, params: dict, body=None):
        url = self.base + path + "?" + urllib.parse.urlencode(params)
        req = urllib.request.Request(url, method="POST", headers=self.headers, data=json.dumps(body).encode() if body is not None else b"")
        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            raw = resp.read().decode()
            return json.loads(raw) if raw else None


def main(argv: list[str] | None = None, post=None) -> int:
    parser = argparse.ArgumentParser(description="Store each cookbook's publication year, read from its title page (no fetching).")
    parser.add_argument("files", nargs="*", help="Project Gutenberg texts (pg*.txt)")
    parser.add_argument("--attribution-csv", help="CSV with columns file and attribution, as the promoter used")
    parser.add_argument("--apply", action="store_true", help="store the years; without it nom-api writes nothing")
    parser.add_argument("--undo", help="take back the run with this stamp instead")
    parser.add_argument("--api", default=os.environ.get("NOM_API", ""))
    args = parser.parse_args(argv)
    if post is None and args.api:
        post = NomApi(args.api, os.environ.get("NOM_API_KEY"), os.environ.get("NOM_API_TOKEN")).post

    if args.undo:
        if args.files:
            parser.error("--undo takes no files")
        if post is None:
            parser.error("set NOM_API or --api to undo")
        print(json.dumps(post(UNDO_PATH, {"stamp": args.undo, "apply": str(args.apply).lower()}), indent=2))
        return 0

    if not args.files:
        parser.error("no files given")
    rows, report = plan(args.files, load_books(args.attribution_csv))
    for entry in report:
        print("\t".join([entry["file"], " | ".join(entry["attributions"]) or "-", str(entry["year"] or "-"), entry["era"], entry["source"], entry["sent"]]))
    counts = defaultdict(int)
    for entry in report:
        counts[entry["era"]] += 1
    print(json.dumps({"files": len(report), "send": len(rows), "byEra": dict(counts)}), file=sys.stderr)
    if post is None or not rows:
        return 0
    try:
        answer = post(SET_PATH, {"apply": str(args.apply).lower()}, rows)
    except urllib.error.HTTPError as exc:
        print(f"nom-api answered HTTP {exc.code}", file=sys.stderr)
        return 2
    print(json.dumps(answer, indent=2))
    if args.apply and answer and answer.get("stamp"):
        print(f"undo with: --undo {answer['stamp']}", file=sys.stderr)
    return 0 if not (answer or {}).get("refused") else 2


if __name__ == "__main__":
    sys.exit(main())
