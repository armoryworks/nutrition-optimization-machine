# NOM — Audit & UX Findings Tracker

_Living document. Maintained during the ongoing audit of NOM. Companion tracker for Forge lives at `forge/docs/AUDIT-TRACKER.md` — keep the two separate._

Last updated: 2026-08-18 (mobile pass added; N-9..N-18 fixed in v0.3.23; N-7/N-14/N-19 in v0.3.24; N-4/N-5/N-6/N-20/N-21 in v0.3.25; N-2/N-3/N-22 in v0.3.26; deep-audit backend items in v0.3.27).

## Audit access

- **Instance:** `nomtest` (NOM, SplitUi tenant — nom-ui on the web box, nom-api + Postgres on the api box). Publicly reachable at `https://nomtest.nommeal.com`.
- **User:** `daniel.hokanson+test@armoryworks.com` / `NomAudit-2026!x`, email confirmed, admin claims `CanManageCuration` + `CanManageUserRoles`.

## Open findings — summary

| # | Area | Sev | Finding | Status |
|---|------|-----|---------|--------|
| N-1 | Navigation | Med (UX) | The "COOK" cluster is 5 overlapping recipe destinations → "which door?" friction | Open |
| N-2 | Navigation | Low (UX) | "Search" duplicated (nav item + header search bar) | Fixed (v0.3.26 — Search nav item removed; header search remains) |
| N-3 | Navigation | Low (UX) | Near-duplicate / abstract icons; collapsed rail loses grouping | Fixed (v0.3.26 — plate/carrot icons; hairline group dividers in the collapsed rail) |
| N-4 | Branding | Med | "Powered by Mealie" footer + GitHub icon on every page (upstream leak) | Fixed (v0.3.25) |
| N-5 | Data / UI gap | High | No UI to author ingredient nutrition → recipes built from the catalog show **empty** nutrition labels | Fixed (v0.3.25) |
| N-6 | Workflow dead-end | High | Recipe approval requires every ingredient curated, but there is **no UI to curate an ingredient** → any recipe using a user-added ingredient can never be approved | Fixed (v0.3.25) |
| N-7 | Error handling | Med | The "ingredients not curated" guard throws `InvalidOperationException` → **HTTP 500**; curation UI shows a generic "Failed to approve item," hiding the real cause | Fixed (v0.3.24) |
| N-8 | Missing UI (built) | — | Recipes had **no author control to publish/make-public** — built + shipped this session (v0.3.22) | Resolved |
| N-9 | Mobile layout | High | Recipe form ingredient row: on a Galaxy S23 Ultra (412 CSS px) the **Ingredient name field is 62 px wide** ("In…"), autocomplete panel is clipped to the same width; Qty/Unit fixed min-widths + nested padding eat the row | Fixed (v0.3.23) |
| N-10 | Mobile layout | Med | Recipe form: sticky "Create Recipe / Cancel" action bar covers ~25% of the mobile viewport; nested section padding (12+16+24 px per side) wastes ~26% of the width | Fixed (v0.3.23) |
| N-11 | Validation UX | Med | Recipe form seeds one blank ingredient + step row; tapping "Add …" leaves it behind, and Save then fails **silently** (no error banner, no scroll to invalid field) | Fixed (v0.3.23) |
| N-12 | Bug (backend) | High | **Pantry "Add" does nothing**: `POST /api/Pantry` → **500** because `PantryOrchestrationService.AddPantryItemAsync` throws `Household N has no associated plans` for a household with no meal plan; the UI's `error:` handler is empty so nothing is shown | Fixed (v0.3.23) |
| N-13 | Mobile layout | Med | Pantry add form: fields wrap onto two rows, Add button is not aligned with the Unit field (40 px button vs 76 px field, `align-self:center` + margin), fill-appearance fields differ from the outline fields elsewhere and read as oversized | Fixed (v0.3.23) |
| N-15 | Bug (UI, all viewports) | High | **My Ingredients**: `TypeError: Cannot read properties of undefined (reading 'length')` — template reads `ing.aliases.length` but `/api/Ingredients/my` returns no `aliases`; change detection aborts every pass, so 2 of 3 rows render blank and the "Loading ingredients…" overlay **never clears** and blocks all taps (the offset spinner in the S23 video) | Fixed (v0.3.23) |
| N-16 | Mobile layout | High | **Meal Plan** week grid is `80px repeat(7, 1fr)` with `overflow:hidden` at every width: at 412 px only Mon–Thu are reachable, recipe names wrap one letter per line ("S / m / B / a."), toolbar overflows ("Toc…" = Today) | Fixed (v0.3.23) |
| N-17 | Mobile layout | Med | Global chrome on phones: floating "PANEL" tab overlaps content on every page (clips shopping quantities); footer is pinned to the bottom of the viewport; the nav drawer still shows a desktop "Collapse" item | Fixed (v0.3.23) |
| N-18 | Mobile layout | Low | Shopping "Scaled to household portions" chip wraps to 3 lines; Home "This Week" day strip clips Sunday; Household member email overflows its card over the Profile/Dietary chips | Fixed (v0.3.23) |
| N-14 | Data | Low | Recipe detail lists ingredients in reverse of authored order (Garlic before Chicken on recipe 324) | Fixed (v0.3.24 — ordered by insertion Id; no sort column yet) |
| N-20 | Security (backend) | High | `PUT /api/Ingredients/{id}` had **no ownership check** — any signed-in user could rewrite any catalog ingredient (including FDC rows); and any save wiped the ingredient's nutrition because the request's `Nutrients` defaulted to an empty list | Fixed (v0.3.25) |
| N-21 | Bug (data model) | High | `IngredientNutrient.Ingredient` was mapped `WithMany()` with no inverse, so `Ingredient.IngredientNutrients` rode a shadow FK (`IngredientEntityId`) and was **always empty** — meal-plan whole-food nutrition and any code reading that navigation saw nothing | Fixed (v0.3.25; orphan column dropped from schema.sql — **applied to prod 2026-08-19** via `db/apply.sh --allow-destructive`; apply to nomtest only *after* the tenant is on ≥ v0.3.25, since older API builds still select that column) |
| N-22 | CI | Med | E2E workflow red since v0.3.15: `nom_api_test` unhealthy — the container's `nom` user had no home, so OpenIddict's development certificate could not be persisted and the API crashed at startup; the workflow also tore the stack down *before* printing logs | Fixed (v0.3.26 — Dockerfile home dir; logs before teardown) |
| N-23 | Auth UX | High | Sign-in popover had **no two-factor step** (an MFA-enabled account got "Invalid email or password") and **no feedback** when the email field failed validation (a username or stray space → silent no-op, no request sent) | Fixed (v0.3.29 — RequiresTwoFactor → authenticator/recovery-code field; LockedOut message; inline email errors) |
| N-19 | Desktop layout | Low | Meal Plan week grid is also cramped at ~1400 px with nav + context panel open (day columns ≈45 px) — same one-letter wrapping as N-16, desktop variant | Fixed (v0.3.24 — container query drops thumbnails under 900px) |

_The UI walkthrough was otherwise clean — all ~30 pages render, zero API failures, no real JS errors (the per-page `ERR_CONNECTION_REFUSED` is Cloudflare's own analytics beacon, a sandbox-only false positive). Backend/functional findings from the earlier deep audit are summarized under "Backend" below._

---

## N-1 / N-2 / N-3 — Navigation & menu (UX)

NOM's left nav is a **flat list** grouped by labels: Home · PLAN (Meal Plan, Shopping, Pantry) · COOK (My Recipes, Cookbooks, Dishes, Ingredients, Search) · PEOPLE (Household, Messages) · Admin · Settings · Collapse. ~11 destinations. Visually clean; the friction is **information architecture, not styling**.

### Why it feels difficult

1. **N-1 — the COOK group is 5 items that all mean roughly the same thing.** My Recipes, Cookbooks, Dishes, Ingredients, Search — recipes live *in* cookbooks, dishes *group* recipes, ingredients *compose* recipes, search *finds* recipes. Four-plus doors to one conceptual room, so every visit begins with a "which one?" decision instead of a reflex.
2. **N-2 — Search is redundant** with the persistent header search bar; the nav item competes with it and eats a slot.
3. **N-3 — icons and collapsed state.** "My Recipes" (open book) and "Cookbooks" (stacked books) read almost identically; "Dishes" (abstract shapes) and "Ingredients" (a droplet) aren't obviously food. **Collapsed, the group labels *and* dividers disappear**, so 11 similar-weight icons become an undifferentiated strip — the spatial map (plan-stuff up top, cook-stuff in the middle) is gone, and the ambiguous icons can only be probed via tooltip, not scanned.
4. **Flat priority.** The weekly job (Plan → Shopping) sits at the same visual weight as rare items (Ingredients, Dishes, Messages). Nothing says "start here."

### Recommendations

- **Consolidate the recipe cluster** into one "Recipes" destination with tabs inside (Mine / Cookbooks / Dishes / Ingredients). Five decisions → one. Fold Search into the header bar you already have. COOK goes from 5 items to 1.
- **Rank by the real job:** Home, **Plan**, **Shopping**, Recipes, Pantry, Household as the primary tier; demote Messages/Ingredients/Dishes. Aim for ~6–7 top-level items.
- **Make icons distinct and food-literal** — a plate for Dishes, a carrot for Ingredients, clearly different glyphs for Recipes vs Cookbooks.
- **Preserve grouping when collapsed** — keep the section dividers in the icon rail; use a hover flyout that lists the group's items with labels rather than a single tooltip.

_(Note: NOM's nav is a "too many overlapping doors" problem — everything is one click but there are too many similar destinations. Forge's is worse: a drill-down that hides all but one category. See the Forge tracker.)_

---

## N-4 — Branding leak

Every page shows a **"Powered by Mealie"** footer and a GitHub icon — NOM's upstream origin (it's Mealie-derived) showing through. For a white-label product these should be removed / rebranded.

## N-5 / N-6 / N-7 / N-8 — Recipe publish & curation (found driving the UI-only nutritionist pass, 2026-08-18)

Drove the full "nutritionist sets up recipes → make public → visible to my user" flow **UI-only** on `nomtest`. Created 3 recipes (321 Mediterranean Salmon Quinoa Bowl, 322 Overnight Oats w/ Banana & Almond, 323 Tofu & Kale Stir-Fry), adding 3 missing ingredients through the UI (Firm Tofu, Kale, Brown Rice). Outcome: **321 and 322 are now Public + Approved (visible); 323 is stuck Pending** — and the gaps below are why.

- **N-8 (resolved) — the missing publish control.** A recipe author had **no UI path to make a recipe public**. The recipe-detail page only offered Edit; visibility + curation-submit endpoints existed (`PUT /Recipe/{id}/visibility`, `POST /Curation/submit`) but nothing called them. Built an author-only "Make public" control on the recipe-detail banner (sets Visibility=Public **and** submits for curation in one click, with a Pending/Public status chip and snackbar). Shipped as **v0.3.22** and deployed to `nomtest`. Verified end-to-end: click → recipe goes Pending → shows "Pending review" chip; after admin approval → "Public" chip.

- **N-5 — nutrition is invisible for anything built in the app.** The ingredient catalog on a fresh tenant has **0 `IngredientNutrient` rows** (90 ingredients, 36 nutrient *types* defined, no values — the FDC import is an ops step never run on a tenant, and `CLAUDE.md` forbids authoring nutrition by non-authoritative means). The ingredient form shows nutrients **read-only** (display list, edit mode only) — there is **no field to enter them**. Net effect: every recipe a user builds from the catalog renders an **empty nutrition label** (confirmed on 321/322/323). Seeded demo recipes look fine only because `RecipeNutrition` was seeded directly. **UI-only, there is no way to give an ingredient nutrition** → the app's central value prop (nutrition-aware planning) is unreachable for user content on a fresh tenant. Fix needs either an FDC-backed catalog seed per tenant or an authoritative-source nutrition entry UI.

- **N-6 — user-added ingredients make a recipe permanently un-approvable.** `CurationOrchestrationService.ApproveAsync` (line 199) refuses approval if any ingredient is not curated: `Cannot approve recipe: The following ingredients are not curated: Firm Tofu, Kale, Brown Rice`. Ingredients created via `/ingredient/new` land **NonCurated** (CurationStatusId 9000), and **there is no UI to curate an ingredient** — no `Admin → Food Catalog` route exists in `app.routes.ts`, despite `CLAUDE.md` referencing one. So recipe 323 (built with the three ingredients I added through the UI) can never go public UI-only. The design assumes an ingredient-curation surface that isn't built.

- **N-7 — that guard surfaces as a raw 500.** The un-curated-ingredient check throws `InvalidOperationException`, which the pipeline returns as **HTTP 500** (`POST /api/Curation/approve`). The curation queue UI catches it as a generic "Failed to approve item" — the admin never sees *which* ingredients are the problem. Should be a 400/422 carrying the ingredient list so the UI can show it.

**Fixed in v0.3.25** (2026-08-19): **N-4** footer is "Armory Works Technology · Terms · © NOM" (marketing-site link when `NOM_UI_CONFIG.marketingSite` is set; Mealie + GitHub links gone). **N-5** the ingredient form (create + edit) has a *Nutrition per 100 g* section — nutrition-facts-label nutrients first, "More nutrients" for vitamins/minerals, values stored in each nutrient's default unit with server-side plausibility checks (kcal ≤ 900, mass ≤ 100 g) — and a new `RecipeNutritionService` derives per-serving `RecipeNutrition` from ingredient facts × grams (mass/volume/count-with-reference-serving) on recipe create/update and whenever an ingredient's nutrition changes; seeded/hand-authored labels (`DateCalculated NULL`) are never overwritten. **N-6** submitting a recipe for curation also queues the author's not-yet-curated ingredients; recipe cards in the curation queue list the blocking ingredients with a "Curate these ingredients" action; Food Catalog gained a "Not yet curated (authored)" filter. Also fixed on the way: N-20 (ingredient update authorization + nutrition wipe) and N-21 (shadow-FK navigation).

## N-9 … N-18 — Mobile pass: recipe entry + pantry (Playwright, Galaxy S23 Ultra emulation, 2026-08-18)

Drove the flow **UI-only** through Playwright at the S23 Ultra CSS viewport (412×915, DPR 3.5, touch, Android UA): login → ☰ → My Recipes → New Recipe → type name/description → pick two ingredients via autocomplete → qty/unit → two steps → Create Recipe (saved as **recipe 324**) → ☰ → Household → Create Household ("Audit Household") → ☰ → Pantry → Add Item. Scripts: `recipe-mobile-ui.js`, `pantry-mobile-ui.js`, `pantry-add-debug.js` (session scratchpad; trivially re-creatable — plain `playwright` v1.62 with a custom device descriptor, no S23 preset ships with Playwright).

### N-9 — Ingredient row is unusable at phone width (High)

Measured on the live page: `.nom-recipe-form__ingredient-row` is **306 px** wide (viewport 412 − page 12 − form 16 − section 24, per side). Qty has `min-width: 80px`, Unit `min-width: 100px`, the remove `mat-icon-button` is 40 px, and there are three 8 px gaps → 244 px are spoken for, so the `flex: 3` name field gets **62 px**. The label truncates to "In…", the typed/selected value is invisible ("…ken"), and the `mat-autocomplete` panel inherits the trigger width so options render as "Chicke / Breast". `recipe-form.component.scss` has **no media query at all**.

Fix: below ~600 px switch the row to a 2-line grid — name full-width on line 1; qty / unit / remove on line 2 (`grid-template-columns: 1fr auto` or `flex-wrap: wrap` with `flex-basis: 100%` on the name). Give the autocomplete panel `panelWidth="auto"` (or a min-width) so it is never narrower than its options.

### N-10 — Sticky action bar + nested padding (Med)

`.nom-form__actions` is `position: sticky` and, with the full-width primary + secondary stacked buttons, is **137 px tall** — about 15% of the viewport — permanently covering the bottom of whatever row is being edited (the ingredient rows sit under it in the screenshots). Combined with the 52 px of padding on each side the form has ~74% of the phone width to work with. Recommend: on mobile collapse the padding to one level (≈16 px), and either make the action bar a single row (Cancel as text-button beside Create) or un-stick it below the fold.

### N-11 — Blank seed rows + silent save failure (Med)

The form starts with one empty ingredient row and one empty step row. A user who taps **Add Ingredient / Add Step** first (natural on a phone where the seed row shows only "In…") ends up with a trailing empty required row. Tapping **Create Recipe** then does nothing visible: no `errorMessage()`, no snackbar, no scroll-to-first-invalid — the invalid field is off-screen above the sticky bar. Recommend: mark all controls touched + scroll the first `.ng-invalid` into view on submit, and drop empty trailing rows before validating (or don't seed rows).

### N-12 — Pantry "Add" is a 500 (High, backend + UX)

Repro through the UI: Pantry → Add Item → "garlic" → 2 → Piece → **Add**. Result: nothing changes, form stays filled. Network: `POST /api/Pantry` → **500 Internal server error**. nom-api log (`tenant-nomtest-nom-api-1`):

```
System.InvalidOperationException: Household 1 has no associated plans. Create a plan first.
   at Nom.Orch.Services.PantryOrchestrationService.AddPantryItemAsync (PantryOrchestrationService.cs:74)
   at Nom.Api.Controllers.PantryController.AddPantryItem (PantryController.cs:53)
```

`PantryItem.PlanId` is a required FK and the service borrows the household's first plan; a fresh household (or any household that hasn't planned yet) has none, so pantry entry is impossible until a meal plan exists — an ordering the UI never states. Same shape as N-7: business-rule `InvalidOperationException` surfaces as 500, and `pantry.component.ts` `addItem()` has an empty `error:` handler, so the user gets **no feedback at all**.

Fix (two halves): (a) backend — make `PlanId` nullable on `PantryItem` or auto-create/attach a plan; at minimum map the exception to 400/409 with a message; (b) UI — show the API error (snackbar/inline) and, if the rule stays, disable Add with a "create a meal plan first" hint.

### N-13 — Pantry add form layout on mobile (Med)

`.nom-pantry__add-fields` is `flex-wrap: wrap` with `min-width: 200px` on the ingredient field and `140px` on the unit, so at 322 px content width it wraps to **Ingredient + Qty / Unit + Add**. The Add button (`align-self: center; margin-top: 4px`, 40 px tall) sits mid-way down a 76 px `mat-form-field` (which reserves subscript space) — the "not inline" look. The pantry fields also use the default *fill* appearance while every other form in the app uses `appearance="outline"`, and default density, so they read as taller/heavier than the recipe form. Recommend: `appearance="outline"` for consistency, `subscriptSizing="dynamic"` (or `-2` density) to drop the reserved helper-text height, and on mobile stack fields full-width with a full-width Add button below (or align the button to the field's input box, not its wrapper).

### N-14 — Ingredient order not preserved (Low)

Recipe 324 was entered Chicken Breast → Garlic; the detail page lists Garlic first. `RecipeIngredient` has no sort column and the detail projection has no `OrderBy`, so ingredient order is whatever Postgres returns.

### N-15 — My Ingredients crashes change detection; loading overlay stuck (High, not mobile-specific)

Reproduced at 412 px **and** 1400 px. `/api/Ingredients/my` returns `[{id, name, description, curationStatus, nutrients, …}]` — no `aliases`. `my-ingredients.component.html` does `@if (ing.aliases.length > 0)`; the first row throws → the CD pass aborts → rows 2–3 never bind (blank names, and the `chevron_right` ligature shows as raw text) → `nom-loading-overlay` never re-renders, so "Loading ingredients…" stays up forever with `pointer-events: auto`, blocking every tap until reload. This is the "offset spinner" in the S23 screen recording (its horizontal offset is just the 280 px min-width panel centred in a viewport whose layout is wider than the screen). Fix: `ing.aliases?.length`, and make `IngredientEditModel.aliases` optional or have the API always emit `[]`. Also on `/onboarding` right after login "Checking your progress… / Loading profile options…" linger for several seconds.

### N-16 — Meal Plan week grid on phones (High)

`plan.component.scss` has no viewport media query (only `@media print`). The calendar is `grid-template-columns: 80px repeat(7, 1fr); overflow: hidden`, so at 412 px each day column is ~47 px: recipe titles wrap one letter per line, Fri/Sat/Sun are **cut off and unreachable** (overflow hidden, no horizontal scroll), the toolbar (print / AI / rules / Shuffle / ▾ / ‹ / Today / ›) overflows the row, and a "Mark as cooked" tooltip stays pinned after touch. Recommend a phone layout: one day at a time (day tabs / swipe) or a vertical day-stack; at minimum `overflow-x: auto` + a fixed column min-width, and collapse the toolbar to icon-only + overflow menu.

### N-17 / N-18 — Global chrome + small wraps (Med / Low)

The right-edge "PANEL" toggle is a fixed 40 px tab that sits over content on every page (it clips "2 × 5 oz bag" in Shopping). The footer ("Powered by Mealie · Terms · © 2026 NOM") is pinned to the viewport bottom on phones and permanently costs ~50 px. The mobile nav drawer fits (Home→Settings) but keeps the desktop-only "Collapse" item. Small wraps: Shopping "Scaled to household portions" chip → 3 lines; Home "This Week" strip shows Mon–Sat and clips Sun; Household member row lets a long email overflow the card.

**Fixed in v0.3.23** (2026-08-18): N-9/N-10 (ingredient row wraps on phones, one level of form padding, single-row sticky action bar, `panelWidth="auto"` on the ingredient autocomplete — which also exposed and fixed a `displayWith` bug that blanked the field after selection); N-11 (blank seed rows pruned on submit, first invalid field scrolled/focused, error banner); N-12 (pantry service resolves/creates a household plan instead of throwing; controller maps business-rule errors to 409; pantry UI shows the message); N-13 (outline fields, dynamic subscript, stacked phone layout, Add aligned to field height); N-15 (`aliases?.length`, API now emits `aliases` + `curationStatusName`); N-16 (phone day strip + single-day grid, wrapping toolbar, no touch tooltips on cell buttons); N-17 (header Panel button replaces the floating tab on phones, page-level scrolling with sticky header — footer no longer pinned, nav Collapse hidden, closed drawers `visibility:hidden` so they can't widen the page); N-18 (shopping chip nowrap + toolbar wrap, week strip fits 7 days, member name wraps). Global: `$font-size-2xl/3xl` are now fluid `clamp()` tokens (24→32px / 28→40px).

_Sweep method: hamburger → every nav destination, screenshot + DOM audit (horizontal overflow, elements past the viewport edge, ≥3-line wrapping in boxes < 70 px, text < 11 px). Pantry, My Recipes, Cookbooks, Dishes, Search, Messages, Admin, Settings passed the automated checks and look acceptable by eye at 412 px._

_Screenshots from the run (viewport + full-page) are in the session scratchpad `shots/` folder; the recipe-form and pantry ones are the evidence for N-9/N-13._

## Backend / functional (load-bearing — see the deep audit)

Most user-impactful items from the earlier deep audit — **all addressed in v0.3.27** (2026-08-19):

- ~~**Dietary restrictions set through the UI never affect meal planning or search**~~ — **Fixed.** UI restrictions reference a *category* (RestrictionTypeId); planning only honoured `Restriction.IngredientId`. A shared `HouseholdRestrictionResolver` now expands each member's (and plan-wide) restrictions through the category's `RestrictionCriterion` rows (exact ingredient or ILIKE pattern on names **and aliases**, with severity) and is used by shuffle, food-group top-up and recipe search — the same source the recipe-detail warning already used, so the two agree. Only medical conditions had criteria; `DefaultRestrictionCriteria` seeds name-keyed baselines (allergies 5, intolerances 4, diets/religious 3) at API startup for any restriction type that has none, so every tenant gets Nut/Egg/Soy/Fish/Shellfish/Sesame/… allergies, Gluten-/Dairy-Free, Vegan/Vegetarian/Pescatarian, Kosher/Halal, Low-FODMAP, etc. enforced. Admin → Diet Categories edits win (types with any criteria are untouched).
- ~~**Meal-plan shuffle can destroy a week**~~ — **Fixed.** Delete + rebuild run in one transaction (relational stores).
- ~~**Message-injection IDOR**~~ — **Fixed.** `SendMessageAsync` requires the sender to be a thread participant (401 otherwise).
- ~~**Cross-tenant delete of food-group rules**~~ — **Fixed.** Delete is bound to the authorized household id.
- ~~**SmartShoppingList serves fabricated prices/nutrition**~~ — **Quarantined.** The controller is behind `Features:SmartShoppingList` (default off → 404, hidden from Swagger); no UI consumer existed. The latent `InvalidCastException` in its nutritional analysis is fixed for when it is enabled.
- ~~**Open redirect** in the grocery OAuth callback~~ — **Fixed** (only same-origin path returns are honoured). ~~**Email confirmation not enforced** at login~~ — **Fixed**: `SignIn.RequireConfirmedEmail` is on whenever SMTP is configured (override `Auth:RequireConfirmedEmail`); the sign-in popover recognises the 401 `NotAllowed` and offers "Resend confirmation email".
- **Orphan API endpoints** — inventory (route-vs-nom-ui heuristic, 2026-08-19): 334 routes; controllers with *no* UI/doc/E2E reference: RecipeAdvanced (21), RecipeSuggestion (18), SmartShoppingList (12), RecipeBulkOperations (12), RecipeAdvancedSearch (9), MeasurementCategory (7), Audience (7), RecipeImport (6), Invitation (6), StagedImport (1). **v0.3.28 quarantines the four recipe layers + SmartShoppingList (72 endpoints) behind `Features:*` flags (404 + hidden from Swagger, default off; enable per tenant).** Left reachable: MeasurementCategory/Audience/Invitation/RecipeImport/StagedImport — plausibly used by Brigade, the scraper sidecar or ops; needs an owner's call before gating.

## Tenant-provisioning note (fixed)

A fresh NOM tenant's `/api/*` initially 500'd because the staged initdb `schema.sql` was 33 tables behind the deployed v0.3.20 image. Fixed by re-staging current `schema.sql`/`seed.sql` to `/opt/nommeal-deploy/api-box/db/`. **Lesson: the staged initdb snapshot must track the deployed nom image version.**
