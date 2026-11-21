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
   already attached, ambiguous, low-score, unmatched, refused and unread records per file kind,
   recipes that two different records matched, and records of a page the harvest scraped twice.
   Records are settled in file order as the import took them: the first record of a recipe takes
   it, a later copy with the same text is already attached, a later different one of the same
   source URL is "rescraped" (the same page scraped again; not stored, and not a double), and any
   other later different one is a double match, wherever the batches split.
3. Every stored document is journaled by nom-api (SourceAttach). --undo-since STAMP removes the
   documents attached at or after STAMP (a dry run unless --apply); an apply names its stamp as
   soon as its first batch is stored, again if a later batch fails, and in every --log line.
4. The harvest files wrap each record: a cookbook line is {"chunk", "book", "recipe"} (or
   {"chunk", "recipe"}), a web line {"url", "scraped_at", "recipe"} and a myplate line
   {"url", "scraped_at", "archive_ts", "recipe"}. A bare scraper record is read as it is. The
   record is sent in the scraper's shape: ingredients and steps given as plain strings become
   {"rawLine", "name"} and {"order", "instruction"}, nested lists are flattened, null and blank
   entries (an empty step, which nom-api refuses) are dropped, a field of the wrong type is left
   out, and a web or myplate record without a sourceUrl takes the wrapper's url.
5. Attribution: a book's recipes were imported with the book's attribution. Give the mapping the
   promoter used with --attribution-csv: a row per file and attribution, optionally naming the
   recipe (column "name") or the wrapper's book (column "book") it belongs to. A file may have
   more than one attribution: a record takes the row that names it, else the file's only
   attribution, else each of the file's attributions in turn until one matches. A file not in the
   CSV is sent without one, and nom-api then uses the record's own author and site, as the
   import did.
6. A newer extraction of a book (ops/cookbook-extract.py) names in each record the recipe whose
   source it replaces ("replacesRecipeId", taken from this script's log of the first attach).
   The key is taken out of the record and sent as the batch's recipeIds; with --new-version
   nom-api stores the record as the next version on that recipe only.
7. The script keeps under nom-api's rate limits (50 calls in 10 seconds, 100 a minute, 1,000 an
   hour per client): by default at most 40 calls in any 10 seconds, --per-minute 80 and
   --per-hour 900, so other operator calls still fit, and on a 429 it waits out Retry-After and sends the same call again. A refused record
   (HTTP 400) is named with its file, line and name, counted as "refused", and the rest of its
   batch is sent again without it.

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
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter, defaultdict, deque
from dataclasses import dataclass, field
from typing import Callable, Iterable, Iterator

ATTACH_PATH = "/api/RecipeSource/attach"
UNDO_PATH = "/api/RecipeSource/attach/undo"
MAX_BATCH = 500
USER_AGENT = "nom-recipe-source-attach/1.1"
OUTCOMES = ("matched", "already-attached", "unmatched", "ambiguous", "low-score", "double-match", "conflict",
            "rescraped", "refused")
RETRY_OUTCOMES = ("unmatched", "low-score")
TARGET_KINDS = ("cookbook", "myplate")
TARGET_RATE = 0.99
PIN_KEY = "replacesRecipeId"
PER_MINUTE = 80
PER_HOUR = 900
PER_BURST = 40
BURST_SECONDS = 10.0
MAX_WAITS = 10
STRING_FIELDS = ("description", "imageUrl", "author", "sourceUrl", "sourceSite", "prepTime", "cookTime", "totalTime",
                 "recipeYield", "rawJsonLd")
MINUTE_FIELDS = ("prepTimeMinutes", "cookTimeMinutes", "totalTimeMinutes")
LIST_FIELDS = ("keywords", "categories", "cuisines", "suitableForDiet")
RECORD_INDEX = re.compile(r"records\[(\d+)\]", re.IGNORECASE)


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


def normalize_url(url) -> str | None:
    """An absolute http(s) URL with its scheme and host lowercased and its fragment dropped, as nom-api keys it; None otherwise."""
    if not isinstance(url, str) or not url.strip():
        return None
    parts = urllib.parse.urlsplit(url.strip())
    if parts.scheme.lower() not in ("http", "https") or not parts.hostname:
        return None
    host = parts.hostname.lower()
    default = {"http": 80, "https": 443}[parts.scheme.lower()]
    port = f":{parts.port}" if parts.port and parts.port != default else ""
    query = f"?{parts.query}" if parts.query else ""
    return f"{parts.scheme.lower()}://{host}{port}{parts.path or '/'}{query}"


def _flatten(value) -> Iterator:
    if isinstance(value, list):
        for item in value:
            yield from _flatten(item)
    elif value is not None:
        yield value


def _text(value) -> str | None:
    return value.strip() if isinstance(value, str) and value.strip() else None


def _ingredients(value) -> list[dict]:
    lines = []
    for item in _flatten(value):
        if isinstance(item, str):
            raw = _text(item)
            if raw:
                lines.append({"rawLine": raw, "name": raw})
        elif isinstance(item, dict):
            raw = _text(item.get("rawLine")) or _text(item.get("text")) or _text(item.get("name"))
            if not raw:
                continue
            line = {"rawLine": raw, "name": _text(item.get("name")) or raw}
            if isinstance(item.get("quantity"), (int, float)) and not isinstance(item.get("quantity"), bool):
                line["quantity"] = item["quantity"]
            for key in ("unit", "notes"):
                if _text(item.get(key)):
                    line[key] = item[key]
            lines.append(line)
    return lines


def _steps(value) -> list[dict]:
    steps = []
    for item in _flatten(value):
        if isinstance(item, str):
            text = _text(item)
            if text:
                steps.append({"order": len(steps) + 1, "instruction": text})
        elif isinstance(item, dict):
            text = _text(item.get("instruction")) or _text(item.get("text"))
            if not text:
                continue
            order = item.get("order")
            step = {"order": order if isinstance(order, int) and not isinstance(order, bool) else len(steps) + 1,
                    "instruction": text}
            if _text(item.get("section")):
                step["section"] = item["section"]
            steps.append(step)
    return steps


def scraper_record(recipe: dict, url: str | None = None) -> dict:
    """The scraper record nom-api takes, from a harvest recipe of any of the shapes the files hold (see rule 4)."""
    record: dict = {"name": _text(recipe.get("name")) or _text(recipe.get("title")) or ""}
    for key in STRING_FIELDS:
        if _text(recipe.get(key)):
            record[key] = recipe[key]
    for key in MINUTE_FIELDS:
        value = recipe.get(key)
        if isinstance(value, int) and not isinstance(value, bool):
            record[key] = value
    servings = recipe.get("recipeServings")
    if isinstance(servings, (int, float)) and not isinstance(servings, bool):
        record["recipeServings"] = servings
    record["ingredients"] = _ingredients(recipe.get("ingredients", recipe.get("recipeIngredient")))
    record["steps"] = _steps(next((recipe[k] for k in ("steps", "instructions", "recipeInstructions") if k in recipe), None))
    for key in LIST_FIELDS:
        values = [v.strip() for v in _flatten(recipe.get(key)) if _text(v)]
        if values:
            record[key] = values
    if "sourceUrl" not in record and url and _text(url):
        record["sourceUrl"] = url.strip()
    if PIN_KEY in recipe:
        record[PIN_KEY] = recipe[PIN_KEY]
    return record


def unwrap(line, kind: str) -> tuple[dict | None, str | None, str | None]:
    """The scraper record of one file line, the wrapper's book, and why the line cannot be read (None when it can)."""
    if not isinstance(line, dict):
        return None, None, "not an object"
    wrapped = "recipe" in line and "name" not in line
    if wrapped and not isinstance(line["recipe"], dict):
        return None, None, "recipe is not an object"
    recipe = line["recipe"] if wrapped else line
    url = None if kind == "cookbook" else (line.get("url") if wrapped else None)
    book = _text(line.get("book")) if wrapped else None
    record = scraper_record(recipe, url)
    if not record["name"]:
        return None, book, "no name"
    if not record["ingredients"]:
        return None, book, "no ingredient lines"
    return record, book, None


def shape_error(record) -> str | None:
    """Why a line is not a record the staged import would take once unwrapped, or None."""
    return unwrap(record, "other")[2]


@dataclass
class Item:
    path: str
    line: int
    record: dict
    book: str | None = None


def read_records(path: str, errors: list[dict]) -> Iterator[Item]:
    """Every record of a JSONL file, unwrapped; a line that holds no readable record is added to errors and skipped."""
    kind = file_kind(path)
    with open(path, encoding="utf-8") as handle:
        for number, text in enumerate(handle, start=1):
            if not text.strip():
                continue
            try:
                line = json.loads(text)
            except json.JSONDecodeError as exc:
                errors.append({"file": path, "line": number, "error": f"not JSON: {exc.msg}"})
                continue
            record, book, reason = unwrap(line, kind)
            if reason:
                errors.append({"file": path, "line": number, "error": reason})
                continue
            yield Item(path, number, record, book)


@dataclass
class Book:
    attribution: str
    name: str | None = None
    book: str | None = None


class Books:
    """The attributions of each file (by base name), from --attribution-csv."""

    def __init__(self, rows: dict[str, list[Book]] | None = None):
        self.rows = rows or {}

    def __contains__(self, name: str) -> bool:
        return name in self.rows

    def attributions(self, name: str) -> list[str]:
        """The file's attributions in CSV order, each once."""
        return list(dict.fromkeys(row.attribution for row in self.rows.get(name, [])))

    def candidates(self, name: str, record_name: str | None = None, book: str | None = None) -> list[str | None]:
        """The attributions to try for a record of the file, in order: the row naming the record or its book, else every attribution of the file; [None] for a file not in the CSV."""
        rows = self.rows.get(name)
        if not rows:
            return [None]
        for key, value in (("name", record_name), ("book", book)):
            if value:
                named = list(dict.fromkeys(row.attribution for row in rows if getattr(row, key) == value))
                if len(named) == 1:
                    return named
        return self.attributions(name)


def load_attributions(path: str | None, file_column: str = "file", attribution_column: str = "attribution") -> Books:
    """The attributions of each file from a CSV with a header row; a file may have several rows, and rows with an empty attribution are left out."""
    if not path:
        return Books()
    rows: dict[str, list[Book]] = defaultdict(list)
    with open(path, encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle)
        missing = {file_column, attribution_column} - set(reader.fieldnames or [])
        if missing:
            raise ValueError(f"{path}: no column {', '.join(sorted(missing))}")
        for row in reader:
            name = os.path.basename((row.get(file_column) or "").strip())
            attribution = (row.get(attribution_column) or "").strip()
            if not name or not attribution:
                continue
            entry = Book(attribution, (row.get("name") or "").strip() or None, (row.get("book") or "").strip() or None)
            if entry not in rows[name]:
                rows[name].append(entry)
    return Books(dict(rows))


def chunks(items: list, size: int) -> Iterator[list]:
    for start in range(0, len(items), size):
        yield items[start:start + size]


class ApiError(Exception):
    """nom-api answered with an error status."""

    def __init__(self, status: int, body: str = "", retry_after: float | None = None):
        super().__init__(f"HTTP {status}: {body[:500]}")
        self.status = status
        self.body = body
        self.retry_after = retry_after


def retry_after_seconds(value) -> float | None:
    """Seconds to wait from a Retry-After header: a number of seconds or an HTTP date."""
    if value is None:
        return None
    text = str(value).strip()
    try:
        return max(0.0, float(text))
    except ValueError:
        pass
    try:
        from email.utils import parsedate_to_datetime
        return max(0.0, parsedate_to_datetime(text).timestamp() - time.time())
    except (TypeError, ValueError):
        return None


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
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                raw = resp.read().decode()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as exc:
            try:
                text = exc.read().decode(errors="replace") if exc.fp else ""
            except OSError:
                text = ""
            raise ApiError(exc.code, text, retry_after_seconds(exc.headers.get("Retry-After") if exc.headers else None)) from exc


Post = Callable[[str, dict, "dict | None"], dict]


class Pacer:
    """Spaces calls so no 10 seconds hold more than per_burst, no minute more than per_minute and no hour more than per_hour (any window, so nom-api's fixed windows hold too)."""

    def __init__(self, per_minute: int = PER_MINUTE, per_hour: int = PER_HOUR, per_burst: int = PER_BURST,
                 clock: Callable[[], float] = time.monotonic, sleep: Callable[[float], None] = time.sleep):
        if per_minute < 1 or per_hour < 1 or per_burst < 1:
            raise ValueError("the call limits must be at least 1")
        self.limits = ((BURST_SECONDS, per_burst), (60.0, per_minute), (3600.0, per_hour))
        self.clock = clock
        self.sleep = sleep
        self.sent: deque[float] = deque()
        self.waited = 0.0

    def wait(self) -> None:
        while True:
            now = self.clock()
            while self.sent and now - self.sent[0] >= 3600.0:
                self.sent.popleft()
            delay = 0.0
            for window, limit in self.limits:
                inside = [t for t in self.sent if now - t < window]
                if len(inside) >= limit:
                    delay = max(delay, inside[len(inside) - limit] + window - now)
            if delay <= 0:
                self.sent.append(now)
                return
            self.pause(delay)

    def pause(self, seconds: float) -> None:
        self.waited += seconds
        self.sleep(seconds)


def paced(post: Post, pacer: Pacer, max_waits: int = MAX_WAITS) -> Post:
    """post through the pacer; a 429 is waited out (Retry-After, else a minute) and the same call sent again, at most max_waits times."""
    def call(path: str, params: dict, body: dict | None = None):
        waits = 0
        while True:
            pacer.wait()
            try:
                return post(path, params, body)
            except ApiError as exc:
                if exc.status != 429 or waits >= max_waits:
                    raise
                waits += 1
                seconds = exc.retry_after if exc.retry_after is not None else 60.0
                log(f"429 from nom-api, waiting {seconds:.0f} s (Retry-After) before sending the same call again")
                pacer.pause(seconds)
    return call


@dataclass
class Summary:
    applied: bool
    stamp: str | None = None
    by_kind: dict[str, Counter] = field(default_factory=lambda: defaultdict(Counter))
    errors: list[dict] = field(default_factory=list)
    refused: list[dict] = field(default_factory=list)
    recipes: dict[int, list[dict]] = field(default_factory=lambda: defaultdict(list))
    taken: dict[int, tuple[str | None, str | None]] = field(default_factory=dict)
    doubled: set[int] = field(default_factory=set)
    rescraped: dict[int, list[dict]] = field(default_factory=lambda: defaultdict(list))

    def settle(self, answer: dict, place: dict) -> str:
        """The outcome of a record across the whole run: the first record of a recipe takes it, a later one with its text is already attached, a later different one of the same URL is rescraped, any other later different one a double match."""
        outcome, recipe, sha = answer["outcome"], answer.get("recipeId"), answer.get("contentSha256")
        if recipe is None:
            return outcome
        url = normalize_url(place.get("url"))
        held = recipe in self.taken
        if outcome in ("matched", "already-attached"):
            if not held:
                self.recipes[recipe].append(place)
                self.taken[recipe] = (sha, url)
                return outcome
            if self.taken[recipe][0] == sha:
                self.recipes[recipe].append(place)
                return "already-attached"
        elif outcome not in ("double-match", "conflict") or (outcome == "conflict" and (not held or self.taken[recipe][0] == sha)):
            return outcome
        if held and url and self.taken[recipe][1] == url:
            self.rescraped[recipe].append(place)
            return "rescraped"
        self.recipes[recipe].append(place)
        self.doubled.add(recipe)
        return "double-match"

    def doubles(self) -> dict[int, list[dict]]:
        """Recipes that two different records matched, across batches and files, with every record that matched them."""
        return {recipe: self.recipes[recipe] for recipe in self.doubled}

    def target(self) -> dict:
        """The staging target of reprocess.md step 2: at least 99 % of cookbook and myplate records, unread and refused lines included, matched or already attached, and no recipe matched by two different records (a page scraped twice is not a double)."""
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
            "met": bool(records) and rate >= TARGET_RATE and not self.doubles(),
        }

    def report(self) -> dict:
        return {
            "applied": self.applied,
            "stamp": self.stamp,
            "byKind": {kind: dict(sorted(counts.items())) for kind, counts in sorted(self.by_kind.items())},
            "readErrors": len(self.errors),
            "refused": self.refused,
            "doubles": {str(recipe): places for recipe, places in sorted(self.doubles().items())},
            "rescraped": {str(recipe): places for recipe, places in sorted(self.rescraped.items())},
            "target": self.target(),
        }


def _place(item: Item, answer: dict | None = None) -> dict:
    return {"file": os.path.basename(item.path), "line": item.line,
            "name": (answer or {}).get("name") or item.record.get("name"), "url": item.record.get("sourceUrl")}


class Sender:
    """Posts batches and names every record nom-api refuses, sending the rest of the batch again without it."""

    def __init__(self, post: Post, params: dict, summary: Summary, apply: bool):
        self.post = post
        self.params = params
        self.summary = summary
        self.apply = apply

    def send(self, batch: list[Item], attribution: str | None) -> list[tuple[Item, dict, str | None]]:
        answers: dict[int, tuple[dict, str | None]] = {}
        pending = list(range(len(batch)))
        while pending:
            items = [batch[i] for i in pending]
            pins = [item.record.get(PIN_KEY) for item in items]
            body = {"records": [{k: v for k, v in item.record.items() if k != PIN_KEY} for item in items],
                    "sourceAttribution": attribution}
            if any(pin is not None for pin in pins):
                body["recipeIds"] = pins
            try:
                result = self.post(ATTACH_PATH, self.params, body)
            except ApiError as exc:
                if exc.status != 400:
                    self._stopped(items[0], exc)
                    raise
                named = sorted({int(i) for i in RECORD_INDEX.findall(exc.body) if int(i) < len(items)})
                if not named and len(items) > 1:
                    position = {id(item): i for i, item in enumerate(batch)}
                    half = len(pending) // 2
                    for part in (pending[:half], pending[half:]):
                        for item, answer, stamp in self.send([batch[i] for i in part], attribution):
                            answers[position[id(item)]] = (answer, stamp)
                    break
                named = named or [0]
                for index in named:
                    item = items[index]
                    reason = _refusal(exc.body, index) or exc.body[:500] or f"HTTP {exc.status}"
                    log(f"refused by nom-api: {os.path.basename(item.path)} line {item.line} ({item.record.get('name')!r}): {reason}")
                    self.summary.refused.append({"file": os.path.basename(item.path), "line": item.line,
                                                 "name": item.record.get("name"), "error": reason})
                    answers[pending[index]] = ({"outcome": "refused", "name": item.record.get("name"), "error": reason}, None)
                pending = [p for k, p in enumerate(pending) if k not in named]
                continue
            except Exception as exc:
                self._stopped(items[0], exc)
                raise
            stamp = result.get("stamp")
            if self.summary.stamp is None and stamp:
                self.summary.stamp = stamp
                if self.apply:
                    log(f"first batch stored; undo with: --undo-since {stamp}")
            for answer in result.get("results", []):
                answers[pending[answer["index"]]] = (answer, stamp)
            break
        return [(batch[i], *answers[i]) for i in sorted(answers)]

    def _stopped(self, item: Item, exc: Exception) -> None:
        if self.apply and self.summary.stamp:
            log(f"{os.path.basename(item.path)} line {item.line}: stopped by {exc!r}; earlier batches are stored, "
                f"undo with: --undo-since {self.summary.stamp}")


def _refusal(body: str, index: int) -> str | None:
    try:
        errors = json.loads(body).get("errors") or {}
    except (ValueError, AttributeError):
        return None
    messages = [f"{key}: {' '.join(map(str, value if isinstance(value, list) else [value]))}"
                for key, value in errors.items() if re.search(rf"records\[{index}\]", key, re.IGNORECASE)]
    return "; ".join(messages) or None


def _better(first: tuple | None, answer: tuple) -> tuple:
    if first is None:
        return answer
    return answer if first[1]["outcome"] == "unmatched" and answer[1]["outcome"] == "low-score" else first


def attach(files: Iterable[str], post: Post, attributions: Books | dict | None = None, apply: bool = False,
           new_version: bool = False, batch_size: int = MAX_BATCH, write_log: Callable[[dict], None] | None = None) -> Summary:
    """Posts every record of every file, a file at a time and at most batch_size records per call, and sums the outcomes."""
    if not 1 <= batch_size <= MAX_BATCH:
        raise ValueError(f"batch size must be 1 to {MAX_BATCH}")
    books = attributions if isinstance(attributions, Books) else Books(
        {name: [Book(value)] for name, value in (attributions or {}).items()})
    summary = Summary(applied=apply)
    sender = Sender(post, {"apply": str(apply).lower(), "newVersion": str(new_version).lower()}, summary, apply)
    for path in files:
        kind = file_kind(path)
        name = os.path.basename(path)
        unread = len(summary.errors)
        items = list(read_records(path, summary.errors))
        unread = len(summary.errors) - unread
        counts = summary.by_kind[kind]
        counts["files"] += 1
        if unread:
            counts["records"] += unread
            counts["unread"] += unread

        groups: dict[tuple, list[Item]] = {}
        for item in items:
            groups.setdefault(tuple(books.candidates(name, item.record.get("name"), item.book)), []).append(item)

        for candidates, group in groups.items():
            def emit(item: Item, answer: dict, stamp: str | None, attribution: str | None) -> None:
                place = _place(item, answer)
                outcome = summary.settle(answer, place)
                counts["records"] += 1
                counts[outcome] += 1
                if apply or outcome == "matched":
                    counts["documents"] += answer.get("documents", 0)
                if write_log is not None:
                    entry = {**place, "kind": kind, "outcome": outcome, "answered": answer["outcome"], "stamp": stamp,
                             **{k: answer.get(k) for k in
                                ("matchedBy", "recipeId", "score", "candidates", "format", "contentSha256", "version", "documents")}}
                    if len(candidates) > 1:
                        entry["attribution"] = attribution
                    if answer.get("error"):
                        entry["error"] = answer["error"]
                    write_log(entry)

            pending = group
            first: dict[int, tuple] = {}
            for turn, attribution in enumerate(candidates):
                later = turn + 1 < len(candidates)
                retry = []
                for batch in chunks(pending, batch_size):
                    for item, answer, stamp in sender.send(batch, attribution):
                        if later and answer["outcome"] in RETRY_OUTCOMES:
                            first[id(item)] = _better(first.get(id(item)), (item, answer, stamp, attribution))
                            retry.append(item)
                        elif answer["outcome"] in RETRY_OUTCOMES and id(item) in first:
                            emit(*_better(first[id(item)], (item, answer, stamp, attribution)))
                        else:
                            emit(item, answer, stamp, attribution)
                pending = retry
                if not pending:
                    break
        log(f"{name}: {len(items)} records, {unread} unread, {dict(counts)}")
    return summary


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Store the source records of imported recipes in nom-api (no fetching).")
    parser.add_argument("files", nargs="*", help="staging JSONL files (cookbook-*.jsonl, myplate-*.jsonl, harvest-*.jsonl)")
    parser.add_argument("--apply", action="store_true", help="store the documents; without it nothing is written")
    parser.add_argument("--new-version", action="store_true",
                        help="store a record on a recipe that already holds a different one of its format as the next version")
    parser.add_argument("--attribution-csv", help="CSV mapping each file (and optionally recipe name or book) to the attribution its recipes were imported with")
    parser.add_argument("--csv-file-column", default="file")
    parser.add_argument("--csv-attribution-column", default="attribution")
    parser.add_argument("--batch-size", type=int, default=MAX_BATCH)
    parser.add_argument("--per-minute", type=int, default=PER_MINUTE, help="most calls in any minute (nom-api allows 100)")
    parser.add_argument("--per-hour", type=int, default=PER_HOUR, help="most calls in any hour (nom-api allows 1,000)")
    parser.add_argument("--log", help="JSONL file for one line per record outcome")
    parser.add_argument("--undo-since", help="remove the documents attached at or after this UTC stamp instead")
    parser.add_argument("--api", default=os.environ.get("NOM_API", ""))
    args = parser.parse_args(argv)

    if not args.api:
        parser.error("set NOM_API or --api")
    api = NomApi(args.api, os.environ.get("NOM_API_KEY"), os.environ.get("NOM_API_TOKEN"))
    pacer = Pacer(args.per_minute, args.per_hour)
    post = paced(api.post, pacer)

    if args.undo_since:
        if args.files:
            parser.error("--undo-since takes no files")
        result = post(UNDO_PATH, {"sinceUtc": args.undo_since, "apply": str(args.apply).lower()})
        print(json.dumps(result, indent=2))
        return 0

    if not args.files:
        parser.error("no files given")
    attributions = load_attributions(args.attribution_csv, args.csv_file_column, args.csv_attribution_column)
    handle = open(args.log, "a", encoding="utf-8") if args.log else None
    try:
        write_log = (lambda entry: handle.write(json.dumps(entry) + "\n")) if handle else None
        summary = attach(args.files, post, attributions, apply=args.apply, new_version=args.new_version,
                         batch_size=args.batch_size, write_log=write_log)
    finally:
        if handle:
            handle.close()

    for error in summary.errors[:20]:
        log(f"read error {error['file']}:{error['line']}: {error['error']}")
    print(json.dumps(summary.report(), indent=2))
    log(f"calls {len(pacer.sent)} in the last hour, waited {pacer.waited:.0f} s for the rate limits")
    if args.apply and summary.stamp:
        log(f"undo with: --undo-since {summary.stamp}")
    return 0 if not summary.errors and not summary.refused else 2


if __name__ == "__main__":
    sys.exit(main())
