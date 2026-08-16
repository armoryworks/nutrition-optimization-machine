# Cross-check sources — vetting record

Which hosts the label cross-checker (`ops/food-catalog-crosscheck.py`) may touch, and
why. **No host is enabled until it appears here with a determination.** The script
refuses to run without an explicit `--allow-host`, so this file is the gate.

Reviewed 2026-08-15. `robots.txt` was read directly for every host below.
Re-check before any large run; these change.

## What actually needs cross-checking

Not everything. **USDA Foundation Foods are lab-analysed reference data — a brand
website is not a better authority than USDA**, so cross-checking them against the web
is pointless. The cross-check exists for **branded products**, where the
manufacturer's own published label *is* the authority.

A caution about scale, measured from the 2025-12-18 branded dataset:

| | |
|---|---|
| Distinct brand owners | **36,743** |
| Top 10 by product count | Walmart, Target, General Mills, Meijer, Safeway, Topco, Wegmans, Kroger, Hy-Vee, Supervalu |

Eight of the top ten are **retailers**, i.e. private-label products whose "manufacturer"
has no product page to check — and whose own sites forbid scraping. **Per-manufacturer
crawling therefore covers only a sliver of the catalog.** Broad coverage has to come
from an aggregate database; manufacturer sites are a high-value supplement for major
brands, not a general solution.

## Determinations

### ✅ USDA FoodData Central — `fdc.nal.usda.gov`
- **robots.txt**: `User-agent: * / Disallow:` — everything permitted.
- **Licence**: CC0 1.0 (public domain). Commercial use fine; citation requested.
- **Use**: freshness diffing. Re-download the current release and compare by `FdcId`
  to catch values USDA has revised or withdrawn. This — not a language model — is the
  real answer to "does our data match current values".
- **Note**: the live API (`api.nal.usda.gov`) needs a free key; bulk downloads do not.

### ⚠️ Open Food Facts — `world.openfoodfacts.org` — **bulk export only, do not crawl**
- **robots.txt**: `Disallow: /api`, `/cgi`, `/facets` for `*`. Product pages are not
  disallowed, but see below.
- **Their stated rule**: *"You are very welcome to use the API for production cases,
  as long as 1 API call = 1 real scan by a user."* Batch-checking our catalog is
  explicitly **not** that, and they discourage API scraping when full exports exist.
- **Determination**: **use the nightly bulk export** (CSV/JSONL/Parquet), never the API
  or the crawler, for batch work. That honours their request, their robots.txt, and
  puts zero load on them. Reserve API calls for genuine single-product user actions
  (e.g. a UPC scan).
- **Licence caution**: database is **ODbL** (share-alike), contents DbCL, images
  CC-BY-SA. Incorporating their values into our database plausibly triggers
  share-alike. **Recommended posture: use OFF as a *verification signal only*** —
  compare, store agree/disagree plus a flag, never copy their numbers into the
  catalog. Worth an attorney's eye before shipping.
- **Why it matters anyway**: it is the only source with broad UPC coverage, which is
  exactly what the 36,743-brand-owner problem needs.

### ✅ Kraft Heinz — `www.kraftheinz.com`
- **robots.txt**: `Allow: /` for `*`, and explicitly `User-agent: ClaudeBot Allow: /`,
  `Claude-User Allow: /`, `Claude-SearchBot`, plus `GPTBot`/`OAI-SearchBot`.
- **Content-Signal**: `search=yes, ai-train=yes, ai-input=yes`.
- **Determination**: **permitted**. This host explicitly welcomes automated agents.

### ✅ Nestlé USA — `www.nestleusa.com`
- **Content-Signal**: `ai-train=no, search=yes, ai-input=yes`.
- **Determination**: **permitted for this use.** We read a page to extract a published
  fact — that is `ai-input`, which they allow. We do **not** train on their content,
  which they forbid. Do not use their pages for any training corpus.

### ✅ Campbell's — `www.campbells.com` — honour `crawl-delay: 10`
- **robots.txt**: allows everything except `/wp-admin/`; declares **`crawl-delay: 10`**.
- **Determination**: **permitted at ≤ 1 request / 10 s.** The fetcher reads the declared
  crawl-delay and never goes faster (`PoliteFetcher.delay_for`).

### ✅ General Mills — `www.generalmills.com`
- **robots.txt**: disallows `/search`, `/sitecore/`, `/shared/`, `/user-profile/`,
  `/*.aspx`, `/privacy-security/`; product content otherwise allowed. No crawl-delay.
- **Determination**: **permitted**, excluding the disallowed paths (the fetcher enforces
  this per-URL, not just per-host).

### ❌ Retailers — Walmart, Target, Kroger, and grocery chains generally
- Kroger refused the connection outright (bot protection). Walmart and Target publish
  long disallow lists and operate anti-bot systems; their terms prohibit automated
  collection.
- **Determination: excluded.** Also low value: their products are private-label, and a
  retailer listing is not the manufacturer's authoritative label.

### ❌ Search engine result pages — Google, Bing
- **Determination: never scraped.** Their terms forbid it and `robots.txt` disallows
  `/search`. Use a licensed API instead — `--search-api brave|google` (Brave Search API
  or Google Programmable Search JSON API, both with free tiers).

## Recommended configuration

```bash
./ops/food-catalog-crosscheck.py catalog.csv \
    --allow-host kraftheinz.com \
    --allow-host nestleusa.com \
    --allow-host campbells.com \
    --allow-host generalmills.com \
    --ollama-url http://192.168.1.56:11434 \
    --limit 50 --out proposals.csv
```

Remember the corroboration rule: with only these four hosts, most products will have a
single source at best, which can only ever raise a **flag**. A numeric proposal needs
two independent hosts agreeing — which in practice means pairing a manufacturer page
with the Open Food Facts export.

## Built checkers

| Tool | Source | Can it change a number? |
|---|---|---|
| `ops/food-catalog-fdc-diff.py` | current USDA release | **Yes** — proposes `update` with an `fdc:` source (USDA is the origin of the record; a difference means our copy is stale). Withdrawn records are flagged, never auto-deleted. |
| `ops/food-catalog-off-compare.py` | OFF nightly bulk export | **No** — flags only. OFF values never appear in `proposed_value` and never enter the catalog; disagreements are expressed as a percentage gap so no OFF datum is copied. |
| `ops/food-catalog-crosscheck.py` | allow-listed manufacturer pages | Only with **≥ 2 independent hosts agreeing** (`label:` source); otherwise flags. |

Verified end to end against the staging catalog: the FDC diff matched 317/317 Foundation
rows with zero differences (expected — same release), and with values deliberately
perturbed it proposed the correct USDA numbers and flagged a withdrawn `FdcId`.

## Open items

- **Attorney confirmation of the ODbL posture** before the OFF comparator is used on
  anything that ships. The code is built to need only a signal, but the call is legal.
- **Licensed search API key** (Brave or Google PSE) is not configured, so candidate-page
  discovery currently requires a URL column in the export.
- **Fetching the OFF export: use `wget`, not `curl`.** Three `curl` attempts from the
  dev box truncated (632 MB / 1017 MB / 463 MB) and still exited 0 — the stream ends
  early and curl reports success, so *always* verify with `gzip -t`. `wget -c` on
  Server-2 pulled the full 1,275,171,186 bytes cleanly:

  ```bash
  ssh daniel@192.168.1.56 'cd ~/off && nohup wget -c --tries=20 --waitretry=10 \
      --read-timeout=60 -O off-products.csv.gz \
      "https://static.openfoodfacts.org/data/en.openfoodfacts.org.products.csv.gz" \
      > wget.log 2>&1 < /dev/null &'
  gzip -t off-products.csv.gz   # must pass before the comparator is trusted
  ```
- Manufacturer product pages rarely expose schema.org `NutritionInformation`; expect the
  local reader (with verbatim verification) to do most of the work, and expect partial
  yield.
