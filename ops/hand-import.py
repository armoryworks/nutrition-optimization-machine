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
   other. Library of Congress books use the library's own OCR, page by page. A plain page is read
   from its HTML. Nothing but open-source tools runs.
4. Extraction: VA recipe cards and NHLBI pages are read by rule; books (NCI, the Library of
   Congress) by ops/cookbook-extract.py's model extraction. Every record then passes the cookbook
   extraction's number check against its own page text: every ingredient line and step, and the
   yield and times, must be found there with every number. A record that fails is dropped and
   counted with its reason; nothing repairs it. A record keeps the document's URL with its page
   (PDF "#page=N", a Library of Congress page "?sp=N") as its source URL, so the staged import
   stores it as its source document and the recipe links to the page it came from. Images and
   the sources' nutrition figures are never taken.
5. Staging: one staged-import batch per document under the source's hand record, with the
   document's attribution line from books.csv. A dry run writes the batches and posts nothing;
   --apply posts them. Undo is withholding the hand source's recipes (journaled, released by its
   own call or the journal's undo).

USAGE
-----
    ./ops/hand-import.py fetch --out ~/hand-import [--source va] [--limit 5]
    ./ops/hand-import.py links --out ~/hand-import --source va > va-links.csv
    ./ops/hand-import.py text --out ~/hand-import
    ./ops/hand-import.py extract --out ~/hand-import [--host http://<model host>:11434 --model qwen2.5:14b-instruct]
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
import json
import os
import random
import re
import shutil
import subprocess
import sys
import tempfile
import time
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
    """The hand sources: domain, the hosts their documents may be fetched from, licence facts and delay."""
    out = {}
    with open(path, encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            out[row["source"]] = Source(
                source=row["source"], domain=row["domain"], hosts=tuple(h.strip().lower() for h in row["hosts"].split() if h.strip()),
                title=row["title"], licence_kind=row["licence_kind"], licence_statement=row["licence_statement"],
                licence_found_at=row["licence_found_at"], min_delay=float(row["min_delay_seconds"]), site=row["site"])
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
    """Fetches listed documents one at a time from the allowed hosts, honouring robots.txt and its crawl-delay."""

    def __init__(self, allowed: dict[str, float], opener: Opener = http_open,
                 clock: Callable[[], float] = time.monotonic, sleep: Callable[[float], None] = time.sleep):
        self.allowed = allowed
        self.opener = opener
        self.clock = clock
        self.sleep = sleep
        self.robots: dict[str, urllib.robotparser.RobotFileParser | None] = {}
        self.delays: dict[str, float] = {}
        self.last: dict[str, float] = {}
        self.requests = 0

    def _wait(self, host: str) -> None:
        if host in self.last:
            remaining = self.last[host] + self.delays.get(host, self.allowed[host]) - self.clock()
            if remaining > 0:
                self.sleep(remaining)
        self.last[host] = self.clock()
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

    def get(self, url: str, headers: dict | None = None) -> tuple[Response, str]:
        """The answer to one GET, following a redirect only to an allowed host and an allowed path."""
        for _ in range(MAX_REDIRECTS + 1):
            host = self.check(url)
            self._wait(host)
            answer = self.opener(url, headers or {})
            if answer.status in (301, 302, 303, 307, 308) and answer.location:
                url = urllib.parse.urljoin(url, answer.location)
                continue
            return answer, url
        raise Refused(f"{url}: more than {MAX_REDIRECTS} redirects")

    def read(self, url: str) -> bytes:
        answer, final = self.get(url)
        if answer.status != 200:
            raise Refused(f"{final}: HTTP {answer.status}")
        return b"".join(answer.body)

    def download(self, url: str, target: str) -> dict:
        """The document saved at target, resumed from target.part when the host serves ranges, and verified."""
        part = target + ".part"
        have = os.path.getsize(part) if os.path.exists(part) else 0
        answer, final = self.get(url, {"Range": f"bytes={have}-"} if have else {})
        if answer.status == 416 and have:
            os.remove(part)
            return self.download(url, target)
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
            raise Refused(f"{final}: {size} bytes of {expected}; the partial file is kept and the next run resumes it")
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
        with open(self.manifest_path, "a", encoding="utf-8") as handle:
            handle.write(json.dumps(entry, ensure_ascii=False) + "\n")
        with open(os.path.join(self.out, "SHA256SUMS"), "w", encoding="utf-8") as handle:
            for file in sorted(self.entries):
                handle.write(f"{self.entries[file]['sha256']}  docs/{file}\n")
        return entry


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
        if all(store.verified(n) for n in names):
            counts["verified"] += 1
            continue
        try:
            if book.kind == "loc-item":
                item_name, text_name = names
                if not store.verified(item_name):
                    store.add(item_name, fetcher.download(loc_json_url(book.url), store.path(item_name)))
                with open(store.path(item_name), encoding="utf-8") as handle:
                    item = json.load(handle)
                problem = loc_item_problem(item)
                if problem:
                    raise Refused(f"{book.url}: {problem}")
                derived = item["resources"][0]["fulltext_derivative"]
                if host_of(derived) not in sources[book.source].hosts:
                    raise Refused(f"{derived}: the OCR text is not on a host of {book.source}")
                entry = fetcher.download(derived, store.path(text_name))
                if entry["type"] != "json":
                    raise Refused(f"{derived}: expected the OCR text as JSON, got {entry['type']}")
                store.add(text_name, {**entry, "resourceUrl": item["resources"][0].get("url")})
            else:
                entry = fetcher.download(book.url, store.path(names[0]))
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
            page = fetcher.read(book.url).decode("utf-8", errors="replace")
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


def loc_pages(path: str) -> list[dict]:
    """The pages of a Library of Congress OCR text file (text.json), keyed by their sequence number."""
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    pages = []
    for key in sorted(data, key=lambda k: int(k) if str(k).isdigit() else 0):
        if not str(key).isdigit():
            continue
        text = (data[key] or {}).get("fulltext") or ""
        pages.append({"n": int(key), "method": "loc-ocr" if text.strip() else "needs-ocr", "text": text})
    return pages


def text_document(book: Book, store: Store, runner: Runner = run, ocr=None) -> dict | None:
    """The page texts of one fetched document, or None when it is not fetched and verified."""
    names = doc_files(book)
    entries = [store.verified(n) for n in names]
    if not all(entries):
        return None
    entry = entries[-1]
    title = book.title
    if book.kind == "html":
        with open(store.path(names[0]), encoding="utf-8", errors="replace") as handle:
            found, text = html_page(handle.read())
        title = found or title
        pages = [{"n": 1, "method": "html", "text": text}]
    elif book.kind == "loc-item":
        pages = loc_pages(store.path(names[1]))
    else:
        pages = pdf_pages(store.path(names[0]), book.layout, runner, ocr)
    if book.layout in ("book", "pages"):
        for page, text in zip(pages, strip_running_heads([p["text"] for p in pages])):
            page["text"] = text
    return {"doc": book.doc_id, "source": book.source, "url": book.url, "resourceUrl": entry.get("resourceUrl"),
            "title": title, "sha256": entry["sha256"], "layout": book.layout, "pages": pages}


AMOUNT_START = re.compile(r"^\s*(?:[\d½¼¾⅓⅔⅛⅜⅝⅞]|(?:a|an|one|two|three|four|five|six|half|pinch|dash)\b)", re.I)
STEP_NUMBER = re.compile(r"^\s*(\d{1,2})[.)]\s+")
CARD_SECTIONS = {"ingredients": "ingredients", "directions": "steps", "instructions": "steps", "method": "steps", "preparation": "steps"}
CARD_END = re.compile(r"^(recipe notes|notes|tips?|nutrition facts|nutrition information|nutritional facts|for more recipes|serving suggestions?)\b", re.I)


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


def _join_ingredients(lines: list[str]) -> list[str]:
    """Ingredient lines with their wrapped parts joined: a text block (blank lines between blocks) starts a line; inside a
    block a line continues the one before when it starts in lower case or with a bracket or comma, or the one before
    ends with a comma, "or", "and" or "-", unless it starts with an amount; a line after an unclosed bracket always
    continues it ("diced (about" / "1/2 cup)")."""
    out: list[str] = []
    gap = True
    for line in lines:
        text = line.strip()
        if not text:
            gap = True
            continue
        unclosed = bool(out) and out[-1].count("(") > out[-1].count(")")
        continues = not gap and out and (text[0].islower() or text[0] in "(,;&" or out[-1].endswith((",", " or", " and", "-")))
        if unclosed or (continues and not AMOUNT_START.match(text)):
            out[-1] = f"{out[-1]} {text}"
        else:
            out.append(text)
        gap = False
    return out


def _join_steps(lines: list[str]) -> list[str]:
    """Steps: numbered steps ("1.") take the lines of their block, and the first block after them that is not numbered
    ends the steps (a note, not a step); unnumbered steps are one block each. A bullet ends the steps."""
    numbered = any(STEP_NUMBER.match(line.strip()) for line in lines)
    out: list[str] = []
    gap = True
    for line in lines:
        text = line.strip()
        if not text:
            gap = True
            continue
        if text.startswith(("•", "▪", "●")):
            break
        number = STEP_NUMBER.match(text)
        if number:
            out.append(text[number.end():])
        elif not out or (gap and not numbered):
            if numbered:
                gap = False
                continue
            out.append(text)
        elif gap and numbered:
            break
        else:
            out[-1] = f"{out[-1]} {text}"
        gap = False
    return out


def parse_card(title: str | None, text: str) -> tuple[dict | None, str | None]:
    """A VA recipe card read by rule: the title, "Prep | Cook | Total", "Yield", the Ingredients and Directions
    sections (wrapped lines joined), up to the notes and the nutrition line, which are not taken."""
    lines = [line.strip() for line in text.split("\n")]
    filled = [line for line in lines if line]
    if not filled:
        return None, "no text"
    name = (title or "").strip() or filled[0]
    record: dict = {"name": name}
    times = re.search(r"Prep(?:aration)?(?: time)?:\s*([^|]+?)\s*\|\s*Cook(?: time)?:\s*([^|]+?)\s*\|\s*Total(?: time)?:\s*(.+)", text, re.I)
    if times:
        for key, value in zip(("prep", "cook", "total"), times.groups()):
            record[f"{key}Time"] = value.strip()
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
    ingredients = _join_ingredients(sections["ingredients"])
    steps = _join_steps(sections["steps"])
    if not ingredients:
        return None, "layout not recognized: no Ingredients section"
    if not steps:
        return None, "layout not recognized: no Directions section"
    record["ingredients"] = [{"rawLine": line} for line in ingredients]
    record["steps"] = [{"order": i + 1, "instruction": step} for i, step in enumerate(steps)]
    return record, None


HTML_FIELDS = {"prep time": "prepTime", "cook time": "cookTime", "total time": "totalTime", "yields": "recipeYield",
               "yield": "recipeYield", "makes": "recipeYield"}
HTML_END = {"prep time", "cook time", "total time", "yields", "yield", "serving size", "nutritional facts", "nutrition facts",
            "back to top", "back to top link"}


def parse_html_recipe(title: str | None, text: str) -> tuple[dict | None, str | None]:
    """An NHLBI recipe page read by rule: the h1 title, the sentence under it, "Ingredients" and "Directions" blocks,
    and the labelled times and yield; the nutrition facts are not taken."""
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
        problem = cookbook.fidelity(step["instruction"], source, number_words=True)
        if problem:
            return f"number check: {step['instruction'][:60]}: {problem}"
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


def page_at(offsets: list[tuple[int, int]], at: int) -> int:
    number = offsets[0][1]
    for start, n in offsets:
        if start > at:
            break
        number = n
    return number


def paged_record(recipe: dict, page: str) -> tuple[dict | None, str | None]:
    """The record of a model answer for a page that holds one recipe, or why it is dropped: the number check on every
    line and step against the page, and every line of the page that starts with an amount (a bare step number aside)
    must lie inside one of the record's ingredient lines (or a step that states it), so a wrapped line ("10-ounce baby lima" / "beans (frozen)")
    counts once and no listed ingredient is left out."""
    name = recipe.get("name") if isinstance(recipe.get("name"), str) else None
    ingredients = [i for i in recipe.get("ingredients") or [] if isinstance(i, str) and i.strip()]
    steps = [s for s in recipe.get("steps") or [] if isinstance(s, str) and s.strip()]
    if not name or not ingredients:
        return None, "no name or no ingredient line"
    record = {"name": name.strip(), "ingredients": [{"rawLine": i.strip()} for i in ingredients],
              "steps": [{"order": n + 1, "instruction": s.strip()} for n, s in enumerate(steps)]}
    problem = number_check(record, page)
    if problem:
        return None, problem
    extracted = [cookbook.normalize(i) for i in ingredients]
    said = [cookbook.normalize(s) for s in steps]
    for line in page.split("\n"):
        text = line.strip()
        if re.fullmatch(r"\d{1,2}[.)]?", text) or not cookbook.INGREDIENT_START.match(text):
            continue
        folded = cookbook.normalize(text)
        if folded and not any(folded in e for e in extracted) and not any(folded in s for s in said):
            return None, f"an ingredient line not extracted: {text[:60]}"
    return record, None


def book_records(doc: dict, ask, paged: bool) -> tuple[list[tuple[dict, list[int]]], list[dict]]:
    """Records of a book by the cookbook extraction: page by page (one recipe to a page) or in chunks cut at recipe
    titles across pages, each record checked by its rules against its own recipe text and given the pages it spans."""
    kept, rejects = [], []
    pages = [p for p in doc["pages"] if p["text"].strip()]
    if not pages:
        return kept, rejects
    if paged:
        for page in pages:
            chunk = cookbook.Chunk(0, len(page["text"]), page["text"], split=True)
            for recipe, reason in _ask(ask, chunk):
                if recipe is None:
                    rejects.append({"page": page["n"], **reason})
                    continue
                record, why = paged_record(recipe, page["text"])
                if record is None:
                    rejects.append({"page": page["n"], "name": recipe.get("name"), "reason": why})
                else:
                    kept.append((record, [page["n"]]))
        return kept, rejects
    text, offsets = "", []
    for page in pages:
        offsets.append((len(text), page["n"]))
        text += page["text"].strip("\n") + "\n\n"
    for chunk in cookbook.chunks(text):
        for recipe, reason in _ask(ask, chunk):
            if recipe is None:
                rejects.append({"page": page_at(offsets, chunk.start), **reason})
                continue
            record, why = cookbook.check_record(recipe, chunk, translate=False)
            first = page_at(offsets, chunk.start)
            if record is None:
                rejects.append({"page": first, "name": recipe.get("name"), "reason": why})
                continue
            own = cookbook.recipe_text(chunk, record["name"]) or chunk.text
            begin = chunk.start + max(0, chunk.text.find(own))
            span = sorted({page_at(offsets, begin), page_at(offsets, begin + max(0, len(own.rstrip()) - 1))})
            record.pop("split", None)
            kept.append((record, list(range(span[0], span[-1] + 1))))
    return kept, rejects


def _ask(ask, chunk) -> list[tuple[dict | None, dict | None]]:
    try:
        answer = json.loads(ask(cookbook.EXTRACT_PROMPT, chunk.text))
        recipes = answer.get("recipes") if isinstance(answer, dict) else None
        if not isinstance(recipes, list):
            raise ValueError("no recipes list")
    except (ValueError, json.JSONDecodeError) as exc:
        return [(None, {"name": None, "reason": f"unreadable answer: {exc}"})]
    return [(r, None) for r in recipes if isinstance(r, dict)]


def page_url(doc: dict, pages: list[int], name: str, crowded: bool) -> str:
    """The source link of a record: the document's URL, with its first page when the document holds more than one recipe
    (a PDF's #page=N, a Library of Congress page ?sp=N), and the recipe's name when another record shares that page."""
    if doc.get("resourceUrl"):
        base = doc["resourceUrl"].split("?")[0]
        url = f"{base}?sp={pages[0]}&st=text"
        return url + "&q=" + urllib.parse.quote(name) if crowded else url
    if len(pages) and doc.get("multi"):
        url = f"{doc['url']}#page={pages[0]}"
        return url + "&search=" + urllib.parse.quote(f'"{name}"') if crowded else url
    return doc["url"]


def extract_document(doc: dict, source: Source, ask=None) -> Extraction:
    """The records of one document, each checked against its page text and given its source link."""
    result = Extraction(pages=len(doc["pages"]))
    result.needs_ocr = sum(1 for p in doc["pages"] if p["method"] == "needs-ocr")
    found: list[tuple[dict, list[int]]] = []
    if doc["layout"] in ("card", "html"):
        text = "\n\n".join(p["text"] for p in doc["pages"])
        parse = parse_card if doc["layout"] == "card" else parse_html_recipe
        record, why = parse(doc.get("title"), text)
        if record is None:
            result.unparsed.append({"doc": doc["doc"], "reason": why})
            return result
        problem = number_check(record, text)
        if problem:
            result.rejects.append({"doc": doc["doc"], "page": 1, "name": record["name"], "reason": problem})
            return result
        found.append((record, [p["n"] for p in doc["pages"] if p["text"].strip()][:1] or [1]))
    else:
        if ask is None:
            result.unparsed.append({"doc": doc["doc"], "reason": "a book needs the model host (--host, --model)"})
            return result
        kept, rejects = book_records(doc, ask, paged=doc["layout"] == "pages")
        found.extend(kept)
        result.rejects.extend({"doc": doc["doc"], **r} for r in rejects)
    doc = {**doc, "multi": doc["layout"] in ("book", "pages")}
    first_pages = Counter(pages[0] for _, pages in found)
    for record, pages in found:
        record.pop("publicDomain", None)
        record["sourceUrl"] = page_url(doc, pages, record["name"], first_pages[pages[0]] > 1)
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
    return out


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


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Hand import of the approved PDF and scanned recipe sources (see the module docstring).")
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("fetch", "links", "text", "extract", "sample", "stage"):
        p = sub.add_parser(name)
        p.add_argument("--out", required=True, help="the download folder, outside the repository")
        p.add_argument("--source", choices=None)
        p.add_argument("--books", default=BOOKS_CSV)
        p.add_argument("--sources", default=SOURCES_CSV)
        if name == "fetch":
            p.add_argument("--limit", type=int, default=0)
        if name == "extract":
            p.add_argument("--host")
            p.add_argument("--model")
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
        allowed = {host: s.min_delay for s in sources.values() for host in s.hosts}
        fetcher = Fetcher(allowed)
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
    if args.command == "text":
        if not have("pdftotext"):
            raise SystemExit("pdftotext (poppler-utils) is not installed")
        if not have("tesseract"):
            log("tesseract is not installed: pages without a text layer are marked needs-ocr")
        counts: Counter = Counter()
        for book in books:
            if book.kind == "index":
                continue
            doc = text_document(book, store)
            if doc is None:
                counts["notFetched"] += 1
                continue
            with open(os.path.join(text_dir, f"{book.doc_id}.pages.json"), "w", encoding="utf-8") as handle:
                json.dump(doc, handle, ensure_ascii=False)
            counts["documents"] += 1
            counts.update(p["method"] for p in doc["pages"])
        print(json.dumps(dict(counts), indent=2))
        return 0

    out_dir = os.path.join(args.out, "records")
    os.makedirs(out_dir, exist_ok=True)
    by_id = {b.doc_id: b for b in books}
    if args.command == "extract":
        ask = cookbook.ollama(args.host, args.model) if args.host and args.model else None
        results: dict[str, list[tuple[str, Extraction]]] = defaultdict(list)
        for book in books:
            path = os.path.join(text_dir, f"{book.doc_id}.pages.json")
            if book.kind == "index" or not os.path.exists(path):
                continue
            with open(path, encoding="utf-8") as handle:
                doc = json.load(handle)
            result = extract_document(doc, sources[book.source], ask)
            results[book.source].append((book.doc_id, result))
            write_jsonl(os.path.join(out_dir, f"{book.doc_id}.records.jsonl"), result.kept)
            write_jsonl(os.path.join(out_dir, f"{book.doc_id}.rejects.jsonl"), result.rejects + result.unparsed)
        summary = summarize(results)
        with open(os.path.join(args.out, "extract-summary.json"), "w", encoding="utf-8") as handle:
            json.dump(summary, handle, indent=2)
        print(json.dumps(summary, indent=2))
        return 0

    records = [row for book in books for row in read_jsonl(os.path.join(out_dir, f"{book.doc_id}.records.jsonl"))]
    if args.command == "sample":
        texts = {}
        for book in books:
            path = os.path.join(text_dir, f"{book.doc_id}.pages.json")
            if os.path.exists(path):
                with open(path, encoding="utf-8") as handle:
                    texts[book.doc_id] = json.load(handle)
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
