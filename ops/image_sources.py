"""
Image source adapters for the recipe image finder.

Each adapter talks to ONE official API, recorded in
docs/architecture/image-sources.md. Nothing here crawls a website or reads a
search-engine result page. Parsing is separated from fetching so it can be
tested against recorded responses without the network.

Licence policy lives here too, because it is the same question for every source:
can NOM show this picture next to a recipe, commercially, with a credit line?

  allowed   CC0, Public Domain Mark / PD, CC BY, CC BY-ND (displayed unmodified),
            and the Pexels / Pixabay / Unsplash licences.
  opt-in    CC BY-SA (--allow-licence by-sa), see the determination doc.
  never     any NC variant, GFDL, "all rights reserved", anything unrecognised.
"""

from __future__ import annotations

import html
import re
import urllib.parse
from dataclasses import dataclass, field

NEVER_ALLOWED = {"nc", "gfdl", "arr", "other", "unknown"}
DEFAULT_ALLOWED = {"cc0", "pd", "by", "by-nd", "pexels", "pixabay", "unsplash"}
OPT_IN = {"by-sa"}
LICENCE_PREFERENCE = ["cc0", "pd", "pixabay", "pexels", "unsplash", "by", "by-nd", "by-sa"]

_TAG_RE = re.compile(r"<[^>]+>")


@dataclass
class Candidate:
    source: str
    source_name: str
    source_id: str
    title: str
    description: str = ""
    tags: list[str] = field(default_factory=list)
    author: str = ""
    author_url: str = ""
    licence: str = ""
    licence_label: str = ""
    licence_url: str = ""
    landing_url: str = ""
    thumb_url: str = ""
    full_url: str = ""
    hotlink: bool = False
    download_location: str = ""
    query: str = ""
    provider: str = ""

    @property
    def key(self) -> str:
        return f"{self.source}:{self.source_id}"

    def metadata_text(self) -> str:
        return " ".join(p for p in [self.title, self.description, " ".join(self.tags)] if p)

    def as_dict(self) -> dict:
        return {k: getattr(self, k) for k in self.__dataclass_fields__} | {"key": self.key}


def strip_html(value: str | None) -> str:
    return re.sub(r"\s+", " ", html.unescape(_TAG_RE.sub(" ", value or ""))).strip()


def normalize_licence(raw: str | None) -> str:
    """
    Map any source's licence spelling to one family code: cc0, pd, by, by-sa, by-nd,
    nc (any non-commercial variant), gfdl, or other.
    """
    value = (raw or "").strip().lower().replace("_", "-").replace(" ", "-")
    if not value:
        return "unknown"
    if "nc" in value.split("-") or "noncommercial" in value or "non-commercial" in value:
        return "nc"
    if "gfdl" in value or "gnu" in value:
        return "gfdl"
    if value in {"cc0", "cc-zero"} or value.startswith("cc0") or "publicdomainzero" in value:
        return "cc0"
    if value in {"pdm", "pd", "public-domain", "publicdomain"} or value.startswith(("pd-", "pdm")) \
            or "public-domain" in value:
        return "pd"
    if re.search(r"(^|-)by-sa(-|$)", value) or value.startswith("cc-by-sa"):
        return "by-sa"
    if re.search(r"(^|-)by-nd(-|$)", value) or value.startswith("cc-by-nd"):
        return "by-nd"
    if re.search(r"(^|-)by(-|$)", value) or value.startswith("cc-by"):
        return "by"
    if value in {"pexels", "pixabay", "unsplash"}:
        return value
    return "other"


def licence_allowed(family: str, allowed: set[str]) -> bool:
    return family not in NEVER_ALLOWED and family in allowed


def resolve_allowed(extra: list[str]) -> set[str]:
    allowed = set(DEFAULT_ALLOWED)
    for item in extra:
        family = normalize_licence(item)
        if family in NEVER_ALLOWED:
            raise ValueError(f"licence '{item}' can never be allowed (see docs/architecture/image-sources.md)")
        if family not in DEFAULT_ALLOWED | OPT_IN:
            raise ValueError(f"unknown licence '{item}'")
        allowed.add(family)
    return allowed


def openverse_licence_param(allowed: set[str]) -> str:
    codes = {"cc0": ["cc0"], "pd": ["pdm"], "by": ["by"], "by-nd": ["by-nd"], "by-sa": ["by-sa"]}
    return ",".join(c for family in sorted(allowed) for c in codes.get(family, []))


def _licence_label(family: str, version: str = "") -> str:
    labels = {"cc0": "CC0", "pd": "Public Domain", "by": "CC BY", "by-nd": "CC BY-ND", "by-sa": "CC BY-SA"}
    label = labels.get(family, family)
    return f"{label} {version}".strip() if family in {"cc0", "by", "by-nd", "by-sa"} else label


def parse_wikimedia(data: dict, query: str) -> list[Candidate]:
    pages = (data.get("query") or {}).get("pages") or []
    if isinstance(pages, dict):
        pages = list(pages.values())
    results: list[Candidate] = []
    for page in pages:
        info = (page.get("imageinfo") or [{}])[0]
        meta = info.get("extmetadata") or {}

        def m(name: str) -> str:
            return str((meta.get(name) or {}).get("value") or "")

        if not str(info.get("mime", "")).startswith("image/") or info.get("mime") == "image/svg+xml":
            continue
        if strip_html(m("Restrictions")):
            continue
        family = normalize_licence(m("License") or m("LicenseShortName"))
        title = page.get("title", "")
        artist_html = m("Artist")
        author_url_match = re.search(r'href="([^"]+)"', artist_html)
        author_url = author_url_match.group(1) if author_url_match else ""
        if author_url.startswith("//"):
            author_url = "https:" + author_url
        name = re.sub(r"^File:", "", title)
        name = re.sub(r"\.(jpe?g|png|webp|tiff?|gif)$", "", name, flags=re.IGNORECASE)
        results.append(Candidate(
            source="wikimedia",
            source_name="Wikimedia Commons",
            source_id=title,
            title=name,
            description=strip_html(m("ImageDescription"))[:1000],
            tags=[t for t in strip_html(m("Categories")).split("|") if t][:30],
            author=strip_html(artist_html)[:255] or strip_html(m("Credit"))[:255],
            author_url=author_url,
            licence=family,
            licence_label=m("LicenseShortName") or _licence_label(family),
            licence_url=m("LicenseUrl"),
            landing_url=info.get("descriptionurl", ""),
            thumb_url=info.get("thumburl", ""),
            full_url="",
            hotlink=False,
            query=query,
            provider="wikimedia",
        ))
    return results


def parse_openverse(data: dict, query: str) -> list[Candidate]:
    results: list[Candidate] = []
    for item in data.get("results", []) or []:
        if item.get("mature"):
            continue
        family = normalize_licence(item.get("license"))
        version = item.get("license_version") or ""
        results.append(Candidate(
            source="openverse",
            source_name="Openverse",
            source_id=str(item.get("id", "")),
            title=item.get("title") or "",
            description="",
            tags=[t.get("name", "") for t in item.get("tags") or [] if t.get("name")][:30],
            author=item.get("creator") or "",
            author_url=item.get("creator_url") or "",
            licence=family,
            licence_label=_licence_label(family, version) if family != "pd" else "Public Domain Mark 1.0",
            licence_url=item.get("license_url") or "",
            landing_url=item.get("foreign_landing_url") or "",
            thumb_url=item.get("thumbnail") or "",
            full_url=item.get("url") or "",
            hotlink=False,
            query=query,
            provider=item.get("source") or item.get("provider") or "",
        ))
    return results


def _slug_words(url: str) -> list[str]:
    path = urllib.parse.urlparse(url).path.strip("/").split("/")
    slug = path[-1] if path else ""
    return [w for w in re.split(r"[-_]", slug) if w and not w.isdigit()]


def parse_pexels(data: dict, query: str) -> list[Candidate]:
    results: list[Candidate] = []
    for photo in data.get("photos", []) or []:
        src = photo.get("src") or {}
        results.append(Candidate(
            source="pexels",
            source_name="Pexels",
            source_id=str(photo.get("id", "")),
            title=photo.get("alt") or "",
            description="",
            tags=_slug_words(photo.get("url", "")),
            author=photo.get("photographer") or "",
            author_url=photo.get("photographer_url") or "",
            licence="pexels",
            licence_label="Pexels License",
            licence_url="https://www.pexels.com/license/",
            landing_url=photo.get("url") or "",
            thumb_url=src.get("medium") or src.get("small") or "",
            full_url=src.get("large2x") or src.get("large") or "",
            hotlink=False,
            query=query,
            provider="pexels",
        ))
    return results


def parse_pixabay(data: dict, query: str) -> list[Candidate]:
    results: list[Candidate] = []
    for hit in data.get("hits", []) or []:
        user, user_id = hit.get("user") or "", hit.get("user_id")
        results.append(Candidate(
            source="pixabay",
            source_name="Pixabay",
            source_id=str(hit.get("id", "")),
            title=" ".join(_slug_words(hit.get("pageURL", ""))),
            description="",
            tags=[t.strip() for t in (hit.get("tags") or "").split(",") if t.strip()],
            author=user,
            author_url=f"https://pixabay.com/users/{user}-{user_id}/" if user and user_id else "",
            licence="pixabay",
            licence_label="Pixabay Content License",
            licence_url="https://pixabay.com/service/license-summary/",
            landing_url=hit.get("pageURL") or "",
            thumb_url=hit.get("webformatURL") or "",
            full_url=hit.get("largeImageURL") or "",
            hotlink=False,
            query=query,
            provider="pixabay",
        ))
    return results


def with_utm(url: str, app_name: str) -> str:
    if not url:
        return url
    parts = urllib.parse.urlparse(url)
    params = urllib.parse.parse_qsl(parts.query) + [("utm_source", app_name), ("utm_medium", "referral")]
    return urllib.parse.urlunparse(parts._replace(query=urllib.parse.urlencode(params)))


def parse_unsplash(data: dict, query: str, app_name: str) -> list[Candidate]:
    """Unsplash requires hotlinking, a credit to photographer AND Unsplash, and utm links back."""
    results: list[Candidate] = []
    for photo in data.get("results", []) or []:
        urls, links, user = photo.get("urls") or {}, photo.get("links") or {}, photo.get("user") or {}
        results.append(Candidate(
            source="unsplash",
            source_name="Unsplash",
            source_id=str(photo.get("id", "")),
            title=photo.get("alt_description") or "",
            description=photo.get("description") or "",
            tags=[t.get("title", "") for t in photo.get("tags") or [] if t.get("title")],
            author=user.get("name") or "",
            author_url=with_utm((user.get("links") or {}).get("html", ""), app_name),
            licence="unsplash",
            licence_label="Unsplash License",
            licence_url=with_utm("https://unsplash.com/license", app_name),
            landing_url=with_utm(links.get("html", ""), app_name),
            thumb_url=urls.get("small") or "",
            full_url=urls.get("regular") or "",
            hotlink=True,
            download_location=links.get("download_location") or "",
            query=query,
            provider="unsplash",
        ))
    return results


def licence_rank(family: str) -> int:
    return LICENCE_PREFERENCE.index(family) if family in LICENCE_PREFERENCE else len(LICENCE_PREFERENCE)
