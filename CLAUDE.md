# NOM Project Instructions

## Recipe Scraping

NOM contains no web-scraping code. URL import is delegated to an external, operator-provided scraper service (reference implementation: private repo `armoryworks/nom-scraper`); the contract and setup live in `docs/scraper-integration.md`. Scraping is whitelist-only — domains must be admin-approved in `recipe.ScrapingSource` — and scraped images/prose are quarantined (`SourceImageUrl`, `ContainsSourceProse`) until curated. Never add code that fetches third-party recipe sites directly from nom-api.

## Database Setup

The database uses a declarative (DACPAC-style) workflow — `db/schema.sql` is the source of truth; there are no EF migrations. See `db/README.md`.

- Fresh or existing DB: `./db/apply.sh` (use `--dry-run` to preview the delta)
- After changing entities in Nom.Data: `./db/sync-from-model.sh` regenerates `db/schema.sql`; `--check` is the CI drift guard.

## E2E Test IDs (`data-testid`)

All interactive and testable elements in Angular templates **must** have `data-testid` attributes for Cypress E2E test stability. Test IDs use kebab-case and follow this convention:

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
