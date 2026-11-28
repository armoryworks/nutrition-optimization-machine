#!/usr/bin/env python3
"""
Hand import of the four recipe sources the owner approved on 2026-11-21: VA Nutrition and Food
Services (www.nutrition.va.gov), NHLBI Healthy Eating Recipes (www.nhlbi.nih.gov), the Library of
Congress's digitized cookbooks (www.loc.gov) and NCI's "Down Home Healthy Cooking" (www.cancer.gov).
They are PDFs, scanned books and plain pages the scraper cannot read, so they come in by hand:
fetch, text, extract, then the staged import under each source's hand record. Design and host
determinations: docs/architecture/hand-import.md and docs/architecture/crosscheck-sources.md.

RULES
-----
1. Only the documents listed in ops/hand-import/books.csv are fetched, and only from the hosts
   ops/hand-import/sources.csv gives their source. Any other host, a redirect to one, and a path
   robots.txt disallows are refused. One request at a time, at least the source's delay apart on a
   host and never faster than robots.txt's crawl-delay. A Library of Congress item is fetched as its
   JSON record first and taken only when its rights field states the public domain and it was
   published in 1930 or earlier; its OCR text (tile.loc.gov) is the document then.
2. Downloads go to a folder outside the repository, resume from a partial file, and are verified
   (length, file type, PDF end marker, JSON parse); every verified file is listed with its size and
   SHA-256 in SHA256SUMS and manifest.jsonl beside it, and a listed file whose sum still matches is
   not fetched again.
3. Text: pdftotext (poppler) for text PDFs, keeping each page apart; a page with no text layer is
   rendered with pdftoppm and read by tesseract. A recipe card's two columns are put one after the
   other. Library of Congress books use the library's own OCR, page by page, and need neither poppler
   nor tesseract; a page the Library's text leaves blank is counted as blank. A plain page is read
   from its HTML. Nothing but open-source tools runs. A PDF page with no text layer where tesseract
   is not installed stops the run (exit 3) with a message naming the nom-hand-import image, and its
   document is not extracted: it is never only counted. With the word list (SCOWL's american-english,
   Debian's wamerican, in the image) each Library page is read for readability; a page whose own text
   fails it (an OCR'd dot-leader table) is marked needsImage, and the images step fetches its page
   image within the tile.loc.gov page-image determination of 2026-11-25: only for an item taken under
   the Library rule, only a page whose Library text fails the readability test (tested again), only
   from tile.loc.gov (image_hosts in sources.csv), under robots.txt as read in the run and the 6 s
   pacing shared with www.loc.gov; anything else is refused and named. The next text run, in the
   image, reads it with tesseract for its words, boxes and confidence and sets its ingredient tables
   back row by row (an amount not read with confidence carries the unread mark, and its recipe is
   never kept or sent to the model; every amount it keeps is read again, its cell cropped from the
   image and enlarged, and an amount that second reading does not confirm, or a lone figure as wide
   as a fraction sign, carries the mark too), keeps the text with fewer garbled lines (a line that
   only carries the unread mark is no garbled line), stores the image's reading on the page and
   deletes the image. A page image is read for its text only: never kept,
   served or sent anywhere.
4. Extraction: VA recipe cards and NHLBI pages are read by rule; Library of Congress books first by
   rule (title, ingredient list or dot-leader table, method in prose: ops/cookbook-extract.py's
   read_by_rule), a recipe whose text fails the readability test is never sent to the model, and
   only what the rules cannot split goes to the model; NCI page by page by the model, its name the
   title the page prints (letter-spaced titles closed up by rule) and its yield the page's "Makes"
   line. Books run in the order the rules read them best (the plan step prints that order and the
   model calls each book needs and still needs), resumable per book across days (--max-calls, a
   status file per book), and runs on one work folder take turns on the model (a lock file in the
   work folder). Every model record must also pass the readability test. Every record then passes the cookbook
   extraction's number check against its own page text: every ingredient line and step, and the
   yield and times, must be found there with every number. A record that fails is dropped and
   counted with its reason; nothing repairs it. A model call that fails is asked again
   (--retries, --timeout); one that still fails drops only its chunk, counted with the reason. In
   pages mode (NCI) a call that times out is not asked again in the run: one long timeout drops
   that page, counted as "model call timed out", and the run goes on. A VA card's name is the
   title the card prints (books.csv's listed title only when it prints none), each named time of
   its header is read (a Chill or Inactive time is not kept on its own), a wrapped ingredient
   line is joined back, a notes heading is only a line that is the heading alone, and a card that
   keeps fewer than half the amount-led lines of its ingredient list, or leaves a line apart inside an
   ingredient block, is dropped by the reader's own check; a short line before a step numbered 1 heads a
   part of the method, kept as its steps' section, and a card's record that keeps fewer steps than its
   method numbers fails the number check; a returned name in letter-spaced type is closed up by rule. Every readable answer is kept in records/<doc>.answers.jsonl as it arrives, so a second
   run with the same --model asks only the chunks still missing or unreadable and no finished record is lost. Before every
   call the model host is read (its loaded models twice, then a one-token request that must answer within --answer-within
   seconds): while another job uses it the call waits, within one --wait-limit for the run; past the
   limit nothing more is asked, every answer kept stays, and the run stops (exit 3) for a later run to ask only what is
   missing. A record keeps the document's URL with its page
   (PDF "#page=N", a Library of Congress page "?sp=N") as its source URL, so the staged import
   stores it as its source document and the recipe links to the page it came from. Images and
   the sources' nutrition figures are never taken.
5. Staging: one staged-import batch per document under the source's hand record, with the
   document's attribution line from books.csv. A dry run writes the batches and posts nothing;
   --apply posts them. Undo is withholding the hand source's recipes (journaled, released by its
   own call or the journal's undo).

USAGE
-----
On the application server, in the nom-hand-import image (ops/hand-import.Dockerfile) through
ops/hand-import-box.sh, which takes the work folder, the API settings and the model address from the
environment; staging first. Run directly, the commands are:

    ./ops/hand-import.py fetch --out ~/hand-import [--source va] [--limit 5]
    ./ops/hand-import.py links --out ~/hand-import --source va > va-links.csv
    ./ops/hand-import.py text --out ~/hand-import [--words <word list>]
    ./ops/hand-import.py images --out ~/hand-import --source loc [--limit 200]
    ./ops/hand-import.py plan --out ~/hand-import --source loc --model qwen2.5:14b-instruct
    ./ops/hand-import.py extract --out ~/hand-import [--host http://<model host>:11434 --model qwen2.5:14b-instruct] [--wait-limit 120] [--max-calls 1500]
    ./ops/hand-import.py sample --out ~/hand-import --per-source 20 > judged-sample.csv
    NOM_API=... NOM_API_KEY=... ./ops/hand-import.py sources [--apply]
    NOM_API=... NOM_API_KEY=... ./ops/hand-import.py stage --out ~/hand-import --source-id va=31 ... [--apply]
    NOM_API=... NOM_API_KEY=... ./ops/hand-import.py withhold --source-id va=31 [--apply]
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import html
import html.parser
import importlib.util
import io
import json
import os
import random
import re
import shutil
import subprocess
import sys
import tempfile
import time
import unicodedata
import uuid
import urllib.error
import urllib.parse
import urllib.request
import urllib.robotparser
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Callable, Iterable

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SOURCES_CSV = os.path.join(HERE, "hand-import", "sources.csv")
BOOKS_CSV = os.path.join(HERE, "hand-import", "books.csv")
USER_AGENT = "nom-hand-import/1.0"
LOC_LATEST_YEAR = 1930
LOC_RIGHTS = "public domain"
MAX_REDIRECTS = 3
CHUNK = 1 << 16
MIN_PAGE_TEXT = 20
MAX_BATCH = 500
KINDS = ("pdf", "html", "loc-item", "index")
LAYOUTS = ("card", "html", "book", "pages")
STAGED_PATH = "/api/StagedImport"
IMAGE = "nom-hand-import"
STOPPED = 3
HAND_PATH = "/api/ScrapingSource/hand"


def _load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, file))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


cookbook = _load("cookbook_extract", "cookbook-extract.py")
attach = _load("recipe_source_attach", "recipe-source-attach.py")


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


@dataclass
class Source:
    source: str
    domain: str
    hosts: tuple[str, ...]
    title: str
    licence_kind: str
    licence_statement: str
    licence_found_at: str
    min_delay: float
    site: str
    image_hosts: tuple[str, ...] = ()


@dataclass
class Book:
    doc_id: str
    source: str
    kind: str
    url: str
    layout: str
    title: str
    attribution: str


def load_sources(path: str = SOURCES_CSV) -> dict[str, Source]:
    """The hand sources: domain, the hosts their documents may be fetched from, licence facts and delay, and the hosts
    whose page images may be fetched for OCR (image_hosts, empty until a determination covers them)."""
    out = {}
    with open(path, encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            out[row["source"]] = Source(
                source=row["source"], domain=row["domain"], hosts=tuple(h.strip().lower() for h in row["hosts"].split() if h.strip()),
                title=row["title"], licence_kind=row["licence_kind"], licence_statement=row["licence_statement"],
                licence_found_at=row["licence_found_at"], min_delay=float(row["min_delay_seconds"]), site=row["site"],
                image_hosts=tuple(h.strip().lower() for h in (row.get("image_hosts") or "").split() if h.strip()))
    return out


def load_books(path: str = BOOKS_CSV, sources: dict[str, Source] | None = None) -> list[Book]:
    """The listed documents; a row of an unknown source, kind or layout, a doubled id, or a URL off its source's hosts is an error."""
    sources = sources if sources is not None else load_sources()
    books, seen = [], set()
    with open(path, encoding="utf-8", newline="") as handle:
        for number, row in enumerate(csv.DictReader(handle), start=2):
            book = Book(**{k: (row.get(k) or "").strip() for k in ("doc_id", "source", "kind", "url", "layout", "title", "attribution")})
            problem = None
            if book.source not in sources:
                problem = f"unknown source {book.source!r}"
            elif book.kind not in KINDS:
                problem = f"unknown kind {book.kind!r}"
            elif book.kind != "index" and book.layout not in LAYOUTS:
                problem = f"unknown layout {book.layout!r}"
            elif not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,80}", book.doc_id) or book.doc_id in seen:
                problem = f"bad or doubled doc_id {book.doc_id!r}"
            elif host_of(book.url) not in sources[book.source].hosts:
                problem = f"{book.url} is not on a host of {book.source}"
            elif book.kind != "index" and not book.attribution:
                problem = "no attribution line"
            if problem:
                raise ValueError(f"{path} line {number}: {problem}")
            seen.add(book.doc_id)
            books.append(book)
    return books


def host_of(url: str) -> str:
    try:
        parts = urllib.parse.urlsplit(url)
    except ValueError:
        return ""
    return (parts.hostname or "").lower() if parts.scheme == "https" else ""


def inside_repo(path: str) -> bool:
    real, repo = os.path.realpath(path), os.path.realpath(REPO)
    return real == repo or real.startswith(repo + os.sep)


@dataclass
class Response:
    status: int
    headers: dict
    body: Iterable[bytes]
    location: str | None = None


Opener = Callable[[str, dict], Response]


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def http_open(url: str, headers: dict, timeout: int = 120) -> Response:
    """One GET without following redirects; a redirect comes back with its Location for the caller to judge."""
    opener = urllib.request.build_opener(_NoRedirect)
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, **headers})
    try:
        resp = opener.open(request, timeout=timeout)
    except urllib.error.HTTPError as exc:
        location = exc.headers.get("Location") if exc.headers else None
        return Response(exc.code, {k.lower(): v for k, v in (exc.headers or {}).items()}, iter(()), location)

    def body():
        try:
            while True:
                block = resp.read(CHUNK)
                if not block:
                    return
                yield block
        finally:
            resp.close()
    return Response(resp.status, {k.lower(): v for k, v in resp.headers.items()}, body())


class Refused(Exception):
    """A fetch the rules do not allow, or one whose answer failed verification."""


class Fetcher:
    """Fetches listed documents one at a time from the allowed hosts, honouring robots.txt and its crawl-delay; the
    hosts of one source (groups) share one pacing."""

    def __init__(self, allowed: dict[str, float], opener: Opener = http_open,
                 clock: Callable[[], float] = time.monotonic, sleep: Callable[[float], None] = time.sleep,
                 groups: dict[str, str] | None = None):
        self.allowed = allowed
        self.groups = groups or {}
        self.opener = opener
        self.clock = clock
        self.sleep = sleep
        self.robots: dict[str, urllib.robotparser.RobotFileParser | None] = {}
        self.delays: dict[str, float] = {}
        self.last: dict[str, float] = {}
        self.requests = 0

    def _wait(self, host: str) -> None:
        group = self.groups.get(host, host)
        if group in self.last:
            remaining = self.last[group] + self.delays.get(host, self.allowed[host]) - self.clock()
            if remaining > 0:
                self.sleep(remaining)
        self.last[group] = self.clock()
        self.requests += 1

    def _robots(self, host: str) -> urllib.robotparser.RobotFileParser:
        if host not in self.robots:
            self.delays[host] = self.allowed[host]
            self._wait(host)
            answer = self.opener(f"https://{host}/robots.txt", {})
            parser = urllib.robotparser.RobotFileParser()
            if answer.status == 200:
                parser.parse(b"".join(answer.body).decode("utf-8", errors="replace").splitlines())
                delay = parser.crawl_delay(USER_AGENT) or parser.crawl_delay("*")
                if delay:
                    self.delays[host] = max(self.allowed[host], float(delay))
                self.robots[host] = parser
            elif answer.status in (404, 410):
                parser.allow_all = True
                self.robots[host] = parser
            elif answer.status in (401, 403):
                parser.disallow_all = True
                self.robots[host] = parser
            else:
                self.robots[host] = None
        parser = self.robots[host]
        if parser is None:
            raise Refused(f"robots.txt of {host} could not be read; nothing is fetched from it this run")
        return parser

    def check(self, url: str) -> str:
        host = host_of(url)
        if host not in self.allowed:
            raise Refused(f"{url}: host {host or '(none)'} is not a listed hand-import host")
        if not self._robots(host).can_fetch(USER_AGENT, url):
            raise Refused(f"{url}: disallowed by robots.txt")
        return host

    def get(self, url: str, headers: dict | None = None, hosts: Iterable[str] | None = None) -> tuple[Response, str]:
        """The answer to one GET, following a redirect only to an allowed host (one of hosts when given) and an allowed
        path."""
        for _ in range(MAX_REDIRECTS + 1):
            if hosts is not None and host_of(url) not in hosts:
                raise Refused(f"{url}: host {host_of(url) or '(none)'} is not a host of this document's source")
            host = self.check(url)
            self._wait(host)
            answer = self.opener(url, headers or {})
            if answer.status in (301, 302, 303, 307, 308) and answer.location:
                url = urllib.parse.urljoin(url, answer.location)
                continue
            return answer, url
        raise Refused(f"{url}: more than {MAX_REDIRECTS} redirects")

    def read(self, url: str, hosts: Iterable[str] | None = None) -> bytes:
        answer, final = self.get(url, hosts=hosts)
        if answer.status != 200:
            raise Refused(f"{final}: HTTP {answer.status}")
        return b"".join(answer.body)

    def download(self, url: str, target: str, hosts: Iterable[str] | None = None, resume: bool = True) -> dict:
        """The document saved at target, resumed from target.part when the host serves ranges, and verified; with resume
        off, target.part is never kept: deleted before the request and whenever the download fails."""
        part = target + ".part"
        if not resume and os.path.exists(part):
            os.remove(part)
        try:
            return self._download(url, target, part, hosts, resume)
        except BaseException:
            if not resume and os.path.exists(part):
                os.remove(part)
            raise

    def _download(self, url: str, target: str, part: str, hosts: Iterable[str] | None, resume: bool) -> dict:
        have = os.path.getsize(part) if os.path.exists(part) else 0
        answer, final = self.get(url, {"Range": f"bytes={have}-"} if have else {}, hosts)
        if answer.status == 416 and have:
            os.remove(part)
            return self._download(url, target, part, hosts, resume)
        if answer.status not in (200, 206):
            raise Refused(f"{final}: HTTP {answer.status}")
        mode = "ab" if answer.status == 206 and have else "wb"
        start = have if mode == "ab" else 0
        expected = None
        if answer.status == 206 and "content-range" in answer.headers:
            total = answer.headers["content-range"].rsplit("/", 1)[-1]
            expected = int(total) if total.isdigit() else None
        elif "content-length" in answer.headers and str(answer.headers["content-length"]).isdigit():
            expected = start + int(answer.headers["content-length"])
        with open(part, mode) as handle:
            for block in answer.body:
                handle.write(block)
        size = os.path.getsize(part)
        if expected is not None and size != expected:
            raise Refused(f"{final}: {size} bytes of {expected}; " +
                          ("the partial file is kept and the next run resumes it" if resume else "the partial file is deleted"))
        kind = verify_file(part, answer.headers.get("content-type", ""))
        os.replace(part, target)
        return {"url": url, "finalUrl": final, "size": size, "type": kind, "sha256": sha256_file(target)}


def verify_file(path: str, content_type: str) -> str:
    """The verified type of a downloaded file (pdf, html, json), or Refused: a PDF must begin with %PDF- and end with
    %%EOF, an HTML page must be closed, a JSON file must parse."""
    with open(path, "rb") as handle:
        head = handle.read(1024)
        handle.seek(max(0, os.path.getsize(path) - 2048))
        tail = handle.read()
    if head.startswith(b"%PDF-"):
        if b"%%EOF" not in tail:
            raise Refused(f"{path}: the PDF has no end marker (truncated)")
        return "pdf"
    if head.startswith(b"\xff\xd8\xff"):
        if not tail.rstrip(b"\x00").endswith(b"\xff\xd9"):
            raise Refused(f"{path}: the JPEG image has no end marker (truncated)")
        return "jpeg"
    if head.startswith(b"\x00\x00\x00\x0cjP  \r\n\x87\n"):
        return "jp2"
    stripped = head.lstrip()
    if stripped[:1] in (b"{", b"["):
        try:
            with open(path, encoding="utf-8") as handle:
                json.load(handle)
        except (ValueError, UnicodeDecodeError) as exc:
            raise Refused(f"{path}: JSON does not parse ({exc})") from exc
        return "json"
    if "html" in content_type.lower() or stripped[:15].lower().startswith((b"<!doctype html", b"<html")):
        if b"</html>" not in tail.lower():
            raise Refused(f"{path}: the HTML page is not closed (truncated)")
        return "html"
    raise Refused(f"{path}: not a PDF, HTML page or JSON file ({content_type or 'no content type'})")


def sha256_file(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(CHUNK), b""):
            digest.update(block)
    return digest.hexdigest()


class Store:
    """The download folder: the files, SHA256SUMS and manifest.jsonl (one line per verified file, the last line of a file wins)."""

    def __init__(self, out: str):
        if inside_repo(out):
            raise ValueError(f"{out} is inside the repository; downloads go to a folder outside it")
        self.out = out
        os.makedirs(os.path.join(out, "docs"), exist_ok=True)
        self.manifest_path = os.path.join(out, "manifest.jsonl")
        self.entries: dict[str, dict] = {}
        if os.path.exists(self.manifest_path):
            with open(self.manifest_path, encoding="utf-8") as handle:
                for line in handle:
                    if line.strip():
                        entry = json.loads(line)
                        if entry.get("deleted"):
                            self.entries.pop(entry["file"], None)
                        else:
                            self.entries[entry["file"]] = entry

    def path(self, name: str) -> str:
        return os.path.join(self.out, "docs", name)

    def verified(self, name: str) -> dict | None:
        entry = self.entries.get(name)
        path = self.path(name)
        if not entry or not os.path.exists(path) or os.path.getsize(path) != entry["size"] or sha256_file(path) != entry["sha256"]:
            return None
        return entry

    def add(self, name: str, entry: dict) -> dict:
        entry = {"file": name, **entry, "fetchedAt": datetime.now(timezone.utc).isoformat(timespec="seconds")}
        self.entries[name] = entry
        self._log(entry)
        return entry

    def delete(self, name: str, reason: str) -> None:
        """Deletes a fetched file and takes it off SHA256SUMS; manifest.jsonl records the deletion with its sum and the
        reason, so a later run never takes the file as fetched."""
        entry = self.entries.pop(name, None)
        if os.path.exists(self.path(name)):
            os.remove(self.path(name))
        self._log({"file": name, "deleted": True, "sha256": (entry or {}).get("sha256"), "reason": reason,
                   "deletedAt": datetime.now(timezone.utc).isoformat(timespec="seconds")})

    def _log(self, line: dict) -> None:
        with open(self.manifest_path, "a", encoding="utf-8") as handle:
            handle.write(json.dumps(line, ensure_ascii=False) + "\n")
        with open(os.path.join(self.out, "SHA256SUMS"), "w", encoding="utf-8") as handle:
            for file in sorted(self.entries):
                handle.write(f"{self.entries[file]['sha256']}  docs/{file}\n")


def loc_item_problem(item: dict) -> str | None:
    """Why a Library of Congress item may not be taken, or None: its rights must state the public domain, it must be
    dated 1930 or earlier, and English."""
    record = item.get("item") or {}
    rights = " ".join(str(r) for r in (record.get("rights_advisory") or record.get("rights") or []))
    if LOC_RIGHTS not in html.unescape(re.sub(r"<[^>]+>", " ", rights)).lower():
        return "its rights field does not state the public domain"
    year = re.match(r"\d{4}", str(record.get("date") or ""))
    if not year or int(year.group(0)) > LOC_LATEST_YEAR:
        return f"not dated {LOC_LATEST_YEAR} or earlier ({record.get('date')})"
    if "english" not in [str(lang).lower() for lang in record.get("language") or []]:
        return "not in English"
    resources = item.get("resources") or []
    if not resources or not resources[0].get("fulltext_derivative"):
        return "no OCR text (fulltext_derivative); list the book's PDF as kind pdf instead"
    return None


def doc_files(book: Book) -> list[str]:
    return {"pdf": [f"{book.doc_id}.pdf"], "html": [f"{book.doc_id}.html"],
            "loc-item": [f"{book.doc_id}.item.json", f"{book.doc_id}.text.json"], "index": []}[book.kind]


def fetch(books: list[Book], sources: dict[str, Source], store: Store, fetcher: Fetcher) -> dict:
    """Fetches every listed document not already verified; returns the counts and each refusal."""
    counts: Counter = Counter()
    refused: list[dict] = []
    for book in books:
        if book.kind == "index":
            continue
        names = doc_files(book)
        hosts = sources[book.source].hosts
        if all(store.verified(n) for n in names):
            counts["verified"] += 1
            continue
        try:
            if book.kind == "loc-item":
                item_name, text_name = names
                if not store.verified(item_name):
                    store.add(item_name, fetcher.download(loc_json_url(book.url), store.path(item_name), hosts))
                with open(store.path(item_name), encoding="utf-8") as handle:
                    item = json.load(handle)
                problem = loc_item_problem(item)
                if problem:
                    raise Refused(f"{book.url}: {problem}")
                derived = item["resources"][0]["fulltext_derivative"]
                if host_of(derived) not in sources[book.source].hosts:
                    raise Refused(f"{derived}: the OCR text is not on a host of {book.source}")
                entry = fetcher.download(derived, store.path(text_name), hosts)
                if entry["type"] != "json":
                    raise Refused(f"{derived}: expected the OCR text as JSON, got {entry['type']}")
                store.add(text_name, {**entry, "resourceUrl": item["resources"][0].get("url")})
            else:
                entry = fetcher.download(book.url, store.path(names[0]), hosts)
                if entry["type"] != book.kind:
                    os.replace(store.path(names[0]), store.path(names[0]) + ".unexpected")
                    raise Refused(f"{book.url}: listed as {book.kind}, served as {entry['type']}")
                store.add(names[0], entry)
            counts["fetched"] += 1
        except Refused as exc:
            counts["refused"] += 1
            refused.append({"doc": book.doc_id, "reason": str(exc)})
            log(f"refused: {exc}")
    return {"counts": dict(counts), "requests": fetcher.requests, "refused": refused}


IMAGE_KINDS = {"image/jpeg": "jpg", "image/jp2": "jp2"}
OCR_WIDTH = 1500


def loc_page_images(item: dict) -> dict[int, str]:
    """The page image of each page of a Library of Congress item, by page sequence number, from its JSON record's
    files (resources[0].files: one list of variants per page): the largest JPEG when it is at least 1,500 pixels wide
    (enough for tesseract), else the JPEG 2000 master, else the largest JPEG."""
    resources = item.get("resources") or []
    files = resources[0].get("files") if resources and isinstance(resources[0], dict) else None
    out = {}
    for number, variants in enumerate(files or [], start=1):
        best = None
        for variant in variants if isinstance(variants, list) else []:
            if not isinstance(variant, dict) or variant.get("mimetype") not in IMAGE_KINDS or not variant.get("url"):
                continue
            width = int(variant.get("width") or 0) if str(variant.get("width") or "0").isdigit() else 0
            jpeg = variant["mimetype"] == "image/jpeg"
            rank = (2 if jpeg and width >= OCR_WIDTH else 1 if not jpeg else 0, width)
            if best is None or rank > best[0]:
                best = (rank, variant["url"])
        if best:
            out[number] = best[1]
    return out


def image_hosts(source: Source) -> tuple[str, ...]:
    """The hosts a source's page images may come from: those of its image_hosts that are also hosts of the source."""
    return tuple(h for h in source.image_hosts if h in source.hosts)


def images(books: list[Book], sources: dict[str, Source], store: Store, fetcher: Fetcher, text_dir: str, limit: int = 0,
           words: frozenset[str] | set[str] | None = None) -> dict:
    """Fetches the page image of a Library page within the limits of the tile.loc.gov page-image determination
    (docs/architecture/crosscheck-sources.md, 2026-11-25); anything outside them is refused and named:
    - only an item already taken under the Library rule: its JSON record and OCR text fetched and verified, and the
      record read again here (rights field states the public domain, dated 1930 or earlier, English);
    - only a page whose Library text fails the readability test, tested here again on the Library's own text with the
      word list (no word list, no image), and never a page whose image was already read (its text is stored);
    - only from a host of the book's own source that is also one of its image_hosts, a redirect included, under
      robots.txt as read in this run and the source's pacing, shared by both Library hosts (fetcher_for);
    - each image verified (a JPEG or JPEG 2000) and listed with its sum until the text step has read it, which then
      deletes it (delete_read_images); part of an image is never kept or resumed, and one left by a run that stopped
      is deleted first (delete_partial_images). An image is read for its text only: never kept, served or sent anywhere."""
    counts: Counter = Counter(partialDeleted=delete_partial_images(store))
    refused: list[dict] = []
    fetched = 0
    for book in books:
        if book.kind != "loc-item":
            continue
        source = sources[book.source]
        path = os.path.join(text_dir, f"{book.doc_id}.pages.json")
        item_name, text_name = doc_files(book)
        if not store.verified(item_name) or not store.verified(text_name):
            counts["refused"] += 1
            refused.append({"doc": book.doc_id, "reason": "the item is not taken: its record and OCR text are not fetched and verified"})
            continue
        with open(store.path(item_name), encoding="utf-8") as handle:
            item = json.load(handle)
        problem = loc_item_problem(item)
        if problem:
            counts["refused"] += 1
            refused.append({"doc": book.doc_id, "reason": f"the item is not taken under the Library rule: {problem}"})
            continue
        if words is None:
            counts["refused"] += 1
            refused.append({"doc": book.doc_id, "reason": "no word list: only a page whose Library text fails the readability test may "
                                                          f"have its image fetched; run images in the {IMAGE} image or give --words"})
            continue
        if not os.path.exists(path):
            counts["noText"] += 1
            continue
        with open(path, encoding="utf-8") as handle:
            stored = {p["n"]: p for p in json.load(handle)["pages"]}
        failing = {p["n"] for p in loc_pages(store.path(text_name), words)[0] if p.get("needsImage")}
        urls = loc_page_images(item)
        for number in sorted(failing | {n for n, p in stored.items() if p.get("needsImage")}):
            if number not in failing:
                counts["refused"] += 1
                refused.append({"doc": book.doc_id, "page": number, "reason": "its Library text passes the readability test"})
                continue
            if "imageText" in stored.get(number, {}):
                counts["alreadyRead"] += 1
                continue
            url = urls.get(number)
            if not url:
                counts["noImage"] += 1
                refused.append({"doc": book.doc_id, "page": number, "reason": "the item's record lists no page image for this page"})
                continue
            name = f"{book.doc_id}.p{number:04d}.{'jp2' if url.lower().split('?')[0].endswith('.jp2') else 'jpg'}"
            if store.verified(name):
                counts["verified"] += 1
                continue
            if limit and fetched >= limit:
                counts["leftForNextRun"] += 1
                continue
            host = host_of(url)
            if host not in image_hosts(source):
                counts["refused"] += 1
                refused.append({"doc": book.doc_id, "page": number, "reason": f"{url}: host {host or '(none)'} has no determination "
                                f"in docs/architecture/crosscheck-sources.md that covers page images for {book.source} (image_hosts "
                                f"in ops/hand-import/sources.csv); record one before fetching"})
                continue
            fetched += 1
            try:
                entry = fetcher.download(url, store.path(name), image_hosts(source), resume=False)
                if entry["type"] not in ("jpeg", "jp2"):
                    os.remove(store.path(name))
                    raise Refused(f"{url}: expected a page image, got {entry['type']}")
                store.add(name, {**entry, "page": number})
                counts["fetched"] += 1
            except Refused as exc:
                counts["refused"] += 1
                refused.append({"doc": book.doc_id, "page": number, "reason": str(exc)})
                log(f"refused: {exc}")
    return {"counts": {k: v for k, v in counts.items() if v}, "requests": fetcher.requests, "refused": refused}


PARTIAL_IMAGE = re.compile(r".+\.p\d{4,}\.(?:jpg|jp2)\.part")


def delete_partial_images(store: Store) -> int:
    """Deletes every part of a page image left in the download folder by a run that stopped, whatever its book.
    Returns how many were deleted."""
    deleted = 0
    for name in sorted(os.listdir(os.path.join(store.out, "docs"))):
        if PARTIAL_IMAGE.fullmatch(name):
            os.remove(store.path(name))
            deleted += 1
    return deleted


def delete_read_images(book: Book, store: Store, doc: dict) -> int:
    """Deletes each page image of a book whose text the page now stores (imageText), once its pages are written; an
    image not read here (no tesseract) stays for a run in the image. Returns how many were deleted."""
    read = {p["n"] for p in doc.get("pages") or [] if "imageText" in p}
    deleted = 0
    for number, path in sorted(page_images(book, store).items()):
        if number in read:
            store.delete(os.path.basename(path), "read for its text, which the page stores")
            deleted += 1
    return deleted


def fetcher_for(sources: dict[str, Source]) -> Fetcher:
    """One fetcher for the hand sources' hosts, the hosts of one source paced as one (www.loc.gov and tile.loc.gov)."""
    return Fetcher({host: s.min_delay for s in sources.values() for host in s.hosts},
                   groups={host: s.source for s in sources.values() for host in s.hosts})


def loc_json_url(url: str) -> str:
    parts = urllib.parse.urlsplit(url)
    query = urllib.parse.parse_qsl(parts.query)
    if ("fo", "json") not in query:
        query.append(("fo", "json"))
    return urllib.parse.urlunsplit((parts.scheme, parts.netloc, parts.path, urllib.parse.urlencode(query), ""))


class _Links(html.parser.HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.links: list[tuple[str, str]] = []
        self._href: str | None = None
        self._text: list[str] = []

    def handle_starttag(self, tag, attrs):
        if tag == "a":
            self._href = dict(attrs).get("href")
            self._text = []

    def handle_data(self, data):
        if self._href is not None:
            self._text.append(data)

    def handle_endtag(self, tag):
        if tag == "a" and self._href is not None:
            self.links.append((self._href, re.sub(r"\s+", " ", "".join(self._text)).strip()))
            self._href = None


def document_links(page: str, base: str, hosts: Iterable[str], pages: bool = False) -> list[tuple[str, str]]:
    """The PDF links on a page, and with pages also the links below the index page's own path, on the given hosts,
    fragments dropped."""
    parser = _Links()
    parser.feed(page)
    prefix = urllib.parse.urlsplit(base).path.rstrip("/") + "/"
    out, seen = [], set()
    for href, text in parser.links:
        url = urllib.parse.urldefrag(urllib.parse.urljoin(base, href))[0]
        path = urllib.parse.urlsplit(url).path
        if host_of(url) not in hosts or url in seen:
            continue
        if path.lower().endswith(".pdf") or (pages and path.startswith(prefix) and len(path) > len(prefix)):
            seen.add(url)
            out.append((url, text))
    return out


def slug(text: str) -> str:
    return re.sub(r"-+", "-", re.sub(r"[^a-z0-9]+", "-", text.lower())).strip("-")[:70] or "doc"


def links(books: list[Book], sources: dict[str, Source], fetcher: Fetcher, source: str | None) -> list[dict]:
    """Rows for books.csv from the listed index pages: documents they link to that are not listed yet. Nothing but the
    index pages is fetched; the operator reviews the rows before adding them."""
    listed = {b.url for b in books}
    ids = {b.doc_id for b in books}
    rows = []
    for book in books:
        if book.kind != "index" or (source and book.source != source):
            continue
        try:
            page = fetcher.read(book.url, sources[book.source].hosts).decode("utf-8", errors="replace")
        except Refused as exc:
            log(f"refused: {exc}")
            continue
        src = sources[book.source]
        for url, text in document_links(page, book.url, src.hosts, pages=book.layout == "html"):
            if url in listed:
                continue
            listed.add(url)
            is_pdf = url.lower().endswith(".pdf")
            base = f"{book.source}-{slug(text or os.path.basename(urllib.parse.urlsplit(url).path.rstrip('/')))}"
            doc_id, n = base, 2
            while doc_id in ids:
                doc_id, n = f"{base}-{n}", n + 1
            ids.add(doc_id)
            rows.append({"doc_id": doc_id, "source": book.source, "kind": "pdf" if is_pdf else "html", "url": url,
                         "layout": "card" if is_pdf else "html", "title": text, "attribution": src.site})
    return rows


BBOX_PAGE = re.compile(r'<page width="([\d.]+)" height="([\d.]+)">(.*?)</page>', re.S)
BBOX_BLOCK = re.compile(r'<block xMin="([\d.]+)" yMin="([\d.]+)" xMax="([\d.]+)" yMax="([\d.]+)">(.*?)</block>', re.S)
BBOX_LINE = re.compile(r"<line[^>]*>(.*?)</line>", re.S)
BBOX_WORD = re.compile(r"<word[^>]*>(.*?)</word>", re.S)


@dataclass
class Block:
    x0: float
    y0: float
    x1: float
    y1: float
    lines: list[str]


def bbox_pages(markup: str) -> list[tuple[float, list[Block]]]:
    """The pages of pdftotext -bbox-layout output: each page's width and its text blocks with their boxes and lines."""
    pages = []
    for page in BBOX_PAGE.finditer(markup):
        blocks = []
        for block in BBOX_BLOCK.finditer(page.group(3)):
            lines = [" ".join(html.unescape(w) for w in BBOX_WORD.findall(line)) for line in BBOX_LINE.findall(block.group(5))]
            lines = [line for line in lines if line.strip()]
            if lines:
                blocks.append(Block(*(float(v) for v in block.groups()[:4]), lines))
        pages.append((float(page.group(1)), blocks))
    return pages


def column_text(width: float, blocks: list[Block]) -> str:
    """A page's text in reading order with its columns one after the other. The column edge is the left edge most blocks
    share in the middle half of the page (three or more, with three or more blocks wholly left of it); top to bottom, a
    block across the edge is read whole, and between two such blocks the left column is read before the right one."""
    edges = Counter(round(b.x0 / 4) * 4 for b in blocks if width * 0.25 < b.x0 < width * 0.75)
    edge = None
    for x, count in edges.most_common():
        if count >= 3 and sum(1 for b in blocks if b.x1 <= x + 2) >= 3:
            edge = x - 2
            break
    ordered = sorted(blocks, key=lambda b: (b.y0, b.x0))
    if edge is None:
        return "\n\n".join("\n".join(b.lines) for b in ordered)
    out: list[str] = []
    left: list[Block] = []
    right: list[Block] = []

    def flush():
        out.extend("\n".join(b.lines) for b in left + right)
        left.clear()
        right.clear()
    for block in ordered:
        if block.x1 <= edge + 4:
            left.append(block)
        elif block.x0 >= edge:
            right.append(block)
        else:
            flush()
            out.append("\n".join(block.lines))
    flush()
    return "\n\n".join(out)


def strip_running_heads(pages: list[str]) -> list[str]:
    """Pages without their running heads and page numbers: the first or last non-blank line of a page when it is a bare
    number, or when its letters begin or end three or more pages and it holds a page number or opens the page."""
    def key(line: str) -> str:
        return re.sub(r"[^a-z]", "", line.lower())

    def edges(lines: list[str]) -> list[int]:
        filled = [i for i, line in enumerate(lines) if line.strip()]
        return sorted({filled[0], filled[-1]}) if filled else []
    split = [page.split("\n") for page in pages]
    seen: Counter = Counter()
    for lines in split:
        seen.update({key(lines[i]) for i in edges(lines) if len(lines[i].strip()) <= 60 and key(lines[i])})
    out = []
    for lines in split:
        drop = set()
        filled = edges(lines)
        for i in filled:
            text = lines[i].strip()
            bare = re.fullmatch(r"[\divxlc.\- ]{1,8}", text, re.I)
            head = len(text) <= 60 and key(text) and seen[key(text)] >= 3 and (re.search(r"\d", text) or i == filled[0])
            if bare or head:
                drop.add(i)
        out.append("\n".join(line for i, line in enumerate(lines) if i not in drop))
    return out


class _MainText(html.parser.HTMLParser):
    BLOCK = {"p", "div", "li", "h1", "h2", "h3", "h4", "h5", "h6", "tr", "br", "section", "article", "dt", "dd", "ul", "ol"}
    SKIP = {"script", "style", "noscript", "svg", "nav", "footer", "header"}

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.parts: list[str] = []
        self.depth_main = 0
        self.skip = 0
        self.title: str | None = None
        self.head_title: str | None = None
        self._h1 = False
        self._h1_text: list[str] = []
        self._title: list[str] | None = None

    def handle_starttag(self, tag, attrs):
        if tag == "title" and self.head_title is None:
            self._title = []
        if tag == "main":
            self.depth_main += 1
        if tag in self.SKIP:
            self.skip += 1
        if tag == "h1" and self.depth_main and self.title is None:
            self._h1 = True
        if tag in self.BLOCK:
            self.parts.append("\n")

    def handle_endtag(self, tag):
        if tag == "title" and self._title is not None:
            self.head_title = re.sub(r"\s+", " ", "".join(self._title)).strip() or None
            self._title = None
        if tag == "main" and self.depth_main:
            self.depth_main -= 1
        if tag in self.SKIP and self.skip:
            self.skip -= 1
        if tag == "h1" and self._h1:
            self._h1 = False
            self.title = re.sub(r"\s+", " ", "".join(self._h1_text)).strip() or None
        if tag in self.BLOCK:
            self.parts.append("\n")

    def handle_data(self, data):
        if self._title is not None:
            self._title.append(data)
        if self.depth_main and not self.skip:
            self.parts.append(data)
            if self._h1:
                self._h1_text.append(data)


def html_page(text: str) -> tuple[str | None, str]:
    """The title (the page title before its " | site" part, else the main content's first h1) and the main content's
    text, one block per line."""
    parser = _MainText()
    parser.feed(text)
    lines = [re.sub(r"[ \t\u00a0]+", " ", line).strip() for line in "".join(parser.parts).split("\n")]
    title = parser.head_title.split(" | ")[0].strip() if parser.head_title else None
    return title or parser.title, "\n".join(line for line in lines if line)


Runner = Callable[[list[str]], str]


def run(command: list[str]) -> str:
    return subprocess.run(command, check=True, capture_output=True, text=True).stdout


def have(tool: str) -> bool:
    return shutil.which(tool) is not None


def pdf_pages(path: str, layout: str, runner: Runner = run, ocr: Callable[[str, int], str | None] | None = None) -> list[dict]:
    """Each page of a PDF: pdftotext's text blocks (-bbox-layout) in reading order, columns one after the other; a page
    with less than 20 letters and digits is read by OCR when it can be, else marked needs-ocr."""
    found = bbox_pages(runner(["pdftotext", "-enc", "UTF-8", "-bbox-layout", path, "-"]))
    ocr = ocr if ocr is not None else (lambda p, n: tesseract_page(p, n, runner))
    pages = []
    for number, (width, blocks) in enumerate(found, start=1):
        text = column_text(width, blocks)
        if len(re.findall(r"[^\W_]", text)) >= MIN_PAGE_TEXT:
            pages.append({"n": number, "method": "pdftotext", "text": text})
            continue
        read = ocr(path, number)
        if read is None:
            pages.append({"n": number, "method": "needs-ocr", "text": ""})
        else:
            pages.append({"n": number, "method": "tesseract", "text": read.strip()})
    return pages


def tesseract_page(path: str, number: int, runner: Runner = run) -> str | None:
    """One page rendered at 300 dpi by pdftoppm and read by tesseract (English), or None when tesseract is not installed."""
    if not have("tesseract"):
        return None
    with tempfile.TemporaryDirectory() as folder:
        prefix = os.path.join(folder, "page")
        runner(["pdftoppm", "-r", "300", "-gray", "-png", "-f", str(number), "-l", str(number), "-singlefile", path, prefix])
        return runner(["tesseract", prefix + ".png", "stdout", "-l", "eng"])


def tesseract_image(path: str, runner: Runner = run, words: frozenset[str] | set[str] | None = None,
                    reread: Callable[[list["Word"]], str | None] | None = None) -> str | None:
    """A page image read by tesseract for its words, their boxes and confidence (sparse text, --psm 11, as TSV) and set
    back into lines by table_text, its ingredient tables row by row, every amount it keeps read again from its cell
    (cell_reader); None when tesseract is not installed."""
    if not have("tesseract"):
        return None
    return table_text(tsv_words(runner(["tesseract", path, "stdout", "-l", "eng", "--psm", "11", "tsv"])), dictionary=words,
                      reread=reread if reread is not None else cell_reader(path, runner))


CELL_SCALE = 3


def cell_reader(path: str, runner: Runner = run) -> Callable[[list["Word"]], str | None]:
    """A second reading of an amount cell of a page image (staging checkpoint 8, T18: a half cup read as "2 Cup" at the
    confidence bar): the cell's words cropped from the image with a margin of half a line, enlarged CELL_SCALE times and
    read by tesseract as one line (--psm 7). Reads None for every cell when the image cannot be opened (no Pillow, an
    unreadable file), so no amount is confirmed."""
    try:
        from PIL import Image
    except ImportError:
        return lambda cell: None

    def read(cell: list["Word"]) -> str | None:
        if not cell:
            return None
        height = max(w.height for w in cell)
        try:
            with Image.open(path) as image:
                box = (max(0, min(w.left for w in cell) - height // 2), max(0, min(w.top for w in cell) - height // 2),
                       min(image.width, max(w.left + w.width for w in cell) + height // 2),
                       min(image.height, max(w.top + w.height for w in cell) + height // 2))
                crop = image.crop(box).convert("L")
                crop = crop.resize((crop.width * CELL_SCALE, crop.height * CELL_SCALE), Image.LANCZOS)
                with tempfile.TemporaryDirectory() as folder:
                    out = os.path.join(folder, "cell.png")
                    crop.save(out)
                    return runner(["tesseract", out, "stdout", "-l", "eng", "--psm", "7"]).strip()
        except (OSError, ValueError, subprocess.CalledProcessError):
            return None

    return read


OCR_BAR = 90
FRACTION_WIDTH = 0.8
AMOUNT_CELL = re.compile(rf"(?:\d{{1,3}}(?:\s?[{cookbook.VULGAR}])?|[{cookbook.VULGAR}]|\d{{1,2}}/\d{{1,2}})(?:\s+[^\W\d_]{{1,12}}\.?)?")


@dataclass
class Word:
    left: int
    top: int
    width: int
    height: int
    conf: float
    text: str


def tsv_words(tsv: str) -> list[Word]:
    """The words of tesseract's TSV output with their boxes and confidence."""
    out = []
    for row in csv.DictReader(io.StringIO(tsv or ""), delimiter="\t", quoting=csv.QUOTE_NONE):
        text = (row.get("text") or "").strip()
        if row.get("level") == "5" and text:
            out.append(Word(int(row["left"]), int(row["top"]), int(row["width"]), int(row["height"]), float(row["conf"]), text))
    return out


def _rows(words: list[Word], height: float) -> list[list[Word]]:
    rows: list[tuple[float, list[Word]]] = []
    for word in sorted(words, key=lambda w: w.top + w.height / 2):
        centre = word.top + word.height / 2
        if rows and abs(centre - rows[-1][0]) <= height / 2:
            row = rows[-1][1] + [word]
            rows[-1] = (sum(w.top + w.height / 2 for w in row) / len(row), row)
        else:
            rows.append((centre, [word]))
    return [sorted(row, key=lambda w: w.left) for _, row in rows]


LEADER = re.compile(r"[.·…_]{2,}")


def _name_run(row: list[Word], bar: float) -> int:
    named = 0
    while named < len(row) and row[named].conf >= bar and re.search(r"[^\W\d_]", row[named].text) and not LEADER.search(row[named].text):
        named += 1
    return named


def confirmed(cell: list[Word], height: float, reread: Callable[[list[Word]], str | None] | None) -> bool:
    """Whether an amount the first reading kept may be written (staging checkpoint 8, T18: "Butter . . . 2 Cup" where the
    page prints a half cup). A lone figure whose box is as wide as a fraction sign (FRACTION_WIDTH of the line's height:
    a printed half is about as wide as it is high, a figure about half) is never confirmed; otherwise, with a second
    reader, the cell read again must give the same number (a fraction sign, "%" or another figure refuses it). With no
    second reader the first reading stands."""
    number = cell[0]
    if re.fullmatch(r"\d", number.text) and number.height >= height / 2 and number.width >= FRACTION_WIDTH * number.height:
        return False
    if reread is None:
        return True
    again = (reread(cell) or "").split()
    return bool(again) and again[0].rstrip(".,") == number.text


def table_row(row: list[Word], height: float, edge: float, middle: float, bar: float = OCR_BAR,
              dictionary: frozenset[str] | set[str] | None = None, reread: Callable[[list[Word]], str | None] | None = None) -> str | None:
    """A row of an ingredient table set with dot leaders, as "name . . . . . . amount", or None when the row is not one.
    The name is the row's first run of words read with confidence at the bar or above. A row is a table row when it
    starts in the left quarter of the text (a centred title does not) and reaches past the middle, and after its name come either at most three words beyond a
    gap wider than two letters (leaders tesseract did not read), or words read with confidence below the bar (leaders
    read as dots, figures and stray letters) followed by at most three confident words (the amount); a line of prose
    goes on with confident words and is no table row. The amount is the row's last run of confident words, at most
    three, right of the middle. Name and amount are kept only when the amount is one number (a whole number, a fraction
    sign or both) and at most one word, the word after the name is no known word cut by the leaders ("crumbs...."), and
    no confident word lies between them; otherwise the unread mark stands in place of what was not read, so the recipe
    is never kept by rule or sent to the model, and a number the page does not hold (a fraction read as "%", a figure
    below the bar) is never written. An amount kept must also be confirmed (confirmed: a second reading of its cell)."""
    if len(row) < 2 or row[0].left - edge >= (middle - edge) / 2 or row[-1].left + row[-1].width <= middle:
        return None
    named = _name_run(row, bar)
    after = row[named:]
    doubtful = [i for i, w in enumerate(after) if w.conf < bar]
    if doubtful:
        if sum(1 for w in after[doubtful[0]:] if w.conf >= bar) > 3:
            return None
    elif not (named and 0 < len(after) <= 3 and after[0].left - (row[named - 1].left + row[named - 1].width) > 2 * height):
        return None
    amount_at = len(row)
    while amount_at > named and len(row) - amount_at < 3 and row[amount_at - 1].conf >= bar and not LEADER.search(row[amount_at - 1].text):
        amount_at -= 1
    name_text = " ".join(w.text for w in row[:named]).strip(" ,")
    amount_text = " ".join(w.text for w in row[amount_at:]).rstrip(".")
    between = row[named:amount_at]
    first = re.match(r"[^\W\d_]{3,}", between[0].text) if between else None
    cut = bool(first) and dictionary is not None and cookbook.known_word(first.group(0).lower(), dictionary)
    sure_name = bool(name_text) and not cut and not any(w.conf >= bar and re.fullmatch(r"[^\W\d_]{3,}", w.text) for w in between)
    sure_amount = bool(amount_text) and row[amount_at].left >= middle and bool(AMOUNT_CELL.fullmatch(amount_text)) \
        and confirmed(row[amount_at:], height, reread)
    return f"{name_text if sure_name else cookbook.UNREAD} . . . . . . {amount_text if sure_name and sure_amount else cookbook.UNREAD}"


FIGURE = re.compile(rf"[\d{cookbook.VULGAR}]")
SIGN = re.compile(r"[^\w\s.,;:!?'\"()\-–—’‘“”]")


MEASURE_WORD = re.compile(r"(?:cups?|cupfuls?|pints?|quarts?|gills?|gallons?|pounds?|ounces?|lbs?|oz|teaspoons?(?:fuls?)?|"
                          r"tablespoons?(?:fuls?)?|tsps?|tbsps?|teacups?(?:fuls?)?|spoonfuls?|saltspoons?(?:fuls?)?|wineglass(?:es|fuls?)?|"
                          r"dozen|pinch|quarter)\.?,?", re.I)
OLD_ONE = re.compile(r"[Il|]")


def _unsure(row: list[Word], i: int, bar: float, height: float = 0.0,
            reread: Callable[[list[Word]], str | None] | None = None) -> bool:
    word = row[i]
    after = row[i + 1].text if i + 1 < len(row) else ""
    if OLD_ONE.fullmatch(word.text) and MEASURE_WORD.fullmatch(after):
        return True
    if word.conf >= bar:
        return bool(re.fullmatch(r"\d{1,3}", word.text)) and bool(MEASURE_WORD.fullmatch(after)) and not confirmed(row[i:i + 2], height, reread)
    beside = [row[j].text for j in (i - 1, i + 1) if 0 <= j < len(row)]
    return i == 0 or bool(FIGURE.search(word.text) or SIGN.search(word.text) or any(FIGURE.search(b) for b in beside))


def _columns(rows: list[list[Word]], height: float, middle: float) -> list[list[Word]]:
    """The rows with a row of a two-column page (an index) cut where the right column starts: at a gap wider than three
    letters across the middle, after which a name starts with a capital and ends in a page number. The two halves are
    read as rows of their own, so a name never takes the other column's number (staging checkpoint 8, T18: "Sally Lunn
    25" where the book prints 16)."""
    out = []
    for row in rows:
        cut = next((i for i in range(1, len(row))
                    if row[i].left - (row[i - 1].left + row[i - 1].width) > 3 * height
                    and row[i - 1].left + row[i - 1].width < middle + height and row[i].left > middle - height
                    and re.match(r"[A-Z]", row[i].text) and len(row) - i >= 2 and re.fullmatch(r"\d{1,4}\.?", row[-1].text)), None)
        out.extend([row[:cut], row[cut:]] if cut else [row])
    return out


COLUMNS_REFUSED = f"{cookbook.UNREAD} the rows of this page's two columns cannot be told apart"
COLUMN_ROWS = 3
COLUMN_CROSSING = 6


@dataclass
class Layout:
    kind: str
    gutter: float = 0.0
    heading: list[Word] | None = None
    left: list[Word] | None = None
    right: list[Word] | None = None


def page_layout(words: list[Word], height: float) -> Layout:
    """How a page's words are laid out (staging checkpoint 9, U17: an index read across its two columns gave "Waffles 26"
    where the book prints 16). A page is set in two columns when a gutter at least two letters wide runs down its middle
    (between four and six tenths of its width, the one nearest the middle) that no word crosses, with at least COLUMN_ROWS rows on each side and
    most rows of the right side starting with a letter, as an index's names or a column of prose do (an ingredient
    table's right side holds its amounts, which start with a figure, and is one column). Rows above the first row that
    has words on both sides (a running head, a centred title) may cross the gutter and are read first. A page whose
    gutter a few other rows cross (at most one in COLUMN_CROSSING of them) is "mixed": those rows cannot be assigned to
    one column, and the page is refused. Any other page, also one whose lines run across the middle (prose, a table set
    with leaders), is one column."""
    left = min(w.left for w in words)
    right = max(w.left + w.width for w in words)
    width = right - left
    if width <= 0:
        return Layout("one")
    best: tuple[int, float] | None = None
    x = left + 0.4 * width
    while x <= left + 0.6 * width:
        crossing = sum(1 for w in words if w.left - height < x < w.left + w.width + height)
        if best is None or crossing < best[0] or (crossing == best[0] and abs(x - left - width / 2) < abs(best[1] - left - width / 2)):
            best = (crossing, x)
        x += height / 2
    if best is None:
        return Layout("one")
    gutter = best[1]
    rows = _rows(words, height)
    sides = [(any(w.left + w.width <= gutter for w in row), any(w.left >= gutter for w in row),
              any(w.left < gutter < w.left + w.width for w in row)) for row in rows]
    lefts = [row for row in rows if any(w.left + w.width <= gutter for w in row)]
    rights = [[w for w in row if w.left >= gutter] for row in rows]
    rights = [r for r in rights if r]
    lettered = sum(1 for r in rights if re.match(r"[^\W\d_]", r[0].text))
    if len(lefts) < COLUMN_ROWS or len(rights) < COLUMN_ROWS or lettered * 2 <= len(rights):
        return Layout("one")
    both = next((i for i, (l, r, _) in enumerate(sides) if l and r), None)
    if both is None:
        return Layout("one")
    crossing = sum(1 for _, _, c in sides[both:] if c)
    if crossing * COLUMN_CROSSING > len(sides) - both:
        return Layout("one")
    if crossing:
        return Layout("mixed", gutter)
    heading = [w for row in rows[:both] for w in row]
    body = [w for row in rows[both:] for w in row]
    return Layout("two", gutter, heading, [w for w in body if w.left + w.width <= gutter], [w for w in body if w.left >= gutter])


def table_text(words: list[Word], bar: float = OCR_BAR, dictionary: frozenset[str] | set[str] | None = None,
               reread: Callable[[list[Word]], str | None] | None = None, columns: bool = True) -> str:
    """A page's words set back into lines, top to bottom; a page set in two columns (page_layout) is read column by
    column, its heading first, then the left column and then the right one, each as a page of its own, and a page whose
    rows cannot be assigned to one column is refused (COLUMNS_REFUSED). A row of an ingredient table is one line (table_row), any
    other line as its words, a word read with confidence below the bar replaced by the unread mark where it may be or
    cut an amount: one that holds a figure or a sign that is no letter or punctuation (a fraction read as "%" or "Y%"),
    stands next to a word with a figure, or starts the line; and, read with confidence or not, an old-style one read as
    a letter ("I cup", "l pint") and a figure before a measure word that its second reading does not confirm
    (confirmed). A two-column row is read as two rows (_columns). A short line that
    starts where a neighbouring table row starts and ends left of the middle, between two table rows or beside one with
    no sentence end, is a table row whose amount was not read at all, and gets the unread mark as its amount. A blank
    line goes before a centred line (a title) and after it, before an indented line that is not a table row (a
    paragraph's first line) and at a wide gap; a wide gap inside a paragraph (the line before it goes on with no sentence
    end, ends with a hyphen, or the line after it starts in lower case) is a line the reading lost, and its place gets a
    line of the unread mark, never nothing (staging checkpoint 9, U17: "gether and bake on hot, well greased griddle.
    Serve im-" dropped with no mark)."""
    if not words:
        return ""
    lettered = [w.height for w in words if re.search(r"[^\W\d_]{2,}", w.text) and w.conf >= bar] or [w.height for w in words]
    height = sorted(lettered)[len(lettered) // 2]
    if columns:
        layout = page_layout(words, height)
        if layout.kind == "mixed":
            return COLUMNS_REFUSED + "\n"
        if layout.kind == "two":
            parts = [table_text(part, bar, dictionary, reread, columns=False) for part in (layout.heading, layout.left, layout.right) if part]
            return "\n".join(part.strip("\n") for part in parts if part.strip()) + "\n"
    left, right = min(w.left for w in words), max(w.left + w.width for w in words)
    middle = left + (right - left) / 2
    rows = _columns(_rows(words, height), height, middle)
    tabled = [table_row(row, height, left, middle, bar, dictionary, reread) for row in rows]
    for i, row in enumerate(rows):
        if tabled[i] is not None or row[-1].left + row[-1].width >= middle:
            continue
        beside = [j for j in (i - 1, i + 1) if 0 <= j < len(rows) and tabled[j] is not None and abs(rows[j][0].left - row[0].left) <= height]
        if beside and (len(beside) == 2 or not re.search(r"[.!?;:]$", row[-1].text)):
            named = _name_run(row, bar)
            tabled[i] = f"{' '.join(w.text for w in row[:named]).strip(' ,') or cookbook.UNREAD} . . . . . . {cookbook.UNREAD}"
    centres = [sum(w.top + w.height / 2 for w in row) / len(row) for row in rows]
    gaps = sorted(b - a for a, b in zip(centres, centres[1:])) or [height * 1.5]
    pitch = gaps[len(gaps) // 2]
    lines: list[str] = []
    previous = None
    previous_prose = False
    for row, centre, table in zip(rows, centres, tabled):
        start, end = row[0].left, row[-1].left + row[-1].width
        centred = table is None and start - left > 2 * height and right - end > 2 * height and abs((start - left) - (right - end)) < 2 * height
        indented = table is None and not centred and start - left > height
        wide = previous is not None and centre - previous > 1.6 * pitch
        text = table if table is not None else " ".join(cookbook.UNREAD if _unsure(row, i, bar, height, reread) else w.text for i, w in enumerate(row))
        lost = wide and table is None and previous_prose and not centred and lines and lines[-1] \
            and (not re.search(r"[.!?:]$", lines[-1]) or lines[-1].endswith("-") or re.match(r"[a-z]", text) is not None)
        if lost:
            lines.append(cookbook.UNREAD)
        elif lines and lines[-1] and (centred or indented or wide):
            lines.append("")
        lines.append(text)
        if centred:
            lines.append("")
        previous = centre
        previous_prose = table is None and not centred
    return "\n".join(lines).strip("\n") + "\n"


def page_reading(text: str, words: frozenset[str] | set[str]) -> dict:
    """How readable a page's text is: its short lines with letters, how many fail the readability test, and of those how
    many carry the unread mark and pass without it (marked, not garbled: the image reading marks what it did not read
    with confidence)."""
    short = [line for line in text.split("\n") if line.strip() and len(line.strip()) <= 60 and re.search(r"[^\W\d_]", line)]
    failing = [line for line in short if cookbook.unreadable(line, words)]
    marked = [line for line in failing if cookbook.UNREAD in line and not cookbook.unreadable(line.replace(cookbook.UNREAD, " "), words)]
    return {"shortLines": len(short), "unreadable": len(failing), "marked": len(marked)}


def loc_pages(path: str, words: frozenset[str] | set[str] | None = None, images: dict[int, str] | None = None,
              ocr: Callable[[str], str | None] | None = None, earlier: dict[int, dict] | None = None) -> tuple[list[dict], list[int]]:
    """The pages of a Library of Congress OCR text file (text.json), keyed by their sequence number, and the pages whose
    page image is fetched but could not be read here (no tesseract). The Library's own OCR is the text; a page the file
    leaves blank (a plate, a blank leaf) is marked loc-blank, not needs-ocr (staging checkpoints 3 to 6 skipped the books
    for a tesseract they do not use). With a word list each page is read for readability, and a page whose text fails
    it (three or more of its short lines, and three in ten or more, unreadable: an OCR'd ingredient table, staging
    checkpoint 7, S22) is marked needsImage. Where its page image was fetched (the images step), it is read by
    tesseract (tesseract_image) and the page stores that reading as imageText, so the image can be deleted; a page
    whose image is gone takes its stored reading (earlier, the page as the earlier text step wrote it) instead. The
    more readable of the two texts is the page's: loc-image-ocr when the image's reading has fewer garbled short lines
    (a line that only carries the unread mark is not garbled: staging checkpoint 8, T18, p. 25 kept the Library's
    garbled table though the image named every row), the Library's otherwise, and always the Library's when the image reading refused the page (imageRefused: its rows of two columns cannot be told apart, page_layout); both readings' counts are kept on the page. A stored reading always stays on its
    page, so the image is never fetched again: with no word list the page keeps the earlier step's choice, and a page
    that no longer fails the test keeps the reading beside the Library's text."""
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    images = images or {}
    earlier = earlier or {}
    ocr = ocr if ocr is not None else tesseract_image
    pages, unread = [], []
    for key in sorted(data, key=lambda k: int(k) if str(k).isdigit() else 0):
        if not str(key).isdigit():
            continue
        text = (data[key] or {}).get("fulltext") or ""
        page = {"n": int(key), "method": "loc-ocr" if text.strip() else "loc-blank", "text": text}
        before = earlier.get(page["n"]) or {}
        stored = before["imageText"] if isinstance(before.get("imageText"), str) else None
        if words is None and stored is not None:
            page["imageText"] = stored
            if before.get("method") == "loc-image-ocr" and text.strip():
                page.update(method="loc-image-ocr", text=stored)
        elif words is not None and text.strip():
            library = page_reading(text, words)
            page["libraryReading"] = library
            page["needsImage"] = cookbook.unreadable_block(text, words)
            if page["needsImage"] and (page["n"] in images or stored is not None):
                read = ocr(images[page["n"]]) if page["n"] in images else stored
                if read is None:
                    unread.append(page["n"])
                else:
                    image = page_reading(read, words)
                    page["imageReading"] = image
                    page["imageText"] = read
                    if read.startswith(COLUMNS_REFUSED):
                        page["imageRefused"] = "columns"
                    elif read.strip() and image["unreadable"] - image["marked"] < library["unreadable"]:
                        page.update(method="loc-image-ocr", text=read, needsImage=cookbook.unreadable_block(read, words))
        if stored is not None and "imageText" not in page:
            page["imageText"] = stored
        pages.append(page)
    return pages, unread


def page_images(book: Book, store: Store) -> dict[int, str]:
    """The page images of a book fetched and verified by the images step, by page sequence number."""
    out = {}
    for name in store.entries:
        found = re.fullmatch(re.escape(book.doc_id) + r"\.p(\d{4,})\.(?:jpg|jp2)", name)
        if found and store.verified(name):
            out[int(found.group(1))] = store.path(name)
    return out


def text_document(book: Book, store: Store, runner: Runner = run, ocr=None, words: frozenset[str] | set[str] | None = None,
                  image_ocr: Callable[[str], str | None] | None = None, earlier: dict | None = None) -> dict | None:
    """The page texts of one fetched document, or None when it is not fetched and verified. A Library book's pages are
    read for readability with the word list, and its fetched page images are read where its own text fails, a page whose
    image is already read and deleted keeping the reading the earlier text step stored (earlier, its pages.json)
    (loc_pages); the pages whose image could not be read here are listed as imageUnread."""
    names = doc_files(book)
    entries = [store.verified(n) for n in names]
    if not all(entries):
        return None
    entry = entries[-1]
    title = book.title
    unread: list[int] = []
    if book.kind == "html":
        with open(store.path(names[0]), encoding="utf-8", errors="replace") as handle:
            found, text = html_page(handle.read())
        title = found or title
        pages = [{"n": 1, "method": "html", "text": text}]
    elif book.kind == "loc-item":
        stored = {p["n"]: p for p in (earlier or {}).get("pages") or [] if isinstance(p.get("imageText"), str)}
        pages, unread = loc_pages(store.path(names[1]), words, page_images(book, store),
                                  image_ocr if image_ocr is not None else (lambda p: tesseract_image(p, runner, words)), stored)
    else:
        pages = pdf_pages(store.path(names[0]), book.layout, runner, ocr)
    if book.layout in ("book", "pages"):
        for page, text in zip(pages, strip_running_heads([p["text"] for p in pages])):
            page["text"] = text
    doc = {"doc": book.doc_id, "source": book.source, "url": book.url, "resourceUrl": entry.get("resourceUrl"),
           "title": title, "sha256": entry["sha256"], "layout": book.layout, "pages": pages}
    if unread:
        doc["imageUnread"] = unread
    return doc


AMOUNT_START = re.compile(r"^\s*(?:[\d½¼¾⅓⅔⅛⅜⅝⅞]|(?:a|an|one|two|three|four|five|six|half|pinch|dash)\b)", re.I)
STEP_NUMBER = re.compile(r"^\s*(\d{1,2})[.)]\s+")
CARD_SECTIONS = {"ingredients": "ingredients", "directions": "steps", "instructions": "steps", "method": "steps", "preparation": "steps"}
DANGLING = re.compile(r"(?:\b(?:into|or|and|to|about|of|with|for|plus|from|if|when|until|than|de|del|di|du|des|della)|[-–(])\s*$", re.I)
ADJECTIVE_END = re.compile(r"(?P<comma>,\s*)?\b(?:plain|grated|shredded|low-?fat|non-?fat|fat-free|reduced-fat|low-sodium|reduced-sodium|unsalted|"
                           r"unsweetened|extra-virgin|whole-wheat|whole-grain|part-skim)\s*$", re.I)
BRACKET_ONLY = re.compile(r"^\([^()]*\)[.,;]?$")
CARD_HEADER = re.compile(r"^(?:Prep(?:aration)?|Cook|Chill|Inactive|Total)(?: time)?:|^Yield:", re.I)
CARD_TIME = re.compile(r"\b(Prep(?:aration)?|Cook|Chill|Inactive|Total)(?: time)?:\s*([^|\n]*)", re.I)
CARD_TIME_FIELDS = {"prep": "prep", "preparation": "prep", "cook": "cook", "total": "total"}
TITLE_PLACEHOLDER = re.compile(r"^recipe title$", re.I)
CARD_END = re.compile(r"^(?:(?:recipe notes|notes|tips?|for more recipes|serving suggestions?)\s*:?\s*$|(?:nutrition facts|nutrition information|nutritional facts)\b(?!\s*\)))", re.I)


def minutes(text: str | None) -> int | None:
    if not text:
        return None
    hours = re.search(r"(\d+(?:\.\d+)?)\s*(?:hours?|hrs?)\b", text, re.I)
    mins = re.search(r"(\d+)\s*(?:minutes?|mins?)\b", text, re.I)
    if not hours and not mins:
        return None
    return int(round(float(hours.group(1)) * 60 if hours else 0)) + (int(mins.group(1)) if mins else 0)


def servings(text: str | None) -> float | None:
    found = re.search(r"(\d+(?:\.\d+)?)", text or "")
    return float(found.group(1)) if found else None


def _join_ingredients(lines: list[str], loose: list[str] | None = None) -> list[str]:
    """Ingredient lines with their wrapped parts joined: a text block (blank lines between blocks) starts a line; inside a
    block a line continues the one before when it starts in lower case or with a bracket or comma, or the one before
    ends with a comma, "or", "and" or "-", unless it starts with an amount; a line after an unclosed bracket always
    continues it ("diced (about" / "1/2 cup)"), and so does a line after one that ends with a word that cannot end it
    ("into", "or", "to", "about", "of", "with", "for", "plus", "from", a dash or an open bracket), even across a block
    break and when it starts with an amount: "cut into" / "½- to 1-inch pieces", "... or" / "1 (16-ounce) bag frozen
    vegetables", "to" / "taste" (staging checkpoint 3, N29). A line after one that ends with "if", "when", "until" or
    "than", or with a word that names no food alone ("or plain" / "Greek yogurt"; after a list comma only on a line with no
    amount, "Optional toppings: Chopped basil, grated" / "Parmesan cheese"), continues it unless it starts with an amount,
    and a line that is only a bracket ("(optional)") always continues the one before (staging checkpoint 4, P26). A line
    after "de", "del", "di", "du", "des" or "della" continues it too ("Pico de" / "Gallo"). A line that starts a new
    ingredient inside a block without an amount is added to loose, when given (staging checkpoint 5, Q23 review)."""
    out: list[str] = []
    gap = True
    for line in lines:
        text = line.strip()
        if not text:
            gap = True
            continue
        unclosed = bool(out) and out[-1].count("(") > out[-1].count(")")
        dangling = bool(out) and bool(DANGLING.search(out[-1]))
        bracket = bool(out) and bool(BRACKET_ONLY.match(text))
        adjective = ADJECTIVE_END.search(out[-1]) if out else None
        modifies = bool(adjective) and not (adjective.group("comma") and AMOUNT_START.match(out[-1]))
        continues = not gap and out and (text[0].islower() or text[0] in "(,;&" or out[-1].endswith((",", " or", " and", "-")))
        if unclosed or dangling or bracket or ((continues or modifies) and not AMOUNT_START.match(text)):
            out[-1] = f"{out[-1]} {text}"
        else:
            if loose is not None and out and not gap and not AMOUNT_START.match(text):
                loose.append(text)
            out.append(text)
        gap = False
    return out


def _starts_part(texts: list[str], at: int) -> bool:
    """Whether the line at `at` heads a part of a numbered method: a short line with no sentence end whose next line with
    text is a step numbered 1 ("Make Dip or Dressing" / "1. To make ranch dip, ...")."""
    text = texts[at]
    if len(text) > 60 or text.endswith((".", "!", "?", ",", ";")):
        return False
    following = next((t for t in texts[at + 1:] if t), None)
    number = STEP_NUMBER.match(following or "")
    return bool(number) and int(number.group(1)) == 1


def _join_steps(lines: list[str]) -> list[tuple[str | None, str]]:
    """Steps, each with the heading of its part (None when the method has no parts): numbered steps ("1.") take the
    lines of their block; a short line before a step numbered 1 heads a part, and the steps after it are that part's
    (staging checkpoint 6, R20: "Make Dip or Dressing" / "1. To make ranch dip, ..." was dropped); any other block after
    numbered steps that is not numbered ends the steps (a note, not a step). Unnumbered steps are one block each. A
    bullet ends the steps."""
    texts = [line.strip() for line in lines]
    numbered = any(STEP_NUMBER.match(text) for text in texts)
    out: list[list] = []
    section: str | None = None
    gap = True
    for at, text in enumerate(texts):
        if not text:
            gap = True
            continue
        if text.startswith(("•", "▪", "●")):
            break
        number = STEP_NUMBER.match(text)
        if number:
            out.append([section, text[number.end():]])
        elif numbered and gap and _starts_part(texts, at):
            section = re.sub(r"\s*:$", "", text)
        elif not out or (gap and not numbered):
            if numbered:
                gap = False
                continue
            out.append([None, text])
        elif gap and numbered:
            break
        else:
            out[-1][1] = f"{out[-1][1]} {text}"
        gap = False
    return [(part, step) for part, step in out]


def numbered_steps(text: str) -> int:
    """The lines of a card's method that start with a step number ("1.", "2)"): from the Directions heading to the notes,
    the nutrition line or another section heading (the count a record's steps are checked against)."""
    count, inside = 0, False
    for line in (line.strip() for line in text.split("\n")):
        head = re.sub(r"[:\s]+$", "", line).lower()
        if head in CARD_SECTIONS:
            inside = CARD_SECTIONS[head] == "steps"
            continue
        if CARD_END.match(line):
            inside = False
            continue
        if inside and STEP_NUMBER.match(line):
            count += 1
    return count


def steps_check(record: dict, text: str) -> str | None:
    """Why a card's record keeps fewer steps than its method numbers, or None (staging checkpoint 6, R20: a card that
    kept 2 of its 3 numbered steps passed the number check, which only finds what a record holds in its page)."""
    listed = numbered_steps(text)
    kept = len(record.get("steps") or [])
    if kept < listed:
        return f"number check: the record keeps {kept} of the {listed} numbered steps of the card's method"
    return None


def card_title(filled: list[str]) -> str | None:
    """The title printed on a card: its lines above the first header line ("Prep: ...", "Yield: ..."), joined, without
    a template's "Recipe Title" placeholder; None when no header line comes within the first four lines."""
    at = next((i for i, line in enumerate(filled[:5]) if CARD_HEADER.match(line)), None)
    if not at:
        return None
    words = [line for line in filled[:at] if not TITLE_PLACEHOLDER.match(line)]
    return re.sub(r"\s+", " ", " ".join(words)).strip() or None


def listed_ingredients(lines: list[str]) -> int:
    """The lines of a card's ingredient list that start with an amount: from the Ingredients heading to the next section
    heading, whatever stands between (the count the reader's kept lines are checked against)."""
    count, inside = 0, False
    for line in lines:
        head = re.sub(r"[:\s]+$", "", line).lower()
        if head in CARD_SECTIONS:
            inside = CARD_SECTIONS[head] == "ingredients"
            continue
        if inside and AMOUNT_START.match(line):
            count += 1
    return count


def parse_card(title: str | None, text: str) -> tuple[dict | None, str | None]:
    """A VA recipe card read by rule: the title as the card prints it (the listed title when the card prints none),
    each named time of the header ("Prep | Cook | Total"; a "Chill" or "Inactive" time is part of the total and is not
    kept on its own), "Yield", the Ingredients and Directions sections (wrapped lines joined), up to the notes and the
    nutrition line, which are not taken (staging checkpoint 4, P27 and P28). A notes heading is only a line that is the
    heading alone ("Recipe Notes"), never a wrapped "Notes)" of an ingredient, and a card whose kept ingredients are
    fewer than half the lines of its list that start with an amount fails the reader's own check (staging checkpoint 5,
    Q23: the arepas card kept 1 of 12)."""
    lines = [line.strip() for line in text.split("\n")]
    filled = [line for line in lines if line]
    if not filled:
        return None, "no text"
    name = card_title(filled) or (title or "").strip() or filled[0]
    record: dict = {"name": name}
    header = text[:m.start()] if (m := re.search(r"^\s*Ingredients\s*:?\s*$", text, re.I | re.M)) else text
    for field, value in CARD_TIME.findall(header):
        key = CARD_TIME_FIELDS.get(field.lower())
        value = value.strip()
        if key is None or f"{key}Time" in record or not re.search(r"\d", value):
            continue
        record[f"{key}Time"] = value
        if minutes(value) is not None:
            record[f"{key}TimeMinutes"] = minutes(value)
    yielded = re.search(r"Yield:\s*([^|\n]+)", text, re.I)
    if yielded:
        record["recipeYield"] = yielded.group(1).strip()
        if servings(yielded.group(1)) is not None:
            record["recipeServings"] = servings(yielded.group(1))
    sections: dict[str, list[str]] = {"ingredients": [], "steps": []}
    current = None
    for line in lines:
        head = re.sub(r"[:\s]+$", "", line).lower()
        if head in CARD_SECTIONS:
            current = CARD_SECTIONS[head]
            continue
        if CARD_END.match(line):
            current = None
            continue
        if current:
            sections[current].append(line)
    loose: list[str] = []
    ingredients = _join_ingredients(sections["ingredients"], loose)
    steps = _join_steps(sections["steps"])
    if not ingredients:
        return None, "layout not recognized: no Ingredients section"
    if not steps:
        return None, "layout not recognized: no Directions section"
    listed = listed_ingredients(lines)
    if len(ingredients) * 2 < listed:
        return None, f"reader check: kept {len(ingredients)} of the {listed} ingredient lines its list starts with an amount"
    if loose:
        return None, f"reader check: a line inside an ingredient block starts no amount and was not joined: {loose[0]!r}"
    record["ingredients"] = [{"rawLine": line} for line in ingredients]
    record["steps"] = [{"order": i + 1, "instruction": step, **({"section": part} if part else {})} for i, (part, step) in enumerate(steps)]
    return record, None


HTML_FIELDS = {"prep time": "prepTime", "cook time": "cookTime", "total time": "totalTime", "yields": "recipeYield",
               "yield": "recipeYield", "makes": "recipeYield"}
HTML_END = {"prep time", "cook time", "total time", "yields", "yield", "serving size", "nutritional facts", "nutrition facts",
            "back to top", "back to top link", "recipe video", "video", "watch the video", "watch video"}


def parse_html_recipe(title: str | None, text: str) -> tuple[dict | None, str | None]:
    """An NHLBI recipe page read by rule: the h1 title, the sentence under it, "Ingredients" and "Directions" blocks,
    and the labelled times and yield; the nutrition facts are not taken. The directions end at the page furniture
    after them: the times, the nutrition facts, "Back to top" and the "Recipe Video" widget label, which is never a
    step (staging checkpoint 3, N30)."""
    lines = [line.strip() for line in text.split("\n") if line.strip()]
    lowered = [line.lower().rstrip(":") for line in lines]
    if "ingredients" not in lowered or "directions" not in lowered:
        return None, "layout not recognized: no Ingredients and Directions blocks"
    first_ingredient = lowered.index("ingredients")
    directions = lowered.index("directions", first_ingredient)
    record: dict = {"name": title or lines[0]}
    if title and title in lines:
        after = lines[lines.index(title) + 1:first_ingredient]
        prose = [line for line in after if line != title and not line.lower().startswith("recipe source")]
        if prose:
            record["description"] = prose[0]
    ingredients = lines[first_ingredient + 1:directions]
    steps = []
    for line, low in zip(lines[directions + 1:], lowered[directions + 1:]):
        if low in HTML_END:
            break
        steps.append(line)
    for i, low in enumerate(lowered):
        if low in HTML_FIELDS and i + 1 < len(lines):
            key = HTML_FIELDS[low]
            record[key] = lines[i + 1]
            if key == "recipeYield":
                if servings(lines[i + 1]) is not None:
                    record["recipeServings"] = servings(lines[i + 1])
            elif minutes(lines[i + 1]) is not None:
                record[key + "Minutes"] = minutes(lines[i + 1])
    if not ingredients or not steps:
        return None, "layout not recognized: an empty Ingredients or Directions block"
    record["ingredients"] = [{"rawLine": line} for line in ingredients]
    record["steps"] = [{"order": i + 1, "instruction": step} for i, step in enumerate(steps)]
    return record, None


def number_check(record: dict, source: str) -> str | None:
    """Why a record is not found in its page text, or None. The cookbook extraction's fidelity check on every ingredient
    line (digits) and step (number words too), and every number of its yield and times must stand in the page."""
    for item in record.get("ingredients") or []:
        problem = cookbook.fidelity(item["rawLine"], source)
        if problem:
            return f"number check: {item['rawLine'][:60]}: {problem}"
    for step in record.get("steps") or []:
        for said in (step.get("section"), step["instruction"]):
            problem = cookbook.fidelity(said, source, number_words=True) if said else None
            if problem:
                return f"number check: {said[:60]}: {problem}"
    stated = cookbook.numbers(source, words=False)
    for key in ("recipeYield", "prepTime", "cookTime", "totalTime"):
        missing = cookbook.numbers(record.get(key) or "", words=False) - stated
        if missing:
            return f"number check: {key} {record[key]!r} states a number not in the page"
    return None


@dataclass
class Extraction:
    kept: list[dict] = field(default_factory=list)
    rejects: list[dict] = field(default_factory=list)
    unparsed: list[dict] = field(default_factory=list)
    needs_ocr: int = 0
    pages: int = 0
    by_rule: int = 0
    model_blocks: int = 0
    unreadable_blocks: int = 0
    model_calls: int = 0
    model_left: int = 0
    unreadable_pages: list[int] = field(default_factory=list)


LEFT_REASONS = ("model host busy", "model call budget spent", "model call failed", "model call timed out", "model call left", "unreadable answer")


def page_at(offsets: list[tuple[int, int]], at: int) -> int:
    number = offsets[0][1]
    for start, n in offsets:
        if start > at:
            break
        number = n
    return number


def name_problem(name: str, page: str) -> str | None:
    """Why a returned name is not the page's, or None: each of its words must stand on the page, letters closed up
    (a word the model made up, "DEGREE" in MIXED GREENS, is not there)."""
    closed = re.sub(r"[^a-z]", "", page.lower())
    page_words = set(cookbook.content_words(page))
    missing = [w for w in cookbook.content_words(name) if w not in page_words and re.sub(r"[^a-z]", "", w) not in closed]
    return f"a word of the name not on the page: {', '.join(missing[:3])}" if missing else None


LIST_HEADING = re.compile(r"(?:FOR\s+(?:THE\s+)?)?[A-Z][A-Z'&\- ]{2,40}:?")
SIDEBAR_TIP = re.compile(r"[B-HJ-Z]\s+[A-Z][a-z]")
BROKEN_WORD = re.compile(r"\b([^\W\d_]+)[\u00ad\u2013\u2010]([^\W\d_]+)\b")


def joined(text: str, words: frozenset[str] | set[str] | None) -> str:
    """The text with a soft hyphen taken out, and a word the page breaks with a dash joined again where the joined word
    is known and its halves are not both words ("min–utes" is "minutes"; "low-fat" stays)."""
    text = text.replace("\u00ad", "")
    if words is None:
        return text

    def join(found: re.Match) -> str:
        left, right = found.group(1), found.group(2)
        whole = (left + right).lower()
        if cookbook.known_word(whole, words) and not (cookbook.known_word(left.lower(), words) and cookbook.known_word(right.lower(), words)):
            return left + right
        return found.group(0)

    return BROKEN_WORD.sub(join, text)


def paged_record(recipe: dict, page: str, words: frozenset[str] | set[str] | None = None) -> tuple[dict | None, str | None]:
    """The record of a model answer for a page that holds one recipe, or why it is dropped: the number check on every
    line and step against the page, and every line of the page that starts with an amount (a bare step number and the
    title's own lines aside) must lie inside one of the record's ingredient lines (or a step that states it), so a
    wrapped line ("10-ounce baby lima" / "beans (frozen)") counts once and no listed ingredient is left out. The name is
    the title the page prints, read by rule (page_heading), when it can be read, else the model's with each word on the
    page; the yield is the page's "Makes ..." line, and a step that is only that sentence is no step; with a word list,
    every line and step must be readable (staging checkpoint 7, S23). A list heading in capitals ("DRESSING", "FOR THE
    TOPPING:") is no ingredient line, a sidebar tip set after its icon letter ("Z To reduce sodium, ...") is no step, and
    a word the page breaks with a soft hyphen is joined again ("min-utes" is "minutes") (staging checkpoint 8, T19)."""
    name = recipe.get("name") if isinstance(recipe.get("name"), str) else None
    ingredients = [joined(i, words) for i in recipe.get("ingredients") or [] if isinstance(i, str) and i.strip() and not LIST_HEADING.fullmatch(i.strip())]
    steps = [joined(s, words) for s in recipe.get("steps") or [] if isinstance(s, str) and s.strip() and not SIDEBAR_TIP.match(s.strip())]
    if not name or not ingredients:
        return None, "no name or no ingredient line"
    title, title_lines = page_heading(page, words)
    if title is None:
        name = despaced(name.strip(), page)
        problem = name_problem(name, page)
        if problem:
            return None, problem
    steps = [rest for rest in (_after_yield(s) for s in steps) if rest]
    record = {"name": title or name, "ingredients": [{"rawLine": i.strip()} for i in ingredients],
              "steps": [{"order": n + 1, "instruction": s.strip()} for n, s in enumerate(steps)]}
    printed = page_yield(page)
    if printed:
        record["recipeYield"] = printed
        if re.search(r"\bserv", printed, re.I) and servings(printed) is not None:
            record["recipeServings"] = servings(printed)
    problem = number_check(record, joined(page, words))
    if problem:
        return None, problem
    if words is not None:
        problem = cookbook.readability_check(ingredients, steps, words)
        if problem:
            return None, problem
    extracted = [cookbook.normalize(i) for i in ingredients]
    said = [cookbook.normalize(s) for s in steps]
    for line in page.split("\n"):
        text = line.strip()
        if re.fullmatch(r"\d{1,2}[.)]?", text) or not cookbook.INGREDIENT_START.match(text) or text in title_lines:
            continue
        folded = cookbook.normalize(text)
        if folded and not any(folded in e for e in extracted) and not any(folded in s for s in said):
            return None, f"an ingredient line not extracted: {text[:60]}"
    return record, None


TITLE_WORDS = frozenset("""
a an and the with of in on for to or n style home homemade down healthy easy quick old fashioned country southern new orleans
minute minutes hour one two three four five six seven eight nine ten red green black white yellow brown hot cold spicy sweet sour
chicken turkey beef pork ham fish catfish salmon tuna shrimp crab oyster oysters bean beans rice pea peas greens collard collards
cabbage corn potato potatoes sweet yam yams okra tomato tomatoes onion onions pepper peppers squash carrot carrots spinach
stew soup gumbo chili chowder salad slaw coleslaw sauce gravy creole cajun jambalaya casserole pie cobbler cake bread cornbread
biscuits muffins pudding fried baked oven smothered grilled roast roasted barbecue barbecued bbq stuffed steamed boiled
apple apples peach peaches banana berry berries lemon orange fruit dressing dip relish loaf meat meatloaf breakfast dinner lunch
""".split())


def despaced(name: str, page: str = "") -> str:
    """A title with its letter-spaced runs closed up by rule ("C AT F I S H STEW with RICE" is "CATFISH STEW with RICE"): a
    run of three or more tokens of one or two letters, mostly single, is one or more words; its letters are cut into the
    fewest words of the page's own text and a short list of cookbook title words, and kept as one word when they cannot
    be (staging checkpoint 4, P34)."""
    tokens = name.split(" ")
    words = TITLE_WORDS | {w.lower() for w in re.findall(r"[A-Za-z]{2,}", page)}
    out: list[str] = []
    i = 0
    while i < len(tokens):
        j = i
        while j < len(tokens) and re.fullmatch(r"[A-Za-z]{1,2}", tokens[j]):
            j += 1
        run = tokens[i:j]
        if len(run) >= 3 and sum(len(t) == 1 for t in run) * 3 >= len(run) * 2:
            out.extend(_segment("".join(run), words))
            i = j
        else:
            out.append(tokens[i])
            i += 1
    return " ".join(out)


PAGE_HEADINGS = {"INGREDIENTS", "NUTRITIONCONTENT", "NUTRITIONFACTS", "DIRECTIONS", "METHOD", "PERSERVING"}
YIELD_LINE = re.compile(r"^\s*(?:makes|serves|yields?:?)\s+(?P<amount>.*?\S)\s*\.?\s*$", re.I)


TITLE_JOINERS = {"and", "with", "or", "of", "n", "in", "a", "the"}


def _title_line(line: str) -> bool:
    text = line.strip()
    letters = [ch for ch in text if ch.isalpha()]
    upper = "".join(t for t in re.findall(r"[^\W\d_]+", text) if t not in TITLE_JOINERS)
    return (0 < len(text) <= 40 and len(letters) >= 2 and bool(upper) and upper.isupper()
            and "".join(letters).upper() not in PAGE_HEADINGS and not re.match(r"with\s", text))


def _spaced(lines: list[str]) -> bool:
    return any(sum(1 for t in line.split() if len(t) <= 2) >= 3 or len(re.sub(r"\W", "", line)) <= 3 for line in lines)


def _merge_fragments(fragments: list[str], overlap: bool) -> str:
    merged = ""
    for frag in fragments:
        if not merged:
            merged = frag
            continue
        if len(frag) <= 3 and frag in merged[-12:]:
            continue
        k = next((k for k in range(min(len(frag), len(merged)) - 1, 0, -1) if merged.endswith(frag[:k])), 0) if overlap else 0
        merged += frag[k:]
    return merged


def _segment_all(text: str, words: set[str], lenient: bool = False) -> str | None:
    out, last_alpha = "", False
    for part in re.findall(r"[A-Za-z]+|[^A-Za-z]+", text):
        if not part[0].isalpha():
            out += part
            last_alpha = False
            continue
        pieces = _segment(part, words)
        if len(pieces) == 1 and pieces[0].lower() not in words:
            if not lenient or len(part) > 12 or not re.search(r"[AEIOUY]", part, re.I):
                return None
        if out.endswith(("’", "'")) and len(pieces[0]) > 1:
            out += " "
        out += (" " if last_alpha else "") + " ".join(pieces)
        last_alpha = True
    return out


def _title_runs(lines: list[str]) -> list[tuple[list[str], int]]:
    runs: list[tuple[list[str], int]] = []
    run: list[str] = []
    end = 0
    for at, line in enumerate(lines):
        if not line.strip():
            continue
        if _title_line(line):
            run.append(line.strip())
            end = at
            continue
        if run:
            runs.append((run, end))
            run = []
    if run:
        runs.append((run, end))
    return runs


def _close_up(run: list[str], words: set[str]) -> str | None:
    fragments = [re.sub(r"\s+", "", line) for line in run]
    candidates = []
    for overlap in (True, False):
        merged = _merge_fragments(fragments, overlap)
        candidates.append(merged)
        candidates.extend(merged[:at] + merged[at + 1:] for at in range(len(merged) - 1)
                          if merged[at] == merged[at + 1] and merged[at].isalpha())
    for lenient in (False, True):
        read = [t for t in (_segment_all(c, words, lenient) for c in candidates) if t is not None]
        if read:
            return min(read, key=lambda t: len(t.split()))
    return None


def page_heading(page: str, dictionary: frozenset[str] | set[str] | None = None) -> tuple[str | None, list[str]]:
    """The recipe title a page prints, read by rule, with the lines it is read from; (None, []) when the page prints
    none. The title is a run of short lines in capitals (the page's INGREDIENTS and NUTRITION CONTENT headings aside; a
    run followed by an ingredient line is a heading of the list, "DRESSING", "TOPPING"; a run in spaced letters before
    any other), with the line after it when that starts with "with" ("BAKED FRIED CHICKEN BREAST with MIXED
    VEGETABLES"). A title set in spaced letters comes through the text layer cut into overlapping pieces ("M I XE" /
    "XED" / "D G R E E N S"): the pieces are closed up, their overlaps taken once, and the letters cut into the fewest
    words of the page's own text (its title lines aside), the cookbook title words and the word list, a letter the layer
    doubles taken once ("SKIIL LET" is SKILLET); a title that cannot be cut into known words is not read (staging
    checkpoint 7, S23: the model read MIXED GREENS as "MIXED DEGREE GREENS" and lost "Breast" on page 13)."""
    lines = page.split("\n")
    runs = []
    for run, end in _title_runs(lines):
        following = next((line.strip() for line in lines[end + 1:] if line.strip()), "")
        if AMOUNT_START.match(following) and not re.match(r"\d{1,2}$", following):
            continue
        runs.append((run, end, following))
    if not runs:
        return None, []
    run, end, following = next((r for r in runs if _spaced(r[0])), runs[0])
    if _spaced(run):
        own = {w.lower() for line in run for w in re.findall(r"[A-Za-z]{2,}", line)}
        page_words = {w.lower() for w in re.findall(r"[A-Za-z]{2,}", "\n".join(line for line in lines if line.strip() not in run))}
        words = {w.lower() for w in TITLE_WORDS} | (page_words - own) | {w for w in dictionary or () if len(w) >= 3}
        title = _close_up(run, words)
        if title is None:
            return None, []
    else:
        title = " ".join(run)
    used = list(run)
    if re.match(r"with\s", following, re.I) and len(following) <= 60:
        title = f"{title} {following}"
        used.append(following)
    return re.sub(r"\s+", " ", title).strip(), used


def page_title(page: str, dictionary: frozenset[str] | set[str] | None = None) -> str | None:
    """The recipe title a page prints, read by rule (page_heading), or None."""
    return page_heading(page, dictionary)[0]


def _yield_sentence(line: str) -> tuple[str, str] | None:
    found = YIELD_LINE.match(line)
    if not found:
        return None
    amount = found.group("amount")
    cut = re.search(r"\.\s+(?=[A-Z])", amount)
    rest = line[found.start("amount") + cut.end():].strip() if cut else ""
    amount = amount[:cut.start()] if cut else amount
    if not re.search(r"\d", unicodedata.normalize("NFKC", amount)):
        return None
    return amount, rest


def page_yield(page: str) -> str | None:
    """The yield a page prints ("Makes 6 servings", "Makes about ⅓ cup."), as its amount ("6 servings"), or None. The
    amount ends with its sentence: in "Makes 12 muffins. Bake 20 minutes." it is "12 muffins" (review of
    ck16-hand-import, CK16-R3)."""
    for line in page.split("\n"):
        found = _yield_sentence(line)
        if found:
            return found[0]
    return None


def _after_yield(step: str) -> str:
    """A step without a yield sentence it opens with: empty when the step is only that sentence, the rest of the step
    when more follows ("Makes 12 muffins. Bake 20 minutes." is "Bake 20 minutes.")."""
    found = _yield_sentence(" ".join(step.split()))
    return step if found is None else found[1]


def _segment(letters: str, words: frozenset[str] | set[str]) -> list[str]:
    best: list[list[str] | None] = [[]] + [None] * len(letters)
    for end in range(1, len(letters) + 1):
        for start in range(end):
            piece = letters[start:end]
            if best[start] is not None and piece.lower() in words and (best[end] is None or len(best[start]) + 1 < len(best[end])):
                best[end] = best[start] + [piece]
    return best[-1] or [letters]


def _span(offsets: list[tuple[int, int]], begin: int, length: int) -> list[int]:
    span = sorted({page_at(offsets, begin), page_at(offsets, begin + max(0, length - 1))})
    return list(range(span[0], span[-1] + 1))


def book_records(doc: dict, ask, paged: bool, words: frozenset[str] | set[str] | None = None,
                 result: Extraction | None = None) -> tuple[list[tuple[dict, list[int]]], list[dict]]:
    """Records of a book: page by page by the model (one recipe to a page), or across pages first by rule and then by
    the model for what the rules cannot split. In a book the recipes whose shape the rules read (title, ingredient
    list, method) are kept by rule with the same checks a model's record passes; a recipe whose text fails the
    readability test is not sent to the model and its pages are listed for the page image; only the rest goes to the
    model, in chunks cut at recipe titles (staging checkpoint 7, S22: about 13,000 chunks for 98 books). Each record is
    checked against its own recipe text and given the pages it spans. With result, the counts of rule records, model
    calls, calls still to make and unreadable pages are added to it."""
    kept, rejects = [], []
    stats = result if result is not None else Extraction()
    pages = [p for p in doc["pages"] if p["text"].strip()]
    if not pages:
        return kept, rejects
    if paged:
        for page in pages:
            chunk = cookbook.Chunk(0, len(page["text"]), page["text"], split=True)
            stats.model_calls += 1
            for recipe, reason in _ask(ask, chunk):
                if recipe is None:
                    rejects.append({"page": page["n"], **reason})
                    stats.model_left += reason["reason"].startswith(LEFT_REASONS)
                    continue
                record, why = paged_record(recipe, page["text"], words)
                if record is None:
                    rejects.append({"page": page["n"], "name": recipe.get("name"), "reason": why})
                else:
                    kept.append((record, [page["n"]]))
        return kept, rejects
    text, offsets = "", []
    for page in pages:
        offsets.append((len(text), page["n"]))
        text += page["text"].strip("\n") + "\n\n"
    found = cookbook.read_by_rule(text, words if words is not None else frozenset())
    unreadable_pages: set[int] = set()
    for block in found:
        if block.outcome == "rule":
            record = dict(block.record)
            record.pop("split", None)
            kept.append((record, _span(offsets, block.start, len(block.text.rstrip()))))
            stats.by_rule += 1
        elif block.outcome == "unreadable":
            pages_of = _span(offsets, block.start, len(block.text.rstrip()))
            unreadable_pages.update(pages_of)
            stats.unreadable_blocks += 1
            rejects.append({"page": pages_of[0], "name": block.title, "reason": block.reason})
        elif block.outcome == "model":
            stats.model_blocks += 1
    stats.unreadable_pages = sorted(set(stats.unreadable_pages) | unreadable_pages)
    for chunk in cookbook.model_chunks(found):
        stats.model_calls += 1
        for recipe, reason in _ask(ask, chunk):
            if recipe is None:
                rejects.append({"page": page_at(offsets, chunk.start), **reason})
                stats.model_left += reason["reason"].startswith(LEFT_REASONS)
                continue
            record, why = cookbook.check_record(recipe, chunk, translate=False, words=words)
            first = page_at(offsets, chunk.start)
            if record is None:
                rejects.append({"page": first, "name": recipe.get("name"), "reason": why})
                continue
            own = cookbook.recipe_text(chunk, record["name"]) or chunk.text
            begin = chunk.start + max(0, chunk.text.find(own))
            record.pop("split", None)
            kept.append((record, _span(offsets, begin, len(own.rstrip()))))
    return kept, rejects


def _ask(ask, chunk) -> list[tuple[dict | None, dict | None]]:
    try:
        answer = json.loads(ask(cookbook.EXTRACT_PROMPT, chunk.text))
        recipes = answer.get("recipes") if isinstance(answer, dict) else None
        if not isinstance(recipes, list):
            raise ValueError("no recipes list")
    except CallBudgetSpent as exc:
        return [(None, {"name": None, "reason": f"model call budget spent: {exc}; asked on the next run"})]
    except NotAsked as exc:
        return [(None, {"name": None, "reason": f"model call left: {exc}"})]
    except ModelHostBusy as exc:
        return [(None, {"name": None, "reason": f"model host busy: {exc}; not asked this run, asked on the next"})]
    except cookbook.ModelCallTimedOut as exc:
        return [(None, {"name": None, "reason": f"model call timed out: {exc}; the page is dropped this run and asked again on the next"})]
    except cookbook.ModelCallFailed as exc:
        return [(None, {"name": None, "reason": f"model call failed: {exc}; asked again on the next run"})]
    except (ValueError, json.JSONDecodeError) as exc:
        return [(None, {"name": None, "reason": f"unreadable answer: {exc}; never kept, asked again on the next run"})]
    return [(r, None) for r in recipes if isinstance(r, dict)]


class ModelHostBusy(Exception):
    """The model host stayed in use by another job past the run's wait limit; nothing more is asked this run."""


class CallBudgetSpent(ModelHostBusy):
    """The run made its --max-calls model calls; nothing more is asked this run."""


class NotAsked(ModelHostBusy):
    """A call the plan counts and does not make."""


class CallBudget:
    """Model calls up to a budget for the run (--max-calls; 0 is no budget), so a long book run is cut into runs a day
    apart: past the budget every call raises CallBudgetSpent, every answer kept stays, and the next run asks only what
    is still missing (staging checkpoint 7, correction 2: the Library's extraction is a multi-day run)."""

    def __init__(self, ask, limit: int):
        self.ask = ask
        self.limit = limit
        self.made = 0
        self.spent = False

    def __call__(self, system: str, user: str) -> str:
        if self.limit and self.made >= self.limit:
            self.spent = True
            raise CallBudgetSpent(f"{self.limit} calls made this run")
        self.made += 1
        return self.ask(system, user)


class ModelQueue:
    """One model step at a time per work folder: an extract run takes its turn on a lock file before it asks the model
    and gives the turn to a run that waits for it after `turn` calls or at the end of a document, so NCI and the
    Library of Congress take turns on the shared model host instead of asking it side by side (staging checkpoint 7,
    correction 1: the two runs waited on each other's calls). A run that waits leaves a marker beside the lock and holds
    a lock on it while it waits; the run holding the turn gives it up only when such a marker is there and held, and
    waits until the other run has taken it. A marker no run holds was left by a run that was killed while it waited
    (docker stop on PID 1 ends in SIGKILL, and no finally runs): it is no waiting run and is removed (review of
    ck16-hand-import, CK16-R2)."""

    def __init__(self, path: str, turn: int = 20, poll: float = 0.5, sleep: Callable[[float], None] = time.sleep,
                 handover: float = 120.0):
        self.path = path
        self.turn = turn
        self.poll = poll
        self.sleep = sleep
        self.handover = handover
        self.handle = None
        self.calls = 0
        self.marker = f"{path}.waiting.{os.getpid()}.{uuid.uuid4().hex}"

    @staticmethod
    def _held(marker: str) -> bool:
        import fcntl
        try:
            handle = open(marker, "r")
        except FileNotFoundError:
            return False
        with handle:
            try:
                fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                return True
            try:
                os.remove(marker)
                log(f"removed a waiting marker no run holds ({marker}): its run was stopped while it waited")
            except FileNotFoundError:
                pass
            return False

    def _others(self) -> list[str]:
        folder, base = os.path.split(self.path)
        return [os.path.join(folder, n) for n in os.listdir(folder or ".")
                if n.startswith(base + ".waiting.") and not n.endswith(".new") and os.path.join(folder, n) != self.marker
                and self._held(os.path.join(folder, n))]

    def acquire(self) -> None:
        if self.handle is not None:
            return
        import fcntl
        handle = open(self.path, "a+")
        try:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            mark = open(self.marker + ".new", "w")
            fcntl.flock(mark, fcntl.LOCK_EX)
            mark.write(str(os.getpid()))
            mark.flush()
            os.replace(self.marker + ".new", self.marker)
            log(f"another extract run has the model turn ({self.path}); waiting for it")
            try:
                fcntl.flock(handle, fcntl.LOCK_EX)
            finally:
                try:
                    os.remove(self.marker)
                except FileNotFoundError:
                    pass
                mark.close()
        self.handle = handle
        self.calls = 0

    def release(self) -> None:
        if self.handle is None:
            return
        import fcntl
        fcntl.flock(self.handle, fcntl.LOCK_UN)
        self.handle.close()
        self.handle = None

    def turn_over(self) -> None:
        """Gives the turn to a waiting run, if there is one, and waits until it has taken it."""
        waiting = self._others()
        if self.handle is None or not waiting:
            return
        self.release()
        waited = 0.0
        while waited < self.handover and any(self._held(m) for m in waiting):
            self.sleep(self.poll)
            waited += self.poll

    def wrap(self, ask):
        def call(system: str, user: str) -> str:
            if self.handle is not None and self.calls >= self.turn:
                self.turn_over()
            self.acquire()
            self.calls += 1
            return ask(system, user)
        return call


def ollama_ps(host: str, timeout: int = 30) -> list[dict]:
    """The models an Ollama host holds loaded now (GET /api/ps)."""
    request = urllib.request.Request(host.rstrip("/") + "/api/ps", headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode()).get("models") or []


def ollama_answers(host: str, model: str, timeout: float) -> bool:
    """Whether the model host answers a one-token request to the model within timeout seconds, sent with the context
    size of the real calls so it never reloads the model. A request on a model already loaded leaves its expires_at
    alone until it finishes, so only a request of one's own shows that the model is free; one that does not answer in
    time is abandoned, and the host dropped it with the connection."""
    body = {"model": model, "stream": False, "options": {**cookbook.OLLAMA_OPTIONS, "num_predict": 1},
            "messages": [{"role": "user", "content": "1"}]}
    request = urllib.request.Request(host.rstrip("/") + "/api/chat", data=json.dumps(body).encode(), method="POST",
                                     headers={"Content-Type": "application/json", "User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            json.loads(response.read().decode())["message"]
    except cookbook.MODEL_ERRORS + (ValueError, TypeError):
        return False
    return True


def host_in_use(read: Callable[[], list[dict]], probe: float = 2.0, sleep: Callable[[float], None] = time.sleep,
                answers: Callable[[], bool] | None = None) -> bool:
    """Whether another request is running on the model host: two readings of its loaded models, probe seconds apart, in
    which a model was loaded or its expires_at moved (it moves with the clock while a model's first request after its
    load is in flight: staging checkpoint 6, section 28), or, when the readings show neither, a one-token request
    (answers) that the host does not answer in time. Ollama sets expires_at only when a model's last request finishes,
    so a request on a model already loaded leaves both readings the same (staging checkpoint 6, section 28, 10:42Z:
    qwen2.5vl:7b loaded and in use; review of ck14-hand-import-box, CK14-1). A host that does not answer counts as in
    use."""
    try:
        first = read()
        sleep(probe)
        second = read()
    except cookbook.MODEL_ERRORS + (ValueError, AttributeError):
        return True
    before = {m.get("name"): m.get("expires_at") for m in first if isinstance(m, dict)}
    if any(m.get("name") not in before or m.get("expires_at") != before[m.get("name")] for m in second if isinstance(m, dict)):
        return True
    return answers is not None and not answers()


class HostGate:
    """Model calls that wait for a free host: before each call the host is read, and while another job uses it the call
    waits, poll seconds at a time, out of one budget of limit seconds for the whole run. Past the budget this call and
    every later one raise ModelHostBusy at once, so the run stops asking, keeps every answer it has, and the next run
    asks only the pages and chunks still missing (staging checkpoint 6, section 28: NCI was given up whole when the
    host was busy at three checks)."""

    def __init__(self, ask, in_use: Callable[[], bool], limit: float, poll: float, sleep: Callable[[float], None] = time.sleep):
        self.ask = ask
        self.in_use = in_use
        self.limit = limit
        self.poll = poll
        self.sleep = sleep
        self.waited = 0.0
        self.exhausted = False

    def __call__(self, system: str, user: str) -> str:
        if self.exhausted:
            raise ModelHostBusy(f"in use past the wait limit of {self.limit / 60:g} minutes")
        while self.in_use():
            if self.waited + self.poll > self.limit:
                self.exhausted = True
                raise ModelHostBusy(f"in use past the wait limit of {self.limit / 60:g} minutes")
            log(f"model host in use by another job; waiting {self.poll:g} s ({self.waited / 60:.1f} of {self.limit / 60:g} minutes waited)")
            self.sleep(self.poll)
            self.waited += self.poll
        return self.ask(system, user)


def page_url(doc: dict, pages: list[int], name: str) -> str:
    """The source link of a record: the document's URL, with its first page and the recipe's name when the document
    holds more than one recipe (a PDF's #page=N&search="name", a Library of Congress page ?sp=N&q=name), so a record's
    link never depends on which other records a run keeps."""
    if doc.get("resourceUrl"):
        base = doc["resourceUrl"].split("?")[0]
        return f"{base}?sp={pages[0]}&st=text&q=" + urllib.parse.quote(name)
    if len(pages) and doc.get("multi"):
        return f"{doc['url']}#page={pages[0]}&search=" + urllib.parse.quote(f'"{name}"')
    return doc["url"]


def ocr_missing(missing: dict[str, int]) -> str:
    """The message that stops a run where PDF pages with no text layer found no tesseract: the documents, their pages,
    and the image that has it."""
    listed = ", ".join(f"{doc} ({n})" for doc, n in sorted(missing.items()))
    return (f"{sum(missing.values())} PDF pages have no text layer and tesseract is not installed here: {listed}. Run text and "
            f"extract in the {IMAGE} image (ops/hand-import.Dockerfile, through ops/hand-import-box.sh), which has tesseract.")


def _no_host(system: str, user: str) -> str:
    raise NotAsked("no model host given")


def extract_document(doc: dict, source: Source, ask=None, words: frozenset[str] | set[str] | None = None) -> Extraction:
    """The records of one document, each checked against its page text and given its source link. A document with a page
    that still needs OCR is not extracted: it is counted unparsed with the image that reads it. A book is read with the
    word list (the readability test); without one it is not extracted. Without a model host a book keeps what the rules
    read, and the rest is counted as model calls left."""
    result = Extraction(pages=len(doc["pages"]))
    result.needs_ocr = sum(1 for p in doc["pages"] if p["method"] == "needs-ocr")
    if result.needs_ocr:
        result.unparsed.append({"doc": doc["doc"], "reason": f"needs OCR: {result.needs_ocr} pages have no text layer; run text in the {IMAGE} image"})
        return result
    if doc["layout"] in ("book", "pages") and words is None:
        words = cookbook.load_words()
        if words is None:
            result.unparsed.append({"doc": doc["doc"], "reason": f"no word list for the readability test (wamerican); run extract in the {IMAGE} image or give --words"})
            return result
    found: list[tuple[dict, list[int]]] = []
    if doc["layout"] in ("card", "html"):
        text = "\n\n".join(p["text"] for p in doc["pages"])
        parse = parse_card if doc["layout"] == "card" else parse_html_recipe
        record, why = parse(doc.get("title"), text)
        if record is None:
            result.unparsed.append({"doc": doc["doc"], "reason": why})
            return result
        problem = number_check(record, text) or (steps_check(record, text) if doc["layout"] == "card" else None)
        if problem:
            result.rejects.append({"doc": doc["doc"], "page": 1, "name": record["name"], "reason": problem})
            return result
        found.append((record, [p["n"] for p in doc["pages"] if p["text"].strip()][:1] or [1]))
    else:
        if ask is None and doc["layout"] == "pages":
            result.unparsed.append({"doc": doc["doc"], "reason": "a book needs the model host (--host, --model)"})
            return result
        kept, rejects = book_records(doc, ask or _no_host, paged=doc["layout"] == "pages", words=words, result=result)
        if ask is None and result.model_calls:
            result.unparsed.append({"doc": doc["doc"], "reason": "a book needs the model host (--host, --model)"})
            rejects = [r for r in rejects if not r["reason"].startswith("model call left")]
        found.extend(kept)
        result.rejects.extend({"doc": doc["doc"], **r} for r in rejects)
    doc = {**doc, "multi": doc["layout"] in ("book", "pages")}
    for record, pages in found:
        record.pop("publicDomain", None)
        record["sourceUrl"] = page_url(doc, pages, record["name"])
        record["sourceSite"] = source.site
        result.kept.append({"doc": doc["doc"], "source": doc["source"], "pages": pages, "documentSha256": doc["sha256"], "recipe": record})
    seen: Counter = Counter(r["recipe"]["sourceUrl"] for r in result.kept)
    doubled = {url for url, n in seen.items() if n > 1}
    if doubled:
        for item in [r for r in result.kept if r["recipe"]["sourceUrl"] in doubled]:
            result.kept.remove(item)
            result.rejects.append({"doc": doc["doc"], "page": item["pages"][0], "name": item["recipe"]["name"],
                                   "reason": "two records with one name on one page"})
    return result


def write_jsonl(path: str, rows: Iterable[dict]) -> None:
    with open(path, "w", encoding="utf-8") as handle:
        for row in rows:
            handle.write(json.dumps(row, ensure_ascii=False) + "\n")


def read_jsonl(path: str) -> list[dict]:
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def summarize(results: dict[str, list[tuple[str, Extraction]]]) -> dict:
    out = {}
    for source, items in sorted(results.items()):
        reasons: Counter = Counter()
        for _, r in items:
            reasons.update(x["reason"].split(":")[0] for x in r.rejects)
        out[source] = {
            "documents": len(items), "pages": sum(r.pages for _, r in items), "pagesNeedingOcr": sum(r.needs_ocr for _, r in items),
            "kept": sum(len(r.kept) for _, r in items), "dropped": sum(len(r.rejects) for _, r in items),
            "droppedByReason": dict(sorted(reasons.items())), "unparsedDocuments": sum(len(r.unparsed) for _, r in items),
        }
        if any(r.model_calls or r.by_rule for _, r in items):
            out[source].update({
                "keptByRule": sum(r.by_rule for _, r in items), "modelCalls": sum(r.model_calls for _, r in items),
                "modelCallsLeft": sum(r.model_left for _, r in items),
                "unreadablePages": sum(len(r.unreadable_pages) for _, r in items),
            })
            left = {doc: r.model_left for doc, r in items if r.model_left}
            if left:
                out[source]["modelCallsLeftByDocument"] = left
    return out


def rule_share(result: Extraction) -> float:
    """How well the rules read a book: the share of its recipes read by rule, of those read by rule, left to the model
    and too damaged to read."""
    found = result.by_rule + result.model_blocks + result.unreadable_blocks
    return result.by_rule / found if found else 0.0


def status_row(doc_id: str, result: Extraction) -> dict:
    """A book's state after a run: what the rules kept, the model calls made and still to make, its unreadable pages."""
    return {"doc": doc_id, "ruleShare": round(rule_share(result), 3), "keptByRule": result.by_rule, "kept": len(result.kept),
            "dropped": len(result.rejects), "modelCalls": result.model_calls, "modelCallsLeft": result.model_left,
            "unreadablePages": result.unreadable_pages, "done": result.model_left == 0 and not result.unparsed,
            "at": datetime.now(timezone.utc).isoformat(timespec="seconds")}


def answered_only(checkpoint: str, model: str):
    """A model call that only reads the answers a checkpoint file holds (the plan): a chunk not answered yet is counted
    as a call left, never asked."""
    def nothing(system: str, user: str) -> str:
        raise NotAsked("not asked by the plan")
    return cookbook.resumable(nothing, checkpoint, retries=0, model=model or "")


def plan_rows(docs: list[dict], sources: dict[str, Source], words, model: str, out_dir: str) -> list[dict]:
    """Every book of the run with what the rules read, the model calls it needs and the calls still left with --model,
    in the order the extraction takes them: the books the rules read best first, then the fewest calls left."""
    rows = []
    for doc in docs:
        result = extract_document(doc, sources[doc["source"]], answered_only(os.path.join(out_dir, f"{doc['doc']}.answers.jsonl"), model), words)
        rows.append(status_row(doc["doc"], result))
    return sorted(rows, key=lambda r: (-r["ruleShare"], r["modelCallsLeft"], r["doc"]))


def hand_bodies(sources: dict[str, Source]) -> list[dict]:
    """The POST api/ScrapingSource/hand bodies of the hand sources, with their licence facts."""
    return [{"domain": s.domain, "title": s.title, "licenceKind": s.licence_kind, "licenceStatement": s.licence_statement,
             "licenceFoundAt": s.licence_found_at, "notes": "Hand import (ops/hand-import.py); never fetched by the scraper."}
            for s in sources.values()]


def batches(records: list[dict], books: dict[str, Book], source_ids: dict[str, int]) -> list[tuple[str, dict]]:
    """Staged-import bodies: one per document (split at 500 recipes), under its hand source, with its attribution line."""
    by_doc: dict[str, list[dict]] = defaultdict(list)
    for row in records:
        by_doc[row["doc"]].append(row)
    out = []
    for doc_id, rows in by_doc.items():
        book = books[doc_id]
        if book.source not in source_ids:
            raise ValueError(f"no --source-id for {book.source}")
        for start in range(0, len(rows), MAX_BATCH):
            out.append((doc_id, {"recipes": [r["recipe"] for r in rows[start:start + MAX_BATCH]], "sourceId": source_ids[book.source],
                                 "sourceAttribution": book.attribution, "importKeywordsAsTags": False}))
    return out


def judged_sample(records: list[dict], texts: dict[str, dict], per_source: int, seed: int) -> list[dict]:
    """A seeded random sample of records per source for a person to judge against the page, verdict left blank."""
    rng = random.Random(seed)
    by_source: dict[str, list[dict]] = defaultdict(list)
    for row in records:
        by_source[row["source"]].append(row)
    out = []
    for source in sorted(by_source):
        rows = by_source[source]
        for row in rng.sample(rows, min(per_source, len(rows))):
            pages = {p["n"]: p["text"] for p in texts.get(row["doc"], {}).get("pages", [])}
            recipe = row["recipe"]
            out.append({"source": source, "doc": row["doc"], "pages": " ".join(map(str, row["pages"])), "url": recipe["sourceUrl"],
                        "name": recipe["name"], "ingredients": " | ".join(i["rawLine"] for i in recipe["ingredients"]),
                        "steps": len(recipe.get("steps") or []), "pageText": "\n".join(pages.get(n, "") for n in row["pages"])[:3000],
                        "verdict": "", "note": ""})
    return out


def parse_ids(values: list[str]) -> dict[str, int]:
    out = {}
    for value in values:
        key, _, number = value.partition("=")
        if not number.isdigit():
            raise ValueError(f"--source-id {value!r}: expected source=id")
        out[key] = int(number)
    return out


def api(args):
    base = os.environ.get("NOM_API")
    if not base:
        raise SystemExit("NOM_API is not set")
    client = attach.NomApi(base, os.environ.get("NOM_API_KEY"), os.environ.get("NOM_API_TOKEN"))
    return attach.paced(client.post, attach.Pacer(per_minute=args.per_minute, per_hour=args.per_hour))


def queue_path(out: str) -> str:
    """The model queue's lock file: in the image's work folder (/work) when --out is in it, so every run on that folder
    shares it, else in --out."""
    real = os.path.realpath(out)
    if real == "/work" or real.startswith("/work/"):
        return "/work/model-queue.lock"
    return os.path.join(out, "model-queue.lock")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Hand import of the approved PDF and scanned recipe sources (see the module docstring).")
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("fetch", "links", "text", "images", "plan", "extract", "sample", "stage"):
        p = sub.add_parser(name)
        p.add_argument("--out", required=True, help="the download folder, outside the repository")
        p.add_argument("--source", choices=None)
        p.add_argument("--books", default=BOOKS_CSV)
        p.add_argument("--sources", default=SOURCES_CSV)
        if name in ("fetch", "images"):
            p.add_argument("--limit", type=int, default=0)
        if name in ("text", "images", "plan", "extract"):
            p.add_argument("--words", help="the word list of the readability test (default: SCOWL's american-english from wamerican)")
        if name in ("plan", "extract"):
            p.add_argument("--model")
        if name == "extract":
            p.add_argument("--host")
            p.add_argument("--max-calls", type=int, default=0, help="model calls this run may make (default 0: no budget); past it the "
                                                                    "run stops and keeps every answer, and the next run goes on")
            p.add_argument("--queue", help="the lock file runs on one work folder take turns on for the model (default: "
                                           "model-queue.lock in /work, or in --out outside the image)")
            p.add_argument("--turn", type=int, default=20, help="model calls a run makes before it gives the turn to a waiting run (default 20)")
            p.add_argument("--timeout", type=int, default=600, help="seconds one model call may take (default 600)")
            p.add_argument("--retries", type=int, default=2, help="times a failed model call is asked again (default 2); in pages mode a "
                                                                   "call that times out is not asked again in the run")
            p.add_argument("--wait-limit", type=float, default=120, help="minutes the run may wait in all for a model host another job is "
                                                                         "using (default 120); past it the run stops and keeps every answer")
            p.add_argument("--wait-poll", type=float, default=60, help="seconds between readings of a busy model host (default 60)")
            p.add_argument("--probe", type=float, default=2, help="seconds between the two readings that tell a busy host (default 2)")
            p.add_argument("--answer-within", type=float, default=30, help="seconds the host has to answer a one-token request before "
                                                                           "it counts as in use (default 30)")
        if name == "sample":
            p.add_argument("--per-source", type=int, default=20)
            p.add_argument("--seed", type=int, default=20261121)
        if name == "stage":
            p.add_argument("--source-id", action="append", default=[])
            p.add_argument("--apply", action="store_true")
            p.add_argument("--log")
            p.add_argument("--per-minute", type=int, default=attach.PER_MINUTE)
            p.add_argument("--per-hour", type=int, default=attach.PER_HOUR)
    for name in ("sources", "withhold", "release"):
        p = sub.add_parser(name)
        p.add_argument("--sources", default=SOURCES_CSV)
        p.add_argument("--apply", action="store_true")
        p.add_argument("--per-minute", type=int, default=attach.PER_MINUTE)
        p.add_argument("--per-hour", type=int, default=attach.PER_HOUR)
        if name != "sources":
            p.add_argument("--source-id", action="append", default=[], required=True)
    args = parser.parse_args(argv)

    sources = load_sources(args.sources)
    if args.command == "sources":
        bodies = hand_bodies(sources)
        if not args.apply:
            print(json.dumps(bodies, indent=2, ensure_ascii=False))
            return 0
        post = api(args)
        for body in bodies:
            answer = post(HAND_PATH, {}, body)
            print(f"{body['domain']}: source id {answer.get('id')}")
        return 0
    if args.command in ("withhold", "release"):
        post = api(args)
        for key, source_id in parse_ids(args.source_id).items():
            path = f"/api/ScrapingSource/{source_id}/{'withhold' if args.command == 'withhold' else 'release'}-recipes"
            body = {"reason": "hand import undone (ops/hand-import.py)"} if args.command == "withhold" else None
            print(json.dumps({key: post(path, {"apply": str(args.apply).lower()}, body)}, ensure_ascii=False))
        return 0

    books = [b for b in load_books(args.books, sources) if not args.source or b.source == args.source]
    if args.command in ("fetch", "links"):
        fetcher = fetcher_for(sources)
        if args.command == "links":
            rows = links(books, sources, fetcher, args.source)
            writer = csv.DictWriter(sys.stdout, fieldnames=["doc_id", "source", "kind", "url", "layout", "title", "attribution"])
            writer.writeheader()
            writer.writerows(rows)
            log(f"{len(rows)} unlisted documents found with {fetcher.requests} requests")
            return 0
        store = Store(args.out)
        listed = [b for b in books if b.kind != "index"]
        report = fetch(listed[:args.limit] if args.limit else listed, sources, store, fetcher)
        print(json.dumps(report, indent=2, ensure_ascii=False))
        return 2 if report["refused"] else 0

    store = Store(args.out)
    text_dir = os.path.join(args.out, "text")
    os.makedirs(text_dir, exist_ok=True)
    words = cookbook.load_words([args.words] if getattr(args, "words", None) else None) if args.command in ("text", "images", "plan", "extract") else None
    if args.command == "text":
        if any(b.kind == "pdf" for b in books) and not have("pdftotext"):
            raise SystemExit(f"pdftotext (poppler-utils) is not installed: run the PDF sources in the {IMAGE} image (ops/hand-import-box.sh)")
        if words is None and any(b.kind == "loc-item" for b in books):
            log(f"no word list (wamerican): the Library's pages are not read for readability; run text in the {IMAGE} image or give --words")
        counts: Counter = Counter(partialImagesDeleted=delete_partial_images(store))
        missing: dict[str, int] = {}
        image_unread: dict[str, int] = {}
        for book in books:
            if book.kind == "index":
                continue
            pages_path = os.path.join(text_dir, f"{book.doc_id}.pages.json")
            earlier = None
            if book.kind == "loc-item" and os.path.exists(pages_path):
                with open(pages_path, encoding="utf-8") as handle:
                    earlier = json.load(handle)
            doc = text_document(book, store, words=words, earlier=earlier)
            if doc is None:
                counts["notFetched"] += 1
                continue
            with open(pages_path, "w", encoding="utf-8") as handle:
                json.dump(doc, handle, ensure_ascii=False)
            if book.kind == "loc-item":
                counts["imagesDeleted"] += delete_read_images(book, store, doc)
            counts["documents"] += 1
            counts.update(p["method"] for p in doc["pages"])
            counts["pagesNeedingImage"] += sum(1 for p in doc["pages"] if p.get("needsImage") and p["method"] == "loc-ocr")
            unread = sum(1 for p in doc["pages"] if p["method"] == "needs-ocr")
            if unread:
                missing[book.doc_id] = unread
            if doc.get("imageUnread"):
                image_unread[book.doc_id] = len(doc["imageUnread"])
        print(json.dumps({k: v for k, v in counts.items() if v or k not in ("pagesNeedingImage", "imagesDeleted", "partialImagesDeleted")}, indent=2))
        if missing:
            log(ocr_missing(missing))
        if image_unread:
            log(f"{sum(image_unread.values())} page images were fetched and not read, tesseract is not installed here: "
                f"{', '.join(f'{d} ({n})' for d, n in sorted(image_unread.items()))}. Run text in the {IMAGE} image.")
        return STOPPED if missing or image_unread else 0
    if args.command == "images":
        report = images(books, sources, store, fetcher_for(sources), text_dir, args.limit, words)
        print(json.dumps(report, indent=2, ensure_ascii=False))
        return 2 if report["refused"] else 0

    out_dir = os.path.join(args.out, "records")
    os.makedirs(out_dir, exist_ok=True)
    by_id = {b.doc_id: b for b in books}
    texts: dict[str, dict] = {}
    for book in books:
        path = os.path.join(text_dir, f"{book.doc_id}.pages.json")
        if book.kind != "index" and os.path.exists(path):
            with open(path, encoding="utf-8") as handle:
                texts[book.doc_id] = json.load(handle)
    if args.command == "plan":
        rows = plan_rows([texts[b.doc_id] for b in books if b.doc_id in texts and b.layout == "book"], sources, words, args.model, out_dir)
        with open(os.path.join(args.out, "extract-plan.json"), "w", encoding="utf-8") as handle:
            json.dump(rows, handle, indent=2)
        print(json.dumps({"books": len(rows), "keptByRule": sum(r["keptByRule"] for r in rows),
                          "modelCalls": sum(r["modelCalls"] for r in rows), "modelCallsLeft": sum(r["modelCallsLeft"] for r in rows),
                          "unreadablePages": sum(len(r["unreadablePages"]) for r in rows), "order": rows}, indent=2))
        return 0
    if args.command == "extract":
        model = cookbook.ollama(args.host, args.model, timeout=args.timeout) if args.host and args.model else None
        gate = HostGate(model, lambda: host_in_use(lambda: ollama_ps(args.host), args.probe,
                                                   answers=lambda: ollama_answers(args.host, args.model, args.answer_within)),
                        args.wait_limit * 60, args.wait_poll) if model else None
        queue = ModelQueue(args.queue or queue_path(args.out), turn=args.turn) if model else None
        budget = CallBudget(queue.wrap(gate), args.max_calls) if model else None
        order = {r["doc"]: i for i, r in enumerate(plan_rows([texts[b.doc_id] for b in books if b.doc_id in texts and b.layout == "book"],
                                                            sources, words, args.model, out_dir))}
        ordered = sorted((b for b in books if b.doc_id in texts), key=lambda b: (b.layout == "book", order.get(b.doc_id, 0)))
        results: dict[str, list[tuple[str, Extraction]]] = defaultdict(list)
        try:
            for book in ordered:
                doc = texts[book.doc_id]
                ask = cookbook.resumable(budget, os.path.join(out_dir, f"{book.doc_id}.answers.jsonl"), retries=args.retries, model=args.model,
                                         retry_timeouts=book.layout != "pages") if model else None
                result = extract_document(doc, sources[book.source], ask, words)
                results[book.source].append((book.doc_id, result))
                write_jsonl(os.path.join(out_dir, f"{book.doc_id}.records.jsonl"), result.kept)
                write_jsonl(os.path.join(out_dir, f"{book.doc_id}.rejects.jsonl"), result.rejects + result.unparsed)
                if book.layout in ("book", "pages"):
                    with open(os.path.join(out_dir, f"{book.doc_id}.status.json"), "w", encoding="utf-8") as handle:
                        json.dump(status_row(book.doc_id, result), handle, indent=2)
                if queue is not None:
                    queue.turn_over()
        finally:
            if queue is not None:
                queue.release()
        summary = summarize(results)
        with open(os.path.join(args.out, "extract-summary.json"), "w", encoding="utf-8") as handle:
            json.dump(summary, handle, indent=2)
        print(json.dumps(summary, indent=2))
        missing = {doc: r.needs_ocr for items in results.values() for doc, r in items if r.needs_ocr}
        if missing:
            log(ocr_missing(missing))
        if gate is not None and gate.exhausted:
            log(f"the model host at {args.host} stayed in use by another job past --wait-limit {args.wait_limit:g} minutes: the pages and "
                f"chunks not asked are counted as 'model host busy'; every answer is kept, so run extract again to ask only those")
        if budget is not None and budget.spent:
            log(f"the run made its --max-calls {args.max_calls} model calls: the chunks not asked are counted as 'model call budget spent'; "
                f"every answer is kept, so run extract again (another day) to go on")
        return STOPPED if missing or (gate is not None and gate.exhausted) or (budget is not None and budget.spent) else 0

    records = [row for book in books for row in read_jsonl(os.path.join(out_dir, f"{book.doc_id}.records.jsonl"))]
    if args.command == "sample":
        rows = judged_sample(records, texts, args.per_source, args.seed)
        writer = csv.DictWriter(sys.stdout, fieldnames=list(rows[0]) if rows else ["source"])
        writer.writeheader()
        writer.writerows(rows)
        return 0

    planned = batches(records, by_id, parse_ids(args.source_id))
    bodies_dir = os.path.join(args.out, "staged")
    os.makedirs(bodies_dir, exist_ok=True)
    totals: Counter = Counter()
    post = api(args) if args.apply else None
    log_handle = open(args.log, "a", encoding="utf-8") if args.log else None
    try:
        for index, (doc_id, body) in enumerate(planned, start=1):
            with open(os.path.join(bodies_dir, f"{index:04d}-{doc_id}.json"), "w", encoding="utf-8") as handle:
                json.dump(body, handle, ensure_ascii=False)
            totals["batches"] += 1
            totals["recipes"] += len(body["recipes"])
            if post is None:
                continue
            answer = post(STAGED_PATH, {}, body) or {}
            for key in ("imported", "skippedDuplicates", "refused"):
                totals[key] += int(answer.get(key) or 0)
            totals["failed"] += len(answer.get("failures") or []) - int(answer.get("refused") or 0)
            if log_handle:
                log_handle.write(json.dumps({"doc": doc_id, "at": datetime.now(timezone.utc).isoformat(timespec="seconds"), "answer": answer},
                                            ensure_ascii=False) + "\n")
    finally:
        if log_handle:
            log_handle.close()
    print(json.dumps({"apply": bool(args.apply), **dict(totals), "bodies": bodies_dir}, indent=2))
    return 2 if totals["refused"] or totals["failed"] else 0


if __name__ == "__main__":
    sys.exit(main())
