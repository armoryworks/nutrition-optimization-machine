#!/usr/bin/env python3
"""
Cross-check NOM catalog nutrition against publicly published labels.

    catalog CSV -> find candidate pages -> extract label -> normalize by mass
                -> corroborate -> proposals CSV -> admin approves

DESIGN CONSTRAINTS (these are the point, not decoration)
--------------------------------------------------------
1. The model never invents numbers. Structured pages (schema.org
   NutritionInformation) are parsed deterministically with no model at all. For
   unstructured pages the local Ollama model acts purely as a READER, and every
   number it returns must appear **verbatim in the fetched text** or it is discarded.
2. Everything is mass-normalized to per 100 g before comparison. Labels are per
   serving; the catalog is per 100 g. See ops/nutrition_normalize.py.
3. A single source never changes a number. A numeric proposal requires >= 2
   independent sources agreeing within tolerance; it is then emitted with a
   `label:` source (authoritative per ProposalPolicy) and still needs an admin.
   One source, or sources that disagree, produces a `flag` for a human instead.
4. Crawling is polite and lawful: robots.txt is honoured per host, requests are
   rate-limited, a contact UA is sent, and only allow-listed hosts are fetched.
   Search engine result pages are NOT scraped — use a licensed search API
   (Brave / Google Programmable Search) via --search-api.

This lives in ops/ deliberately: nom-api never fetches third-party sites
(see CLAUDE.md and docs/scraper-integration.md).

USAGE
-----
    ./ops/food-catalog-crosscheck.py catalog.csv \
        --allow-host example-brand.com --allow-host anotherbrand.com \
        --out proposals.csv --limit 50

    # with the local reader for unstructured pages
    ... --ollama-url http://192.168.1.56:11434 --ollama-model qwen2.5:3b
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import urllib.robotparser
from dataclasses import dataclass
from datetime import date

sys.path.insert(0, __file__.rsplit("/", 1)[0])
from nutrition_normalize import NormalizedNutrition, agrees, normalize  # noqa: E402

USER_AGENT = "NomCatalogCrossCheck/1.0 (+https://nommeal.com/bot; contact: admin@nommeal.com)"
DEFAULT_DELAY_SECONDS = 2.0

# Nutrient keys we compare, mapped to their catalog column in the export CSV.
COMPARED = {
    "kcal": "kcal_per_100g",
    "protein_g": "protein_per_100g",
    "carb_g": "carb_per_100g",
    "fat_g": "fat_per_100g",
}


@dataclass
class SourceReading:
    url: str
    host: str
    nutrition: NormalizedNutrition


class PoliteFetcher:
    """robots.txt-respecting, rate-limited, allow-list-gated fetcher."""

    def __init__(self, allowed_hosts: set[str], delay: float = DEFAULT_DELAY_SECONDS,
                 timeout: int = 20):
        self.allowed_hosts = {h.lower().lstrip("www.") for h in allowed_hosts}
        self.delay = delay
        self.timeout = timeout
        self._robots: dict[str, urllib.robotparser.RobotFileParser | None] = {}
        self._last_hit: dict[str, float] = {}

    def _host_of(self, url: str) -> str:
        return (urllib.parse.urlparse(url).hostname or "").lower().lstrip("www.")

    def is_allowed_host(self, url: str) -> bool:
        host = self._host_of(url)
        return any(host == h or host.endswith("." + h) for h in self.allowed_hosts)

    def _robots_for(self, url: str) -> urllib.robotparser.RobotFileParser | None:
        parts = urllib.parse.urlparse(url)
        key = f"{parts.scheme}://{parts.netloc}"
        if key in self._robots:
            return self._robots[key]
        parser = urllib.robotparser.RobotFileParser()
        parser.set_url(f"{key}/robots.txt")
        try:
            parser.read()
        except Exception:
            parser = None  # unreadable robots.txt -> treat as disallowed below
        self._robots[key] = parser
        return parser

    def can_fetch(self, url: str) -> tuple[bool, str]:
        if not self.is_allowed_host(url):
            return False, "host_not_allowlisted"
        parser = self._robots_for(url)
        if parser is None:
            return False, "robots_unreadable"
        if not parser.can_fetch(USER_AGENT, url):
            return False, "robots_disallow"
        return True, ""

    def delay_for(self, url: str) -> float:
        """
        Effective politeness delay: never faster than the host's declared crawl-delay.
        Campbell's, for example, asks for 10 s — honouring that is the difference between
        a polite client and an abusive one.
        """
        parser = self._robots_for(url)
        declared = None
        if parser is not None:
            try:
                declared = parser.crawl_delay(USER_AGENT) or parser.crawl_delay("*")
            except Exception:
                declared = None
        return max(self.delay, float(declared)) if declared else self.delay

    def _throttle(self, host: str, delay: float) -> None:
        last = self._last_hit.get(host)
        if last is not None:
            wait = delay - (time.monotonic() - last)
            if wait > 0:
                time.sleep(wait)
        self._last_hit[host] = time.monotonic()

    def get(self, url: str) -> str | None:
        ok, why = self.can_fetch(url)
        if not ok:
            print(f"    skip {url}: {why}", file=sys.stderr)
            return None
        self._throttle(self._host_of(url), self.delay_for(url))
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                if resp.status != 200:
                    return None
                charset = resp.headers.get_content_charset() or "utf-8"
                return resp.read(3_000_000).decode(charset, errors="replace")
        except (urllib.error.URLError, TimeoutError, ValueError) as exc:
            print(f"    fetch failed {url}: {exc}", file=sys.stderr)
            return None


# ---------------------------------------------------------------- extraction

_JSONLD_RE = re.compile(
    r'<script[^>]+type=["\']application/ld\+json["\'][^>]*>(.*?)</script>',
    re.IGNORECASE | re.DOTALL,
)
_TAG_RE = re.compile(r"<[^>]+>")


def _walk(node) -> list[dict]:
    """Every dict inside a JSON-LD blob, including @graph and nested lists."""
    found = []
    if isinstance(node, dict):
        found.append(node)
        for value in node.values():
            found += _walk(value)
    elif isinstance(node, list):
        for item in node:
            found += _walk(item)
    return found


def extract_jsonld_nutrition(html: str) -> NormalizedNutrition | None:
    """
    Deterministic extraction of schema.org NutritionInformation — no model involved.
    This is the highest-trust path and is tried first.
    """
    for match in _JSONLD_RE.finditer(html):
        try:
            blob = json.loads(match.group(1).strip())
        except json.JSONDecodeError:
            continue
        for node in _walk(blob):
            type_ = node.get("@type")
            types = type_ if isinstance(type_, list) else [type_]
            if not any(str(t).lower() == "nutritioninformation" for t in types):
                continue
            result = normalize(
                serving_size=node.get("servingSize"),
                kcal=node.get("calories"),
                protein=node.get("proteinContent"),
                carb=node.get("carbohydrateContent"),
                fat=node.get("fatContent"),
            )
            if result:
                result.notes.append("schema.org JSON-LD (no model)")
                return result
    return None


def html_to_text(html: str) -> str:
    text = re.sub(r"<(script|style)[^>]*>.*?</\1>", " ", html, flags=re.IGNORECASE | re.DOTALL)
    return re.sub(r"\s+", " ", _TAG_RE.sub(" ", text)).strip()


READER_PROMPT = """Extract the nutrition facts panel from the text below. Reply with ONLY JSON.

{"serving_size": "<exact text>", "calories": "<exact text>", "protein": "<exact text>",
 "carbohydrate": "<exact text>", "fat": "<exact text>"}

Rules:
- Copy values EXACTLY as they appear in the text. Do not convert, round, or compute.
- If a field is not present in the text, use null. Never estimate or recall from memory.
- You are transcribing, not answering from knowledge.

Text:
"""


def extract_with_reader(text: str, ollama_url: str, model: str, timeout: int) -> NormalizedNutrition | None:
    """
    Local model used strictly as a transcriber for unstructured pages. Every number it
    returns must appear verbatim in the source text, otherwise it is discarded — that
    check is what mechanically prevents the model from supplying remembered values.
    """
    payload = json.dumps({
        "model": model,
        "prompt": READER_PROMPT + text[:6000],
        "stream": False,
    }).encode()
    req = urllib.request.Request(
        f"{ollama_url.rstrip('/')}/api/generate", data=payload,
        headers={"Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            reply = json.loads(resp.read().decode()).get("response", "")
    except Exception as exc:
        print(f"    reader failed: {exc}", file=sys.stderr)
        return None

    start, end = reply.find("{"), reply.rfind("}")
    if start < 0 or end <= start:
        return None
    try:
        fields = json.loads(reply[start : end + 1])
    except json.JSONDecodeError:
        return None

    # Verbatim guard: every number claimed must be present in the source text.
    haystack = re.sub(r"\s+", "", text.lower())
    for key, value in list(fields.items()):
        if value is None:
            continue
        for number in re.findall(r"\d+(?:[.,]\d+)?", str(value)):
            if re.sub(r"\s+", "", number.lower()) not in haystack:
                print(f"    discarded '{key}={value}' (not verbatim in source)", file=sys.stderr)
                fields[key] = None
                break

    result = normalize(
        serving_size=fields.get("serving_size"),
        kcal=fields.get("calories"),
        protein=fields.get("protein"),
        carb=fields.get("carbohydrate"),
        fat=fields.get("fat"),
    )
    if result:
        result.notes.append(f"read by {model}, verbatim-verified")
    return result


# ---------------------------------------------------------------- search

def search_urls(query: str, api: str | None, key: str | None, cx: str | None, count: int) -> list[str]:
    """
    Candidate pages via a LICENSED search API. Scraping Google/Bing result pages is a
    ToS violation and is deliberately not implemented.
    """
    if not api or not key:
        return []
    try:
        if api == "brave":
            url = "https://api.search.brave.com/res/v1/web/search?" + urllib.parse.urlencode(
                {"q": query, "count": count})
            req = urllib.request.Request(url, headers={
                "Accept": "application/json", "X-Subscription-Token": key})
            with urllib.request.urlopen(req, timeout=20) as resp:
                data = json.loads(resp.read().decode())
            return [r["url"] for r in data.get("web", {}).get("results", []) if r.get("url")]

        if api == "google":
            url = "https://www.googleapis.com/customsearch/v1?" + urllib.parse.urlencode(
                {"q": query, "key": key, "cx": cx or "", "num": min(count, 10)})
            with urllib.request.urlopen(url, timeout=20) as resp:
                data = json.loads(resp.read().decode())
            return [i["link"] for i in data.get("items", []) if i.get("link")]
    except Exception as exc:
        print(f"    search failed: {exc}", file=sys.stderr)
    return []


# ---------------------------------------------------------------- comparison

def compare(row: dict, readings: list[SourceReading]) -> list[dict]:
    """
    Turn corroborated readings into proposals. Numeric changes need >= 2 independent
    hosts agreeing; anything weaker becomes a flag for a human.
    """
    proposals: list[dict] = []
    ing_id = row.get("ingredient_id", "")
    fdc_id = row.get("fdc_id", "")
    today = date.today().isoformat()

    for field, column in COMPARED.items():
        ours_raw = row.get(column, "")
        try:
            ours = float(ours_raw) if ours_raw not in ("", None) else None
        except ValueError:
            ours = None

        values = [(r, getattr(r.nutrition, field)) for r in readings
                  if getattr(r.nutrition, field) is not None]
        if not values:
            continue

        # Independent = distinct host.
        by_host: dict[str, float] = {}
        for reading, value in values:
            by_host.setdefault(reading.host, value)

        # Corroboration: at least two hosts agreeing with each other.
        corroborated: float | None = None
        hosts_agreeing: list[str] = []
        host_items = list(by_host.items())
        for i, (host_a, val_a) in enumerate(host_items):
            agreeing = [host_a] + [h for h, v in host_items[i + 1:] if agrees(val_a, v)]
            if len(agreeing) >= 2:
                corroborated, hosts_agreeing = val_a, agreeing
                break

        if ours is not None and corroborated is not None and agrees(ours, corroborated):
            continue  # catalog matches published labels — nothing to do

        density_assumed = any(r.nutrition.assumed_density for r, _ in values)
        sources = ", ".join(sorted(by_host))

        if corroborated is not None and not density_assumed:
            # >= 2 independent labels agree and disagree with us: authoritative enough
            # to PROPOSE a numeric change — an admin still has to approve it.
            proposals.append({
                "action": "update",
                "ingredient_id": ing_id,
                "fdc_id": fdc_id,
                "field": column,
                "current_value": "" if ours is None else f"{ours:g}",
                "proposed_value": f"{corroborated:.2f}",
                "confidence": f"{min(0.5 + 0.15 * len(hosts_agreeing), 0.95):.2f}",
                "reason": (f"{len(hosts_agreeing)} published labels agree on "
                           f"{corroborated:.1f} per 100 g (mass-normalized); catalog has "
                           f"{'nothing' if ours is None else f'{ours:g}'}. Sources: {sources}"),
                "source": f"label:{today}",
            })
        else:
            why = ("only one source" if corroborated is None
                   else "serving mass inferred from volume (assumed density)")
            proposals.append({
                "action": "flag",
                "ingredient_id": ing_id,
                "fdc_id": fdc_id,
                "field": "",
                "current_value": "" if ours is None else f"{ours:g}",
                "proposed_value": "",
                "confidence": "0.30",
                "reason": (f"Published {column} may disagree with the catalog "
                           f"({', '.join(f'{v:.1f}' for v in by_host.values())} vs "
                           f"{'nothing' if ours is None else f'{ours:g}'}) — {why}. "
                           f"Needs a human. Sources: {sources}"),
                "source": f"review:crosscheck/{today}",
            })
    return proposals


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("catalog_csv", help="CSV from GET /api/FoodCatalog/export")
    ap.add_argument("--out", default="crosscheck-proposals.csv")
    ap.add_argument("--allow-host", action="append", default=[],
                    help="host permitted for fetching (repeatable). REQUIRED — nothing is "
                         "fetched from a host you have not vetted for ToS + robots.")
    ap.add_argument("--url-column", default=None,
                    help="CSV column holding a page URL to check, if you already have one")
    ap.add_argument("--search-api", choices=["brave", "google"], default=None)
    ap.add_argument("--search-key", default=None)
    ap.add_argument("--search-cx", default=None, help="Google Programmable Search engine id")
    ap.add_argument("--ollama-url", default=None, help="enable the reader for unstructured pages")
    ap.add_argument("--ollama-model", default="qwen2.5:3b")
    ap.add_argument("--limit", type=int, default=25)
    ap.add_argument("--delay", type=float, default=DEFAULT_DELAY_SECONDS)
    ap.add_argument("--timeout", type=int, default=60)
    args = ap.parse_args()

    if not args.allow_host:
        sys.exit("error: --allow-host is required. Vet each host's ToS and robots.txt first;\n"
                 "       record the determination before enabling it (see CLAUDE.md).")

    with open(args.catalog_csv, newline="", encoding="utf-8") as fh:
        rows = list(csv.DictReader(fh))[: args.limit]
    if not rows:
        sys.exit("no rows to check")

    fetcher = PoliteFetcher(set(args.allow_host), delay=args.delay)
    all_proposals: list[dict] = []

    for index, row in enumerate(rows, start=1):
        name = row.get("name", "")
        print(f"[{index}/{len(rows)}] {name[:60]}", file=sys.stderr)

        urls: list[str] = []
        if args.url_column and row.get(args.url_column):
            urls.append(row[args.url_column])
        urls += search_urls(f"{name} nutrition facts", args.search_api,
                            args.search_key, args.search_cx, count=5)
        urls = [u for u in dict.fromkeys(urls) if fetcher.is_allowed_host(u)][:4]
        if not urls:
            print("    no allow-listed candidate pages", file=sys.stderr)
            continue

        readings: list[SourceReading] = []
        for url in urls:
            html = fetcher.get(url)
            if not html:
                continue
            nutrition = extract_jsonld_nutrition(html)
            if nutrition is None and args.ollama_url:
                nutrition = extract_with_reader(html_to_text(html), args.ollama_url,
                                                args.ollama_model, args.timeout)
            if nutrition:
                host = (urllib.parse.urlparse(url).hostname or "").lower().lstrip("www.")
                readings.append(SourceReading(url=url, host=host, nutrition=nutrition))
                print(f"    read {url} -> {nutrition.kcal and f'{nutrition.kcal:.0f}'} kcal/100 g",
                      file=sys.stderr)

        all_proposals += compare(row, readings)

    fields = ["action", "ingredient_id", "fdc_id", "field", "current_value",
              "proposed_value", "confidence", "reason", "source"]
    with open(args.out, "w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(fh, fieldnames=fields)
        writer.writeheader()
        writer.writerows(all_proposals)

    updates = sum(1 for p in all_proposals if p["action"] == "update")
    print(f"\nWrote {len(all_proposals)} proposal(s) to {args.out} "
          f"({updates} corroborated numeric, {len(all_proposals) - updates} flags).",
          file=sys.stderr)
    print("Nothing is applied until an admin approves it in Admin -> Food Catalog.",
          file=sys.stderr)


if __name__ == "__main__":
    main()
