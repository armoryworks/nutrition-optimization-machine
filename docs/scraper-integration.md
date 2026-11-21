# Recipe Scraper Integration

NOM ships **without any web-scraping capability**. URL-based recipe import is delegated to an external, operator-provided *scraper service*. This is deliberate:

- **Responsibility stays with the operator.** Whoever runs a scraper owns its crawling behavior, the sites it touches, and the legal posture of doing so. NOM's public codebase never fetches third-party sites.
- **Whitelist-only, admin-approved.** Even with a scraper configured, NOM refuses to scrape any domain an admin has not explicitly approved (see *Source whitelist* below).
- **Gated on rights, by rule.** An approved web source is fetched only when the rights class derived from its stored source check allows it: clear, a recorded written permission, or facts-only with facts-only use approved by an admin. Forbidden and permission-needed sources are never fetched, and a facts-only source's recipes show facts and a link only (`docs/architecture/source-rights.md`, `SourceRights:Enforce`).
- **Copyright-aware by design.** Scraped facts (ingredients, quantities, times) are stored; the source's photo and verbatim prose are quarantined for curation review and never published. Shown prose that later fails the prose check is withheld: never served, while the recipe stays usable with rule-built key facts and a source link until it is rewritten.

The reference implementation lives in a **private** repository: `armoryworks/nom-scraper`. You can request access, or build your own service against the contract below — anything that speaks this contract works.

## Configuring NOM to use a scraper

`nom-api` `appsettings.json` (or environment variables):

```json
"RecipeScraper": {
  "BaseUrl": "http://your-scraper-host:8080",
  "ApiKey": "the-shared-secret-you-configured-on-the-scraper",
  "TimeoutSeconds": 90
}
```

- `RecipeScraper__BaseUrl` / `RecipeScraper__ApiKey` as env vars in Docker.
- When `BaseUrl` is empty (the default), every scraping endpoint responds with "scraping is not enabled on this server" and the rest of NOM works normally.

## Spinning up the reference scraper (private repo)

```bash
git clone git@github.com:armoryworks/nom-scraper.git
cd nom-scraper
docker build -t nom-scraper .
docker run -d --name nom-scraper -p 8080:8080 -e ApiKey=$(openssl rand -hex 24) nom-scraper
```

Point `RecipeScraper:BaseUrl` at it and use the same `ApiKey`. The service enforces robots.txt, per-domain rate limits (default 5s), an honest `NomBot` User-Agent, response-size caps, and an SSRF guard. It extracts schema.org Recipe JSON-LD only and **never fabricates data** — pages without structured recipe data fail cleanly. See its README for tuning (`Scraper__AllowedDomains`, crawl delay, etc.).

## The contract (build your own)

Authentication: every request carries `X-Api-Key: <shared secret>`.

### `POST /api/scrape`

Request: `{ "url": "https://example.com/recipes/pancakes" }`

### `POST /api/parse`

Request: `{ "content": "<html … or raw JSON-LD>", "sourceUrl": "optional" }` — parse without fetching.

### Discovery and source-check jobs (optional)

NOM's source lane (see *Source discovery and checks* below) uses two kinds of budgeted job. Both
are started with a `POST` that answers **202** with the job and read with `GET …/jobs/{jobId}`
(`DELETE` asks a running job to stop). A job is `running`, `completed`, `partial` (the budget,
a cancel or a shutdown left work undone; `result` holds what was found) or `failed`; a 404 means
the job is unknown or was lost in a restart, a 429 that as many jobs of the kind run as the
service allows. Jobs live in the scraper's memory, so NOM treats a lost job as a failed run.

- `POST /api/discover/jobs` `{ "seedDomains": [...at most 20], "maxCandidates": 10, "budgetSeconds": 600 }`:
  follows outbound links from the seeds (in the order sent) and probes found domains for a recipe
  signal. `result`: `candidates` (`domain`, `evidenceUrl`, `signal`, `discoveredVia`),
  `probedWithoutSignal`, `partial`, `stopReason`, `seedsVisited`, `seedsNotVisited`,
  `candidatesNotProbed` (leads with no signal), `seedsRefused` (search engines and platforms,
  never fetched), `pagesFetched`. Discovery proposes; it never imports.
- `POST /api/source-check/jobs` `{ "domain": "www.example.org", "sampleSize": 20, "budgetSeconds": 1200, "includeRecipes": true }`:
  reads the site's robots.txt (NomBot, `*` and the named AI crawlers, crawl-delay), its
  reservation signals (TDMRep, `noai`), up to 6 terms, legal or licence pages, the sitemap and a
  spread sample of recipe pages, and returns facts only, with its own verdict and reasons under a
  rule version (`source-check/4`; NOM still reads a stored `source-check/2` report). `result` is null while
  running and the full report once the job ends; a report that did not finish is `complete: false` and never
  clear. Nothing is imported. NOM reads the `source-check/4` facts `access.homepageLinks` (0: a homepage
  built by scripts), `access.refusedPages`, `enumeration.refusedPages`, `samples.refused`, the
  `NoAddress` homepage outcome and the `off-domain` terms outcome.
  With `"sampleSize": 0` (a **terms-only** check) the scraper reads robots.txt, the homepage,
  `tdmrep.json` and the terms pages only, no sitemap and no recipe page, and reports `samples.requested` 0.
  NOM asks for one only when an admin starts it for a source that is forbidden or needs permission, and only
  with `SourceDiscovery:TermsOnlyChecks` on; a scraper without the mode must not be sent one.

The older synchronous `POST /api/discover` remains in the reference scraper for compatibility;
NOM no longer calls it.

### Response (both endpoints, HTTP 200)

```json
{
  "success": true,
  "failureReason": "None",
  "error": null,
  "recipe": {
    "name": "…", "description": "…", "imageUrl": "…", "author": "…",
    "sourceUrl": "…", "sourceSite": "example.com",
    "prepTime": "PT15M", "cookTime": "PT20M", "totalTime": "PT35M",
    "prepTimeMinutes": 15, "cookTimeMinutes": 20, "totalTimeMinutes": 35,
    "recipeYield": "12", "recipeServings": 12,
    "ingredients": [
      { "rawLine": "2 cups all-purpose flour", "name": "all-purpose flour",
        "quantity": 2, "unit": "cups", "notes": null }
    ],
    "steps": [
      { "order": 1, "section": null, "instruction": "Whisk the dry ingredients…" }
    ],
    "keywords": ["…"], "categories": ["…"], "cuisines": ["…"],
    "suitableForDiet": ["VegetarianDiet"],
    "rawJsonLd": "{…the schema.org Recipe node as published…}"
  }
}
```

On failure `success` is `false`, `recipe` is `null`, and `failureReason` is one of `InvalidUrl`, `DomainNotAllowed`, `RobotsDisallowed`, `FetchFailed`, `NotHtml`, `ResponseTooLarge`, `NoStructuredRecipeData`.

Contract rules an implementation must honor:

1. **Never fabricate.** Unparsed quantities/units are `null` with the original text in `rawLine`; a page with no recipe data is a failure, not a guessed recipe.
2. **Fetch politely.** Respect robots.txt and rate-limit per domain; identify honestly via User-Agent.
3. **Include `rawJsonLd`** when available — NOM stores it for provenance and later enrichment.

## What NOM does with the result

- **Source whitelist:** before any scrape, NOM checks the URL's domain against the `recipe.ScrapingSource` table. Unknown domains create a *Pending* request, notify all `CanManageCuration` admins in-app and by email, and nothing is fetched. Admins reject a request under **Admin → Scraping Sources** (`/api/ScrapingSource`); a request becomes a candidate, and it is approved only from the queue once its source check is clear. Approval is an explicit acceptance of responsibility for that source. The staged import (`POST /api/StagedImport`) is under the same whitelist: with `SourceDiscovery:StagedImportGate` on it takes a recipe only from an approved source (its URL's domain, or the hand source a URL-less batch names with `sourceId`), and the licence comes from that source's record, never from the caller's `publicDomain` flag.
- **Vetting:** imports are checked for plausibility (realistic times/servings, completeness, parseable quantities). Suspect recipes get `RequiresRevision` curation status with the issues recorded in `Recipe.VettingIssues` for admin review.
- **Copyright quarantine:** `Recipe.SourceImageUrl` (review-only, never published; the public `Image` stays empty until a curator uploads one via the normal asset flow) and `Recipe.ContainsSourceProse = true` until the description/steps are rewritten and a curator clears it. The raw JSON-LD is kept in `recipe.ScrapedDocument`.
- **Prose rewrite:** the clean-room rewrite lane (`ProseRewriteHostedService` running `ProseLaneService`, on when `Ai:BatchOllamaUrl` is set; design in `docs/architecture/prose-lane.md`) writes each withheld or quarantined recipe's method again without ever showing the writer the source's wording: every amount of the source method is masked as a token that code puts back, a model turns the masked method into fact sheets, the writer writes from the sheets, the ingredient names and the dish name only, and the description is built by rule from the dish name and main ingredients. A rewrite is written only when it keeps to the form rules, passes prose check v2 against the source and is found faithful step by step by a judge of another model family (`Ai:Roster:ReaderB`); it then lifts `ProseWithheld` or `ContainsSourceProse`, and every overwritten description, step and state is journaled in `audit.AuditLogEntry` as `ProseRewrite`. The lane runs on the lane frame: it decides in shadow and writes nothing until its audited sample qualifies, pauses itself on a planted-fault canary let through, a gate yield under half or audited errors above 5 %, and undoes its writes since its last clean window; `POST /api/ProseRewrite/undo?sinceUtc=&laneVersion=&apply=false` is the same undo for an operator. A recipe all four attempts fail is given up under the lane version and counted by failure class on the Pipeline page; it is taken again when the lane version (prompts, models, check) changes. The old paraphrase lane (`RewriteFidelity` as its gate, `ProseRewrite:CheckFidelity`) is gone. `POST /api/ProseRewrite/recheck` runs prose check v2 over the recipes whose prose is shown (a dry run unless `apply=true`); with apply, failures have their prose **withheld** (`Recipe.ProseWithheld`, `ProseWithhold` journal rows, curation status unchanged): the recipe stays in search, suggestions and meal plans, no endpoint serves its description or steps, and its page shows rule-built key facts and a link to the source instead. `POST /api/ProseRewrite/recheck/undo?sinceUtc=&apply=false` releases them again (a dry run unless `apply=true`), skipping those whose prose has been replaced since; it also takes back recipes an earlier re-check put into quarantine (`ProseRecheck` rows). A weekly job (`ProseRecheck:Enabled`, held by the platform feature `pause-prose-recheck`) withholds what fails whenever the check or the stock index changes and once a week. Withheld is a holding state: the rewrite lane works the withheld recipes off, approved first. `ProseRewrite:DrainWhileFull` is on by default only when `AI_PROSE_REWRITE_MAX_PER_DAY` is above 0: without a cap the lane takes one sweep per interval (25 every 10 minutes). A model answer with an HTTP error status sends the recipe to the back of the queue uncharged; no answer or a missing model ends the sweep. Prose check v2 (`ProseCheck`: amounts with units and ranges, amounts against the ingredient lines, verb and ingredient coverage, copy measures that ignore the stock phrases of `recipe.ProseStockPhrase`, rebuilt by `POST /api/ProseRewrite/stock/rebuild`) is the gate of both the re-check and the lane; see `docs/architecture/prose-check.md`.
- **Dedup:** a normalized source URL is imported at most once. Normalizing drops the fragment, except one that opens a page of a document (`#page=12`, the PDF open parameter a hand import gives each recipe of a multi-recipe PDF), so the recipes of one document stay apart.

## Stored sources

Every recipe keeps the source it was built from in `recipe.ScrapedDocument`, so later rule fixes can rebuild its lines without fetching anything again. `Format` says what `RawJsonLd` holds:

| Format | Content | Stored by |
|---|---|---|
| `jsonld` | the schema.org Recipe node exactly as published | any import whose record carries `rawJsonLd` |
| `scraper-recipe` | the scraper record of a recipe with a source URL (web harvest, myplate), as JSON | the staged import (`POST /api/StagedImport`), and the source attach |
| `staged-cookbook` | the record of a recipe with no source URL (a book extraction), as JSON | the same |

A record is stored in one JSON form (`Support/SourceRecord.cs`: camelCase, nulls left out, `rawJsonLd` left out because it is its own `jsonld` document), so the import and the attach store the same text for the same record. `ContentSha256` is the lowercase hex SHA-256 of that text (null on documents stored before it was kept). `Version` numbers the documents of one format on one recipe from 1. The record is stored as the import received it, before the import normalizes its amounts. The prose lane, the prose re-check, the stock-phrase index and the line unfold read `jsonld` documents only. The rebuild (`POST /api/RecipeReprocess/rebuild`) reads the newest record first and falls back to the `jsonld` document through the scraper's `POST /api/parse`; it builds the lines with the import's own line builder and keeps the recipe's Id (see `docs/architecture/recipe-reprocess.md`).

**Attaching the sources of older imports.** Recipes imported before the staged import kept its record (the cookbooks, myplate and 23 web recipes) get theirs from the staging files with `ops/recipe-source-attach.py`, run where the files are. It posts batches of at most 500 records to `POST /api/RecipeSource/attach?apply=false|true&newVersion=false` (`CanManageCuration`; a dry run unless `apply=true`; nothing is fetched from any site). nom-api matches each record with the import's own keys: the normalized URL, else the name and the attribution the batch was imported with (`sourceAttribution`, else the record's own author and site). Because books repeat names, the key is not enough: at least 80 % of the record's ingredient lines must equal a line of the recipe, a `" + "` fold part of one, or a line the compound split rewrote as parts (`salt and pepper` stored as `salt` and `pepper`; the line as its `LineSplit` journal entry holds it), after entity, spacing and case normalization, and one recipe must agree better than every other. The answer gives each record one outcome:

- `matched`: stored (with its `jsonld` too when the recipe holds none);
- `already-attached`: the recipe already holds this exact text;
- `ambiguous` (a tie), `low-score` (under 80 %), `unmatched` (no recipe has the key): not stored;
- `double-match`: an earlier record of the batch took this recipe and this record's text differs; not stored;
- `conflict`: the recipe already holds a different document of the record's format; stored as the next `Version` only with `newVersion=true`.

A newer extraction of a book (`ops/cookbook-extract.py`, see `docs/architecture/recipe-reprocess.md`) may name in each record the recipe whose source it replaces (`replacesRecipeId`, from the log of the first attach); the script sends those as the batch's `recipeIds` (by position, null for a record matched as usual). A pinned record is matched by that id only (`matchedBy` `pin`), stored as the next `Version` with `newVersion=true`, and only on a recipe that already holds a record of its format (otherwise `unmatched`); its line agreement is reported, not required, because a translation does not repeat the old lines. Records of one batch pinned to the same recipe are all `doubleMatch` and none is stored, so a pin never picks the first of two candidates.

Records are settled in batch order, as the staged import took them (it keeps the first record of a URL, or of a name and attribution): the first record of a recipe takes it, a later copy with the same text is `already-attached`, and a later different one is a `double-match`. Each answer carries the record's `contentSha256`, and the script settles records of different batches the same way (a dry run's second `matched` copy becomes `already-attached`; a different record, or a `conflict` with a record an earlier batch of the run took, becomes `double-match`), so where the batches split changes no outcome. Its staging target counts lines it could not read as records that were not attached. An apply names its undo stamp as soon as its first batch is stored, again if a later batch fails, and in every `--log` line.

The script reads the harvest files as they are. Each line wraps the record: a cookbook line is `{"chunk", "book", "recipe"}` (or `{"chunk", "recipe"}`), a web line `{"url", "scraped_at", "recipe"}`, a myplate line `{"url", "scraped_at", "archive_ts", "recipe"}`; a bare scraper record is read too. It sends the record in the scraper's shape: plain-string ingredients and steps become `{"rawLine", "name"}` and `{"order", "instruction"}`, nested lists are flattened, null and blank entries are dropped (nom-api refuses a whole call with an empty step instruction), a field of the wrong type is left out, and a web or myplate record without `sourceUrl` takes the wrapper's `url` (book records keep none, so they are matched by name and attribution). The stored text is this conversion. `--attribution-csv` (columns `file`, `attribution`, optional `name` and `book`) may give a file more than one attribution: a record is sent first under the row that names it or its wrapper's `book`, and when that finds no recipe (unmatched or low-score) it is tried under the file's other attributions, since a book's name list may give a recipe the other edition's attribution than the import stored (cookbook-6429: 100 recipes listed under 1937, all 122 imported under 1907, so give such a file one row per attribution and no names, or names only where they are certain); a record no row names takes the file's only attribution, else it is first sent as a dry run under each of the file's attributions and stored under the one that matches it with the best score; where two match it equally to different recipes (the same recipe in both editions of a book), it takes the first recipe no other record of the run took, so each edition's recipe gets its own record. A later record of the same normalized URL as the one that took a recipe is reported as `rescraped` (the harvest scraped the page twice; stored once, not a double, and it does not fail the target). A record nom-api refuses (HTTP 400) is named with its file, line and name, counted as `refused`, and the rest of its batch is sent again; any other error stops the run with the undo stamp. The script keeps under the API's per-client rate limits (50 calls in 10 seconds, 100 a minute, 1,000 an hour): by default at most 40, 80 (`--per-minute`) and 900 (`--per-hour`), leaving room for other operator calls under the same key, and it waits out a 429's `Retry-After` and sends the same call again. A full pass with the 360 web files as they are is about 810 calls (the 475 estimated before counted merged web files), so a dry run, an apply and the one-book undo proof's re-apply (about 380 calls) each need an hour of their own with nothing else under the key; merging the web files first lowers the count.

Each stored document is journaled in `audit.AuditLogEntry` (`SourceAttach`, entity `ScrapedDocument`, the recipe, version, checksum, score and key in `NewValue`). `POST /api/RecipeSource/attach/undo?sinceUtc=&apply=false` soft-deletes the documents attached at or after `sinceUtc` that are still there, journaled as `SourceAttachUndo`; a later attach stores them again. A repeated undo counts the documents an earlier undo already removed as `alreadyUndone`, apart from `skipped` (gone some other way), so a second run over the same window reads as done, not as failed. Documents stored by an import are never touched by the undo.

## Source discovery and checks (optional, off by default)

With a scraper configured, NOM can look for new recipe sources and judge them, and re-judge the
approved ones. Nothing here approves a source: an admin approves every domain, and only a site
whose check is clear is offered for that approval. Full design:
`docs/architecture/recipe-sources.md`.

```json
"SourceDiscovery": { "Enabled": true, "IntervalHours": 24, "SeedsPerRun": 3, "MaxChecksPerDay": 10, "RecheckDays": 7 }
```

- **Discovery** runs the scraper's discovery job with the three approved web sources discovered
  longest ago and polls it to its end. Each run is stored (`recipe.SourceDiscoveryRun`); a job that
  could not start, failed, was lost or never ended is a **failed run**, shown as failed on the
  Pipeline page with what it had found kept.
- Found domains become candidates (`recipe.SourceCandidate`); a domain that is already a source,
  or that an admin rejected, is not. A user's request for an unknown domain becomes a candidate too.
- **Source checks** run one at a time, at most `MaxChecksPerDay` a day: approved sources due for
  their weekly re-check first, then requests, then candidates. NOM stores each report whole
  (`recipe.SourceCheck`) and draws one verdict from it (`SourceAdmissionPolicy`, no model): clear
  only when the check finished under `source-check/4`, the scraper and every one of its reasons
  agree, the licence is public domain, CC0, the Public Domain Mark or CC BY, the full 20 pages were
  sampled, NOM's own vetting passes 18 of the sample recipes and 85 % of their lines carry a
  quantity or need none, and no admin rejected the domain before.
- **The approval queue** (Admin → Scraping Sources) lists only clear candidates, each with the
  licence statement and where it was found, the terms read, robots rules for all crawlers and for
  AI crawlers, the crawl-delay and the sample yield, and one approve button. Sites that are not
  clear are listed apart with their reasons and cannot be approved there.
- An approved source starts in **probation** (21 days or 50 recipes); its live figures then pass it
  or pause its harvesting.
- **Re-judging**: every approved source is re-checked weekly by the same rule and the verdict is
  shown beside it, except a source whose rights class is forbidden or permission-needed, which no
  automatic check fetches; an admin may start a terms-only check of it by hand. Harvesting is paused only when a clear verdict turns not clear; a source that
  was never clear is only shown, because what happens to it and its recipes is the owner's decision.
  The admin can **pause harvesting** for a source or **withhold its recipes** (kept from search,
  suggestions and meal plans) in one click; both are journaled and undone from the journal on the
  same page.
- The staged import refuses recipes from a source whose harvesting is paused and, in probation,
  past its recipe limit, so a pause holds even before the harvester reads the list.
- **Hand sources** (PDFs, scanned books) are recorded by an admin with their licence facts; the
  scraper never fetches them (a URL import or bulk scrape on a hand source's host is refused before
  the scraper is called) and they are imported only by a staged import that names them. Their
  listed documents are fetched, read and extracted by `ops/hand-import.py` (see
  `docs/architecture/hand-import.md`), never by nom-api.
- **The harvester** reads `GET /api/ScrapingSource/harvestable` (approved web sources, harvest not
  paused, recipes not withheld, with the probation limit and crawl-delay) instead of keeping its own
  list.

Before a domain is approved the lane reads, through the scraper only, its robots.txt, reservation
signals, terms pages, sitemap and at most 20 sample pages; imports only ever happen from approved
domains. `SourceDiscovery:AutoApprove`, which once whitelisted a domain on one clean probe, is gone.

## Media storage (optional)

Curator-approved images can be stored on a fast local volume instead of the database:

```json
"Media": { "RootPath": "/mnt/nom-media" }
```

Mount the volume (e.g. the fast storage on Server-2/.56 or X-Server-01/.65) into the api container and point `Media:RootPath` at it. When unset, images continue to be stored in the database.
