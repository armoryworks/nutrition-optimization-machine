#!/usr/bin/env python3
"""
Find openly licensed photos for published recipes that have none, and prove each
one is right before it goes anywhere near a recipe page.

    recipes without an image (from nom-api)
        -> query each vetted image API (Commons, Openverse, Pexels, Pixabay, Unsplash)
        -> licence filter -> download thumbnails for LOCAL analysis only
        -> triangulate: (a) metadata  (b) blind caption  (c) directed VQA  (d) second model
        -> all pass: attach via nom-api   some pass: review queue   else: nothing

DESIGN CONSTRAINTS
------------------
1. Official APIs only, each with a recorded determination in
   docs/architecture/image-sources.md. No scraping, no search-engine result pages.
   Sources without a key are skipped, not failed.
2. A wrong photo is worse than none. Auto-attach needs EVERY check to pass; the
   vision models never see each other's answers, and the caption model is not
   told the recipe. See ops/image_triangulation.py.
3. The finder never writes to the database. Everything goes through nom-api
   (/api/RecipeImages), authenticated as a curation admin (X-Api-Key or Bearer).
   nom-api never fetches a third-party URL: rehosted image bytes are uploaded by
   this script; hotlink-only sources (Unsplash) are stored as their remote URL.
4. Every candidate's scores and reasons are written to a JSONL audit log, and
   the same evidence is stored with each queued or attached candidate.

USAGE
-----
    export NOM_API=https://nom.example.com NOM_API_KEY=...         # curation admin token
    export PEXELS_API_KEY=... PIXABAY_API_KEY=...                  # optional
    ./ops/recipe-image-finder.py --ollama-url http://192.168.1.95:11434 --limit 20

    # offline / rehearsal: recipes from a file, nothing sent to nom-api
    ./ops/recipe-image-finder.py --recipes-json recipes.json --dry-run --log run.jsonl
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import io
import json
import os
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import urllib.robotparser
from datetime import datetime, timezone

sys.path.insert(0, __file__.rsplit("/", 1)[0])
import image_sources as src  # noqa: E402
import image_triangulation as tri  # noqa: E402

USER_AGENT = "NomRecipeImageFinder/1.0 (+https://nommeal.com/bot; contact: admin@nommeal.com) bot"
CACHE_TTL_SECONDS = 24 * 3600
MAX_IMAGE_BYTES = 8_000_000

API_HOSTS = {
    "commons.wikimedia.org", "api.openverse.org", "api.pexels.com", "pixabay.com", "api.unsplash.com",
}

CAPTION_PROMPT = (
    "Describe this photograph in one or two plain sentences for someone who cannot see it. "
    "Name the food or dish you see, how it is served, and the main visible components. "
    "If it is not food, say what it is instead."
)

VQA_PROMPT = """Look at the photograph and answer about it. Reply with ONLY JSON:
{{"is_photograph": "yes|no",
 "finished_dish": "yes|no",
 "raw_ingredients_only": "yes|no",
 "packaging_or_product": "yes|no",
 "people_prominent": "yes|no",
 "text_or_watermark": "yes|no",
 "collage": "yes|no",
 "dish_matches": "yes|no|unsure",
 "visible_ingredients": ["..."]}}

Definitions:
- finished_dish: a cooked or assembled dish ready to eat, plated or in its cooking vessel.
- raw_ingredients_only: only uncooked, separate ingredients are shown.
- text_or_watermark: any overlaid words, logos or watermarks (not text on a plate or label in the background).
- dish_matches: could this plausibly be "{name}"? Answer "no" if it is clearly a different dish.
- visible_ingredients: up to 8 foods you can actually see. Do not list what you merely expect.
"""

RANK_PROMPT = """You are judging whether a photograph would be a good, honest illustration for this recipe.

Recipe: {name}
{description}
Key ingredients: {ingredients}

Score 0-10: 10 = clearly this exact dish, finished and appetising; 5 = similar dish or uncertain;
0 = a different food, not food, raw ingredients, people, text or packaging.
Reply with ONLY JSON: {{"score": <0-10>, "reason": "<short>"}}"""

PAIR_PROMPT = """You are shown two photographs: Image A (the first image) and Image B (the second image).
Which one is the more accurate, honest illustration of this recipe as it would be served?

Recipe: {name}
{description}
Key ingredients: {ingredients}

Judge only how well each photo shows THIS dish, finished. Ignore photo quality unless it hides the food.
Reply with ONLY JSON: {{"better": "A" or "B" or "same", "reason": "<short>"}}"""


def log(message: str) -> None:
    print(message, file=sys.stderr)


class Http:
    """Polite JSON/bytes client: descriptive UA, per-host spacing, 24 h response cache, robots for file hosts."""

    def __init__(self, cache_dir: str | None, delay: float = 1.0, timeout: int = 30):
        self.cache_dir = cache_dir
        self.delay = delay
        self.timeout = timeout
        self._last: dict[str, float] = {}
        self._robots: dict[str, urllib.robotparser.RobotFileParser | None] = {}
        self.failures = 0
        if cache_dir:
            os.makedirs(cache_dir, exist_ok=True)

    def _throttle(self, host: str) -> None:
        last = self._last.get(host)
        if last is not None:
            wait = self.delay - (time.monotonic() - last)
            if wait > 0:
                time.sleep(wait)
        self._last[host] = time.monotonic()

    def _cache_path(self, url: str) -> str | None:
        if not self.cache_dir:
            return None
        return os.path.join(self.cache_dir, hashlib.sha256(url.encode()).hexdigest() + ".json")

    def get_json(self, url: str, headers: dict | None = None, cache: bool = True) -> dict | None:
        path = self._cache_path(url) if cache else None
        if path and os.path.exists(path) and time.time() - os.path.getmtime(path) < CACHE_TTL_SECONDS:
            with open(path, encoding="utf-8") as fh:
                return json.load(fh)
        host = urllib.parse.urlparse(url).hostname or ""
        self._throttle(host)
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/json",
                                                   **(headers or {})})
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                data = json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            self.failures += 1
            log(f"    {host}: HTTP {exc.code}")
            if exc.code == 429:
                raise RateLimited(host) from exc
            return None
        except (urllib.error.URLError, TimeoutError, ValueError) as exc:
            self.failures += 1
            log(f"    {host}: {exc}")
            return None
        if path:
            with open(path, "w", encoding="utf-8") as fh:
                json.dump(data, fh)
        return data

    def robots_allows(self, url: str) -> bool:
        parts = urllib.parse.urlparse(url)
        if (parts.hostname or "") in API_HOSTS:
            return True
        key = f"{parts.scheme}://{parts.netloc}"
        if key not in self._robots:
            parser = urllib.robotparser.RobotFileParser()
            parser.set_url(f"{key}/robots.txt")
            try:
                req = urllib.request.Request(f"{key}/robots.txt", headers={"User-Agent": USER_AGENT})
                with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                    parser.parse(resp.read().decode("utf-8", errors="replace").splitlines())
            except urllib.error.HTTPError as exc:
                if 400 <= exc.code < 500 and exc.code not in (401, 403):
                    parser.allow_all = True
                else:
                    parser = None
            except Exception:
                parser = None
            self._robots[key] = parser
        parser = self._robots[key]
        return parser is not None and parser.can_fetch(USER_AGENT, url)

    def get_bytes(self, url: str, headers: dict | None = None) -> tuple[bytes, str] | None:
        if not url.startswith("https://"):
            return None
        if not self.robots_allows(url):
            log(f"    robots.txt disallows {url}")
            return None
        host = urllib.parse.urlparse(url).hostname or ""
        self._throttle(host)
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, **(headers or {})})
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                ctype = (resp.headers.get_content_type() or "").lower()
                body = resp.read(MAX_IMAGE_BYTES + 1)
        except (urllib.error.URLError, TimeoutError, ValueError) as exc:
            log(f"    download failed {url}: {exc}")
            return None
        if len(body) > MAX_IMAGE_BYTES or not ctype.startswith("image/"):
            return None
        return body, ctype


class RateLimited(Exception):
    pass


class Source:
    key = ""
    name = ""

    def __init__(self, http: Http, allowed: set[str]):
        self.http = http
        self.allowed = allowed
        self.disabled_reason = ""

    def enabled(self) -> bool:
        return not self.disabled_reason

    def search(self, query: str, count: int) -> list[src.Candidate]:
        raise NotImplementedError

    def resolve_full_url(self, candidate: src.Candidate) -> str:
        return candidate.full_url


class WikimediaSource(Source):
    key, name = "wikimedia", "Wikimedia Commons"
    endpoint = "https://commons.wikimedia.org/w/api.php"
    fields = "ImageDescription|Artist|LicenseShortName|License|LicenseUrl|Restrictions|Categories|Credit"

    def _query(self, params: dict) -> dict | None:
        base = {"action": "query", "format": "json", "formatversion": "2", "prop": "imageinfo",
                "iiprop": "url|extmetadata|mime|size", "iiextmetadatafilter": self.fields, "maxlag": "5"}
        return self.http.get_json(self.endpoint + "?" + urllib.parse.urlencode({**base, **params}))

    def search(self, query: str, count: int) -> list[src.Candidate]:
        data = self._query({"generator": "search", "gsrnamespace": "6", "gsrlimit": str(count),
                            "gsrsearch": f"{query} filetype:bitmap", "iiurlwidth": "640"})
        return src.parse_wikimedia(data or {}, query)

    def resolve_full_url(self, candidate: src.Candidate) -> str:
        data = self._query({"titles": candidate.source_id, "iiurlwidth": "1200"})
        pages = ((data or {}).get("query") or {}).get("pages") or []
        info = (pages[0].get("imageinfo") or [{}])[0] if pages else {}
        return info.get("thumburl") or ""


class OpenverseSource(Source):
    key, name = "openverse", "Openverse"
    endpoint = "https://api.openverse.org/v1/images/"

    def __init__(self, http: Http, allowed: set[str], skip_providers: set[str]):
        super().__init__(http, allowed)
        self.skip_providers = skip_providers
        self.token = self._token()

    def _token(self) -> str | None:
        client_id, secret = os.environ.get("OPENVERSE_CLIENT_ID"), os.environ.get("OPENVERSE_CLIENT_SECRET")
        if not client_id or not secret:
            return None
        body = urllib.parse.urlencode({"grant_type": "client_credentials", "client_id": client_id,
                                       "client_secret": secret}).encode()
        req = urllib.request.Request("https://api.openverse.org/v1/auth_tokens/token/", data=body,
                                     headers={"User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                return json.loads(resp.read().decode()).get("access_token")
        except Exception as exc:
            log(f"  openverse: token request failed ({exc}); continuing anonymously")
            return None

    def search(self, query: str, count: int) -> list[src.Candidate]:
        params = {"q": query, "license": src.openverse_licence_param(self.allowed), "category": "photograph",
                  "mature": "false", "page_size": str(min(count, 20))}
        headers = {"Authorization": f"Bearer {self.token}"} if self.token else None
        data = self.http.get_json(self.endpoint + "?" + urllib.parse.urlencode(params), headers=headers)
        return [c for c in src.parse_openverse(data or {}, query) if c.provider not in self.skip_providers]


class PexelsSource(Source):
    key, name = "pexels", "Pexels"

    def __init__(self, http: Http, allowed: set[str]):
        super().__init__(http, allowed)
        self.api_key = os.environ.get("PEXELS_API_KEY", "")
        if not self.api_key:
            self.disabled_reason = "PEXELS_API_KEY not set"

    def search(self, query: str, count: int) -> list[src.Candidate]:
        url = "https://api.pexels.com/v1/search?" + urllib.parse.urlencode({"query": query, "per_page": str(count)})
        return src.parse_pexels(self.http.get_json(url, headers={"Authorization": self.api_key}) or {}, query)


class PixabaySource(Source):
    key, name = "pixabay", "Pixabay"

    def __init__(self, http: Http, allowed: set[str]):
        super().__init__(http, allowed)
        self.api_key = os.environ.get("PIXABAY_API_KEY", "")
        if not self.api_key:
            self.disabled_reason = "PIXABAY_API_KEY not set"

    def search(self, query: str, count: int) -> list[src.Candidate]:
        params = {"key": self.api_key, "q": query[:100], "image_type": "photo", "category": "food",
                  "safesearch": "true", "per_page": str(max(3, count))}
        data = self.http.get_json("https://pixabay.com/api/?" + urllib.parse.urlencode(params))
        return src.parse_pixabay(data or {}, query)


class UnsplashSource(Source):
    key, name = "unsplash", "Unsplash"

    def __init__(self, http: Http, allowed: set[str], app_name: str, ai_confirmed: bool):
        super().__init__(http, allowed)
        self.access_key = os.environ.get("UNSPLASH_ACCESS_KEY", "")
        self.app_name = app_name
        if not self.access_key:
            self.disabled_reason = "UNSPLASH_ACCESS_KEY not set"
        elif not ai_confirmed:
            self.disabled_reason = ("API Terms s.12 routes AI use of API content to unsplash.com/data; "
                                    "pass --unsplash-ai-confirmed once Unsplash has confirmed in writing")

    def headers(self) -> dict:
        return {"Authorization": f"Client-ID {self.access_key}", "Accept-Version": "v1"}

    def search(self, query: str, count: int) -> list[src.Candidate]:
        params = {"query": query, "per_page": str(count), "content_filter": "high"}
        data = self.http.get_json("https://api.unsplash.com/search/photos?" + urllib.parse.urlencode(params),
                                  headers=self.headers())
        return src.parse_unsplash(data or {}, query, self.app_name)

    def track_download(self, download_location: str) -> bool:
        if not download_location.startswith("https://api.unsplash.com/"):
            return False
        return self.http.get_json(download_location, headers=self.headers(), cache=False) is not None


class Ollama:
    def __init__(self, url: str, timeout: int, keep_alive: str, cpu_only: bool = False):
        self.url = url.rstrip("/")
        self.timeout = timeout
        self.keep_alive = keep_alive
        self.extra = {"num_gpu": 0} if cpu_only else {}
        self.failures = 0

    def missing_models(self, models: list[str]) -> list[str] | None:
        try:
            with urllib.request.urlopen(self.url + "/api/tags", timeout=15) as resp:
                names = {m.get("name", "") for m in json.loads(resp.read().decode()).get("models", [])}
        except Exception as exc:
            log(f"ollama unreachable: {exc}")
            return None
        names |= {n.removesuffix(":latest") for n in names}
        return [m for m in models if m not in names]

    def _post(self, path: str, payload: dict) -> dict | None:
        req = urllib.request.Request(self.url + path, data=json.dumps(payload).encode(),
                                     headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                return json.loads(resp.read().decode())
        except Exception as exc:
            self.failures += 1
            log(f"    ollama {path} failed: {exc}")
            return None

    def generate(self, model: str, prompt: str, image: bytes | None = None, as_json: bool = False) -> str:
        return self.generate_many(model, prompt, [image] if image is not None else [], as_json)

    def generate_many(self, model: str, prompt: str, images: list[bytes], as_json: bool = False) -> str:
        payload = {"model": model, "prompt": prompt, "stream": False, "keep_alive": self.keep_alive,
                   "options": {"temperature": 0, "seed": 7, "num_predict": 300, **self.extra}}
        if images:
            payload["images"] = [base64.b64encode(i).decode() for i in images]
        if as_json:
            payload["format"] = "json"
        data = self._post("/api/generate", payload)
        return (data or {}).get("response", "").strip()

    def embed(self, model: str, texts: list[str]) -> list[list[float]]:
        data = self._post("/api/embed", {"model": model, "input": texts, "keep_alive": self.keep_alive,
                                         "options": dict(self.extra)})
        return (data or {}).get("embeddings") or []

    def unload(self, model: str) -> None:
        self._post("/api/generate", {"model": model, "keep_alive": 0})


def parse_json_object(text: str) -> dict:
    start, end = text.find("{"), text.rfind("}")
    if start < 0 or end <= start:
        return {}
    try:
        value = json.loads(text[start:end + 1])
    except json.JSONDecodeError:
        return {}
    return value if isinstance(value, dict) else {}


def to_model_jpeg(data: bytes, ctype: str) -> tuple[bytes | None, int | None]:
    """Returns JPEG/PNG bytes the vision models accept, plus a 64-bit dHash when Pillow is available."""
    try:
        from PIL import Image
    except ImportError:
        return (data if ctype in ("image/jpeg", "image/png") else None), None
    try:
        image = Image.open(io.BytesIO(data))
        image = image.convert("RGB")
    except Exception:
        return None, None
    small = image.convert("L").resize((9, 8))
    pixels = list(small.getdata())
    bits = 0
    for row in range(8):
        for col in range(8):
            bits = (bits << 1) | (1 if pixels[row * 9 + col] > pixels[row * 9 + col + 1] else 0)
    if image.width > 768:
        image = image.resize((768, int(image.height * 768 / image.width)))
    out = io.BytesIO()
    image.save(out, format="JPEG", quality=88)
    return out.getvalue(), bits


class NomApi:
    def __init__(self, base: str, api_key: str | None, token: str | None, timeout: int = 60):
        self.base = base.rstrip("/")
        self.headers = {"Content-Type": "application/json", "Accept": "application/json"}
        if api_key:
            self.headers["X-Api-Key"] = api_key
        elif token:
            self.headers["Authorization"] = f"Bearer {token}"
        self.timeout = timeout

    def call(self, method: str, path: str, body: dict | None = None):
        req = urllib.request.Request(self.base + path, method=method, headers=self.headers,
                                     data=json.dumps(body).encode() if body is not None else None)
        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            raw = resp.read().decode()
            return json.loads(raw) if raw else None

    def missing(self, limit: int) -> list[dict]:
        return self.call("GET", f"/api/RecipeImages/missing?limit={limit}") or []

    def submit(self, payload: dict) -> dict | None:
        return self.call("POST", "/api/RecipeImages/candidates", payload)

    def pending_download_tracking(self) -> list[dict]:
        return self.call("GET", "/api/RecipeImages/download-tracking") or []

    def mark_tracked(self, candidate_id: int) -> None:
        self.call("POST", f"/api/RecipeImages/candidates/{candidate_id}/download-tracked")


def recipe_from_dict(row: dict) -> tri.RecipeText:
    return tri.RecipeText(
        recipe_id=int(row.get("id") or row.get("recipeId") or 0),
        name=row.get("name", ""),
        description=row.get("description") or "",
        dish_group=row.get("dishGroup") or "",
        ingredients=[i for i in row.get("ingredients") or [] if isinstance(i, str)],
        steps=[s for s in row.get("steps") or [] if isinstance(s, str)],
    )


class Finder:
    def __init__(self, args, sources: list[Source], ollama: Ollama, thresholds: tri.Thresholds, http: Http):
        self.args = args
        self.sources = sources
        self.ollama = ollama
        self.t = thresholds
        self.http = http

    def embed_one(self, text: str, prefix: str) -> list[float] | None:
        vectors = self.ollama.embed(self.args.embed_model, [f"{prefix}: {text}"])
        return vectors[0] if vectors else None

    def gather(self, recipe: tri.RecipeText, rejected: set[str]) -> list[src.Candidate]:
        found: dict[str, src.Candidate] = {}
        for query in tri.build_queries(recipe, self.args.queries_per_recipe):
            for source in self.sources:
                if not source.enabled():
                    continue
                try:
                    results = source.search(query, self.args.per_source)
                except RateLimited:
                    source.disabled_reason = "rate limited (HTTP 429) — stopped for this run"
                    log(f"    {source.name}: rate limited; disabled for the rest of the run")
                    continue
                for c in results:
                    if c.key in rejected or c.key in found:
                        continue
                    if not src.licence_allowed(c.licence, source.allowed):
                        continue
                    if not c.thumb_url:
                        continue
                    found[c.key] = c
        return list(found.values())

    def analyse(self, recipe: tri.RecipeText, candidates: list[src.Candidate]) -> tuple[dict, dict, dict]:
        doc_name = self.embed_one(recipe.name + (f" ({recipe.dish_group})" if recipe.dish_group else ""), "search_document")
        doc_desc = self.embed_one(f"{recipe.name}. {recipe.description}", "search_document") if recipe.description else None
        doc_steps = self.embed_one(tri.recipe_document(recipe), "search_document")

        scored = []
        for c in candidates:
            sim = tri.cosine(self.embed_one(c.metadata_text()[:1500], "search_query"), doc_desc or doc_name)
            scored.append((tri.metadata_check(recipe, c.metadata_text(), sim, self.t), c))
        scored.sort(key=lambda pair: (pair[0].score, -src.licence_rank(pair[1].licence)), reverse=True)
        shortlist = scored[: self.args.max_vision]

        evidence: dict[str, tri.CandidateEvidence] = {}
        images: dict[str, bytes] = {}
        hashes: dict[str, int | None] = {}
        by_key: dict[str, src.Candidate] = {}
        for meta, c in shortlist:
            fetched = self.http.get_bytes(c.thumb_url)
            if not fetched:
                continue
            jpeg, dhash = to_model_jpeg(*fetched)
            if not jpeg:
                continue
            images[c.key], hashes[c.key], by_key[c.key] = jpeg, dhash, c
            evidence[c.key] = tri.CandidateEvidence(key=c.key, metadata=meta)

        preference = sorted(images, key=lambda k: (src.licence_rank(by_key[k].licence), -evidence[k].metadata.score))
        for dup, original in tri.mark_duplicates(hashes, preference).items():
            evidence[dup].duplicate_of = original

        log(f"    {len(images)} thumbnail(s) to analyse with {self.args.vision_model} and {self.args.rank_model}")
        for index, (key, image) in enumerate(images.items(), start=1):
            started = time.monotonic()
            caption = self.ollama.generate(self.args.vision_model, CAPTION_PROMPT, image)
            cap_vec = self.embed_one(caption, "search_query") if caption else None
            sims = {"name": tri.cosine(cap_vec, doc_name), "instructions": tri.cosine(cap_vec, doc_steps)}
            if doc_desc:
                sims["description"] = tri.cosine(cap_vec, doc_desc)
            evidence[key].caption = tri.caption_check(caption, sims, self.t)

            answers = parse_json_object(self.ollama.generate(
                self.args.vision_model, VQA_PROMPT.format(name=recipe.name), image, as_json=True))
            vqa, content_ok, content_reasons = tri.vqa_check(recipe, answers, self.t)
            evidence[key].vqa, evidence[key].content_ok, evidence[key].content_reasons = vqa, content_ok, content_reasons
            log(f"      [{index}/{len(images)}] {key[:70]}: caption+vqa {time.monotonic() - started:.0f} s")

        rank_prompt = RANK_PROMPT.format(name=recipe.name, description=(recipe.description or "")[:400],
                                         ingredients=", ".join(tri.ingredient_heads(recipe.ingredients)[:8]) or "n/a")
        for key, image in images.items():
            reply = parse_json_object(self.ollama.generate(self.args.rank_model, rank_prompt, image, as_json=True))
            try:
                evidence[key].rank_score = max(0.0, min(10.0, float(reply.get("score"))))
            except (TypeError, ValueError):
                evidence[key].rank_score = None
            evidence[key].vqa.detail["rank_reason"] = str(reply.get("reason", ""))[:300] if evidence[key].vqa else ""

        pair_prompt = PAIR_PROMPT.format(name=recipe.name, description=(recipe.description or "")[:400],
                                         ingredients=", ".join(tri.ingredient_heads(recipe.ingredients)[:8]) or "n/a")
        pairs = tri.round_robin(evidence)
        for first, second in pairs:
            for a, b in ((first, second), (second, first)):
                reply = parse_json_object(self.ollama.generate_many(
                    self.args.rank_model, pair_prompt, [images[a], images[b]], as_json=True))
                tri.record_vote(evidence, a, b, str(reply.get("better", "")))
        if pairs:
            log(f"      head-to-head: {len(pairs)} pair(s) x 2 orders; "
                + ", ".join(f"{k[:40]} {sum(sum(v) for v in evidence[k].pairwise.values())}"
                            for k in tri.eligible(evidence)))

        return evidence, by_key, images

    def full_image(self, candidate: src.Candidate) -> tuple[bytes, str] | None:
        source = next((s for s in self.sources if s.key == candidate.source), None)
        url = source.resolve_full_url(candidate) if source else candidate.full_url
        return self.http.get_bytes(url) if url else None


def candidate_payload(c: src.Candidate, decision: tri.Decision, status: str, image: tuple[bytes, str] | None) -> dict:
    payload = {
        "sourceKey": c.source, "sourceName": c.source_name, "sourceId": c.source_id[:255],
        "title": c.title[:511], "author": c.author[:255], "authorUrl": c.author_url[:1023],
        "license": c.licence_label[:64], "licenseCode": c.licence, "licenseUrl": c.licence_url[:1023],
        "landingUrl": c.landing_url[:1023], "imageUrl": c.full_url[:2047], "hotlink": c.hotlink,
        "downloadLocation": c.download_location[:2047] or None,
        "score": decision.per_candidate.get(c.key, {}).get("combined"),
        "checks": decision.per_candidate.get(c.key, {}), "status": status,
    }
    if image is not None:
        payload["imageBase64"] = base64.b64encode(image[0]).decode()
        payload["contentType"] = image[1]
    return payload


def load_state(path: str | None) -> dict:
    if path and os.path.exists(path):
        with open(path, encoding="utf-8") as fh:
            return json.load(fh)
    return {}


def save_state(path: str | None, state: dict) -> None:
    if path:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(state, fh, indent=1, sort_keys=True)


def build_sources(args, http: Http, allowed: set[str]) -> list[Source]:
    wanted = set(args.source or ["wikimedia", "openverse", "pexels", "pixabay", "unsplash"])
    sources: list[Source] = []
    if "wikimedia" in wanted:
        sources.append(WikimediaSource(http, allowed))
    if "openverse" in wanted:
        sources.append(OpenverseSource(http, allowed, {"wikimedia"} if "wikimedia" in wanted else set()))
    if "pexels" in wanted:
        sources.append(PexelsSource(http, allowed))
    if "pixabay" in wanted:
        sources.append(PixabaySource(http, allowed))
    if "unsplash" in wanted:
        sources.append(UnsplashSource(http, allowed, args.unsplash_app_name, args.unsplash_ai_confirmed))
    return sources


def track_unsplash_downloads(api: NomApi, sources: list[Source], dry_run: bool) -> None:
    unsplash = next((s for s in sources if isinstance(s, UnsplashSource) and s.access_key), None)
    if not unsplash:
        return
    for row in api.pending_download_tracking():
        if dry_run:
            log(f"  would track Unsplash download for candidate {row.get('id')}")
            continue
        if unsplash.track_download(row.get("downloadLocation", "")):
            api.mark_tracked(int(row["id"]))


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--api", default=os.environ.get("NOM_API"), help="nom-api base URL (env NOM_API)")
    ap.add_argument("--recipes-json", help="read recipes from this file instead of nom-api (implies nothing is sent)")
    ap.add_argument("--dry-run", action="store_true", help="analyse and log, but send nothing to nom-api")
    ap.add_argument("--limit", type=int, default=20)
    ap.add_argument("--source", action="append", choices=["wikimedia", "openverse", "pexels", "pixabay", "unsplash"])
    ap.add_argument("--allow-licence", action="append", default=[],
                    help="opt in to an extra licence family (only 'by-sa' is accepted)")
    ap.add_argument("--unsplash-ai-confirmed", action="store_true",
                    default=os.environ.get("NOM_UNSPLASH_AI_CONFIRMED") == "1")
    ap.add_argument("--unsplash-app-name", default=os.environ.get("NOM_UNSPLASH_APP_NAME", "nom"))
    ap.add_argument("--ollama-url", default=os.environ.get("NOM_OLLAMA_URL", "http://192.168.1.95:11434"))
    ap.add_argument("--vision-model", default="qwen2.5vl:7b")
    ap.add_argument("--rank-model", default="gemma3:4b")
    ap.add_argument("--embed-model", default="nomic-embed-text")
    ap.add_argument("--keep-alive", default="5m")
    ap.add_argument("--ollama-cpu", action="store_true",
                    help="run the models on the Ollama host's CPU (num_gpu=0) so a busy GPU is left alone")
    ap.add_argument("--no-unload", action="store_true", help="leave the models loaded after the run")
    ap.add_argument("--queries-per-recipe", type=int, default=2)
    ap.add_argument("--per-source", type=int, default=8)
    ap.add_argument("--max-vision", type=int, default=6, help="candidates per recipe sent to the vision models")
    ap.add_argument("--max-review", type=int, default=3)
    ap.add_argument("--cache-dir", default=os.path.join(tempfile.gettempdir(), "nom-image-finder-cache"))
    ap.add_argument("--state", default="recipe-image-finder.state.json")
    ap.add_argument("--retry-days", type=int, default=30, help="skip recipes that found nothing this recently")
    ap.add_argument("--log", default="recipe-image-finder.jsonl")
    ap.add_argument("--timeout", type=int, default=180)
    ap.add_argument("--max-errors", type=int, default=2, help="stop after this many recipes in a row hit model failures")
    for name, default in vars(tri.Thresholds()).items():
        ap.add_argument("--" + name.replace("_", "-"), type=type(default), default=default, dest=f"t_{name}")
    args = ap.parse_args()

    thresholds = tri.Thresholds(**{k: getattr(args, f"t_{k}") for k in vars(tri.Thresholds())})
    try:
        allowed = src.resolve_allowed(args.allow_licence)
    except ValueError as exc:
        sys.exit(f"error: {exc}")

    api = None
    if args.recipes_json:
        with open(args.recipes_json, encoding="utf-8") as fh:
            rows = json.load(fh)
        args.dry_run = True
    else:
        if not args.api:
            sys.exit("error: --api (or NOM_API) is required unless --recipes-json is given")
        key, token = os.environ.get("NOM_API_KEY"), os.environ.get("NOM_TOKEN")
        if not key and not token:
            sys.exit("error: set NOM_API_KEY (X-Api-Key of a curation admin) or NOM_TOKEN")
        api = NomApi(args.api, key, token)
        rows = api.missing(args.limit)

    http = Http(args.cache_dir)
    sources = build_sources(args, http, allowed)
    for s in sources:
        log(f"source {s.name}: {'enabled' if s.enabled() else 'skipped — ' + s.disabled_reason}")
    if not any(s.enabled() for s in sources):
        sys.exit("error: no image source is enabled")
    if api:
        track_unsplash_downloads(api, sources, args.dry_run)

    ollama = Ollama(args.ollama_url, args.timeout, args.keep_alive, args.ollama_cpu)
    missing = ollama.missing_models([args.vision_model, args.rank_model, args.embed_model])
    if missing is None:
        sys.exit(f"error: Ollama at {args.ollama_url} is not reachable")
    if missing:
        sys.exit(f"error: pull these models on the Ollama host first: {', '.join(missing)}")
    finder = Finder(args, sources, ollama, thresholds, http)
    state = load_state(args.state)
    now = datetime.now(timezone.utc)
    totals = {"attach": 0, "review": 0, "none": 0, "skipped": 0, "error": 0}
    consecutive_errors = 0

    with open(args.log, "a", encoding="utf-8") as audit:
        for index, row in enumerate(rows[: args.limit], start=1):
            recipe = recipe_from_dict(row)
            last = state.get(str(recipe.recipe_id))
            if last and (now - datetime.fromisoformat(last)).days < args.retry_days:
                totals["skipped"] += 1
                continue
            log(f"[{index}/{min(len(rows), args.limit)}] #{recipe.recipe_id} {recipe.name}")
            search_failures = http.failures
            candidates = finder.gather(recipe, set(row.get("rejectedCandidates") or []))
            log(f"    {len(candidates)} licensed candidate(s)")
            searches_failed = http.failures > search_failures
            failures_before = ollama.failures
            evidence, by_key, _ = finder.analyse(recipe, candidates) if candidates else ({}, {}, {})
            if ollama.failures > failures_before:
                totals["error"] += 1
                consecutive_errors += 1
                log(f"    -> error: {ollama.failures - failures_before} model call(s) failed; nothing decided, retried next run")
                if consecutive_errors >= args.max_errors:
                    log("    stopping: the Ollama host keeps failing (busy GPU? try --ollama-cpu or run off-hours)")
                    break
                continue
            consecutive_errors = 0
            decision = tri.decide(evidence, thresholds, args.max_review)
            totals[decision.outcome] += 1
            log(f"    -> {decision.outcome}: {'; '.join(decision.reasons)}")

            audit.write(json.dumps({
                "at": now.isoformat(), "recipe_id": recipe.recipe_id, "recipe": recipe.name,
                "outcome": decision.outcome, "chosen": decision.chosen, "review": decision.review,
                "reasons": decision.reasons, "thresholds": vars(thresholds),
                "models": {"vision": args.vision_model, "rank": args.rank_model, "embed": args.embed_model},
                "candidates": [{**c.as_dict(), "evidence": decision.per_candidate.get(c.key)}
                               for c in candidates if c.key in decision.per_candidate],
                "unanalysed": [c.key for c in candidates if c.key not in decision.per_candidate],
            }) + "\n")

            if decision.outcome == "none":
                if not searches_failed:
                    state[str(recipe.recipe_id)] = now.isoformat()
                continue
            if args.dry_run or api is None:
                continue

            keys = [decision.chosen] if decision.outcome == "attach" else decision.review
            status = "attached" if decision.outcome == "attach" else "pending"
            payloads = []
            for key in keys:
                c = by_key[key]
                image = None if c.hotlink else finder.full_image(c)
                if not c.hotlink and image is None:
                    log(f"    could not fetch the full image for {key}; skipped")
                    continue
                payloads.append(candidate_payload(c, decision, status, image))
            if not payloads:
                continue
            try:
                result = api.submit({"recipeId": recipe.recipe_id, "batch": f"image-finder-{now:%Y%m%d}",
                                     "candidates": payloads})
                log(f"    submitted: {result}")
            except urllib.error.HTTPError as exc:
                log(f"    nom-api rejected the submission: HTTP {exc.code} {exc.read()[:300]!r}")
            if decision.outcome == "attach" and api:
                track_unsplash_downloads(api, sources, False)

    save_state(args.state, state)
    if not args.no_unload:
        for model in {args.vision_model, args.rank_model, args.embed_model}:
            ollama.unload(model)
    log(f"\nDone: {totals['attach']} attached, {totals['review']} queued for review, "
        f"{totals['none']} with no usable image, {totals['error']} errored, {totals['skipped']} skipped (tried recently). "
        f"Log: {args.log}")


if __name__ == "__main__":
    main()
