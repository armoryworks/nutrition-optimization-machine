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
"curated-only" meal-plan gate keeps trusting only reviewed data. Clean,
high-confidence records may auto-promote; borderline ones queue for an admin.

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

### FDC branded bulk import — BLOCKED (execution)
The existing importer (`Nom.Import/Services/FdcFoodImporterService.cs`) is a
SQL-staging pipeline (`DataImportScripts/01_create_staging*.sql`,
`03_transform_from_staging.sql`) with settings-driven quality filtering
(`QualityFilterSettings`). Extending it for Branded Foods + a validator post-pass
that lands records as `PendingCuration` **cannot be verified here**: it needs the
multi-GB FDC bulk download and a Postgres instance to stage into.
**To unblock:** run against a real DB with the FDC dataset; extend the transform
to include the branded tables; add a C# post-pass that runs
`FoodDataQualityValidator` (computing per-100g + serving from the staged nutrient
rows) and sets curation status.

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
