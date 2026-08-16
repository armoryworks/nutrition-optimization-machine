# NOM Project Instructions

## Recipe Scraping

NOM contains no web-scraping code. URL import is delegated to an external, operator-provided scraper service (reference implementation: private repo `armoryworks/nom-scraper`); the contract and setup live in `docs/scraper-integration.md`. Scraping is whitelist-only — domains must be admin-approved in `recipe.ScrapingSource` — and scraped images/prose are quarantined (`SourceImageUrl`, `ContainsSourceProse`) until curated. Never add code that fetches third-party recipe sites directly from nom-api.

## Grocery Export

NOM contains no retailer-integration code. Sending a shopping list to a store or
share sheet is delegated to an external, operator-provided grocery service
(reference implementation: private repo `armoryworks/nom-grocery`); the contract
lives in `docs/grocery-integration.md`. Retailer OAuth tokens are stored
encrypted in `shopping.GroceryConnection` and never returned to the client.
Never add code that calls a retailer API directly from nom-api, and never
hardcode provider names in nom-ui — the UI renders whatever the service
advertises.

## Database Setup

The database uses a declarative (DACPAC-style) workflow — `db/schema.sql` is the source of truth; there are no EF migrations. See `db/README.md`.

- Fresh or existing DB: `./db/apply.sh` (use `--dry-run` to preview the delta)
- After changing entities in Nom.Data: `./db/sync-from-model.sh` regenerates `db/schema.sql`; `--check` is the CI drift guard.

## Food Catalog Imports (USDA FDC and cross-checks)

Catalog data is imported by `Nom.Import` and verified by the scripts in `ops/`.
Full design in `docs/architecture/food-catalog-ingestion.md`; host determinations in
`docs/architecture/crosscheck-sources.md`.

**Never import straight into production.** The required order is:

1. Restore a **production snapshot into a staging database** and import against that.
   A fresh staging DB hides collisions with existing rows and, because its seed is
   always present, hides missing-seed bugs entirely. (A real example: measurement
   lookup returned `0` when no row matched, which only breaks against a differently
   seeded database.)
2. `./db/apply.sh --dry-run` against the target to review the schema delta first.
3. Import, then verify end to end through the app — not just via SQL.
4. `dotnet run -- --purge-fdc` is the undo: it soft-deletes FDC-sourced rows that no
   recipe references. Every imported row carries `FdcId`, so a batch is always
   reversible.

**Rules that hold regardless of source:**

- Imports land as `PendingCuration`. Meal planning and the severe-restriction gate
  only draw from `Curated`, so **an import alone changes nothing users see** until an
  admin approves it in Admin → Food Catalog. `--import-fdc --curated` is a deliberate
  exception for USDA Foundation data; branded data must stay pending.
- Every record passes `FoodDataQualityValidator` (per-100 g plausibility + an Atwater
  cross-check). Nutrition is stored **per 100 g**; a serving is derived per person from
  caloric need, never stored as a fixed per-food value.
- **Automated reviewers may not author nutrition numbers.** `ProposalPolicy` rejects a
  nutrient change unless its source is authoritative (`fdc:`, `label:`, `admin:`,
  `deterministic:`). Models classify, flag and normalize names — they do not supply
  values. To verify numbers, diff against the current FDC release by `FdcId`.
- Classify from a **category**, not a product name, and leave ambiguous cases
  unclassified. A wrong food group is worse than none, because household food-group
  minimums would count the wrong food (a BBQ sauce once landed in Dairy).
- Cross-check fetching lives in `ops/`, never in nom-api, and only touches hosts with a
  recorded determination in `docs/architecture/crosscheck-sources.md`. Honour
  `robots.txt` including `crawl-delay`; never scrape search engine result pages.
- Open Food Facts is **ODbL** — use its bulk export as a *signal only*. Never copy its
  values into the catalog or into a proposal.
- After downloading any bulk export, **verify it**: `curl` exits 0 on a truncated
  stream. Use `wget -c` and confirm with `gzip -t`.

## E2E Test IDs (`data-testid`)

All interactive and testable elements in Angular templates **must** have `data-testid` attributes for Cypress E2E test stability. Test IDs use kebab-case and follow this convention:

> **Enforced since 2026-08-16 by `npm run lint:testids`** (`nom-ui/scripts/lint-testids.mjs`, runs in the Test Frontend workflow). This rule had been prose-only since March and was measured at 50% coverage — and *lower* on templates written after the rule than before it. It is now a per-file ratchet against `nom-ui/scripts/testid-baseline.json`: a template **not** in the baseline must be 100% covered (new work follows the rule); a baselined template may not lose coverage; a template that improved fails with `RATCHET DOWN` until you rerun `NOM_TESTID_UPDATE_BASELINE=1 npm run lint:testids` and commit the rewritten baseline **in the same commit**. It only tightens — never hand-edit a number upward. When you touch a baselined template for other reasons, add the missing test IDs while you're there; that's how the 48-file register drains.

| Element | Pattern | Example |
|---------|---------|---------|
| Page wrapper | `{page-name}` | `data-testid="register"` |
| Form | `{page}-form` | `data-testid="register-form"` |
| Submit button | `{page}-submit-btn` | `data-testid="register-submit-btn"` |
| Named action button | `{page}-{action}-btn` | `data-testid="plan-shuffle-btn"` |
| List container | `{page}-list` | `data-testid="my-ingredients-list"` |
| List item | `{page}-item` | `data-testid="ingredient-item"` |
| Card / grid item | `{page}-card` | `data-testid="recipe-card"` |
| Dialog wrapper | `{dialog-name}-dialog` | `data-testid="clone-plan-dialog"` |
| Dialog action | `{dialog}-{action}-btn` | `data-testid="clone-plan-clone-btn"` |
| Navigation link | `nav-{target}` | `data-testid="nav-meal-plan"` |
| Empty state | `{page}-empty` | `data-testid="pantry-empty"` |
| Error display | `{page}-error` | `data-testid="register-error"` |

When creating or modifying components, add `data-testid` to:
- Page-level wrapper divs
- Forms and submit buttons
- Action buttons (create, delete, shuffle, etc.)
- List containers and individual items
- Dialogs and their action buttons
- Navigation links in menus
- Empty states and error displays
- Interactive controls users would target in E2E tests
