# Recipe image sources — vetting record

Which image APIs the recipe image finder (`ops/recipe-image-finder.py`) may query, under
which licences, and how each source's photos may be stored and shown. **No source is
used until it appears here with a determination.** The finder only knows the five
adapters below; adding a sixth means adding a section here first.

Reviewed 2026-10-09. Terms pages and `robots.txt` were read directly for every host,
and one live API call per keyless source was made to confirm field names and rate-limit
headers. Re-check before any large run; these change.

## Ground rules (all sources)

- **Official APIs only.** Nothing is crawled, no web page is parsed, no search-engine
  result page is touched. Each source's documented search endpoint is the only entry
  point; image files are fetched only from URLs those APIs return.
- **`robots.txt` and API hosts.** RFC 9309 robots rules govern *crawlers*. The finder
  does not crawl: it calls documented APIs under their own terms, which are what we
  honour (rate limits, attribution, hotlinking). Several API hosts disallow `/api/` or
  `/w/` for `User-agent: *` to keep search-engine crawlers out of dynamic endpoints
  (recorded per source below). **Every non-API file host is checked against its
  `robots.txt` before a download** (`Http.robots_allows`); an unreadable `robots.txt`
  (network error, 401/403) means *no download*.
- **Identification.** Every request carries
  `NomRecipeImageFinder/1.0 (+https://nommeal.com/bot; contact: admin@nommeal.com) bot`,
  per the Wikimedia User-Agent policy. Requests are serial, at least 1 s apart per host,
  and API responses are cached for 24 h (Pixabay requires this; it also spares everyone
  else's quota).
- **Keys come from the environment.** A source without its key is skipped with a message,
  never failed. No key is ever sent to nom-api or stored in the database.
- **nom-api never fetches a third-party URL** (CLAUDE.md). The finder uploads the bytes
  of rehosted photos; hotlinked photos are stored as their remote URL.
- **Local analysis only.** Thumbnails are downloaded to memory for the local vision
  models, never written to disk, never retained after the run, never used for training.

## Licence determinations

NOM is operated by Armory Works Technology, LLC as a hosted product (Apache-2.0 code,
but the *service* is run by a company). Any reuse must therefore be lawful as
**commercial** use, and the image is shown next to our recipe text with a credit line.

| Licence family | Determination | Why |
|---|---|---|
| CC0, Public Domain Mark, PD-* | ✅ allowed | No conditions. We still show a credit line as a courtesy. |
| CC BY (any version) | ✅ allowed | Commercial use permitted with attribution; we store creator, source link, licence name and licence link, and show them under the photo. |
| CC BY-ND | ✅ allowed, **displayed unmodified** | Copying and display are permitted; adaptation is not. We only resize (a technical format change CC 4.0 §2(a)(4) expressly permits; for 2.0/3.0 a pure scale is not an adaptation either) and the recipe page renders ND photos with `object-fit: contain`, **never cropped** (`ImageCredit.noDerivatives`). |
| CC BY-SA | ⚠️ **excluded by default**, opt-in `--allow-licence by-sa` | Displaying an unmodified BY-SA photo is permitted, but the hero image is cropped to a banner on most screens, and a cropped photo is plausibly an *adaptation* that must itself be released BY-SA. It also brings share-alike obligations into a commercial product next to proprietary text — the same posture this repo took for ODbL (Open Food Facts). Wikimedia Commons food photos are heavily BY-SA, so this exclusion costs real yield; it is a deliberate, reversible choice that wants an attorney's sign-off before it is flipped. If enabled, BY-SA should also be rendered uncropped. |
| Any **NC** variant | ❌ **never** (refused by the finder *and* by nom-api) | The service is commercial. Even if no fee is charged for a given recipe page, NC licensors read "commercial" broadly (ads, subscriptions, a company operating the site). Not worth the dispute. |
| GFDL | ❌ never | Requires distributing the full licence text with each copy; impractical for a recipe card. Dual-licensed GFDL/CC BY files are taken under their CC licence. |
| "All rights reserved", unknown, unparseable | ❌ never | |
| Pexels License | ✅ allowed | Free commercial use; attribution optional but requested by the API terms (we always credit). No standalone redistribution — a photo illustrating a recipe page is not standalone. |
| Pixabay Content License | ✅ allowed | Same shape as Pexels. No standalone redistribution; no use implying endorsement; recognisable people/brands are the user's responsibility (the content gate rejects people-prominent photos anyway). |
| Unsplash License | ✅ allowed (source itself is gated, see below) | Free commercial use; the API terms add hotlinking and attribution. |

Commons files with any `Restrictions` value (personality rights, trademarks, insignia)
are dropped regardless of licence. Openverse `mature` results are dropped.

## Source determinations

### ✅ Wikimedia Commons — `commons.wikimedia.org/w/api.php`
- **Endpoint**: `action=query&generator=search&gsrnamespace=6&prop=imageinfo&iiprop=url|extmetadata|mime|size&iiurlwidth=640`
  with `gsrsearch="<dish> filetype:bitmap"` and `maxlag=5`. `extmetadata` carries
  `License`, `LicenseShortName`, `LicenseUrl`, `Artist`, `ImageDescription`,
  `Categories`, `Restrictions`.
- **Key**: none.
- **Rate**: no hard read limit; serial requests, `maxlag`, descriptive UA (API:Etiquette).
- **robots.txt**: `commons.wikimedia.org` disallows `/w/` and `/api/` for `*` (crawler
  rule — see ground rules; the API is documented for bots). `upload.wikimedia.org`
  disallows only `/wikipedia/commons/archive/`; the thumbnails we fetch are allowed.
- **Storage**: **rehost**. Licences permit copying; rehosting keeps reader IPs away
  from a third party and survives file renames. The finder requests a 1200 px
  thumbnail (`iiurlwidth=1200`) at attach time and uploads it; nom-api re-encodes it
  to JPEG ≤ 1200 px and strips metadata.
- **Attribution**: creator (`Artist`, HTML stripped, user link kept), file page link,
  licence short name + URL.

### ✅ Openverse — `api.openverse.org/v1/images/`
- **Endpoint**: `?q=…&license=cc0,pdm,by,by-nd&category=photograph&mature=false&page_size=20`.
  The licence filter is built from the allowed set, so NC/SA results are never even returned.
- **Key**: optional. Anonymous is enough for small runs; register an OAuth app and set
  `OPENVERSE_CLIENT_ID` / `OPENVERSE_CLIENT_SECRET` for higher limits (client-credentials
  token, sent as `Bearer`).
- **Rate (measured, anonymous)**: `20/min` burst, `200/day` sustained
  (`x-ratelimit-*` headers). A 429 disables the source for the rest of the run.
- **robots.txt**: `Disallow: /v1/images/` for `*` under a "Block API endpoints" comment —
  aimed at crawlers; the same paths are the documented API. Our UA is not one of the AI
  crawler agents they block by name, and we are not collecting a corpus.
- **Licence accuracy**: Openverse states it cannot guarantee licence metadata ("verify a
  work's license"). It reports the provider's own metadata (e.g. Flickr's licence field);
  we store it with the provider and landing page so a complaint can be traced. Results
  whose provider is `wikimedia` are skipped when the Commons adapter is enabled (same
  files, better metadata there).
- **Storage**: **rehost**. Analysis uses Openverse's own thumbnail proxy (an API URL); the
  full image is downloaded from the provider URL Openverse returns (e.g.
  `live.staticflickr.com`, which has no `robots.txt` → allowed) after a robots check.
- **Attribution**: `creator` + `creator_url`, `foreign_landing_url`, licence label from
  `license` + `license_version`, `license_url`.

### ✅ Pexels — `api.pexels.com/v1/search` — key required
- **Endpoint**: `?query=…&per_page=8`, header `Authorization: <key>`.
- **Key**: `PEXELS_API_KEY` (free, from a Pexels account). Skipped when unset.
- **Rate**: 200 requests/hour, 20,000/month by default (`X-Ratelimit-*` headers).
- **robots.txt**: `api.pexels.com` disallows `/` for `*` (crawler rule; API host).
  `images.pexels.com` allows everything.
- **Terms**: show a prominent link to Pexels and credit the photographer ("Photo by X on
  Pexels" linking to the photo page) — we do both. Do not replicate Pexels' core
  functionality (we don't). Terms §8 prohibit scraping/data-mining "for unauthorised
  purposes, including machine learning" — we use the authorised API, analyse a thumbnail
  transiently to *verify* a match, and never train on or retain it. §11: delete content
  on a valid claim — the admin **Remove** action does exactly that.
- **Storage**: **rehost** the `large2x` size (licence grants download/copy).

### ✅ Pixabay — `pixabay.com/api/` — key required
- **Endpoint**: `?key=…&q=…&image_type=photo&category=food&safesearch=true&per_page=8`.
- **Key**: `PIXABAY_API_KEY` (free). Skipped when unset.
- **Rate**: 100 requests / 60 s per key. **Responses must be cached for 24 h** — the
  finder's cache does this for every source.
- **robots.txt**: `Disallow: /api/` for `*` (crawler rule; documented API).
  `cdn.pixabay.com` has no `robots.txt` → allowed.
- **Terms**: permanent hotlinking is **not** allowed — "download them to your server
  first". Show where images come from. Systematic mass downloads prohibited — we fetch
  at most one full image per attached/queued candidate.
- **Storage**: **rehost** `largeImageURL` (≤ 1280 px), as required.

### ⚠️ Unsplash — `api.unsplash.com/search/photos` — key required, **gated**
- **Endpoint**: `?query=…&per_page=8&content_filter=high`, headers
  `Authorization: Client-ID <key>`, `Accept-Version: v1`.
- **Key**: `UNSPLASH_ACCESS_KEY`. Demo apps get 50 req/h; production approval 1,000 req/h.
- **robots.txt**: `api.unsplash.com` disallows `/` for `*` (API host). `images.unsplash.com`
  allows everything.
- **API terms that bind us**:
  - §6 **hotlink**: "directly use or embed the related image URLs returned by the API".
    → stored as the remote `urls.regular` URL, never rehosted; nom-api refuses an
    Unsplash candidate that is not hotlinked or not on `images.unsplash.com`.
  - §6 **download event**: notify `links.download_location` when the photo is used.
    → nom-api lists in-use Unsplash photos not yet reported
    (`GET /api/RecipeImages/download-tracking`); the finder pings each one (only ever to
    `https://api.unsplash.com/…`) and marks it (`POST …/download-tracked`). nom-api
    itself never calls Unsplash.
  - §9 **attribution**: credit the photographer *and* Unsplash with links back; links
    carry `utm_source=<app>&utm_medium=referral` (`NOM_UNSPLASH_APP_NAME`, default `nom`).
  - §12 directs any use of API content "in connection with any machine learning and/or
    artificial intelligence purposes" to `unsplash.com/data`. Running a local vision
    model over a thumbnail to verify it is plausibly such a use.
- **Determination: implemented but OFF.** The adapter only runs with
  `--unsplash-ai-confirmed` (or `NOM_UNSPLASH_AI_CONFIRMED=1`), which the operator should
  set only after Unsplash confirms in writing that classification-only inference (no
  training, no retention) is acceptable. Second precondition: the Unsplash guidelines
  want the credit wherever the photo is displayed; NOM's recipe *cards* (search, plans,
  cookbooks) show the image without a credit line today. Add a card credit before
  enabling Unsplash for production.

### ❌ Not used
- **Search-engine image search** (Google/Bing image results): never scraped; licences
  unknowable.
- **Flickr API directly**: Flickr's API terms require a commercial key for commercial
  use; Openverse already indexes Flickr's CC content with licence filtering.
- **Open Food Facts images**: CC BY-SA, product packaging — the wrong kind of photo and the
  wrong licence.
- **Smithsonian Open Access / museum APIs**: CC0 and lawful, but essentially no plated-dish
  photography; not worth a key.

## Triangulation (what "verified" means)

Four independent checks; **all four** must pass to attach unattended
(`ops/image_triangulation.py`, every threshold a CLI flag):

| Check | Evidence | Default threshold |
|---|---|---|
| (a) metadata | uploader's title/description/tags vs the dish name (lexical coverage of the name's key words, plural-folded) **and** `nomic-embed-text` similarity to name + description | coverage ≥ 0.6 **and** cosine ≥ 0.62 |
| (b) blind caption | `qwen2.5vl:7b` describes the photo **without being told the recipe**; the caption is embedded and compared to the recipe's name, description and instructions (best of the three); a caption mentioning watermark/logo/collage/packaging/illustration fails | cosine ≥ 0.64 |
| (c) directed VQA | `qwen2.5vl:7b` answers JSON questions: photograph? finished dish? raw ingredients only / packaging / people prominent / text or watermark / collage? plausibly *this* dish? which foods are visible? | content gate all clean; `dish_matches = yes`; ≥ 50 % of visible foods are foods the recipe mentions (ingredients, name, description, steps) |
| (d) second model | `gemma3:4b` (a different model family) scores each photo 0–10, then judges **every pair of eligible photos head-to-head, shown in both orders** | score ≥ 7, **≥ 1 eligible rival**, and wins 100 % of head-to-head votes |

- The **content gate** in (c) is absolute: a photo with a watermark, people, packaging, a
  collage or raw ingredients is never attached *or* queued.
- **Review** = the best photo passes the content gate and ≥ 2 of the 4 checks; up to three
  are queued per recipe. Otherwise nothing happens and the recipe is retried after 30 days.
- Near-identical photos (64-bit dHash, Hamming ≤ 6) collapse to the best-licensed copy so
  a photo cannot lose the head-to-head to itself.
- Why head-to-head instead of a score margin: on the calibration runs `gemma3:4b` gave
  **every** pancake candidate 9/10, so absolute scores cannot carry a margin. Every pair of
  eligible candidates is compared in both orders (A/B then B/A); a model that just picks a
  position splits the vote, which counts as no preference. A lone eligible candidate cannot
  show a margin, so it is queued rather than attached (`--rank-min-rivals 0` to relax).
- Calibration (2026-10-09; seed recipes Classic Buttermilk Pancakes, Creamy Tomato Basil
  Soup, Coconut Shrimp Curry; 14 real Commons/Openverse candidates analysed): caption
  similarity to the *right* recipe 0.69–0.74 vs ≤ 0.58 to the wrong ones; metadata 0.68–0.74
  vs ≤ 0.55. Thresholds sit in those gaps. Re-check them when changing models.
- Outcome on that sample: **0 attached, 3 queued, 0 nothing**. The second model disagreed
  with the first on every recipe (e.g. it preferred a plated mozzarella-noodle dish over a
  bowl of tomato soup for "Creamy Tomato Basil Soup"), so nothing went live unattended —
  the intended failure mode. `gemma3:12b` was tried as the second model on the same soup
  pairs and showed the same plating bias at 3× the cost, so `gemma3:4b` stays the default
  (`--rank-model` switches it). Expect most recipes to land in review until a stronger
  second model is available; loosen with `--rank-win-share 0.75` only after reviewing a
  batch.

## How a photo becomes a recipe image

1. `GET /api/RecipeImages/missing` — published (Public + Approved) recipes with no image
   and nothing waiting in the queue, most-rated first, with ingredients, steps and
   previously rejected candidate keys.
2. The finder queries the enabled sources, filters licences, downloads thumbnails into
   memory and runs the four checks (`ops/image_triangulation.py`).
3. `POST /api/RecipeImages/candidates` — all four pass → `attached` (live immediately,
   only if the recipe still has no image); otherwise up to three `pending` candidates go
   to **Admin → Recipe Images**. Each row carries licence, credit and the full evidence.
4. Approve attaches (and supersedes the siblings); Reject records the photo so it is never
   proposed again; Remove takes an attached photo and its credit back off the recipe
   (licence takedowns). A recipe author's own upload always clears the credit fields.

## Running it

```bash
export NOM_API=https://<nom-api host>          # base URL, no /api
export NOM_API_KEY=<curation admin API token>  # POST /api/User/api-tokens {"name":"image-finder"} as a CanManageCuration user
export PEXELS_API_KEY=... PIXABAY_API_KEY=...  # optional; skipped when unset
export OPENVERSE_CLIENT_ID=... OPENVERSE_CLIENT_SECRET=...   # optional
./ops/recipe-image-finder.py --ollama-url http://192.168.1.95:11434 --limit 25 \
    --state /var/lib/nom-image-finder/state.json --log /var/log/nom-image-finder.jsonl
```

- Models on the Ollama host: `qwen2.5vl:7b` (≈6.0 GB on disk), `gemma3:4b` (≈3.3 GB),
  `nomic-embed-text` (≈0.3 GB). The finder refuses to start if one is missing.
- **The GPU is shared.** `qwen2.5:14b-instruct` holds ≈11.9 GB of the 16 GB card while in
  use; the vision models cannot load beside it, and Ollama queues their requests until the
  14b is idle. Schedule the finder off-hours (nightly), or pass `--ollama-cpu` to run the
  models on the host's CPU without touching the GPU (≈3 min per candidate instead of
  seconds). Model failures never record a recipe as "nothing found": it is retried next run,
  and two failing recipes in a row stop the run. Models are unloaded at the end
  (`--no-unload` to keep them).
- Rehearse first: `--dry-run` (or `--recipes-json file`) analyses and logs but sends nothing.
- Budget per run is set by the keyless Openverse quota (200 requests/day anonymous):
  `--limit 25` with two queries per recipe stays well inside it.

## Open items

- Attorney confirmation of the BY-SA exclusion (and whether to allow it uncropped).
- Unsplash: written confirmation re §12, and a credit on recipe cards, before enabling.
- Openverse: register an OAuth app before regular production runs (200/day anonymous).
