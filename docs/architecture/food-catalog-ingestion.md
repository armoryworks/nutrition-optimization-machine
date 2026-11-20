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
- **Atwater cross-check**: 4·protein + 4·carb + 9·fat, plus 7·alcohol when the source
  reports alcohol (FDC nutrient 1018, or an `alcohol` label column), must land within
  ±30% of stated calories (skipped below 20 kcal so low-calorie foods don't trip it) —
  catches mis-scaled units. Alcohol also counts toward the macro sum. It is not a
  bypass: white wine at 82 kcal with 10.3 g alcohol passes (82.8 computed), the same
  wine stated at 300 kcal still fails, and a food with no alcohol figure is judged on
  its three macros as before.

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

`--purge-fdc` takes **every** FDC food no recipe line uses, which on a catalog that has
been curated is most of the USDA foods, not one batch. To take back one
`--import-fdc` run use its run stamp instead (see *One run, one stamp* below).

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
(resolved by name), along with `ReferenceServingGrams` (median of single-unit portions),
`GramsPerMilliliter` and `GramsPerItem` from `food_portion.csv` (see *Household portions*
below).

**One run, one stamp.** `--import-fdc <csv-dir> [--sr-legacy] [--curated] [--only <ids-file>]
[--dry-run]`:

- `--dry-run` reads the files and the catalog and prints the same report without saving.
- `--only <ids-file>` reads just the listed FDC ids (separated by whitespace or commas) and
  names any that are not in the file as that data type. Without it the whole file is read,
  and every food already in the catalog also gets the nutrients and density it lacks, so a
  plain SR Legacy run after a new Nutrient row is added writes that nutrient onto every
  SR Legacy food.
- An FDC id held by a **retired** (soft-deleted) food counts as existing: the food is
  skipped and reported, never recreated (`FdcId` is unique across deleted rows too) and
  never backfilled.
- A run is one transaction. Everything it writes carries one timestamp, printed as the
  run stamp, and is journaled in `audit."AuditLogEntry"` (entity `Ingredient`, change type
  `FdcImport`): one row per food created (`FdcId`), per existing food that gained nutrient
  rows (`IngredientNutrients`, the count) and per density filled (`GramsPerMilliliter`).
- `--import-fdc --undo <stamp> [--dry-run]` takes that run back from the journal: a food it
  created is soft-deleted unless a recipe line already uses it (those are listed and their
  journal rows kept), the nutrient rows it added to existing foods are removed, a density it
  filled is cleared when unchanged, and its attach proposals still pending are rejected. A
  retired food keeps its FDC id, so importing again skips it; restore one with
  `UPDATE recipe."Ingredient" SET "IsDeleted" = false, "DeletedAt" = NULL WHERE "FdcId" = '<id>'`.

### Attaching a USDA food to a catalog food by id — BUILT ✅
`--import-fdc` offers a catalog food a USDA record only when the two share a name. A seeded
food with an everyday name ("Dry White Wine", #164) and no USDA record takes one in place
with `--attach-fdc <csv-dir> <pairs.tsv> [--sr-legacy] [--dry-run]`. `pairs.tsv` has a
header row `ingredient_id<TAB>name<TAB>fdc_id` and one food per row:

```
ingredient_id	name	fdc_id
164	Dry White Wine	174837
```

Each row becomes one **pending** `fdc_attach` proposal (source `fdc:<id>`, batch
`fdc-attach`) carrying the USDA values, reference serving, g/ml and item weight; nothing on
the food changes until it is applied in Admin → Food Catalog → Links (the certain-links
lane applies `fdc:` attaches on its own when it is on). A row is passed over, with the
reason printed, when the food is missing or deleted (`food_not_found`), goes by another name
(`name_differs`), already has that record (`already_attached`) or another
(`has_usda_record`), already has a proposal waiting (`already_proposed`), the FDC id is held
by or promised to another food, retired ones included (`fdc_id_taken`), the id is not in the
file as that data type (`fdc_food_not_in_file`; pass `--sr-legacy` for an SR Legacy id), or
the USDA values fail the quality gate.

Applying it keeps the food's id, name and recipe lines, sets `FdcId` and `FdcDataType`,
fills only the nutrients, serving, g/ml and item weight the food lacks, stamps each new
`IngredientNutrient` row with the FDC id, keeps the USDA description as the food's
`fdc:description` alias (the name the link matcher's comma segments work on), and journals
every column it set in `audit."AuditLogEntry"` (entity `Ingredient`, change type `FdcAttach`,
old and new value). The API release must be deployed before the importer writes the
proposal: the nutrient list is stored as `[nutrientId, amount, measurementId]` triples so a
food with every stored nutrient fits the 2,047-character proposal column, and an older API
does not read that shape.

`--attach-fdc --undo <pairs.tsv> [--dry-run]` takes the same pairs back from that journal:
each column returns to its old value when it still holds what the attach gave it, the
stamped nutrient rows and the description alias are removed, the proposal is marked
Rejected and the food's recipe lines are marked changed so their labels are recalculated.
A proposal still pending is simply rejected. An attach applied before the journal existed
is reported as `not_journaled` and left alone.

### Household portions → gram conversions — BUILT ✅
A recipe line in cups or pieces only counts toward nutrition when its ingredient can
turn that unit into grams (`RecipeNutritionService.GramsFor`): volume units need
`Ingredient.GramsPerMilliliter`, count units (Piece, Clove, Stalk, Dozen…) need
`Ingredient.GramsPerItem`, the weight of one item (falling back to
`ReferenceServingGrams` when it is unknown). The item weight is kept apart from
`ReferenceServingGrams` on purpose: that one is the reference serving a standalone food
is planned in (meal-plan food-group top-ups count "1" as one reference serving), and one
raspberry (1.9 g) or one garlic clove (3 g) is not a serving. Both conversions come from
USDA's `food_portion.csv` (+ `measure_unit.csv`), deterministically — never from a model:

- **Grams per mL** (`UsdaVolumeDensity`): portion grams ÷ (amount × mL), with
  cup = 236.588 mL, tbsp = 14.787, tsp = 4.929, fl oz = 29.574 (also pint, quart, mL, L).
  Foundation names the unit by `measure_unit_id`; SR Legacy leaves it undetermined
  (9999) and puts it in the modifier. A plain measure beats a prepared one
  ("cup" over "cup, chopped"), a larger measure beats a smaller one, purchase yields
  ("pint as purchased, yields") never count, and results outside 0.05–3 g/mL are dropped.
- **Weight of one item** (`UsdaCountWeight`): only discrete items count — a portion
  whose unit is a piece word (clove, each, piece, fruit, stalk, item, unit, whole, egg,
  ear, spear, pod, sprig, link, patty, fillet, breast, thigh, drumstick, wing, leg,
  chop), a bare size ("medium", "large"), or the food's own name ("1 cucumber" for
  *Cucumber, with peel, raw*; "10 raspberries" for *Raspberries, raw*). Wholes that hold
  many pieces (head, bulb, bunch, loaf, rack, pie, pizza, cake, a whole chicken, turkey, duck, goose or capon — also from "½ goose") and fractions
  (slice, wedge, strip, half, chopped/sliced/diced) never count, nor do weights,
  volumes, servings or packages. So "1 piece" of garlic is a **clove** (3 g), never a
  bulb, decided by the portion text rather than by food name.
  **Which one wins:** the size the food's own description names (*Eggs, Grade A,
  Large* → large), then **medium**, then an unsized item, then large, small, and
  extra-large/jumbo/mini; a single-item portion beats a multi-item one
  ("1 clove" over "3 cloves"), file order breaks ties, and a multi-item portion is
  divided by its amount. Rounded to 0.01 g.

**Cans and packages** are the exception among count units: a can, jar, bottle, box or bag
holds whatever its label says, so neither the weight of one item nor the reference serving
stands in for it (one can of tomatoes is not one tomato). A line weighs what it says:

- `IngredientLineParser` reads a stated size as the amount, in the size's own unit:
  "1 (15 oz) can black beans" → 15 Ounce, "2 cans (15 oz each) corn" → 30 Ounce,
  "1 (13.5 fl oz) can coconut milk" → 399.242 Milliliter, "1 (3-pound) chicken" → 3 Pound,
  "1 1/2 (180g) cups flour" → 180 Gram. A size that could be each or total
  ("4 (6-ounce) salmon filets", "2 cans diced tomatoes (14.5 oz)") is not read, and
  nothing above 15 kg is.
- A container with no stated size is stored in a **container unit**: Can, or Package for a
  jar, bottle, box, bag, packet, envelope, carton, tub, tube or pouch
  (`measurement."Measurement"."IsContainer"`). `GramsFor` gives a container unit no weight,
  so the line reports `no-conversion` and the recipe page asks the cook for grams or ounces.

Lines stored before the parser read sizes are brought over by an admin lane next to relink
(`PackageLineService`, curation-gated, scraped recipes only, lines stored as Piece, Can or
Package). The startup repair (`ScrapedRecipeRepairService`) leaves these lines alone so
that every change is journaled:

```
GET  /api/FoodCatalog/reread/preview?sampleSize=50     # counts per transition, lines that stop or start counting, samples; no writes
POST /api/FoodCatalog/reread/apply?maxLines=5000       # one audit.AuditLogEntry (ChangeType LineReread) per changed line
POST /api/FoodCatalog/reread/undo?sinceUtc=<utc time>  # restores each line that still holds what the re-read gave it
```

Apply also marks the unchanged can and package lines that were weighed as one item or one
serving as modified, so the nutrition sweep recalculates their recipes' labels. On an
existing database run `./db/apply.sh` and `db/fix-container-units.sql` before the API
version that reads packages starts: an import whose unit has no measurement row falls
back to Gram.

**Backfill for foods already in the catalog** (no nutrient changes):

```
dotnet run -- --backfill-portions <foundation-csv-dir> <sr-legacy-csv-dir> --dry-run   # report only
dotnet run -- --backfill-portions <foundation-csv-dir> <sr-legacy-csv-dir>             # write
dotnet run -- --backfill-portions --undo                                               # reverse
```

It covers every non-deleted ingredient with an `FdcId`, plus catalog rows linked to a
USDA food by an applied `fdc_link` proposal. Only **empty** fields are filled — a
value already present (admin-entered or imported) is kept and listed as `kept` with
the USDA figure for comparison — so a rerun is a no-op. Most Foundation foods publish
no portions; for those the USDA food with the **same description** in the other
supplied release (normally SR Legacy) is used, and its id is the provenance. Each
filled value is logged as an *Applied* `FoodCatalogProposal` (batch `fdc-portions`,
field `grams_per_milliliter` / `grams_per_item`, source `fdc:<id the portion
came from>`, reason quoting the portion). `--undo` clears every logged value that is
still unchanged and marks its row Rejected. Touched ingredients bump their recipe
lines' `LastModifiedDate`, so the API's nutrition sweep recalculates those labels.
The report gives no-conversion recipe lines and recipes before → after, and how many
already-counted piece lines change weight because they used the reference serving as a
per-item weight (e.g. "4 strawberries" weighed as four 144 g cups, now four 12 g berries).
`GramsPerItem` is a new column (`./db/apply.sh` adds it); `ReferenceServingGrams` is
never written by the backfill.

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

### Label nutrients + nutrient backfill — BUILT ✅
Both FDC importers read the full FDA label set through `Nom.Import/Services/FdcNutrients.cs`
(FDC nutrient ids verified against the 2025-12-18 `nutrient.csv`): energy (1008, else
2048/2047), protein (1003), carbohydrate (1005), fat (1004), total sugars (2000, else
1063), added sugars (1235), saturated fat (1258), trans fat (1257), cholesterol (1253),
sodium (1093), dietary fibre (1079, else 2033), vitamin D (1114 µg, else 1110 IU ÷ 40),
calcium (1087), iron (1089) and potassium (1092). Amounts are stored per 100 g in the
seeded Nutrient row's unit (g, mg or µg); a nutrient whose row is missing or stored in a
different unit is skipped, never converted by guesswork. Each non-macro value passes
`FoodDataQualityValidator.CheckComponent` (per-100 g ceiling; saturated/trans fat may not
exceed total fat, fibre/sugars may not exceed carbohydrate); a failed value is dropped on
its own and reported, never zeroed.

Foods imported before this have only the macros. `dotnet run -- --backfill-nutrients
<csv-dir> [--dry-run]` fills the missing values onto every ingredient already linked by
`FdcId`, from one FDC directory at a time (run it once per Foundation, SR Legacy and
Branded directory). It only writes nutrients an ingredient has no row for, so it never
overwrites and a rerun writes nothing; a soft-deleted row counts as present, so a value an
admin removed is not put back. Every row one run writes carries the same `CreatedDate`,
printed at the end of the report, so a run can be undone exactly:
`DELETE FROM nutrient."IngredientNutrient" WHERE "CreatedDate" = '<printed stamp>';`.
Trans fat needs its Nutrient row first: run `db/fix-trans-fat-nutrient.sql` on databases
seeded before it existed.

Alcohol (FDC 1018, grams of ethanol) is read the same way and stored against the
`Alcohol` Nutrient row, which has no parent; run `db/fix-alcohol-nutrient.sql` on databases
seeded before it existed. Only SR Legacy reports it (73 foods above zero, the rest zero),
so the first SR Legacy backfill after the row exists writes one mostly-zero row per SR
Legacy food. The stored value is what lets the catalog audit count alcohol energy; without
the row a wine is still accepted at import but the audit reports it as `atwater_mismatch`.
The recipe label does not list alcohol.

### Vitamins and minerals + SR Legacy supplement — BUILT ✅
`FdcNutrients` also reads the vitamins and minerals that carry an FDA Daily Value, into the
seeded Nutrient rows of the same name: vitamin A (1106, µg RAE), vitamin C (1162), vitamin E
(1109, alpha-tocopherol), vitamin K (1185, phylloquinone), thiamin (1165), riboflavin (1166),
niacin (1167), vitamin B6 (1175), folate (1190, µg DFE), vitamin B12 (1178), biotin (1176),
pantothenic acid (1170), choline (1180), phosphorus (1091), iodine (1100), magnesium (1090),
zinc (1095), selenium (1103), copper (1098), manganese (1101), chromium (1096), molybdenum
(1102) and chloride (1088). A quantity FDC reports only in another definition is left unknown
rather than converted: vitamin A in IU is not RAE, total folate is not DFE. Niacin is the
preformed niacin FDC reports, so it is a floor for the niacin equivalents a label counts. SR
Legacy and Foundation carry no added sugars, chromium or chloride, and biotin, iodine and
molybdenum for a few dozen foods only. Each value passes the same per-100 g ceiling check
(`FoodDataQualityValidator.MaxComponentPer100g`); `--backfill-nutrients` fills them onto
foods imported earlier. `FdaDailyValues` holds their Daily Values and the recipe label's
Vitamins view lists them.

A Foundation food reports only what USDA analysed for that sample set (garlic: five of the
stored nutrients), while SR Legacy holds the full profile of the same food. After the
backfill, `dotnet run -- --supplement-nutrients <foundation-csv-dir> <sr-legacy-csv-dir>
[--dry-run]` fills what an ingredient linked to a Foundation food still lacks from the SR
Legacy food USDA lists under the **same NDB number** (`foundation_food.csv` ↔
`sr_legacy_food.csv`), or, for an older Foundation food the file gives no NDB number
(granulated sugar, whole milk), from the one SR Legacy food with the very same description;
a Foundation food with a new NDB number has no counterpart and stays as it is, and a pair whose descriptions differ in how the food was cooked (portabella
mushroom against grilled portabella) is passed over. The four macros are never taken from the other record, a part of fat or
carbohydrate is checked against the ingredient's own stored total, and a stored value is
never overwritten. Each row carries the SR Legacy FDC id in `IngredientNutrient.FdcId` and
the run's `CreatedDate`, so supplemented values can be told apart and a run undone with the
same `DELETE … WHERE "CreatedDate" = '<printed stamp>'`.

### Manufacturer labels for foods USDA does not cover — BUILT ✅
A catalog food with no USDA record (the hand-seeded "Greek Yogurt", "Marinara Sauce",
"Balsamic Glaze" and the like) can take the Nutrition Facts one manufacturer publishes for
its own product. `dotnet run -- --import-label-nutrition <labels.tsv> [--dry-run]`
(`Nom.Import/Services/LabelNutritionImportService.cs`) reads a tab-separated file with one
transcribed label per row: `ingredient_id`, `name`, `brand`, `product`, `url`, `serving` (as
printed), `serving_grams`, the printed household measure (`serving_volume` with
`serving_volume_unit`, or `serving_pieces`), then one column per `FdcNutrients` key holding
the amount **per serving**, left empty where the label prints no number ("<1 g", a % Daily Value only, "not a
significant source of"). The importer does the only arithmetic there is: amount × 100 ÷ the
printed serving weight. A label with no gram weight is refused (a wine label gives fluid
ounces only), as is a row whose `name` is not the food's name, a food that has a USDA record,
and a label whose macros fail `FoodDataQualityValidator`. Only nutrients the food has no row
for are written. A food with no density or item weight takes the one the household measure
gives ("3/4 cup (170g)" → g/ml by the catalog's own cup, "5 crackers (16g)" → 3.2 g each), so
its cup and piece lines count; the serving is never stored as `ReferenceServingGrams`,
because a count line would then read "1 jar" as one serving. The label as printed is kept as an applied `FoodCatalogProposal` (field
`label_nutrition`, source `label:manufacturer:<host>`, the run's batch name), so every value
traces back to its page; `ProposalPolicy` refuses that field from any source that is not
authoritative. Undo a run with `DELETE … WHERE "CreatedDate" = '<printed stamp>'` on
`nutrient."IngredientNutrient"`.

The manufacturer's own label counts as authoritative on its own (decision of 2026-10-22);
two agreeing sources are needed only when neither is the manufacturer. The pages are read by
the operator, never fetched by nom-api, and each host is recorded in `crosscheck-sources.md`.
A label covers the FDA panel only, so these foods carry no vitamins beyond vitamin D.

### One recipe row per source line — BUILT ✅
A recipe may name the same food on several lines (sugar for the dough, sugar for rolling), and
the key of `recipe.RecipeIngredient` has included `SortOrder` since 2026-10-08, so each line
is its own row. Nothing joins lines any more: the scrape and photo imports write one row per
line at a running position, and a food merge (`CatalogCleanupService.MergeRecipeLinksAsync`)
gives a line that would collide with the target food's line the recipe's next free position
(reported as `LinksRepositioned`). Before that, a repeat was appended to the first row as
`"a + b"` and its amount was lost, which is why a label could be short by whole cups.

`POST /api/FoodCatalog/relink/unfold?apply=false&maxRows=5000&sampleSize=50`
(`Nom.Orch/Services/LineUnfoldService.cs`, curation admin) takes those rows apart again. It
is a dry run unless `apply=true`, and the dry run reports exactly what the same call would do.
The row keeps its place, its first line and its stored amount; each further line becomes a new
row on the same food at the end of the recipe, with the amount `IngredientLineParser` reads
from it (0 on the Gram measurement when it reads none). Rules:

- `" + "` also stands inside single lines ("1 cup + 2 Tablespoons sugar", "1 large egg + 1
  egg white"). Where the recipe has a `ScrapedDocument`, the row is split only into whole
  lines of its `recipeIngredient` list, fewest lines first; a row that is one line of that
  list is not a fold (`SkippedSingleSourceLine`), and a row that does not add up to lines of
  the list is left alone (`SkippedAmbiguous`). A recipe with no stored document (the cookbook
  imports) has every part read as a line, unless a part is only an amount ("3/4 c + 2 T
  milk"), which also leaves the row alone. `Support/FoldedLine.cs` holds these rules and
  nothing else.
- A later part that repeats an earlier one and states no amount is dropped ("butter + butter"
  is one line); a repeated line with an amount is a real second line.
- When the first line states no amount and the stored amount is exactly a later line's
  ("flour + 2 quarts flour" stored as 8 cups), the amount goes with that line.
- Only scraped recipes, and only rows whose `LineKind` is empty or `ingredient`; rows
  classified as artefact, note, instruction, reference or equipment are counted
  (`SkippedOtherKind`) and left.
- No line changes food. Run `relink/preview` and `relink/apply` afterwards; they now see each
  line on its own.

Every row taken apart is one `audit.AuditLogEntry` (`EntityType` `RecipeIngredient`,
`EntityId` the recipe, `ChangeType` `LineUnfold`, the joined text and amount in `OldValue`,
the added positions in `NewValue`). `POST /api/FoodCatalog/relink/undo?sinceUtc=<start>`
puts a run back: the sweep goes newest first, so a line relinked after it was unfolded
returns to its food before its row is joined again, the added rows are deleted and the
journal entry removed. A row whose lines were edited or removed since is skipped and its
entry stays. Changed rows carry a fresh `LastModifiedDate`/`CreatedDate`, so the nutrition
sweep recalculates their recipes in both directions.

Order of work, as for any catalog change: restore a production snapshot into staging, dry run
with `maxRows=50000` and compare the counts with the last staging run, note the UTC time,
apply in batches until `Remaining` is 0, open a few of the sampled recipes in the app, prove
`relink/undo` and re-apply, and only then repeat on production with its own dry run and
recorded start time.

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

### Label cross-check — BUILT ✅ (`ops/food-catalog-crosscheck.py`)

Verifies catalog nutrition against publicly published labels. Lives in `ops/`
because nom-api never fetches third-party sites (see CLAUDE.md).

**Mass normalization is the crux.** Labels publish *per serving*; the catalog
stores *per 100 g*. A 55 g bar at 240 kcal is **436 kcal/100 g** — compare the raw
numbers and essentially every product looks wrong. `ops/nutrition_normalize.py`
parses the serving mass (`"1 bar (55 g)"` → 55 g, parenthesised mass beats the
count), converts oz/lb/ml, rescales every nutrient by `100/serving_grams`, and
**refuses** rather than guesses when no serving mass is published. Volume servings
are converted at an assumed 1 g/ml and marked, so they can never drive a numeric
change on their own. 30 unit tests.

**The model cannot supply numbers:**
- schema.org `NutritionInformation` is parsed **deterministically — no model**.
- For unstructured pages the local Ollama model is a *transcriber*, prompted to
  copy values exactly; then every number it returns must appear **verbatim in the
  fetched text** or it is discarded. That check, not the prompt, is what prevents
  remembered values leaking in.

**Corroboration gate:** a numeric change requires **≥ 2 independent hosts** agreeing
within tolerance; it is then emitted with a `label:` source (authoritative under
`ProposalPolicy`) and still needs admin approval. One source, disagreeing sources,
or an assumed density produces a **`flag`** with a `review:` source, which the
policy forbids from changing any nutrient value.

**Crawling posture:** allow-list required (the script refuses to run without
`--allow-host`), robots.txt honoured per host (unreadable robots = disallowed),
per-host rate limiting, contact UA. Search engine result pages are **not** scraped;
use a licensed API via `--search-api brave|google`.

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
