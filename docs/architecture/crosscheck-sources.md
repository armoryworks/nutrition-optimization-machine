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

### Manufacturer label pages read once by hand — 2026-11-17

Fourteen catalog foods with no USDA record took one manufacturer label each
(`--import-label-nutrition`, see `food-catalog-ingestion.md`). One product page per brand was
opened and its Nutrition Facts transcribed; nothing was crawled. `robots.txt` was read for
every host first. **None of these hosts is cleared for automated runs**: their terms of use
were not reviewed, so each needs its own determination above before a script touches it.

| Host | robots.txt for `*` | Read |
|---|---|---|
| `www.bobsredmill.com` | product pages allowed; `GPTBot`, `Google-Extended`, `PerplexityBot` barred | rolled oats, honey oat granola |
| `raos.com` | all allowed, `crawl-delay: 10` | marinara sauce |
| `filippoberio.com` | all allowed, `crawl-delay: 10` | classic balsamic glaze |
| `www.sargento.com` | product pages allowed; `Content-Signal: search=yes, ai-input=yes, ai-train=no` | shredded mozzarella |
| `smartlabel.mondelez.info` | no rules | Premium saltines |
| `www.lundberg.com` | product pages allowed | thin rice cakes (label image) |
| `shop.goya.com` | product pages allowed | black beans (label image) |
| `smartlabel.nestleusa.com` | no rules | Toll House dark chocolate morsels |
| `www.delallo.com` | product pages allowed, `crawl-delay: 10` for the AI agents it names | spaghetti, orzo (label images) |
| `www.chobani.com` | product pages allowed | whole milk plain Greek yogurt |
| `www.mediasite.lionimozzarella.com` | uploads allowed, `crawl-delay: 10` | fresh mozzarella (nutrition PDF) |
| `www.olives.com` | all allowed, `crawl-delay: 10` | Pearls Kalamata olives |

Left alone, with the reason:

- `smartlabel.pepsico.info` (Quaker): `Content-Signal: ai-input=no`.
- `www.barilla.com`, `www.dececco.com`, `www.verybestbaking.com`: answered 403 to an automated request.
- `www.ghirardelli.com`, `smartlabel.kelloggs.com`: answered 405.
- `www.mezzetta.com`: answered 429, then redirected a browser away.
- `usa.fage`: certificate chain could not be verified, so `robots.txt` was unreadable.
- Wine: winery pages state calories and carbohydrate per 5 fl oz with no gram weight, so no
  label can be rescaled to per 100 g. USDA SR Legacy 174837 ("Alcoholic beverage, wine,
  table, white") is the per-weight source: Dry White Wine (#164) takes it in place through
  `--attach-fdc` (see `food-catalog-ingestion.md`), now that the quality gate counts
  alcohol at 7 kcal/g.

### Recipe documents for the hand import — 2026-11-20

The four recipe sources the owner approved as hand sources (2026-11-21) are fetched only by
`ops/hand-import.py`, and only the documents listed in `ops/hand-import/books.csv`, from the hosts
`ops/hand-import/sources.csv` gives each source (design: `hand-import.md`). The script refuses any
other host, a redirect to one and any path robots.txt disallows, sends one request at a time and
spaces requests to a host by the larger of the delay below and robots.txt's `crawl-delay`, the hosts
of one source sharing one spacing (`www.loc.gov` and `tile.loc.gov` together stay within the Library's
10 requests a minute). A document's redirect is followed only to a host of its own source. These
hosts are cleared for that script only, not for the label cross-checker; nom-api refuses a user URL
import or bulk scrape on a hand source's host, so the scraper never fetches them. `robots.txt` was read
directly on each host on 2026-11-20; licence facts are those of the 2026-11-20 source research.
Images are never taken from any of them, with one exception: the Library's page images on
`tile.loc.gov`, read for their text only, under the determination below.

| Host | robots.txt for `*` | Delay | Licence facts | Credit line |
|---|---|---|---|---|
| `www.nutrition.va.gov` | answers 404: no rules, no crawl-delay | 5 s | A U.S. Department of Veterans Affairs work (17 U.S.C. 105). No public-domain line on the site; VA web policy (digital.va.gov web policies): "VA websites will only use original, VA-authored or co-authored articles". No partner credit in the sample; no terms on automated access. | none required; NOM credits "U.S. Department of Veterans Affairs, Nutrition and Food Services" |
| `www.nhlbi.nih.gov` | disallows admin, search, `/node/` and some query paths; the recipe pages under `/health/heart-healthy-living/healthy-foods/healthy-eating-recipes/` are allowed; no crawl-delay | 5 s | "information posted on the NHLBI website within the nhlbi.nih.gov domain is in the public domain" (trademark, branding and logo page). It asks that NHLBI's formatted products not be changed; NOM takes recipe facts and the import rules rewrite nothing it shows as NHLBI's. Some images are licensed from third parties. | none required; NOM credits "National Heart, Lung, and Blood Institute (NIH)" |
| `www.loc.gov` | disallows admin-type paths only, not `/item/` or `/resource/`; `Crawl-Delay: 5` | 6 s | Selected Digitized Books: each item's rights field reads "The books in this collection are in the public domain and are free to use and reuse." loc.gov/legal: the Library generally does not own the rights, so each item is taken only when its own rights field carries that statement and it was published in 1930 or earlier (the script checks both on the item's JSON record). loc.gov/legal also asks for no more than 10 requests a minute, hence 6 s. | "Library of Congress" with the book's title and year |
| `tile.loc.gov` | answers 404: no rules | 6 s | The Library's storage host for the OCR text (`fulltext_derivative`) and the page images (`resources[0].files`) of an item fetched from `www.loc.gov`; same item, same statement. The text file an approved item's record names is fetched; a page image only within the page-image determination of 2026-11-25 below. | as above |
| `www.cancer.gov` | `User-agent: *` with no disallow; no crawl-delay | 5 s | "all text within National Cancer Institute (NCI) products is free of copyright" and "may be reused without our permission" (copyright and reuse policy). Only graphics credited to NCI are free; no NCI logo. One document: "Down Home Healthy Cooking", NIH Publication No. 06-3408. | "Credit the National Cancer Institute as the source"; adapted text says so: "Adapted from National Cancer Institute, Down Home Healthy Cooking (NIH Publication No. 06-3408)" |

#### `tile.loc.gov` page images — determination of 2026-11-25

The owner approved the Library of Congress's public-domain cookbooks as a hand source (2026-11-21). The
Library's own OCR garbles the ingredient tables those books set with dot leaders (staging checkpoint 7,
S22: 13 of the 14 recipes read from Lizzie's cook book were lost and the one kept was garbage), so the
recipes on such pages can only be read from the page image. The lead extends the `tile.loc.gov`
determination to those page images, and only within these limits, which `ops/hand-import.py images`
enforces (each refusal is named in its report, and `ops/tests/test_hand_import_images.py` holds a test
for each):

- **Only items already taken under the Library rule.** The item's JSON record and its OCR text must have
  been fetched and verified, and the record is read again before any image: its rights field states the
  public domain ("The books in this collection are in the public domain and are free to use and
  reuse."), it is dated 1930 or earlier, and it is in English.
- **Only pages whose Library text fails the readability test**, tested again on the Library's own text
  with the word list (SCOWL's `american-english`) when the images step runs; without a word list no
  image is fetched. A page whose image was already read is never fetched again.
- **Only from `tile.loc.gov`**, the one host in the `loc` source's `image_hosts` (`ops/hand-import/sources.csv`);
  the image URL is the one the item's own record lists for that page, and a redirect is followed only
  to `tile.loc.gov`.
- **At the recorded pacing**, 6 s between requests, shared with `www.loc.gov` (the two hosts are paced as
  one, within the Library's 10 requests a minute), or the larger `crawl-delay`; **under `robots.txt` as
  read on the day**: the step reads it at the start of each run (it answered 404, no rules, on
  2026-11-20), honours any rule it then holds, and fetches nothing from the host when it cannot be read.
- **Read for its text, then deleted; never served or kept.** The image is read with tesseract inside the
  `nom-hand-import` image; once the text step has stored the page's reading (`imageText` in the page's
  text file), it deletes the image and records the deletion in `manifest.jsonl`; the stored reading stays
  on the page whatever later runs read. Part of an image is never kept: a failed download deletes it, and
  one left by a stopped run is deleted at the start of the next run. An image is never a
  recipe's image, never staged, never sent to the NOM API and never published.

Reasons: the images are scans of the same public-domain items, under the same rights statement as their
text, which is already taken; they are read for facts (amounts, foods, the method) that the text file
holds in garbled form; nothing of the scan is kept or shown; and the alternative is losing every recipe
whose table the Library's OCR garbled. Before any larger run, a one-book proof on staging
(`docs/architecture/hand-import.md`, "Page images") reads its records against the page images.

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

Both verified end to end against real data and the staging catalog.

**FDC diff:** matched 317/317 Foundation rows with zero differences (expected — same
release); with values deliberately perturbed it proposed the correct USDA numbers and
flagged a withdrawn `FdcId`.

**OFF comparison** (2,677 branded products vs the full 4.5M-row export):

| | |
|---|---|
| Matched by barcode | **2,624 / 2,677 (98%)** |
| Agreed with our values | 2,456 (**94% of matches**) |
| Disagreed | 168 → 269 flags |
| Disagreements by nutrient | calories 77, carbs 74, fat 61, protein 57 |

Every output was a `flag`, none carried a `proposed_value`, none targeted a nutrient
field, and the only source was `review:off/<date>` — the ODbL posture held in practice,
not just in tests.

**Reading the 94% agreement:** it is a genuine cross-source corroboration of the FDC
branded data we imported, which is reassuring given how noisy that dataset is in the
raw. The 168 disagreements are the queue worth a human's time — they are *not* proof
our value is wrong, since OFF is crowd-sourced; they are the shortlist to check
against the manufacturer's published label.

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
