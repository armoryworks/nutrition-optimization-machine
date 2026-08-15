# Food Catalog Ingestion (whole foods & branded products)

How NOM populates the ingredient catalog with directly-edible **whole foods**
(apples, honey) and **branded products** (protein bars, frozen dinners) so they
can be scheduled as standalone meal items and satisfy household food-group
requirements. Companion to the standalone-foods / food-group feature.

## Principle

Nutrition **facts** are not copyrightable; we source them from the authoritative
public dataset (USDA FoodData Central) rather than scraping. Any web fetch is a
narrow, whitelist-gated fallback that reads a publisher's **own** structured data,
governed by the same operator-scraper model as recipe import
(`docs/scraper-integration.md`). NOM's public codebase never scrapes broadly.

## Sources (in priority order)

1. **USDA FoodData Central (FDC) bulk datasets — the spine.**
   - *Branded Foods* = the USDA Global Branded Food Products Database:
     manufacturer-submitted products (Nestlé, Kraft, General Mills, …) with
     full nutrition + serving sizes.
   - *Foundation Foods* + *SR Legacy* = produce, honey, staples.
   - **License: CC0 1.0 Universal (public domain).** Commercial use &
     redistribution permitted; USDA *requests* (not requires) a source citation.
     Bulk downloads need **no account or API key** (only the live API needs a
     free key). Confirmed at <https://fdc.nal.usda.gov/download-datasets/>.
   - Caveat: manufacturer data is unverified and the Label Insight branded feed
     stopped updating 2023-11-16 — records can be stale/noisy (see quality gate).

2. **Local AI enrichment (Ollama).** Classifies each food into a food group,
   flags directly-edible whole foods, normalizes/dedupes names. Enhances — never
   replaces — the deterministic heuristics.

3. **Whitelist-gated manufacturer fetch — gap-fill only.** For products missing
   from FDC, read the manufacturer's *own published* structured nutrition
   (schema.org `NutritionInformation` JSON-LD) from **admin-approved domains
   only**, via the operator-scraper contract. Never general web scraping.

## Pipeline

```
FDC bulk CSV ─▶ staging ─▶ transform ─▶ QUALITY GATE ─▶ classify ─▶ Ingredient
                                          (reject/         (food group,   (curation:
                                           quarantine)      IsWholeFood)   PendingCuration)
                                                                              │
gap-fill (gated JSON-LD) ─▶ QUALITY GATE ─▶ classify ─────────────────────────┘
```

Imported records land **non-curated / pending review** — the severe-restriction
"curated-only" meal-plan gate keeps trusting only reviewed data.

> **Operational gotcha:** meal-plan food-group top-up draws candidates only from
> **Curated** ingredients. Foods imported with the default status are therefore
> *invisible to planning* until someone reviews them — an import alone does not
> make food-group rules start filling plans.
>
> `--import-fdc <dir> --curated` lands Foundation Foods as Curated instead. That
> is defensible for Foundation specifically (USDA-authored reference data with
> validated nutrition) and is what makes the feature work out of the box. The
> **Branded** catalog should stay pending — it is manufacturer-submitted and
> unverified, which is precisely why the quality gate rejects ~4% of it.

## Quality gate — DONE ✅

`Nom.Data/Nutrition/FoodDataQualityValidator.cs` (pure, deterministic, 11 tests).
Rejects physically implausible records so noisy FDC branded data doesn't pollute
the catalog:

- Energy ≤ 900 kcal/100g; each macro ≤ 100 g/100g; macro sum ≤ 105 g/100g.
- Required: name (alphabetic, ≤ 200 chars), calories, macros.
- **Atwater cross-check**: 4·protein + 4·carb + 9·fat must land within ±30% of
  stated calories (skipped below 20 kcal so low-calorie foods don't trip it) —
  catches mis-scaled units.

**Serving size is NOT validated.** Per-100g nutrition is the stable, person-
independent fact. The amount a person actually eats is derived per-person from
their caloric need / metabolic rate (the same way portions scale recipes), not
stored as a fixed per-food value. So the catalog stores per-100g facts; serving
is a downstream, per-person computation.

## Provenance & re-runnability

Every FDC-sourced ingredient already carries `FdcId` (+ `FdcDataType`), so FDC
records are identifiable for a clean purge-and-rerun (`WHERE "FdcId" IS NOT NULL`).
`Nom.Import --purge-fdc` soft-deletes FDC-sourced ingredients (skipping any still
referenced by a recipe) so a bad import batch can be rolled back without touching
authored data. **Gauge quality in staging first; never import straight into prod.**

Both the FDC import and the gated fetch call this before persisting.

## Also DONE ✅

- `Ingredient.FoodGroupId` (10-group vocabulary) + keyword auto-classifier +
  manual admin override (`CurationController`).
- `Ingredient.IsWholeFood` flag + whole-food-first ingredient search + standalone
  scheduling into meal-plan slots.

## Remaining work & blockers

### FDC Foundation import — BUILT ✅ + gauged in staging
`Nom.Import/Services/FdcFoundationImportService.cs` — a focused CSV loader (not the
old SQL-staging path): reads `food.csv` + `food_nutrient.csv`, applies
`FoodDataQualityValidator` to the per-100g macros, classifies by **FDC food
category** (authoritative — 99% classified, far better than name keywords), and
lands accepted foods as `PendingCuration` ingredients (idempotent by `FdcId`,
name-deduped). Run: `dotnet run -- --import-fdc <csv-dir>`.

**Gauged against the real 2025-12-18 Foundation dataset in a staging DB:** 436
foundation foods → **317 accepted** (99% classified, all quarantined), 70 rejected
(mostly incomplete records missing a macro), 49 name-dupes skipped. Spot-checks all
correct (Beef→Protein, Almond butter→Nuts/Seeds, Kiwi→Fruits, Khorasan→Grains).

**Finding from the run:** many Foundation foods report energy only under the
Atwater-factor nutrient ids (2048 specific / 2047 general), not general Energy
(1008) — the loader now accepts any, preferring the most specific. Fixing this
took acceptance from 107 → 317.

Per-100g `IngredientNutrient` rows are persisted against the seeded Nutrient rows
(resolved by name), along with `ReferenceServingGrams` from `food_portion.csv`
(median of single-unit portions).

### FDC Branded import — BUILT ✅ + gauged in staging
`Nom.Import/Services/FdcBrandedImportService.cs` —
`dotnet run -- --import-fdc-branded <csv-dir> [--limit N]`. This is the catalog
with the packaged products people schedule as standalone items (protein bars,
frozen dinners, yogurts) — i.e. the "big producers" case. The dataset is ~2M rows
across ~2.7 GB of CSV, so all three files are **streamed** and the candidate set is
**bounded by `--limit`**; US-market, non-discontinued rows are preferred.

**Gauged on the real 2025-12-18 branded dataset (3 000-row sample):** 2 677
accepted (89%), 93% classified, ~94% flagged directly-edible, **100% with a
reference serving** (branded records nearly always publish `serving_size` — this
is where the reference-gram basis pays off). 131 rejected, **109 of them Atwater
mismatches** — exactly the manufacturer-submitted noise the gate exists for.

**Precision fix found by spot-checking the run:** the compound retail category
`"Ketchup, Mustard, BBQ & Cheese Sauce"` matched the `cheese` → Dairy keyword, so
BBQ and duck sauce were classified Dairy — which would have let a condiment
satisfy a household *dairy* minimum. Condiment/sauce categories are now
deliberately left **unclassified** (precision over recall); nut/seed butters are
checked first so they stay Nuts/Seeds. Covered by regression tests.

**Recommended rollout:** do **not** bulk-load all ~2M branded rows — it would
swamp ingredient search and the catalog for little gain. Import a bounded, curated
subset (`--limit`), review the pending-curation queue, and grow it as real demand
appears (a UPC lookup path would later fetch specific products on demand).

### AI enrichment (food group + IsWholeFood) — BUILT ✅ (runtime pending)
`Nom.Import/Services/FoodGroupEnrichmentService.cs` — a batch job that classifies
ingredients: deterministic keyword classification always runs
(`FoodGroupHeuristics`, shared with nom-api), and when an `IAiService` is provided
the local model refines the food group and supplies the whole-food flag. AI output
is validated against the known vocabulary (`FoodGroupCatalog`), so a hallucinated
group is discarded (`FoodEnrichmentParser`, unit-tested).

**Placement decided:** it lives in `Nom.Import`, NOT open-core nom-api and NOT the
`nom-commerce` overlay — Nom.Import already owns the AI-enhancement infrastructure
(`IAiService`/`OllamaService`/`AiEnhancementSettings`) and enrichment is inherently
batch. nom-api keeps only the deterministic heuristic; no Ollama client is
re-introduced into open-core and nothing entangles with the overlay.

**Remaining (runtime only):** DI wiring in Nom.Import's `ServiceCollectionExtensions`
(register `OllamaService` as `IAiService` when the Ollama URL is configured) and a
run against the Server-2 Ollama box. Config-gated: absent AI → heuristic-only.

### Gated manufacturer gap-fill — BLOCKED (source selection + ToS)
Reads schema.org `NutritionInformation` JSON-LD from admin-approved domains via
the operator-scraper contract. A `ScrapingSource`-style whitelist row must carry
a recorded **ToS + robots.txt review determination** before any domain is
enabled.
- **Blocker:** specific domains can't be chosen until the FDC import reveals what
  is actually missing, and each candidate's ToS + robots.txt must be read and
  recorded first (per project policy — the FDC terms were reviewed this way and
  cleared).
- Note: schema.org nutrition is defined for `Recipe`/`MenuItem`, so many product
  pages won't expose machine-readable nutrition; expect partial yield.
