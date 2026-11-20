#!/usr/bin/env python3
"""
Store the source records of already imported recipes on those recipes in nom-api.

The cookbook, myplate and web recipes imported before the staged import kept its record have
their source only in the staging files on the harvest host (the JSONL files the staged import
was fed from). This script reads those files and posts them in batches to
POST /api/RecipeSource/attach, which matches each record to the one recipe imported from it
(normalized URL, else name and attribution, and at least 80 % of the record's ingredient lines
agreeing) and stores it as a ScrapedDocument. Design: reprocess.md 2a; contract in
docs/scraper-integration.md ("Stored sources").

RULES
-----
1. Nothing is fetched. The script reads local files only; its one network peer is nom-api,
   authenticated as a curation admin (X-Api-Key or Bearer).
2. A dry run unless --apply. Run the dry run over every file first: it reports matched,
   already attached, ambiguous, low-score, unmatched and unread records per file kind, and
   recipes that two different records matched. Records are settled in file order as the import
   took them: the first record of a recipe takes it, a later copy with the same text is already
   attached and a later different one is a double match, wherever the batches split.
3. Every stored document is journaled by nom-api (SourceAttach). --undo-since STAMP removes the
   documents attached at or after STAMP (a dry run unless --apply); an apply names its stamp as
   soon as its first batch is stored, again if a later batch fails, and in every --log line.
4. Attribution: a book's recipes were imported with the book's attribution. Give the same
   mapping the promoter used with --attribution-csv (one row per file); a file not in it is sent
   without one, and nom-api then uses the record's own author and site, as the import did.

USAGE
-----
    export NOM_API=https://nom.example.com NOM_API_KEY=...
    ./ops/recipe-source-attach.py ~/harvest-review/cookbook-*.jsonl ~/harvest-review/myplate-usda.jsonl \\
        --attribution-csv books.csv --log attach-dry.jsonl
    ./ops/recipe-source-attach.py ... --apply --log attach-apply.jsonl
    ./ops/recipe-source-attach.py --undo-since 2026-11-21T10:00:00.0000000Z [--apply]
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from typing import Callable, Iterable, Iterator

ATTACH_PATH = "/api/RecipeSource/attach"
UNDO_PATH = "/api/RecipeSource/attach/undo"
MAX_BATCH = 500
USER_AGENT = "nom-recipe-source-attach/1.0"
OUTCOMES = ("matched", "already-attached", "unmatched", "ambiguous", "low-score", "double-match", "conflict")
TARGET_KINDS = ("cookbook", "myplate")
TARGET_RATE = 0.99


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


def file_kind(path: str) -> str:
    """The kind of staging file by its name: cookbook-*, myplate-* or harvest-* (web); anything else is "other"."""
    name = os.path.basename(path).lower()
    if name.startswith("cookbook-"):
        return "cookbook"
    if name.startswith("myplate"):
        return "myplate"
    if name.startswith("harvest-"):
        return "web"
    return "other"


def shape_error(record) -> str | None:
    """Why a record is not a scraper record the staged import would take, or None."""
    if not isinstance(record, dict):
        return "not an object"
    if not isinstance(record.get("name"), str):
        return "no name"
    ingredients = record.get("ingredients")
    if not isinstance(ingredients, list) or any(
            not isinstance(i, dict) or not isinstance(i.get("rawLine"), str) for i in ingredients):
        return "ingredients are not a list of objects with rawLine"
    return None


@dataclass
class Item:
    path: str
    line: int
    record: dict


def read_records(path: str, errors: list[dict]) -> Iterator[Item]:
    """Every record of a JSONL file; a line that is not a scraper record is added to errors and skipped."""
    with open(path, encoding="utf-8") as handle:
        for number, text in enumerate(handle, start=1):
            if not text.strip():
                continue
            try:
                record = json.loads(text)
            except json.JSONDecodeError as exc:
                errors.append({"file": path, "line": number, "error": f"not JSON: {exc.msg}"})
                continue
            reason = shape_error(record)
            if reason:
                errors.append({"file": path, "line": number, "error": reason})
                continue
            yield Item(path, number, record)


def load_attributions(path: str | None, file_column: str = "file", attribution_column: str = "attribution") -> dict[str, str]:
    """File base name to attribution, from a CSV with a header row; rows with an empty attribution are left out."""
    if not path:
        return {}
    mapping: dict[str, str] = {}
    with open(path, encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle)
        missing = {file_column, attribution_column} - set(reader.fieldnames or [])
        if missing:
            raise ValueError(f"{path}: no column {', '.join(sorted(missing))}")
        for row in reader:
            name = os.path.basename((row.get(file_column) or "").strip())
            attribution = (row.get(attribution_column) or "").strip()
            if name and attribution:
                if mapping.get(name, attribution) != attribution:
                    raise ValueError(f"{path}: two attributions for {name}")
                mapping[name] = attribution
    return mapping


def chunks(items: list, size: int) -> Iterator[list]:
    for start in range(0, len(items), size):
        yield items[start:start + size]


class NomApi:
    def __init__(self, base: str, api_key: str | None, token: str | None, timeout: int = 300):
        self.base = base.rstrip("/")
        self.headers = {"Content-Type": "application/json", "Accept": "application/json", "User-Agent": USER_AGENT}
        if api_key:
            self.headers["X-Api-Key"] = api_key
        elif token:
            self.headers["Authorization"] = f"Bearer {token}"
        self.timeout = timeout

    def post(self, path: str, params: dict, body: dict | None = None):
        url = self.base + path + "?" + urllib.parse.urlencode(params)
        req = urllib.request.Request(url, method="POST", headers=self.headers,
                                     data=json.dumps(body).encode() if body is not None else b"")
        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            raw = resp.read().decode()
            return json.loads(raw) if raw else None


Post = Callable[[str, dict, "dict | None"], dict]


@dataclass
class Summary:
    applied: bool
    stamp: str | None = None
    by_kind: dict[str, Counter] = field(default_factory=lambda: defaultdict(Counter))
    errors: list[dict] = field(default_factory=list)
    recipes: dict[int, list[dict]] = field(default_factory=lambda: defaultdict(list))
    taken: dict[int, str | None] = field(default_factory=dict)
    doubled: set[int] = field(default_factory=set)

    def settle(self, answer: dict, place: dict) -> str:
        """The outcome of a record across the whole run: the first record of a recipe takes it, a later one with its text is already attached, a later different one is a double match."""
        outcome, recipe, sha = answer["outcome"], answer.get("recipeId"), answer.get("contentSha256")
        if recipe is None:
            return outcome
        held = recipe in self.taken
        if outcome in ("matched", "already-attached"):
            self.recipes[recipe].append(place)
            if not held:
                self.taken[recipe] = sha
                return outcome
            if self.taken[recipe] == sha:
                return "already-attached"
        elif outcome == "double-match":
            self.recipes[recipe].append(place)
        elif outcome == "conflict" and held and self.taken[recipe] != sha:
            self.recipes[recipe].append(place)
        else:
            return outcome
        self.doubled.add(recipe)
        return "double-match"

    def doubles(self) -> dict[int, list[dict]]:
        """Recipes that two different records matched, across batches and files, with every record that matched them."""
        return {recipe: self.recipes[recipe] for recipe in self.doubled}

    def target(self) -> dict:
        """The staging target of reprocess.md step 2: at least 99 % of cookbook and myplate records, unread lines included, matched or already attached, and no recipe matched by two different records."""
        counts = Counter()
        for kind in TARGET_KINDS:
            counts.update(self.by_kind.get(kind, Counter()))
        records = counts["records"]
        found = counts["matched"] + counts["already-attached"]
        rate = found / records if records else None
        return {
            "records": records,
            "matchedOrAttached": found,
            "rate": round(rate, 4) if rate is not None else None,
            "doubles": len(self.doubles()),
            "met": bool(records) and rate >= TARGET_RATE and not self.doubles() and counts["double-match"] == 0,
        }

    def report(self) -> dict:
        return {
            "applied": self.applied,
            "stamp": self.stamp,
            "byKind": {kind: dict(sorted(counts.items())) for kind, counts in sorted(self.by_kind.items())},
            "readErrors": len(self.errors),
            "doubles": {str(recipe): places for recipe, places in sorted(self.doubles().items())},
            "target": self.target(),
        }


def attach(files: Iterable[str], post: Post, attributions: dict[str, str], apply: bool = False,
           new_version: bool = False, batch_size: int = MAX_BATCH, write_log: Callable[[dict], None] | None = None) -> Summary:
    """Posts every record of every file, a file at a time and at most batch_size records per call, and sums the outcomes."""
    if not 1 <= batch_size <= MAX_BATCH:
        raise ValueError(f"batch size must be 1 to {MAX_BATCH}")
    summary = Summary(applied=apply)
    params = {"apply": str(apply).lower(), "newVersion": str(new_version).lower()}
    for path in files:
        kind = file_kind(path)
        attribution = attributions.get(os.path.basename(path))
        unread = len(summary.errors)
        items = list(read_records(path, summary.errors))
        unread = len(summary.errors) - unread
        counts = summary.by_kind[kind]
        counts["files"] += 1
        if unread:
            counts["records"] += unread
            counts["unread"] += unread
        for batch in chunks(items, batch_size):
            body = {"records": [item.record for item in batch], "sourceAttribution": attribution}
            try:
                result = post(ATTACH_PATH, params, body)
            except Exception as exc:
                if apply and summary.stamp:
                    log(f"{os.path.basename(path)} line {batch[0].line}: stopped by {exc!r}; earlier batches are stored, "
                        f"undo with: --undo-since {summary.stamp}")
                raise
            stamp = result.get("stamp")
            if summary.stamp is None and stamp:
                summary.stamp = stamp
                if apply:
                    log(f"first batch stored; undo with: --undo-since {stamp}")
            counts["records"] += len(batch)
            for answer in result.get("results", []):
                item = batch[answer["index"]]
                place = {"file": os.path.basename(path), "line": item.line, "name": answer.get("name")}
                outcome = summary.settle(answer, place)
                counts[outcome] += 1
                if apply or outcome == "matched":
                    counts["documents"] += answer.get("documents", 0)
                if write_log is not None:
                    write_log({**place, "kind": kind, "outcome": outcome, "answered": answer["outcome"], "stamp": stamp,
                               **{k: answer.get(k) for k in
                                  ("matchedBy", "recipeId", "score", "candidates", "format", "contentSha256", "version", "documents")}})
        log(f"{os.path.basename(path)}: {len(items)} records, {unread} unread, {dict(counts)}")
    return summary


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Store the source records of imported recipes in nom-api (no fetching).")
    parser.add_argument("files", nargs="*", help="staging JSONL files (cookbook-*.jsonl, myplate-*.jsonl, harvest-*.jsonl)")
    parser.add_argument("--apply", action="store_true", help="store the documents; without it nothing is written")
    parser.add_argument("--new-version", action="store_true",
                        help="store a record on a recipe that already holds a different one of its format as the next version")
    parser.add_argument("--attribution-csv", help="CSV mapping each file to the attribution its recipes were imported with")
    parser.add_argument("--csv-file-column", default="file")
    parser.add_argument("--csv-attribution-column", default="attribution")
    parser.add_argument("--batch-size", type=int, default=MAX_BATCH)
    parser.add_argument("--log", help="JSONL file for one line per record outcome")
    parser.add_argument("--undo-since", help="remove the documents attached at or after this UTC stamp instead")
    parser.add_argument("--api", default=os.environ.get("NOM_API", ""))
    args = parser.parse_args(argv)

    if not args.api:
        parser.error("set NOM_API or --api")
    api = NomApi(args.api, os.environ.get("NOM_API_KEY"), os.environ.get("NOM_API_TOKEN"))

    if args.undo_since:
        if args.files:
            parser.error("--undo-since takes no files")
        result = api.post(UNDO_PATH, {"sinceUtc": args.undo_since, "apply": str(args.apply).lower()})
        print(json.dumps(result, indent=2))
        return 0

    if not args.files:
        parser.error("no files given")
    attributions = load_attributions(args.attribution_csv, args.csv_file_column, args.csv_attribution_column)
    handle = open(args.log, "a", encoding="utf-8") if args.log else None
    try:
        write_log = (lambda entry: handle.write(json.dumps(entry) + "\n")) if handle else None
        summary = attach(args.files, api.post, attributions, apply=args.apply, new_version=args.new_version,
                         batch_size=args.batch_size, write_log=write_log)
    finally:
        if handle:
            handle.close()

    for error in summary.errors[:20]:
        log(f"read error {error['file']}:{error['line']}: {error['error']}")
    print(json.dumps(summary.report(), indent=2))
    if args.apply and summary.stamp:
        log(f"undo with: --undo-since {summary.stamp}")
    return 0 if not summary.errors else 2


if __name__ == "__main__":
    sys.exit(main())
