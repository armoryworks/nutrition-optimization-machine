# NOM — Audit & UX Findings Tracker

_Living document. Maintained during the ongoing audit of NOM. Companion tracker for Forge lives at `forge/docs/AUDIT-TRACKER.md` — keep the two separate._

Last updated: 2026-08-18.

## Audit access

- **Instance:** `nomtest` (NOM, SplitUi tenant — nom-ui on the web box, nom-api + Postgres on the api box). Publicly reachable at `https://nomtest.nommeal.com`.
- **User:** `daniel.hokanson+test@armoryworks.com` / `NomAudit-2026!x`, email confirmed, admin claims `CanManageCuration` + `CanManageUserRoles`.

## Open findings — summary

| # | Area | Sev | Finding | Status |
|---|------|-----|---------|--------|
| N-1 | Navigation | Med (UX) | The "COOK" cluster is 5 overlapping recipe destinations → "which door?" friction | Open |
| N-2 | Navigation | Low (UX) | "Search" duplicated (nav item + header search bar) | Open |
| N-3 | Navigation | Low (UX) | Near-duplicate / abstract icons; collapsed rail loses grouping | Open |
| N-4 | Branding | Med | "Powered by Mealie" footer + GitHub icon on every page (upstream leak) | Open |

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

## Backend / functional (load-bearing — see the deep audit)

Most user-impactful items from the earlier deep audit:

- **Dietary restrictions set through the UI never affect meal planning or search** — the UI `RestrictionRequest` DTO carries neither `IngredientId` nor `Severity`, the only fields shuffle/search enforce; the severe-allergy Curated-only gate is unreachable, yet the recipe-detail page still warns about the very recipe shuffle just planned. Looks safe, isn't. **(Highest priority.)**
- **Meal-plan shuffle can destroy a week** — the delete's `SaveChanges` commits ~420 lines before the rebuild, no transaction.
- **Message-injection IDOR** — `SendMessageAsync` inserts into a client-supplied `threadId` with no participant check.
- **Cross-tenant delete of food-group rules** — authz checks the query-string household, delete operates on an unbound rule id.
- **SmartShoppingList serves fabricated prices/nutrition** from live endpoints, with a latent `InvalidCastException`.
- **Open redirect** in the grocery OAuth callback; **email confirmation not enforced** at login.
- **~150 orphan API endpoints (~40%)** with no UI consumer — an older superseded layer worth quarantining.

## Tenant-provisioning note (fixed)

A fresh NOM tenant's `/api/*` initially 500'd because the staged initdb `schema.sql` was 33 tables behind the deployed v0.3.20 image. Fixed by re-staging current `schema.sql`/`seed.sql` to `/opt/nommeal-deploy/api-box/db/`. **Lesson: the staged initdb snapshot must track the deployed nom image version.**
