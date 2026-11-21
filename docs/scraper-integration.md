# Recipe Scraper Integration

NOM ships **without any web-scraping capability**. URL-based recipe import is delegated to an external, operator-provided *scraper service*. This is deliberate:

- **Responsibility stays with the operator.** Whoever runs a scraper owns its crawling behavior, the sites it touches, and the legal posture of doing so. NOM's public codebase never fetches third-party sites.
- **Whitelist-only, admin-approved.** Even with a scraper configured, NOM refuses to scrape any domain an admin has not explicitly approved (see *Source whitelist* below).
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

### `POST /api/discover` (optional)

Request: `{ "seedDomains": ["approved-site.com", …], "maxCandidates": 10 }`

Follows outbound links from the approved seed sites' homepages and probes each
candidate domain's homepage (one page, robots-aware) for a recipe signal.
Response: `{ "candidates": [{ "domain", "evidenceUrl", "signal", "discoveredVia" }], "probedWithoutSignal": n }`.
Discovery proposes; it never imports.

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

- **Source whitelist:** before any scrape, NOM checks the URL's domain against the `recipe.ScrapingSource` table. Unknown domains create a *Pending* request, notify all `CanManageCuration` admins in-app and by email, and nothing is fetched. Admins approve/reject under **Admin → Scraping Sources** (`/api/ScrapingSource`). Approval is an explicit acceptance of responsibility for that source.
- **Vetting:** imports are checked for plausibility (realistic times/servings, completeness, parseable quantities). Suspect recipes get `RequiresRevision` curation status with the issues recorded in `Recipe.VettingIssues` for admin review.
- **Copyright quarantine:** `Recipe.SourceImageUrl` (review-only, never published; the public `Image` stays empty until a curator uploads one via the normal asset flow) and `Recipe.ContainsSourceProse = true` until the description/steps are rewritten and a curator clears it. The raw JSON-LD is kept in `recipe.ScrapedDocument`.
- **Prose rewrite:** the clean-room rewrite lane (`ProseRewriteHostedService` running `ProseLaneService`, on when `Ai:BatchOllamaUrl` is set; design in `docs/architecture/prose-lane.md`) writes each withheld or quarantined recipe's method again without ever showing the writer the source's wording: every amount of the source method is masked as a token that code puts back, a model turns the masked method into fact sheets, the writer writes from the sheets, the ingredient names and the dish name only, and the description is built by rule from the dish name and main ingredients. A rewrite is written only when it keeps to the form rules, passes prose check v2 against the source and is found faithful step by step by a judge of another model family (`Ai:Roster:ReaderB`); it then lifts `ProseWithheld` or `ContainsSourceProse`, and every overwritten description, step and state is journaled in `audit.AuditLogEntry` as `ProseRewrite`. The lane runs on the lane frame: it decides in shadow and writes nothing until its audited sample qualifies, pauses itself on a planted-fault canary let through, a gate yield under half or audited errors above 5 %, and undoes its writes since its last clean window; `POST /api/ProseRewrite/undo?sinceUtc=&laneVersion=&apply=false` is the same undo for an operator. A recipe all four attempts fail is given up under the lane version and counted by failure class on the Pipeline page; it is taken again when the lane version (prompts, models, check) changes. The old paraphrase lane (`RewriteFidelity` as its gate, `ProseRewrite:CheckFidelity`) is gone. `POST /api/ProseRewrite/recheck` runs prose check v2 over the recipes whose prose is shown (a dry run unless `apply=true`); with apply, failures have their prose **withheld** (`Recipe.ProseWithheld`, `ProseWithhold` journal rows, curation status unchanged): the recipe stays in search, suggestions and meal plans, no endpoint serves its description or steps, and its page shows rule-built key facts and a link to the source instead. `POST /api/ProseRewrite/recheck/undo?sinceUtc=&apply=false` releases them again (a dry run unless `apply=true`), skipping those whose prose has been replaced since; it also takes back recipes an earlier re-check put into quarantine (`ProseRecheck` rows). A weekly job (`ProseRecheck:Enabled`, held by the platform feature `pause-prose-recheck`) withholds what fails whenever the check or the stock index changes and once a week. Withheld is a holding state: the rewrite lane works the withheld recipes off, approved first. `ProseRewrite:DrainWhileFull` is on by default only when `AI_PROSE_REWRITE_MAX_PER_DAY` is above 0: without a cap the lane takes one sweep per interval (25 every 10 minutes). A model answer with an HTTP error status sends the recipe to the back of the queue uncharged; no answer or a missing model ends the sweep. Prose check v2 (`ProseCheck`: amounts with units and ranges, amounts against the ingredient lines, verb and ingredient coverage, copy measures that ignore the stock phrases of `recipe.ProseStockPhrase`, rebuilt by `POST /api/ProseRewrite/stock/rebuild`) is the gate of both the re-check and the lane; see `docs/architecture/prose-check.md`.
- **Dedup:** a normalized source URL is imported at most once.

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

The script reads the harvest files as they are. Each line wraps the record: a cookbook line is `{"chunk", "book", "recipe"}` (or `{"chunk", "recipe"}`), a web line `{"url", "scraped_at", "recipe"}`, a myplate line `{"url", "scraped_at", "archive_ts", "recipe"}`; a bare scraper record is read too. It sends the record in the scraper's shape: plain-string ingredients and steps become `{"rawLine", "name"}` and `{"order", "instruction"}`, nested lists are flattened, null and blank entries are dropped (nom-api refuses a whole call with an empty step instruction), a field of the wrong type is left out, and a web or myplate record without `sourceUrl` takes the wrapper's `url` (book records keep none, so they are matched by name and attribution). The stored text is this conversion. `--attribution-csv` (columns `file`, `attribution`, optional `name` and `book`) may give a file more than one attribution: a record takes the row that names it or its wrapper's `book`, else the file's only attribution, else each of the file's attributions in turn while the answer is `unmatched` or `low-score` (nothing is stored on those). A later record of the same normalized URL as the one that took a recipe is reported as `rescraped` (the harvest scraped the page twice; stored once, not a double, and it does not fail the target). A record nom-api refuses (HTTP 400) is named with its file, line and name, counted as `refused`, and the rest of its batch is sent again; any other error stops the run with the undo stamp. The script keeps under the API's per-client rate limits (50 calls in 10 seconds, 100 a minute, 1,000 an hour): by default at most 40, 80 (`--per-minute`) and 900 (`--per-hour`), leaving room for other operator calls under the same key, and it waits out a 429's `Retry-After` and sends the same call again. A full pass is about 475 calls, so a dry run and an apply of every file take more than one hour.

Each stored document is journaled in `audit.AuditLogEntry` (`SourceAttach`, entity `ScrapedDocument`, the recipe, version, checksum, score and key in `NewValue`). `POST /api/RecipeSource/attach/undo?sinceUtc=&apply=false` soft-deletes the documents attached at or after `sinceUtc` that are still there, journaled as `SourceAttachUndo`; a later attach stores them again. Documents stored by an import are never touched by the undo.

## Automatic source discovery (optional, off by default)

With a scraper configured, NOM can look for new public recipe sources automatically:

```json
"SourceDiscovery": { "Enabled": true, "IntervalHours": 168, "MaxCandidatesPerRun": 10, "AutoApprove": false }
```

On each run, NOM sends its **approved** domains to the scraper's `/api/discover`;
newly found candidate domains are registered as **Pending** scraping sources,
which triggers the standard admin prompt (in-app message + email). The admin
approves or rejects each one — discovery never scrapes a candidate beyond the
single robots-compliant probe page, and imports only ever happen from approved
domains. Domains an admin has rejected are never re-proposed.

Discovery needs at least one approved source to seed from, so approve a first
site by importing from it (or inserting a row) before enabling this.

With `AutoApprove: true` (off by default), candidates that pass a **clean-probe
gate** skip the queue entirely: NOM scrapes the candidate's evidence URL through
the scraper service (robots-aware, one page) and auto-whitelists the domain only
when the probe returns a complete structured recipe — https, schema.org JSON-LD
present, at least one ingredient and step, and zero vetting issues. Anything
less stays Pending for human review. Admins are still notified of every
auto-approval, with a pointer to Admin → Scraping Sources to revoke it; a
rejected domain is never auto-approved or re-proposed. Enabling this widens the
set of sites scraped without a human in the loop — the operator-responsibility
posture above applies to that choice.

## Media storage (optional)

Curator-approved images can be stored on a fast local volume instead of the database:

```json
"Media": { "RootPath": "/mnt/nom-media" }
```

Mount the volume (e.g. the fast storage on Server-2/.56 or X-Server-01/.65) into the api container and point `Media:RootPath` at it. When unset, images continue to be stored in the database.
