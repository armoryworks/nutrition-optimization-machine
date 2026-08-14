# Recipe Scraper Integration

NOM ships **without any web-scraping capability**. URL-based recipe import is delegated to an external, operator-provided *scraper service*. This is deliberate:

- **Responsibility stays with the operator.** Whoever runs a scraper owns its crawling behavior, the sites it touches, and the legal posture of doing so. NOM's public codebase never fetches third-party sites.
- **Whitelist-only, admin-approved.** Even with a scraper configured, NOM refuses to scrape any domain an admin has not explicitly approved (see *Source whitelist* below).
- **Copyright-aware by design.** Scraped facts (ingredients, quantities, times) are stored; the source's photo and verbatim prose are quarantined for curation review and never published.

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
- **Dedup:** a normalized source URL is imported at most once.

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
