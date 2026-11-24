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

2. **Local AI enrichment (Ollama).** Normalizes/dedupes names. Food groups and the
   whole-food flag come from the USDA category only (see *Catalog admission*), never
   from a model or a name.

3. **Whitelist-gated manufacturer fetch — gap-fill only.** For products missing
   from FDC, read the manufacturer's *own published* structured nutrition
   (schema.org `NutritionInformation` JSON-LD) from **admin-approved domains
   only**, via the operator-scraper contract. Never general web scraping.

## Pipeline

```
FDC bulk CSV ─▶ staging ─▶ transform ─▶ QUALITY GATE ─▶ classify ─▶ Ingredient
                                          (reject/         (food group,   (curation: by the
                                           quarantine)      IsWholeFood)   admission rule)
                                                                              │
gap-fill (gated JSON-LD) ─▶ QUALITY GATE ─▶ classify ─────────────────────────┘
```

Imported records are judged by the catalog admission rule; the severe-restriction
"curated-only" gate trusts only foods the rule or an admin admitted.

> **Operational gotcha:** meal-plan food-group top-up draws candidates only from
> **Curated** ingredients, and never a food marked recipe-only (`IsWholeFood` false) or
> one with more than 0.5 g alcohol per 100 g, so an admitted spirit is never scheduled
> as a Beverages serving.
>
> `--curated` is retired: an import lands every food `PendingCuration` with the rule's
> verdict stored, and only the gated `--admit` pass curates (see *Catalog admission* below). The **Branded** catalog stays pending until two or more independent sources agree with a
> food's values (see *Branded lift*) — it is manufacturer-submitted and unverified, which is
> precisely why the quality gate rejects ~4% of it.

## Catalog admission — by rule, no model

`Nom.Data/Nutrition/CatalogAdmissionRule.cs` decides each USDA food's status, food group and
edibility from USDA's own data. The importer applies it to every food it creates and stores
the verdict (category, group, edibility, energy check, reasons), but lands every food
`PendingCuration`: only `Nom.Import --admit`, with its file, energy-rate, size and audit gates,
changes a status. Run `--admit` after every `--import-fdc`.

| Check | Rule |
|---|---|
| Source | Foundation and SR Legacy (USDA-authored numbers). Branded stays `PendingCuration`, reason `awaiting_second_source`, until two or more independent sources agree with its values (see *Branded lift* below), or `sources_disagree` when a disagreement is not outweighed; plus `zero_energy` when it states 0 kcal in a category that is not water, tea, coffee, soda, sweetener, salt, spice or seasoning. |
| Category | `Ingredient.FdcCategoryId` (USDA `food_category_id`). The rule never rejects a food. Admitted: the 19 mapped categories plus Spices and Herbs (2), Soups, Sauces and Gravies (6), American Indian/Alaska Native Foods (24, code 3500), and the four prepared-food categories Baby Foods (3), Fast Foods (21), Meals, Entrees and Side Dishes (22) and Restaurant Foods (25), whose foods are admitted as **prepared foods** (see *Prepared foods* below; owner answer 2026-11-20, second round). Any other category is held, `category_not_admitted:<id>`; no category is held, `category_missing`. |
| Plausibility and energy | `FoodDataQualityValidator` (below), with the food's own calorie factors and fiber. |
| Duplicate | The food's name or USDA description, as a wording key, must not be a live curated food's name or USDA description alias: held as `duplicate_of:#<id>`. |
| Food group | From the category only: `CatalogAdmissionRule.CategoryToGroup`; Spices and Herbs, Soups, Sauces and Gravies, the mixed category 24 and the four prepared-food categories give none. Branded: the branded category text only (`FoodGroupHeuristics.ClassifyByCategory`; a condiment or sauce category gives none, a category naming oils is Fats/Oils). **No path groups a food by its name**: the importers, `--enrich-food-groups` and `POST /api/Curation/ingredients/auto-classify-food-groups` all read the stored category, and a food without one is left alone. Each group or edibility they set or clear is journaled as `GroupFromCategory` under one stamp per run, so `--admit --undo <stamp>` takes the run back. A model never sets a group: `ProposalPolicy` refuses a `food_group` proposal unless its source is authoritative (`admin:`, `deterministic:`, `fdc:`, `label:`), and `ops/food-catalog-review.py` only flags a group it doubts. |
| Edibility | From the category only (`CategoryDirectlyEdible`). |

All checks pass: **Curated**. Any failure: `PendingCuration` with the reasons stored in
`Ingredient.AdmissionReason` — held by rule, not work for a person; a newer USDA release or a
`label:`/`admin:` source is what changes it. A wording load never promotes a held food, and a
lane's link apply never curates one (a person's approval may). A food an admin **Rejected**
is hidden from search and from every link, relink and wording candidate list, and no link
apply curates it. `Ingredient.EnergyCheck` records how the energy was confirmed: `own-factors`,
`general`, `exempt-small` or `atwater_mismatch`. `Ingredient.FdcCategory` keeps USDA's
category wording (the branded category for branded foods).

### The admission pass

```
dotnet run -- --admit <foundation-csv-dir> <sr-legacy-csv-dir> [--branded <branded-csv-dir>] \
    --checksums ops/fdc-releases.sha256 [--dry-run]
dotnet run -- --admit --undo <stamp> [--dry-run]
```

For every live Foundation and SR Legacy food (and branded with `--branded`) it reads the
category, description, nutrients and calorie factors from the USDA files, stores
`FdcCategoryId`, `FdcCategory`, `EnergyCheck` and `AdmissionReason`, re-derives the food
group and edibility from the category (curated foods too, so a name-derived group such as
black pepper in Vegetables is cleared), stores the USDA description as an
`fdc:description` alias where the food has none, and withdraws open model link picks
(`ai:`) that point at a food an admin rejected or at a prepared food. **Only a `PendingCuration` food changes status.** A
curated food the rule would not curate stays curated and is reported as `kept curated`.

It stops by itself and writes nothing when:

- a USDA file it reads is not recorded in the checksum file (sha256sum format; a truncated
  or different download). `ops/fdc-releases.sha256` records SR Legacy 2018-04, Foundation
  2025-12-18 and Branded 2025-12-18; add a release's lines only after its download is
  verified (`wget -c`, then `unzip -t`/`gzip -t` on the archive);
- a USDA food in the catalog has no category in the files (wrong release given);
- fewer than 99 % of the USDA foods pass the energy check;
- more than 10,000 foods would change status, group or edibility.

Before it writes, it lays its changes over an untracked copy of each USDA source and runs the
catalog audit on it (`FoodCatalogAuditService.Findings`, the same code as the audit endpoint),
so the dry run shows the same outcome as the apply. A food that would gain a high or medium
finding it does not have now is **held** instead of admitted or regrouped: its status, group and
edibility stay as they are, its USDA facts and description are stored, and its admission reason
becomes `audit:<code>` (for example `audit:group_energy_outlier`). The report lists such foods
under "Held for the audit" with the change they would have had. Holding a food can move a group
median, so the projection is repeated (up to five passes) until no new finding appears. The run
stops, writing nothing, when it would hold more than 1 % of the foods it changes (at least one
may always be held): a run that breaks the audit for many foods is a bad release or a bad rule,
not one bad food. After it writes, it re-runs the audit on the written catalog; a high or medium
finding on a food it admitted or regrouped that was not there before still makes the run undo
itself by its stamp (a safety net the projection should make unreachable). Rule version
`catalog-admission/3` since this hold, `catalog-admission/4` since the branded branch reads the
stored source counts (`Ingredient.AgreeingSources`, `DisagreeingSources`), so a branded food the
lift curated stays curated with no reason when `--admit --branded` judges it again.

Why the hold exists (staging checkpoint 1, 2026-11-20): #95452 "potato pancakes" (curated, no
group, SR "Potato pancakes", category Vegetables and Vegetable Products, 268 kcal/100 g) was
regrouped none → Vegetables by its category. The audit's `group_energy_outlier` flags a food
over six times its group's median kcal, and the Vegetables median of the SR foods is far below
45 kcal, so the one fried vegetable dish undid the whole run of 1,714 changed foods, and the
dry run could not show it because the audit ran only after the write. Now the pancakes keep no
group with the reason stored, and the rest of the run stands. A held food is re-judged on every
run; it is lifted when the audit no longer flags it (for example after an admin sets its group).

Every change carries the run's stamp in `audit."AuditLogEntry"`: entity `Ingredient`,
change types `CatalogAdmit`, `GroupFromCategory` (`FoodGroupId`,
`IsWholeFood`) and `CatalogFacts` (`FdcCategoryId`, `FdcCategory`, `EnergyCheck`,
`AdmissionReason`); entity `IngredientAlias`, change type `FdcDescription`; entity
`FoodCatalogProposal`, change type `CatalogPickWithdraw` (the pass's own, so the time-window
`relink/undo?sinceUtc=` never takes an admission's withdrawal and `--admit --undo` never takes the
relink's; withdrawals journaled as `ProposalWithdraw` under an admission run's stamp before this
type existed, as on staging at checkpoint 1, are left out of the relink undo too); and one
`CatalogAdmitRun` row with the rule version. `--admit --undo <stamp>` puts back each value the food still holds as the run
left it (a value changed since is left alone and listed), removes the description aliases it
added and restores the picks it withdrew. A demoted food that a recipe line uses is listed as
`in use`; approved recipes are not changed. The run's journal is kept: the undo writes each
value it put back as `CatalogAdmitUndo` (old and new swapped) under a stamp of its own, plus a
`CatalogAdmitUndo` run row naming the stamp it undid, and prints that stamp, so a self-undo or
a manual undo can be read from the record and `--admit --undo <undo-stamp>` puts the run back.
A repeated `--admit --undo <stamp>` of a run an earlier undo already took back counts each value
that undo put back, and that still stands as it left it, as `Already undone`, not as `Skipped`
(staging checkpoint 2, M9: the second dry run read "Skipped: 46397 (changed since the run)" and
looked like a failed undo); `Skipped` is left for values someone changed since.

Operator notes (staging checkpoint 2): run the importer from its own build folder (it reads
`appsettings.json` from the working directory); on staging point `Ai__OllamaUrl` and
`Ai__BatchOllamaUrl` at a closed port and set `ImportSettings__AiEnhancement__EnableAiEnhancement=false`;
after the v0.3.121 rollout's alcohol import and backfill, `--backfill-nutrients` has nothing left to
fill (its dry run reports 0 rows), so no new run is needed.

The bulk status endpoint `POST /api/FoodCatalog/curation-status` journals every change too
(entity `Ingredient`, change type `CurationStatusSet`, old and new status, the admin), so a
bulk promotion like D4 of 2026-11-03 can be reversed from the record from now on.

The rule's golden set is `catalog-admission.tsv` (see `golden-sets.md`); its floor is every
row.

### Prepared foods

A food whose stored `FdcCategoryId` is Baby Foods (3), Fast Foods (21), Meals, Entrees and Side
Dishes (22) or Restaurant Foods (25) is a prepared food (`Nom.Data/Nutrition/PreparedFoods.cs`;
since rule version `catalog-admission/2`). So is a branded food whose stored FDC branded category is a
whole-meal category (`PreparedFoods.BrandedMealCategories`, such as Frozen Dinners & Entrees; see
`prepared-contents.md`), which is curated only by the branded lift. The admission rule judges it by the same checks as any
other USDA food and curates it when they pass, with no food group, so a household can put it
in a plan. It is a dish, not an ingredient, so:

| Path | What it does with a prepared food |
|---|---|
| Plan | `GET /api/ingredients/search?q=…&prepared=true` (the plan dialog's food picker, "Add a whole food or a prepared meal") finds it and marks it `isPrepared`; the picker shows "Prepared" (`whole-food-result-prepared`) and adds it as a standalone slot (`MealPlan.IngredientId`), the same path as an apple. The week view marks the entry `isPrepared`. Without `prepared=true` (recipe editor, pantry, shopping list) the search leaves it out. |
| Recipe lines | Never a line's food. Link candidates (`IngredientLinkService.CandidatesAsync`: staple rule, name matcher, model picker), the auto-apply gate (`LinkAutoApplyGuard` holds a pick at one as `prepared food`), link apply (`ApplyFdcLinkAsync` refuses it for a person too), the verified wording load (`food is a prepared food`, never promoted), `relink/verified`, the relink plan, `MoveFood`, `MoveWordings`, retire, the compound-line vocabulary, the importer's verified-wording, trusted-alias and applied-link resolution, and the catalog cleanup's merge plan (a residue whose name leads to one is left for a person) all leave it out. A recipe save refuses a new line on one; a line a recipe already had stays editable. Recipe approval (admin and the auto-approval sweep) refuses a recipe with a line on one, since such a line is a wrong link. |
| Plan setup and shuffle | Household → Meal Split → "Use prepared meals for" (`meal-split-prepared-{breakfast,lunch,dinner,snacks,dessert}`) stores, per meal, that prepared meals fill it by default (`meal-plan-options` `preparedMealTypeIds`, household preference `meal_plan_prepared_meals`; empty by default, so a household that has not chosen it gets no prepared meal from shuffle). Shuffle then fills each empty cell of such a meal with **one** prepared meal instead of its recipe courses (`MealPlanOrchestrationService.LoadPreparedPoolsAsync`): a Curated food of Fast Foods, Meals, Entrees and Side Dishes or Restaurant Foods (`PreparedFoods.MealCategoryIds`; baby foods only by hand) that the household's steward/provider locks, its direct restrictions on that prepared food itself and its nutrient criteria (read on the food's own stored nutrients) allow, drawn at random three per cell and scored by the same nutrient, calorie and macro-mix scoring as recipes on one reference serving (100 g when it has none), preferring one not yet used that week. A recipe is judged against a restriction by every ingredient line; a prepared meal has no lines, so its name would be the only thing left to judge, and "Fast foods, cheeseburger" passes Vegetarian's patterns. So when the household has an allergy or medical restriction (severity ≥ 4), or any restriction judged by what a dish contains (`RestrictedIngredientSet.ReadsContents`: a category criterion with an ILIKE or word pattern, such as every default diet, a criterion or direct restriction naming a food that is not a prepared food, or a capped alcohol criterion), only prepared meals whose contents were read from an authoritative statement (USDA FDC's for a branded meal, or the household's own confirmed label) and pass the same gate as a recipe's lines are drawn (`PreparedContentsGate`, see `prepared-contents.md`); a meal none of whose prepared meals passes gets recipes (`severe_restriction`, `diet_restriction`). Nor when a curated-only member shuffles (`curated_only`). The shuffle response says why (`preparedMealsHeld`: `severe_restriction`, `diet_restriction`, `curated_only`, `none_available`; shown as `plan-prepared-held`). A household can still add a prepared meal by hand; the picker shows why one is held for it (`whole-food-result-held`). |
| Shopping list | A planned prepared meal is one item to buy, never split into ingredients. The week view gives each prepared entry `servingsToBuy`: its quantity for each active member who eats that meal that day (exclusions honoured). The shopping list sums these per food into the "Prepared Meals" aisle as "N servings" (`shopping-item-prepared`), takes pantry pieces of the same food off, and checks it off by the food id like any other line. |
| Contents | `recipe.IngredientContents` holds, as evidence, what each statement says the meal contains, read by rule (`IngredientStatementReader`) and resolved to curated foods or recognised terms (`PreparedContentsService`); `Nom.Import --prepared-contents` loads FDC's statements, the label confirmation a household's. The Pipeline page counts curated prepared meals with read contents, unread and none (`pipeline-prepared-contents`). |
| Food groups | Never a top-up and never a serving: `ApplyFoodGroupRulesAsync` leaves it out of the candidates and does not count a recipe line or a planned slot on one toward a household minimum, even if someone gave it a group by hand. |

`--admit` reports how many prepared foods it admitted and withdraws open model picks that point
at one. An exact-name import match is the one path left: a scraped wording that is a prepared
food's whole live name still lands on it (the name is unique, so the importer cannot create a second
food of that name); the approval gate then holds the recipe.

### Branded lift (`cleanup2/catalog-branded`)

A branded food's numbers are the manufacturer's own submission to USDA, and 238 of 4,471 state
0 kcal. So one FDC branded record is never enough: a branded food is admitted only when **two or
more independent sources agree** with its stored per-100 g values, its FDC record counting as
one (owner answer 2026-11-20: "two or more, more is better"). No model takes part, and no value
is ever taken from a source: the lift only decides whether the stored FDC values are believed.

**Sources** are the rows of `recipe.IngredientVerification` for the food
(`BrandedLiftService.Reading`):

| Source | Counts | Key |
|---|---|---|
| A household's confirmed label (`label:user:{personId}`) with an explicit gram weight, or printed per 100 g | yes, when it agrees | one per household (two members reading one package are one source; its last reading counts) |
| A manufacturer's page (`manufacturer:{host}`) written by the Release B lookup lane | yes, when it agrees | one per host |
| A later FDC branded release (`fdc:{id}`) | never: the same submitter | |
| Open Food Facts (`off:{barcode}`) | a disagreement counts against the food; an agreement never for it (ODbL, signal only) | one |
| Anything else, an unconfirmed label, a label with a serving in millilitres only | ignored or incomplete | |

**Agreement** (`Nom.Data/Nutrition/BrandedSourceAgreement.cs`, `branded-agreement/2`): energy,
protein, carbohydrate and fat must all agree, each within **10 %** of the stored value (imports.md
2d, the owner's rule). The only exception is a value so small that 10 % is less than a label can
print: there the two figures' label rounding is allowed, but never more than **5 kcal or 0.5 g
per 100 g**. A figure's rounding is half the FDA step (5 kcal, 0.5 g) scaled to 100 g by its
printed serving (the FDC record's `ReferenceServingGrams`, the label's `ServingGrams`), or half a
per-100 g step (0.5 kcal, 0.05 g) for a figure printed per 100 g. A value **disagrees** only when
it lies outside both 10 % and the two figures' whole rounding. A value in between is
**inconclusive**: a label with a small serving whose printed figures cannot confirm 10 % (a 5 g
seasoning serving rounds kcal to 100 per 100 g) neither lifts the food nor counts against it. A
"less than" value ("<1 g", or "not a significant source") is judged the same way against its
bound. One value that disagrees makes the source disagree; an inconclusive or missing value leaves
it counted neither way.

**Decision** (`CatalogAdmissionRule.SourcesLift`, inside `Decide`): agreeing minus disagreeing
must be at least two. With no disagreement two agreeing sources lift a food; each disagreement
needs one more agreeing source. Every other check of the rule still holds the food (zero energy,
plausibility, the energy check, a stored duplicate or audit reason): agreement never overrides
them. A food the rule never judged (no stored `EnergyCheck`) is left for `--admit --branded`.
`Ingredient.AgreeingSources` and `DisagreeingSources` store the counts; ingredient search puts
the branded food more sources agree on first among equal branded foods (a USDA-authored or
hand-made food is not ranked by the count, and an equally matching curated USDA food comes before
any branded one) and returns the count, and the plan's food picker shows "N sources agree".
**Never fewer than two** (owner answer 3): a food the rules curated (its last journaled status
change is `BrandedLift`, `BrandedLiftUndo`, `CatalogAdmit` or `CatalogAdmitUndo`) goes back to
`PendingCuration` with the reason stored when its sources no longer lift it. A food an admin
curated is never demoted: it stays curated with `sources_disagree` stored and is counted on the
Pipeline page.

**When it runs.** On every label a household confirms for a branded food
(`IngredientVerificationService.ConfirmAsync`), for that food; and as a pass over every branded
food with evidence or a stored count:

```bash
dotnet run --project nom-api/Nom.Import -- --branded-lift --dry-run
dotnet run --project nom-api/Nom.Import -- --branded-lift
dotnet run --project nom-api/Nom.Import -- --branded-lift --undo <stamp> [--dry-run]
```

or `POST /api/FoodCatalog/branded-lift?apply=false|true` and
`POST /api/FoodCatalog/branded-lift/undo?stamp=<stamp>&apply=false|true` (curation admin). A pass
stops, writing nothing, when it would change more than 10,000 foods. Proving it on staging needs
evidence rows first: the only second source this build reads is a household's confirmed label, so
a staging test household ticks a pending branded food on its shopping list in the app and adds the
label; from the API alone the pass judges nothing (staging checkpoint 2). A household is asked for a
label for a pending branded food (it no longer counts as verified by its FDC id alone), so the
shopping lane brings the second source. The Release B lookup lane (a no-op today) is to call
`IBrandedLiftService.RunAsync` for the food after it writes a `manufacturer:` or `off:` row.

**Journal and undo.** One stamp per run in `audit."AuditLogEntry"`: entity `Ingredient`, change
types `BrandedLift` (`CurationStatusId`), `BrandedLiftFacts` (`AgreeingSources`,
`DisagreeingSources`, `AdmissionReason`) and `BrandedLiftEvidence` (every source by key and
verdict, for the record), `BrandedLiftRows` (`AgreeingRows`: the evidence row ids that agreed, for
a food the run lifted), and one `BrandedLiftRun` row with the rule versions. The undo puts back
each value the food still holds as the run left it (a value changed since is skipped) and writes
`BrandedLiftUndo` rows under its own stamp. For every food it puts back to pending it also
**excludes the evidence that lifted it**: one `BrandedLiftExclude` row per evidence row (entity
`IngredientVerification`, `LiftExcluded` = `true`), which every later lift, pass or confirmation,
leaves out; a new label from any household still counts. When the counts changed since the run (a
later source arrived), the undo counts the food again without the excluded rows and stores those
counts under its stamp, so `--admit` never curates it from a stale count; the report lists any
food the other sources still lift. Undoing the undo's stamp applies the run again and lets the
evidence back in. The golden set is `branded-lift.tsv`.

**Pipeline page.** "Pending foods, by what holds them" splits the pending count into held by rule
(by reason), awaiting a second source, pending with no reason, and rejected (by an admin; the
rule rejects none). "Branded foods, by agreeing sources" shows those awaiting, held by a
disagreement or another reason, the curated ones by 2, 3 and 4+ agreeing sources (only those the
sources lift with no stored reason), those curated by an admin (fewer than two, or a reason the
rule still holds), and those curated with a later disagreement.

## Quality gate — DONE ✅

`Nom.Data/Nutrition/FoodDataQualityValidator.cs` (pure, deterministic, 11 tests).
Rejects physically implausible records so noisy FDC branded data doesn't pollute
the catalog:

- Energy ≤ 900 kcal/100g; each macro ≤ 100 g/100g; macro sum ≤ 105 g/100g.
- Required: name (alphabetic, ≤ 200 chars), calories, macros.
- **Energy check** (`FoodDataQualityValidator.CheckEnergy`): protein, fat and carbohydrate
  times USDA's own calorie factors (`food_calorie_conversion_factor.csv` through
  `food_nutrient_conversion_factor.csv`), plus 6.93 kcal per gram of alcohol, within ±5 %
  of stated calories — compared with the energy by specific factors (FDC 2048) where
  Foundation states it apart from its general 1008 figure. Where a food has no factors (or
  USDA ships 0/0/0), 4·protein + 4·(carb − fiber) + 2·fiber + 9·fat + 7·alcohol within ±15 %,
  where a known acidic food (citrus, vinegar) may state less, or plain 4·protein + 4·carb +
  9·fat + 7·alcohol on total carbohydrate within ±5 %: USDA stated many SR Legacy foods by those
  general factors (nettles, cattail shoots, chokecherries, seaweed, guar gum, the Northern Plains
  and Alaska Native foods), where the fiber-aware formula runs 16 % to 47 % low (staging
  checkpoint 2, M8; `catalog-admission/5`, which curates those foods by rule on the next
  `--admit`). A food whose own factors miss its stated energy is confirmed the same way when
  that energy equals plain 4·protein + 4·carb + 9·fat + 7·alcohol within ±5 %: USDA lists factors
  for it but stated its energy by the general ones (Alaska pollock raw and cooked, WHEATENA,
  tomato soup from dry mix, fat-free egg substitute, turkey gravy, chicken mushroom soup; staging
  checkpoint 3, N3, where 7 of 14 curated foods kept as `atwater_mismatch` had the wrong reason;
  `catalog-admission/6`). The fiber-aware formula is never a check for a food with its own factors,
  so baobab powder, instant grits and instant oats stay flagged. The audit
  (`GET /api/FoodCatalog/audit`) has no calorie factors, so it reads the stored check: a food the
  admission found a mismatch by its own factors stays an `atwater_mismatch` there too, rather than
  passing on the fiber-aware formula (review of `cleanup2/ck6-data-fixes`, CK6-3). Seasonings and leaveners
  (Spices and Herbs, the "Leavening agents," family) and foods under 20 kcal are exempt.
  Alcohol also counts toward the macro sum. Measured on the local USDA files: SR Legacy
  4,311 of 4,324 foods with factors within 5 % and 3,119 of 3,153 without within 15 %
  (99.4 % with the exemptions); Foundation 344 of 345 with factors. Cocoa powder, oat bran
  and leafy greens, which the old ±30 % general check rejected, pass by their own
  factors; baobab powder and stevia powder do not.
  The catalog audit reads the stored `EnergyCheck` and `FdcCategoryId`, so a food
  confirmed by its own factors, or a renamed spice or baking powder, is not reported as
  an Atwater mismatch.

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

- `Ingredient.FoodGroupId` (10-group vocabulary) + category-only auto-classifier +
  manual admin override (`CurationController`).
- `Ingredient.IsWholeFood` flag + whole-food-first ingredient search + standalone
  scheduling into meal-plan slots.

## Remaining work & blockers

### FDC Foundation import — BUILT ✅ + gauged in staging
`Nom.Import/Services/FdcFoundationImportService.cs` — a focused CSV loader (not the
old SQL-staging path): reads `food.csv` + `food_nutrient.csv`, applies
`FoodDataQualityValidator` to the per-100g macros, classifies by **FDC food
category** only, and lands accepted foods `PendingCuration` with the catalog admission
rule's verdict stored, for `--admit` to curate (idempotent by `FdcId`, name-deduped). Run: `dotnet run -- --import-fdc <csv-dir>`.

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

**One run, one stamp.** `--import-fdc <csv-dir> [--sr-legacy] [--only <ids-file>]
[--dry-run]`:

- `--dry-run` reads the files and the catalog and prints the same report without saving.
- `--only <ids-file>` reads just the listed FDC ids (separated by whitespace or commas) and
  names any that are not in the file as that data type. Without it the whole file is read,
  and every food already in the catalog also gets the nutrients and density it lacks, so a
  plain SR Legacy run after a new Nutrient row is added writes that nutrient onto every
  SR Legacy food. The report counts the backfilled rows per nutrient key, so a dry run
  shows exactly which nutrients the apply would add.
- An SR Legacy run creates foods only for ids listed with `--only`. Without `--only` it
  only backfills existing foods and reports the rest as `Not created`: the SR file holds
  foods whose catalog rows were since moved to Foundation records, and creating them again
  would add pending duplicates of curated foods.
- An FDC id that a proposal on a live food already names is skipped and listed as
  `Skipped (claimed)`: a pending `fdc_attach` that promises the id to another food (so
  importing it cannot make that attach unappliable, whatever order the attach and the
  import run in), or a proposal whose values came from that USDA food before its food moved
  to another record.
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
  retired food keeps its FDC id and nutrient rows, and each retirement is journaled as
  `FdcImportUndo` (`NewValue` the FDC id). Importing that id again revives the same food
  (`Revived` in the report; journaled as an `FdcImport` row of the new run, so the new run's
  undo retires it again) instead of skipping it, unless a live food now holds its name or its
  FDC id. A food retired any other way is still skipped (`SkippedRetired`).

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
stamped nutrient rows are removed, the description alias is removed when the attach added
it and otherwise gets back the context and removal it had before (the attach journals that
alias's earlier state), the proposal is marked Rejected and the food's recipe lines are
marked changed so their labels are recalculated. A column or alias changed since the attach
is left and named in the report line.
A proposal still pending is simply rejected; one the certain-links lane applied while the
undo ran is left Applied and reported as `applied_meanwhile_rerun`, and running the undo
again takes it back through the journal. An attach applied before the journal existed
is reported as `not_journaled` and left alone. Run `--attach-fdc --undo` with the importer of the same release as
the API that applied the attach: an older importer does not read the alias state the attach
journals and deletes an alias it should restore.

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
  nothing above 15 kg is. A size written after a part count or after "about" is the amount
  of the whole line: "1/2 can (about 8 ounces)" → 8 Ounce, "2 cans (about 30 ounces)" →
  30 Ounce. A whole count with a plain size is still each ("2 cans (15 oz) beans" → 30
  Ounce), so "2 cans (28 ounces) coconut milk", where 28 is the total, reads as 56 and needs
  a person.
- A can, tin or jar line whose food is used drained never takes the stated size, which is the
  net weight with the liquid: the line is stored as that many cans or packages instead
  (`no-conversion`), at import and in the re-read lane, whose preview counts them as
  `DrainedAsContainers`. Drained means the food's values are for its drained solids (its name
  contains "drained": "Chickpeas ..., canned, drained, rinsed in tap water") or the line itself
  says "drained" ("1 (12-ounce) jar roasted red peppers, drained"; "not drained" does not
  count), so the same wording gives the same reading on any food
  (`PackageLineService.CountsDrained`). A frozen or dry package ("1 package (9 ounces) frozen
  artichokes") is weighed as sold even on a drained food. No drained-weight factor is invented.
- A size in front of a utensil ("two-quart tin mould", "8-inch pan") is not an amount: the
  line is not read.
- A size in pounds and ounces is added up in ounces: "1 can hominy (1 lb. 13 ounce can)" and
  "1 1 lb. 13 oz. can peaches" → 29 Ounce, "1 can (1 pound 5 ounces) cherry pie filling" → 21 Ounce.
- A numbered can ("1 No. 2 can tomatoes", "1 #2½ can peaches", "1 No. 303 can peas", "1 can
  (No. 2) tomatoes") is a count of cans with no stated size; `IngredientLineParser.CanNumber`
  gives the number ("2", "2½", "303") for a cited can table to size, and the name drops it.
- A container with no stated size is stored in a **container unit**: Can, or Package for a
  jar, bottle, box, bag, packet, envelope, carton, tub, tube or pouch
  (`measurement."Measurement"."IsContainer"`). `GramsFor` gives a container unit no weight,
  so the line reports `no-conversion` and the recipe page asks the cook for grams or ounces.

### Container sizes (`cleanup2/container-sources`, `cleanup2/container-weighing`)

An unsized can, jar, bottle, box or package line is weighed at the most common size of that
food's container (owner answer, 2026-11-20: "look up the most common size of that ingredient's
container and assume it"). `container-sources` built where the sizes come from and how they are
measured; `container-weighing` weighs the lines with them (see "Weighing" below).

**Where a size is kept.** `nutrient."IngredientContainerSize"`: one row per food, container
kind (can, jar, bottle, box, bag, packet, envelope, carton, tub, package), era (`modern`,
`historic`, `any`) and size word (empty, `small`, `large`), with `NetGrams`, `DrainedGrams`
(only for a food whose USDA description is drained solids), `SizeText`, the deterministic
`SourceRef`, the `Class` it is scored under, how many sources agreed, the branded mode's share
and product count, the hold-out figures and a `Status` (`shadow`, `active`, `paused`,
`retired`). Lines are never rewritten from it. A row is read for weighing only while it is
`active` **and** the lane `container-sizes` is live on the lane frame. `recipe."RecipeSourceEra"`
holds a book source's year and era (`historic` before 1960, `modern` from 1960); a web recipe
is modern, so is a recipe with no source at all (written in the app), and a book with no row is
`unknown`. `ops/source-era.py` fills it from the books' own title pages (see "Book years" below).

**Where a size comes from, in order** (`Nom.Import --container-sizes`, no model anywhere):

1. A size the line states, or a numbered can it names: the parser reads those, and the cited
   can table sizes a numbered can by the recipe's era.
2. A USDA portion on the very food (SR Legacy or Foundation `food_portion.csv`,
   `UsdaContainerPortion`): the portion's unit must be the container word itself ("can",
   "can (303 x 406)", "package (3 oz)", "envelope (1 tbsp)", "packet"). Yields, prepared or
   diluted containers, servings, fractions of a container, single and fun-size packs are
   refused; a "container" with no size is refused. A portion that says drained, or any
   container portion of a drained-solids food, is a drained weight; portions of one key that
   disagree by more than 10 % give nothing.
3. The branded mode (`BrandedContainerScan`, `ContainerModeRule`): FDC Branded Foods
   `package_weight` (read by `FdcPackageWeight`: grams or millilitres win over ounces, GS1 codes
   ONZ/OZA/LBR/GRM/MLT/LTR/GLL, multi-packs refused, never divided), one product per GTIN (the
   latest `available_date`), United States, not discontinued, in the branded categories of the
   food's class with every class word and no excluded word. The class map is
   `Nom.Orch/Containers/container-classes.tsv`: a USDA category plus USDA description words,
   never a product name; a food in no class gets no branded size. The mode counts only with
   at least 20 products, at least half of them in the mode's ±5 % band, and that band at least
   twice the next one. "small" and "large" take the mode below or above the overall mode's
   band with the same tests. Only a weight is taken from branded data, never a nutrient value.
4. A standard of fill for wine and spirits (750 mL, 27 CFR 4.72 and 5.203), times the food's
   density, for any era.
5. Otherwise the line stays uncounted.

Sources of one key must agree within 10 % or the key is refused as "sources disagree" (canned
corn, brine pack: SR 482 g against the 15.25 oz mode). A portion's net wins over the mode. A
class's drained ratio (median of its members' drained/net, all within ±8 %) fills a drained
weight for a drained-solids member that has none.

**The cited can table** (`Nom.Orch/Containers/can-sizes-historic.tsv`), every row with title,
publisher, year, page, where to read it and the quoted words; a test refuses a row without them:
the USDA Bureau of Home Economics broadcasts "Buying Canned Foods" (29 September 1932: No. 1/4,
1/2, 1 short, 1 tall, 2, 2½, 3, 10) and "Shopping for Canned Goods" (4 December 1933: No. 2,
No. 303, No. 1 tall), read from the USDA National Agricultural Library scans on archive.org;
the USDA Food Buying Guide for Child Nutrition Programs, Table 2 "Common Can and Jar Sizes"
(No. 10, 3 Cyl, 2½, 2 Cyl, 2, 300, 2 Vacuum, 1 Picnic, 8 oz); 27 CFR 4.72 and 5.203. A range
counts by its middle when it is no wider than 20 % (No. 1 short, 10.5 to 13 oz, gives none).
Rows of one era must agree within 10 %; for an unknown era the historic and the modern size
must agree (No. 2: 20 oz both; No. 2½: 30 oz then, 28 oz now, counts at 28 oz; No. 303 has no
modern row and stays uncounted unless the recipe is historic). One period row names the cans of
a product class: `h33-veg-medium`, from the 1933 broadcast, "common sizes of cans for
vegetables" are No. 303 (16 oz) and No. 2 (20 oz), for foods of USDA category 11 (columns
`products` = `category:11` and `candidates` = `303|2`; a test refuses a period row whose
candidates the table does not size). A row added by hand with its citation names more.

**How it is measured** (`ContainerSizeService.ScoreAsync`, nightly in the API through
`ContainerSizeHostedService`, and after every import run): every live line on a food with a
size that states a weight or volume and names a container is a hold-out line. The stated size
is hidden and the lane asked what it would assume for that food, kind, size word and era; a hit
is within 10 % of the stated size.

A stated size in plain ounces on a food whose size is measured by volume (its branded class is read
in millilitres, such as evaporated milk's 12 fl oz can, or its size is a standard of fill) is
compared in both readings: as a weight, and as fluid ounces through the food's density
(`ContainerSizeResolver.StatedAsFluidOunces`); it agrees when either is within 10 %. Recipes write
"1 (12 oz) can evaporated milk" for the 12 fl oz can, which weighs about 378 g, while 12 oz by weight
is 340 g, 11 % under, so before this every such line missed (staging checkpoint 2, M26: 0 of 40;
`container-sizes/4`). A food with no density has no size measured by volume (the import refuses it),
so only the weight reading applies there. A line that names an item or pack size and states no size
("1 package flour tortillas (street taco size)", "snack size", "family size") is not weighed by the
food's sizes: SR's 484 g package holds 49 g tortillas, not street-taco ones (checkpoint 2, 35714).

A key (`ContainerSizeQualifier`):

| | Applies (`active`) | Stays `shadow` | Pauses itself (`paused`) |
|---|---|---|---|
| ≥ 20 hold-out lines | agreement ≥ 95 % and Wilson lower bound ≥ 90 %; below 35 lines, where no clean run reaches that bound, 95 % and its class qualifying; never while a pause condition holds | otherwise, and a shadow key that meets a pause condition | when active: its class's last 200 lines agree under 90 %, or 3 misses in its own last 20 |
| < 20 lines | its class agrees ≥ 95 % on ≥ 50 lines | otherwise | its class pauses |

A paused key comes back only when its size or source changes (an import writes it in shadow
again) and a fresh score passes; never by waiting. The whole lane runs on the lane frame
(`container-sizes`, tier `*`): each hold-out comparison is one decision, audited by the rule
itself (`AuditorScore.ExactRule`), once per signature (the rule version and every standing
size, so a new import or FDC release starts the count again). It qualifies at a Wilson lower
bound of 93 % and pauses when a full window of 200 agrees under 92 %; a pause undoes the
scorer's changes that made a key active since the last clean window (`ContainerSizeLaneUndo`),
and leaves a key it paused or demoted as it is. The switches
`ai-pause-all` and `ai-pause-container-sizes` hold the nightly run; `ContainerSizes:ScoreEnabled`
(default true) and `ContainerSizes:ScoreEveryHours` (default 24) set it.

Operator notes (staging checkpoint 2): the facts load's dry run proposed 738 sizes and the
hold-out agreed on 442 of 532 sized lines (83.1 %), but evaporated milk scored 0 of 40 against its
354 mL mode while recipes state its cans as "12 oz" by weight; until the scorer compares that class
in one unit (a fix not in this build) the class cannot qualify. The form relinks of the link
re-check decide most of the reach: 43 of 60 unsized can, jar, bottle and package lines read sat on
a raw or dry food (pineapple on raw pineapple, chickpeas on dry chickpeas) that has no can size.

Since `container-weighing` the resolver also answers lines of old (historic) and undated
(unknown) books, so the hold-out sees them; such a line is scored against its key only when the
key's own era answers it (`ContainerBasis.OwnEra`: a modern recipe's food size, an any-era size,
a dated old book's historic size). A line that would take another era's size
(`ContainerBasis.OtherEra`: an undated book's historic size, a period candidate, the modern size
by fallback) is counted apart (`OtherEraLines`, `OtherEraAgree` in the score report and the
import's printout), so lines of old books never pause a modern key. Since
`cleanup2/container-era-sizes` (rule version `container-sizes/2`) those lines are the decisions
of four old-book tiers of the lane, one per source of the size (`ContainerBasis.OldBookTier`):
`same-book`, `same-era`, `period` (an undated book's historic size or a period candidate) and
`modern-fallback`. Each is recorded, audited and evaluated on the frame on its own, with the
lane's bar and pause window, and a line is weighed only while the tier that sized it is live, so
a tier that misses (for example today's size against old books' packages) never holds back
another. The two corpus tiers are scored with the line left out: a line of book B is answered
by the other sized lines of book B, or by the books of its era other than B
(`ContainerCorpusSizes.LeaveOut`), never by itself. Their decisions also set the status of the
corpus row they were answered from (class `corpus-book:<kind>` or `corpus-era:<kind>`, the same
key and class bars as every other key). An old-book tier's pause has nothing to undo (it writes
no key); at the next sweep its lines are weighed by the next tier that is live, or stay
uncounted when none is. Since a line falls through to the next live tier, each tier is scored on
every line it would weigh while the tiers before it are not live, not only on the lines it
answers first. `OtherEraLines` and `OtherEraAgree` count each line once, under the first tier that
answers it, and the report lists each tier (`Tiers`).

**Sizes from the recipe corpus** (`cleanup2/container-era-sizes`, owner's third-round answer
7: "there has to be a way to convert these"). Old books often state a package's size on one
line and leave it out on the next. `ContainerCorpusSizes` (rule `container-corpus/3`) mines the
sized container lines of old and undated books (a book recipe whose `RecipeSourceEra` is
historic or unknown; a line in a weight or volume unit that names a container and states its
size, never a numbered can) and derives, per food, container kind and size word:

- **same book**: the sizes that book's own lines state. One size (within ±5 %) must hold more
  than half of them and at least twice the next ("1 3-oz. package cream cheese" in a book gives
  the size of "1 package cream cheese" in the same book; 3 oz once and 8 oz once gives none, and
  so does 3 oz on exactly half of the lines);
- **same era**: the sizes the books of the era state, each book counted once per size it
  states. At least two different books must state a size within the mode's band (one book
  stating 16 oz and 16.5 oz is one book, not two), and the mode must hold at least half of the
  votes and twice the next. Undated books form their own pool (`unknown`), as `RecipeSourceEra` gives
  no era to compare them with. A recipe whose source URL is a web page outside the book archives
  (gutenberg.org, archive.org, loc.gov, hathitrust.org) is modern unless its era row says otherwise,
  so USDA MyPlate Kitchen, stored with a book-like attribution and no site, never joins the
  undated pool (staging checkpoint 3, N13: it gave plain gelatine an 85 g package from a flavoured
  gelatin line).

A line enters the corpus only when it states one size of one container and names the food it is
linked to (`container-corpus/3`, the same in both readers): a range or an alternative of sizes
("7½ to 8 oz.", "8-oz. or 1-lb.", "(14-15 ounces)"), a range of containers ("one or two 2-oz.
cans"), a count followed by "half" ("1 half pint can", which the parser read as 1½ pints), a size
"and a half" of its unit, a size in super- or subscript digits ("13³⁄₄ oz.", read as 34¼) and a
container used as a vessel ("1 qt. jar with fresh mint leaves") are left out, and a line that names a
flavoured product ("strawberry gelatin", "raspberry-flavored gelatin") counts only on a food whose
description names a flavour or a dessert. The hold-out reads each line with its food's description (or
name) too, so the tiers are scored on the same lines that build them.

Old books often put the size first and give no count: "1 lb. can tomatoes", "1 quart can",
"½ pint can", "15-ounce can", "1 lb. 13 oz. can peaches". Since `cleanup2/parser-size-first`
(`line-builder/7`, `container-corpus/2`, `container-sizes/3`) NOM's line parser reads such a line,
with one container word, as one container of the size it states, as `ops/container-era-facts.py`
already did, so the corpus mines it, the hold-out scores it as a stated line, and the line itself
weighs the size it states and never an assumed one (a drained one counts one can, scaled by the
food's drained share, also when the scraper gave the can's size as the line's amount). Both readers
take a size in pounds and ounces ("1 lb. 13 oz. can", 822 g) and a size led by a number word
("one pound can", "a pound can", "Two-pound can"). A size before plural containers with no count ("2 lb. cans") does not say
how many containers there are: both readers leave it out of the corpus, and the parser still reads
it as 2 pounds of the food. "1 lb. canned tomatoes" names no container and is a pound of the food.

Each size is written as a row of `nutrient."IngredientContainerSize"` in shadow, journaled like
every other: source `deterministic:corpus-book:container-corpus/3:<era>:<g>g:<band>/<lines>:<min>-<max>g`
or `deterministic:corpus-era:container-corpus/3:<era>:<g>g:<books on the mode>/<books>:<min>-<max>g`, the book in `SourceAttribution` (book sizes only), and the
evidence in `Evidence` (the sizes stated and how often, lines, books, the spread and the
worst case it implies). `Nom.Import --container-sizes --corpus [--facts <out.tsv>] [--dry-run]`
writes them, retires a corpus row no line gives any more and never touches a USDA source's row
(the USDA import likewise never retires a corpus row). Its printout also counts, per product
class (container kind and USDA category) and tier, how many unsized container lines of old and
undated books each tier would size and the worst-case error of the sizes it takes (from the
corpus spread, or the span of the period candidates; `-` where no source bounds it), and the
lines no tier sizes, by reason. `ops/container-era-facts.py` mines the same facts from a
read-only capture (`ops/golden/container-era-lines.sql`), written apart from the C# code: the
grams of one container come there from the stored quantity and the line's leading count, here
from NOM's line parser. The capture carries each recipe's source host and the food's description,
so the miner applies the same era and food rules. The two readers are tested against each other row
for row: `container-era-readings.tsv` (each line read, `container-era-facts.py --lines`) and
`container-era-facts.tsv` (the facts) are written by the miner from `container-era-lines.tsv` and
the C# tiers must give both exactly. On a real capture compare `--lines` with the C# reading, and the
facts with `cut -f2- <file> | sort` (staging checkpoint 3 found 56 differing facts from 21 lines;
with `container-corpus/3` the checkpoint's 1,725-line capture reads 749 lines alike in both, 0
differing). Both writers number the facts in one order (each tier in turn, then the scope, then the
food as written with USDA ids by number before the `#` ids of foods with none, the container kind and
the size word), so the two files are equal row for row with their `f-NNN` ids, not only as sets
(staging checkpoint 4, P22: 617 facts equal as sets, 67 at the same id).

**Journal and undo.** Every row written, replaced, retired, rescored or paused is one
`audit."AuditLogEntry"` (entity `IngredientContainerSize`; `ContainerSizeSet`,
`ContainerSizeRetire`, `ContainerSizeStatus`, `ContainerSizePause`) with the row before and
after. `POST /api/FoodCatalog/container-sizes/undo?sinceUtc=T` previews and `&apply=true`
takes back everything since T, newest first (rows the run added are removed, retired rows
restored), skipping a row changed since, each reversal journaled `ContainerSizeUndo`;
`Nom.Import --container-sizes --undo <stamp>` is the same. Right after its step it reports
`Skipped 0`. `GET /api/FoodCatalog/container-sizes` lists the keys with their figures and
`POST /api/FoodCatalog/container-sizes/score` runs the hold-out (dry run unless `apply=true`);
Admin → Pipeline shows the block "Container sizes" (`data-testid="pipeline-container-sizes"`).

**Usage.**

```bash
dotnet run --project nom-api/Nom.Import -- --container-sizes <branded-dir> <sr-legacy-dir> <foundation-dir> --checksums ops/fdc-releases.sha256 --dry-run
dotnet run --project nom-api/Nom.Import -- --container-sizes <branded-dir> <sr-legacy-dir> <foundation-dir> --checksums ops/fdc-releases.sha256
dotnet run --project nom-api/Nom.Import -- --container-sizes --corpus [--facts <out.tsv>] [--dry-run]
dotnet run --project nom-api/Nom.Import -- --container-sizes --score [--dry-run]
dotnet run --project nom-api/Nom.Import -- --container-sizes --undo <stamp> [--dry-run]
```

The branded directory may be left out (portions and standards of fill only); such a run
proposes no branded-mode size and so retires none of the standing ones. Every file read
must be listed in `ops/fdc-releases.sha256`. A dry run scores its proposals against the stored
lines and writes nothing. `ops/golden/container_facts.py` is the same scan and portion reading
written apart from the C# code; it froze `container-facts.tsv` for the golden set, and on the
2025-12-18 release its class sizes equal the C# scan's for all 21 classes.

#### Weighing (`cleanup2/container-weighing`)

`RecipeNutritionService` weighs a measured line in a container unit (Can, Jar, Bottle, Package)
that has no gram weight of its own through `ContainerWeighing`, which calls
`ContainerSizeResolver` with the food's **active** rows only, and only while the lane
`container-sizes` is **live** on the lane frame (its whole-lane `LaneState` row says live; shadow,
paused or no row weighs nothing). The line's own count of containers times the weight of one is
its grams. A line whose text names no container word is read by its unit ("1 Jar" of the food).
Such a line has the status `counted-assumed`: it counts in the label like a stated amount (the
stored label that meal planning and the severe-restriction gate read includes it), and the recipe
page marks it.

The size, in order (the resolver): the size the line states (a drained line scaled by its food's
drained share); a numbered can by the recipe's era (the cited table); else the food's size of
that kind, size word and era:

| Recipe's era | Size taken | Basis |
|---|---|---|
| modern (web, app, a book from 1960) | the food's modern size, else an any-era size (standard of fill) | `food-size`, `any-era` |
| historic or unknown (a book before 1960, or undated) | the size the same book's sized lines give, else the size the books of the same era give, else an any-era size, else a historic size of the food, else the period row's candidate can nearer the food's modern size, else, for every kind, the food's modern size | `same-book`, `same-era`, `any-era`, `period-size`, `period-nearer`, `modern-fallback` |

The period row applies only when the food's modern size lies within the candidates' span widened
by 10 % (`ContainerSizeResolver.PeriodCovers`): a food sold today in a much smaller or larger can
(tomato paste at 6 oz, whole tomatoes at 28 oz) was not one of the medium cans the 1933 source
names, and takes its modern size. With a period row and no modern size there is nothing to choose
by, and the line stays uncounted ("1 can mushrooms" in an old book). A size word ("large can")
never takes a period row, which names medium cans only. This follows the lead decision on the
owner's answer: an unsized can in an old or undated book is not left uncounted where any cited or
modern size exists, and every such line is shown as estimated. Since `cleanup2/container-era-sizes`
the owner's third-round answer ("there has to be a way to convert these") extends it to every
kind: the corpus tiers come first, and the modern fallback is the last tier for packages, boxes,
bags, packets, envelopes, cartons, tubs and bottles as well as cans and jars
(`ContainerSizeResolver.FallsBackToModern`). Every size an old or undated book takes this way is
taken from the first old-book tier that is live (a tier in shadow or paused passes the line to
the next one), and the line's text names the tier ("the size this book states for it elsewhere
(2 of 2 sized lines)", "the most common size books of its time state (4 of 5 books, spread
85-226.8g)", for an undated book "the most common size other undated books state"; books, not
votes, are counted). A line still stays uncounted where no tier gives a size,
or where the food is drained solids and no drained share is known (a corpus size is a net
weight). The size spread this accepts per product class is in the build reports
(`build/container-weighing.md`, `build/container-era-sizes.md`).

**What the cook sees.** On the recipe page a `counted-assumed` line shows its grams with "≈" and
"estimated size" (`data-testid="recipe-ingredient-assumed-size"`, the full text as its title:
"1 can ≈ 14.99 oz (425 g): the most common size in USDA branded data (2025-12-18, 223 of 304
products); estimated"). The label lists the assumed lines ("Estimated: includes N assumed
package sizes", `nutrition-label-assumed`, `-assumed-list`, `-assumed-item`), marks the per-serving
line `, estimated` (`nutrition-label-estimated`) and says the amounts and % Daily Values are
estimated (`nutrition-label-dv-estimated`). The API gives `nutritionStatus = "counted-assumed"`
and `assumedSize` per line and `nutritionSource.assumedLines`. A line weighed at an assumed size
whose food has no nutrient values (`no-data`) still shows its grams with "≈" and the "estimated
size" mark, since `assumedSize` follows the weighing, not the status; the label does not list it.

**Search and the sweep.** The nutrient sorts read a container line as counted when its food has
an active size of the unit's kind while the lane is live
(`RecipeNutritionService.IsUncountedLineWith`; a query cannot read the line's text, so size
words, drained lines, the era and the old-book tier are not looked at there). The nutrition sweep
(`RecipeNutritionRecalcHostedService.ContainerDueRecipeIds`, beside `DueRecipeIds`) recalculates a
recipe with a measured container line when one of its foods' sizes, the lane's mode (whole lane or
any old-book tier) or its book's era changed after its label. That query reads soft-deleted size and
era rows, so a size or a year an undo removed counts as a change: a size applied, paused, retired
or undone, a year written or undone, or the lane going live or pausing, reaches the labels within
one sweep (15 minutes by default). When nothing counts any more and a measured line is in a
container unit, the recalculation removes the derived label, since it may hold an assumed size.

**Book years** (`recipe."RecipeSourceEra"`, plan correction C2.7). `ops/source-era.py` reads each
book's Project Gutenberg text on the harvest host (`~/cookbook-work/pg*.txt`, nothing fetched):
the header's "Original publication:" line when it names one year, else the title page (the first
200 lines after the "*** START OF" marker, leaving out lines about the e-text) when exactly one
year from 1600 to this year is found; otherwise unknown. A year in an attribution is the author's
and is never read. The text is matched to its attribution through the promoter's `books.csv`
(`pgNNNN.txt` and `cookbook-NNNN.jsonl` share NNNN); a file of two attributions is not sent.

```
GET  /api/FoodCatalog/container-sizes/source-eras                      # every book source with its year and era
POST /api/FoodCatalog/container-sizes/source-eras?apply=false          # body: [{ sourceAttribution, publishedYear, yearSource }]
POST /api/FoodCatalog/container-sizes/source-eras/undo?stamp=<stamp>&apply=false
```

A write refuses an attribution no live book recipe carries and a year outside 1600 to this year;
each write is one `SourceEraSet` entry (entity `RecipeSourceEra`, old and new value) under the
run's stamp, and the undo by that stamp restores the old value (`SourceEraUndo`), skipping a row
changed since; a source the undo removed is revived by the next write.

**Jar and Bottle.** The parser reads a jar as `Jar` and a bottle as `Bottle` (measurement rows
added by `db/fix-container-units.sql` and `db/seed.sql`, `IsContainer`); boxes, bags, packets,
envelopes, cartons and tubs stay `Package`. The re-read lane below moves stored lines whose text
names a jar or a bottle from Package (or Piece) to their unit, journaled `LineReread` and undone
by `reread/undo`. `RecipeLineBuilder.Version` is `line-builder/4` since this reading.

Lines stored before the parser read sizes are brought over by an admin lane next to relink
(`PackageLineService`, curation-gated, scraped recipes only, lines stored as Piece, Can, Jar,
Bottle or Package). The startup repair (`ScrapedRecipeRepairService`) leaves these lines alone so
that every change is journaled:

```
GET  /api/FoodCatalog/reread/preview?sampleSize=50     # counts per transition, lines that stop or start counting, samples; no writes
POST /api/FoodCatalog/reread/apply?maxLines=5000       # one audit.AuditLogEntry (ChangeType LineReread) per changed line
POST /api/FoodCatalog/reread/undo?sinceUtc=<utc time>&apply=false  # dry run: lines it would restore and lines changed since
POST /api/FoodCatalog/reread/undo?sinceUtc=<utc time>&apply=true   # restores each line that still holds what the re-read gave it
```

**Undo dry runs.** `reread/undo`, `labels/undo`, `quantities/undo` and `relink/undo` are dry
runs unless `apply=true`, like `startup-fills/undo`, `wordings/unload` and `recheck/undo`;
before v0.3.121 a call without the switch applied. Each result carries `applied`. The dry run
returns the same counts as the apply, skipped included, and follows a line changed twice in the
window as the undo does: the first three keep the restored values in memory and save nothing;
`relink/undo` runs the whole undo against a scratch copy held in memory (`RelinkUndoDryRun`): before
each chunk of 500 entries the copy is given, by a few set-based reads, the lines, reprocess journals and
rebuild headers of the chunk's recipes, the entries, the foods they and those lines name, the foods holding
a name a retired food would take back, a retirement's aliases and the withdrawn proposals; a recipe no
later entry names leaves the copy again. So the dry run only reads the database, locks no row and never
holds back another writer or undo (staging checkpoint 3, N17: after the order-only pass the old
rolled-back dry run took 12 min 17 s, kept its transaction open, and a second undo run meanwhile answered
400). It stops when the caller goes away. The entries run in parts that share no row
(`RelinkUndoDryRun.Parts`), each in a copy of its own: the retirements, picks and proposals go into the
first part with every entry of each recipe whose line entries name a food a retirement changes (a line
moved back needs that food live, and the retirement's undo brings it back); every other recipe's line
entries read and write only that recipe's lines and journals, so they go whole into parts of about 500
entries. The counts are the sum of the parts' and equal the apply's. One copy for the whole window held
every recipe a later entry still named, and the in-memory store reads a whole table for every query, so the
dry run grew with the square of the window (staging checkpoint 4, P16: T1 took 1,969 s at 440 % CPU and
1.5 GiB on the API host). On a generated window of 6,000 line entries over 3,000 recipes, each recipe
named again 3,000 entries later, one copy took 97.7 s and the parts 11.4 s, with the same counts; the parts
grow with the window, not its square. The order-only journal rows an undo carries into are read once
per chunk and written once per chunk, not once per line. The copies run one at a time in the API process
(`RelinkUndoDryRun.StartAsync`): two dry runs called together take turns part by part rather than each
filling cores of their own (staging checkpoint 5, Q20: T1 in 150 s with T7 beside it, but the API at up to
367 % CPU and 1.35 GiB).

Apply also marks the unchanged can and package lines that were weighed as one item or one
serving as modified, so the nutrition sweep recalculates their recipes' labels; a derived
label whose every measured line has become a can or package with no stated size is removed
by that recalculation rather than kept. On an existing database run `./db/apply.sh` and
`db/fix-container-units.sql` before the API version that reads packages (and, since
`container-weighing`, jars and bottles) starts. If the order slips, an imported line whose
measurement has no row (Package, Jar or Bottle before the script) is stored
without an amount and logged as a warning, never counted in grams; the re-read lane lists the
missing measurement in its preview and leaves those lines.

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

The manufacturer's own label counts as authoritative on its own for these foods, which have no
USDA record (decision of 2026-10-22). A branded food, whose USDA record is itself the
manufacturer's submission, needs two or more agreeing sources (*Branded lift* above). The pages are read by
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
  egg white"). The line parser adds up a bare amount and the amount after it in the first
  unit ("1/4 cup + 1 tablespoon light brown sugar" → 0.313 Cup) and reads nothing when the
  units do not add ("1 cup + 2 oz sugar"). Where the recipe has a JSON-LD `ScrapedDocument`, the row is split only into whole
  lines of its `recipeIngredient` list, fewest lines first; a row that is one line of that
  list is not a fold (`SkippedSingleSourceLine`), and a row that does not add up to lines of
  the list is left alone (`SkippedAmbiguous`). A recipe with no JSON-LD document (the cookbook
  imports) has every part read as a line, unless a part is only an amount ("3/4 c + 2 T
  milk"), which also leaves the row alone. `Support/FoldedLine.cs` holds these rules and
  nothing else.
- A later part that repeats an earlier one and states no amount is dropped ("butter + butter"
  is one line); a repeated line with an amount is a real second line.
- When the first line states no amount and the stored amount is exactly a later line's
  ("flour + 2 quarts flour" stored as 8 cups), the amount goes with that line.
- A later line that names no word of the row's food ("2 tablespoons butter" on Honey; words
  that only describe a food, such as "fresh" or "canned", do not count) is added without an
  amount (`UnnamedUnmeasured`), so it counts toward no food until relink moves it to its own.
  The same holds for the first line when it names no word of the food and a later line does
  ("one pint of cold water + one-half box gelatine" on box gelatine): it keeps the row's place
  on the Gram measurement at 0, and the undo gives the row back its stored amount.
- The rows the unfold leaves at 0 stay at 0 by construction: while the `LineUnfold` entry
  stands, the start-up repair leaves every line of the recipe as it is (see "Start-up repair and
  the undo guarantee" below). No `ScrapedRepair:Enabled` setting is needed around an unfold.
- Only scraped recipes, and only rows whose `LineKind` is empty or `ingredient`; rows
  classified as artefact, note, instruction, reference or equipment are counted
  (`SkippedOtherKind`) and left.
- No line changes food. Run `relink/preview` and `relink/apply` afterwards; they now see each
  line on its own.

Every row taken apart is one `audit.AuditLogEntry` (`EntityType` `RecipeIngredient`,
`EntityId` the recipe, `ChangeType` `LineUnfold`, the joined text and amount in `OldValue`,
the added positions with the amount each was given in `NewValue`).
`POST /api/FoodCatalog/relink/undo?sinceUtc=<start>&apply=true` puts a run back: the sweep goes newest
first, so a line relinked after it was unfolded returns to its food before its row is joined
again, the added rows are deleted and the journal entry removed. A row whose lines were
edited (text or amount) or removed since is skipped, counted under `Skipped`, and its entry
stays. When the row's food has since been merged into another by a link, the row and its added
lines are looked for in the same places on the one food that holds them all; if the merge
moved one to another position, the entry is skipped the same way. Changed rows carry a fresh `LastModifiedDate`/`CreatedDate`, so the nutrition
sweep recalculates their recipes in both directions.

Order of work, as for any catalog change: restore a production snapshot into staging, dry run
with `maxRows=50000` and compare the counts with the last staging run, note the UTC time,
apply in batches until `Remaining` is 0, open a few of the sampled recipes in the app, prove
`relink/undo` and re-apply, and only then repeat on production with its own dry run and
recorded start time.

### Line reading rules of NOM's own parser — BUILT ✅
The import keeps the scraper's parsed amount, but every NOM path that reads a raw line (the
re-read lanes, the start-up repair, unfold, the compound rules, and the planned rebuild of a
recipe from its stored source) reads it with `IngredientLineParser` and names it with `IngredientNameNormalizer`,
so a rule fixed here holds for every source. A missing scraper amount is always read from the
raw line. Rules (golden rows in `lines.tsv`, `line-kinds.tsv`, `startup-fills.tsv`, `compound.tsv`):

- Abbreviated spoons are spoons: `tablespn.`, `tablespns.`, `teaspn.`, `teaspns.`, `dessertspn.`
  ("2 tablespns. water" → 2 Tablespoon), and the name drops `tbp` as it drops `tbsp`.
- "pinch X" and "dash X" without "of" are 1 Pinch ("Pinch kosher salt").
- "a couple of" and "a pair of" are 2 ("a couple of ounces of butter" → 2 Ounce, "a pair of ducks" → 2 Piece),
  as are "a couple", "couple of" and "pair of"; a bare "pair" or "couple" is no count ("Pair with
  crusty bread" has no amount and keeps its name).
- An amount after the name, after a comma or a word, is read when nothing before it is a number
  ("Corn starch, ½ cup." → 0.5 Cup, "Mace, in powder ½ oz.", "Potatoes, medium size, 2", "Burro
  gr. 50" → 50 Gram), and the name drops it ("Burro, 100 grammi." → Burro, "Uova, N. 4." → Uova).
  A hyphenated mixed number there is one number ("Flour, 2-3/4 cups" → 2.75 Cup, "Water, 2-1/2
  quarts" → 10 Cup; it was read as the range 2 to 3/4, staging checkpoint 1 K2); a range of
  whole numbers or of two fractions stays a range ("Salt, 2-3 teaspoons" → 2 Teaspoon). The name
  drops a trailing size too ("Potatoes, medium size, 2" → Potatoes, "Onions, of a good size, 3"
  → Onions). The amount may be followed by a description with no number in it ("Egg white, 1,
  partly beaten." → 1 Piece of Egg white, "Flour, 2 cups, sifted" → 2 Cup of Flour; staging
  checkpoint 2 M20); a description holding another amount ("Water, 1 cup, plus 2 tablespoons") is
  not read. A trailing range with no unit is the amount too and leaves the name ("egg-balls, 16 to
  24" → 16 Piece of egg-balls).
- Two measures joined by "and" are added up in the first one's unit ("one quart and a pint of cut
  celery" → 6 Cup, "1 pound and 2 ounces butter" → 1.125 Pound; checkpoint 2 M6); measures that do
  not add (a cup and a pound) are not read so.
- A range of number words is a range like one of digits, read by its lower end, and leaves the name
  ("twelve or sixteen blanched oysters" → 12 Piece of blanched oysters).
- A measure "and a half" adds the half ("a jill and a half of brandy" → 0.75 Cup, "pinch and a
  half of salt" → 1.5 Pinch, "a tumbler and a half of cold water" → 1.5 Cup, "two pints and a
  half of milk" → 5 Cup); the half was lost before (checkpoint 1 K1, K2).
- Old measures: jill (as gill, ½ cup), tumbler (1 cup), pony (1 fl oz). "a drop", "a small
  quantity" and "a very little" are vague: no amount.
- Italian units for the Artusi text: grammi, gr., chilogrammi, litro, mezzo litro, decilitri and
  "N. 4" are read; "q.b." and "un po'" are vague; the name drops the unit, "di" and an elided "d'"
  ("4 decilitri d'acqua" → 400 Milliliter of acqua), and "spicchio" counts a clove ("1/4 di spicchio
  d'aglio" → 0.25 Clove of aglio). The name
  drops a leading "gr." only before "di" or a number ("100 gr. di burro" → burro), so the English
  "gr." of "1 gr. pepper" (green) stays in the name.
- Scraper sample forms: a leading "optional", "optional:", "Entirely optional:", "~" or "just
  under" is skipped before the amount, "juice from 1 lime" reads as "juice of 1 lime" (Lime
  juice, 1 Piece), and HTML entities are decoded first.
- Headings: a line ending in a colon with no amount ("Marmalade Pineapple Glaze:", "Chocolate:")
  and a short "For the ..." line with no amount and no colon ("For the sauce") are labelled
  `note` by the deterministic pre-pass (`RecipeLineClassifier`). A title-case line with no
  signal ("Mashed Potatoes", "Pears") is left: it may be the main ingredient.

### One food per recipe row: compound lines — BUILT ✅
A scraped line can name more than one food ("salt and pepper to taste", "1 cup each flour and
sugar", "butter or margarine"). Until it is split it sits on one food, or on an uncurated
wording row, and the label counts only that food. `GET /api/FoodCatalog/compound/preview?rules=&sampleSize=50`
and `POST /api/FoodCatalog/compound/apply?rules=<rule,...>&maxLines=5000`
(`Nom.Orch/Services/CompoundLineService.cs`, curation admin) repair them by the fixed rules of
`Support/CompoundLineSplitter.cs`; no model is involved. The preview writes nothing and gives,
per rule, the lines, the lines on uncurated and on curated rows and on approved recipes, the
row moves, the rows added and the rows placed at the end of the recipe, plus every compound line
left alone by reason. `apply` requires the rules it may use, so each rule rolls out on its own.

Split into one row per food (each part line is the source line with words deleted, never added):

- `and-salt-pepper`: unquantified salt and pepper in any order or spelling; both rows unmeasured.
- `and-unquantified`: an unquantified list of two to four names that are each a different curated food.
- `and-each`: "1 cup each flour and sugar"; every row gets the full amount.
- `own-quantity`: "2 tablespoons butter and 2 tablespoons flour"; each row keeps its own amount.
- `part-pair`: "rind and juice of 1 lemon"; the juice and the peel each get the fruit count.
- `and-quantified-salt-pepper`: "1/4 tsp salt and pepper"; the salt keeps the amount, the pepper is unmeasured.

Move to the first alternative (text and amount unchanged): `or-independent` (every alternative
is a food), `or-shared-head` ("chicken or vegetable broth" is Chicken Broth) and `or-first-named`
(only the first alternative is in the catalog; uncurated rows only). A curated row moves only
under the first two, and never when its own text names its food. Left alone: protected names
("half and half", "pork and beans", short curated names with "and"), preparation words, lists
with a part not in the catalog, quantified lists without "each" other than salt and pepper, "and"
mixed with "or", more than four names, either reading of a shared head being a food, and every
line whose first alternative is not in the catalog ("cream or milk"). A shared head is read both
ways: the first name (or its last word) joined to the last name's head or to every word of the
last name after its first, in singular or plural, so "milk or dark chocolate chips", "orange or
red bell peppers" and "garlic and onion powder" are left when "milk chocolate chips", "orange
bell pepper" or "garlic powder" is a food or an ingredient name; for an and-list this check runs
even when every part is a food. "Flavor" counts as a flavour word like "flavouring". A list part
counted in pieces must start with a count and name no measure ("a very little salt" and "1
quarter teaspoonful of salt" are left), and "lemon juice and grated rind" after a spoon measure
is left. A bare number in front of a food that has no weight per item is a proportion, not a
count, so "1/2 butter and 1/2 lard" and "½ or ¾ milk and ½ or ¼ water" are left (`bare number on
a food not counted in pieces`). Also left: "each" whose amount does not read ("tablespoonful each
of minced onion and parsley"), a later list part with its own count ("1 pinch of pepper and one
of salt") or its own measure ("one of allspice and half a teaspoonful of cayenne"), a pepper that
is chopped, minced or sliced (a vegetable, not the spice), bare "mustard" beside salt, pepper, a
ground spice, cayenne, paprika or sugar (dry mustard is meant), "mustard and cress" and its plural
"mustard and cresses" (a protected name also holds in the plural), a lemon,
orange or lime alternated with a spice ("lemon or nutmeg"), and a single first name before a
two-word last name that is no food while its first word is ("crab or lobster coral", "duck or
goose giblets": the tail is shared). A bare first alternative that is a food counted in pieces
before a longer last one whose first word is also such a food ("tangerine or orange juice",
"avocado or olive oil to rim pan", "Apricot or Peach-Stones", "lemon or lime gelatin") shares the
last word, so it is left (`alternatives of a whole food and a product of another`); a purpose
("to rim pan", "for frying") is not read as the head. "any jam" as the last alternative makes the
earlier names kinds of jam: "apricot, strawberry, or any jam" moves to Apricot Jam when that is a
food other than the first alternative's, else it is left, and so is any other "any ..." last
alternative. A list of whole foods whose later alternatives end in a product word (jam,
marmalade, preserves, jelly, juice, brandy, sauce, butter, oil) and are no bare product ("lard
or butter" is still picked) names that product, never the first whole food. The first food is
a whole food when it is counted in pieces, when every product is one only fruit makes (jam,
jelly, marmalade, preserves: "blueberries, cherries, or any other jam"), or when its own product
is a food or an ingredient name ("cranberries or grape juice"); "milk or orange juice" is no such
list and still goes to Milk. The line goes to the first food's
own product when the catalog holds it ("apple or quince butter" → Apple Butter), else the
product an "any other" alternative names ("apple, peach, or any other marmalade" → Marmalade),
else it is left (`whole foods sharing a product the catalog does not hold`: "peach or French
brandy", "raspberry, cherry, strawberry, or currant-juice", "avocado, coconut, or vegan butter",
"orange, peach, or apricot jam or preserves"). When the first food's product and a later
alternative are both foods ("avocado or olive oil") the line stays ambiguous. A list name is
found in the line only as a whole word ("salt" is not the start of "saltspoonful"). No
`recipe.RecipeSubstitution` rows are written.

A split is planned only when each part line reads back through `IngredientLineParser` and the
name rules as exactly the row it becomes, so `relink` leaves it as it is, and an acceptably
unquantified source gives acceptably unquantified parts. A part gets no amount from the method
or from any default unless its own text states one: the start-up repair leaves the recipe alone
while the `LineSplit` entry stands. Before v0.3.120 it did not, and a restart filled 146 part rows
of 142 split recipes on staging (2026-11-20; 117 of them salt parts, 102 given a pinch the method
shares with the pepper, and "vinegar and water" gave vinegar 32 cups), which made `relink/undo` skip
those 142 splits. The row stays on
the part that names its food (or moves to the first part); each other part is a new row at the
line's position, or at the end of the recipe when that food already holds it.

Each line is one `audit.AuditLogEntry` (`EntityType` `RecipeIngredient`, `EntityId` the
recipe): `LineSplit` with the row before in `OldValue` and the resulting rows in `NewValue`
(`{i, s, q, m, r}`: food, position, amount, measurement, text), or `LineAlternative` with
`food@position` like a relink. `POST /api/FoodCatalog/relink/undo?sinceUtc=<start>&apply=true` puts them
back with the other line repairs; a split whose rows were edited since is skipped. Do not prune
these journal entries: the source text of a split line lives only there.

Order: `relink/unfold` first (lines holding " + " wait and are counted as `FoldedDeferred`),
then the compound rules one at a time, then `relink/preview`. Same staging-first runbook as
above: dry run, note the UTC time, apply one rule, check recipes in the app, prove the undo,
re-apply, then the next rule; production only after that, behind a dump.

### Start-up repair and the undo guarantee — BUILT ✅
`ScrapedRecipeRepairHostedService` runs `ScrapedRecipeRepairService` over every scraped recipe
30 seconds after each API start (`ScrapedRepair:Enabled`, default on). For a line with no
amount it takes the amount its own text states, or the amount the method states once for it
(`MethodAmountFinder`); a line counted in pieces whose text states a unit takes that unit; it
splits a one-paragraph method into steps, decodes HTML entities and re-vets recipes no person has
reviewed. A line on a stated unit whose amount is what the line gives with the part it adds left out
(`IngredientLineParser.WithoutAddedPart`: the fraction of a hyphenated mixed number, a measure's "and
a half", a second measure joined by "and"), as older import readers took it, takes the whole in the
same unit: "Salt, 1-1/4 cups" at 1 Cup becomes 1.25 Cup, "three pounds and a half of sugar" at 3 Pound
3.5 Pound, "one quart and a pint of cut celery" at 4 Cup 6 Cup (staging checkpoint 2 M6). Any other
amount on a stated unit is left alone (a person's own amount, a right amount, a line that adds
nothing), and so are cans and packages. A row whose amount was set by anything but this pass is left
too: a person last changed the row (the recipe editor stamps the editor on a row whose amount it
changes), or the row no longer carries the amount the pass last journaled for it, so a curator who sets
"Salt, 1-1/4 cups" back to 1 Cup keeps it across restarts. These re-reads are journaled as `StartupFill`
like every other change of the pass, so `startup-fills/undo` takes them back; the undo removes the
entry, so an undone row is filled again on the next start. The pass ends with the log line

```
Scraped recipe repair done in <m>m: <n> recipes, <r> rows repaired (<s> of them re-read on their stated unit), ... <h> recipes left alone because a journaled line operation holds their lines
```

Two rules make it safe to restart the API between any apply and its undo:

- **A held recipe is not touched.** A recipe is held while it has a journal entry with `EntityType`
  `RecipeIngredient` (the recipe as `EntityId`) of any change type other than the classify lane's
  labels (`LineClassify`, `LineClassifySuggested`, which no undo reads) and the repair's own
  `StartupFill`, or a `LinkApply` `Line` entry that names it (`Support/RecipeLineHold.cs`). That
  covers compound splits and alternatives, unfold, relink and verified-wording relink, label review
  and its container re-read, container re-read, the repair lane's fills and the piece-count revert,
  and applied links; an undo removes or relabels its entry, which frees the recipe. A line operation
  added later is held by the same rule as long as it journals under `RecipeIngredient`. The repair
  lane (`RecipeAiRepairService`, its own `AiRepair` entries excepted) and the line-classify lane
  skip held recipes the same way and come back to them after the undo.
- **Every amount it still changes is journaled.** One `audit.AuditLogEntry` per line
  (`EntityType` `RecipeIngredient`, `EntityId` the recipe, `ChangeType` `StartupFill`, the line as
  `LineKind[food:position]`, amount before and after as `food@position|quantity|measurement`), and
  the row gets `LastModifiedDate`, so the nutrition sweep recalculates the label. Only the net change
  of a line is written: a count that is emptied as elliptical and then filled again from the method
  with the same amount ("one of rosemary" on a food with a weight per item) is neither written nor
  journaled, so a start no longer logs that pair on every restart and `startup-fills/undo` cannot
  take half of it back.

The method fill (`MethodAmountFinder`) takes an amount only when it stands next to the line's
food word, with nothing between but measures and preparation words ("two quarts of water",
"three pints of boiling water", "two beaten eggs" is 2). It takes none from a clause that states a
ratio ("to each", "to every", "per", "for every", "in the proportion", "half and half", "half
vinegar and half water", "to a gallon"), from a vessel or a length before or after the food word
("a kettle of water", "1-inch of water", "a water bath", "an oil barrel"), or from "waters" in the
plural ("three or four waters" is washing). It never counts pieces of a liquid (water, milk, wine,
stock and the like), nor of a food without a weight per item, so "one of Moselle wine (or hock)"
beside "one wine glass of the stock" is emptied as elliptical and journaled once.

Since staging checkpoint 1 (K1, 2026-11-20; method fills 3 wrong and 2 doubtful of 30): the
food's own other words may stand between the measure and its last word ("three tablespoonfuls of
olive oil" for "olive oil"), and "teacupful", "tumbler", "jill" and the other old measures count
as measures ("a teacupful of water" → 0.5 Cup). It takes nothing from a per-item ratio ("1/2
teaspoonful of melted butter in each tomato": "in", "into", "on", "over", "upon" or "inside" each
or every), from a step whose temperature the line contradicts (the line "cold vinegar" against
"two quarts of boiling vinegar"; cold, warm and hot are three classes, boiling counts as hot),
from a measure read as pieces ("a handful of chopped Parsley" is not one sprig), from an
alternative ("one-half can of tomatoes, or four chopped fresh tomatoes": a match in a clause
starting with "or" fills nothing at all), or for a folded row of two foods ("melted butter +
maître d'hôtel butter"; the method's amount names one of them). The rebuild's method fill uses
the same finder and the same fold rule.

Since staging checkpoint 2 (M4, M5, M19, 2026-11-21; about 130 wrong of 3,530 start-up rows, and
in the rebuild 8 wrong of 60): a ratio is read per sentence, not per comma clause, so "To every
quart allow half an ounce of allspice, the same quantity of ginger, half a teaspoonful of powdered
mace" and "Season with 1/4 teaspoon salt, ..., per cup of vegetable" fill nothing, and the ratio
forms include "each" before a noun ("on each plate", "around each oyster", "on top of each portion",
"Cover each chop", "for each four persons"), "to a pound of" and "to a quart of"; "each" or "every"
before a time or a side ("on each side", "every few minutes") is no ratio. A range in the method
("six or eight pounds of corned beef", "three to five gallons of cold water", "two or three
tablespoonfuls") is read whole, so it gives its lower end, as the parser reads a line's range; it
gave the upper end. A line that names its food with an "or" ("stewed or canned tomatoes", "French or
common mustard") takes the amount the method states before those same words ("a pint of stewed or
canned tomatoes"); a line joining foods with "and" still takes none. An item of a listing sentence
("add two cloves, one onion, one bay leaf") is filled like any other, but never as a count of a food
with no weight per item (Bay Leaf and Nutmeg have none, so 59107 and 110476 stay without an amount
by that rule).

Since staging checkpoint 3 (N1, N16, 2026-11-21; 5 wrong of 116 targeted start-up fills, 1 of 100 at
random, and 3 of 15 method-fill changes in the detector's reading): a proportion is also "to" a number
and a measure or "of" ("half a pint of water to two pounds of cherries", "three quarters of a pound of
sugar to one of fruit", "one tablespoonful of oil to four of vinegar"), unless a number stands before
the "to" (a range, "two to three cups"), and "for" an amount with no food after it ("for half a pint
dissolve 3 eggs"; "for two quarts of muscles" names the recipe's own food and is no proportion). A
proportion fills nothing even where its other side matches the recipe's own amount ("a teacup of water
to two pounds of sugar" beside a 2 lb sugar line): left empty, the safe side. A range is read whole
however many of the food's own words follow it ("Slice what will make 4 or 5 cups of cold boiled sweet
potatoes" is 4 cups; it read 5). "ten or a dozen onions" counts ten: a dozen is a count, not a measure.
An amount of a liquid another ingredient is dissolved, diluted, softened, soaked or steeped in is that
preparation's ("the cream of tartar dissolved in one teaspoon of hot water"), never the recipe's water.

The line reader since checkpoint 3 (N16): a cross-reference to another recipe of the book ("Marzapane
descritto al N. 579", "tartufi preparati nel modo descritto al N. 269") is no count and leaves the name
("Marzapane"); "Uova - 6 numero" counts six on "Uova", while "Uova, N. 4." counts four as before; a bare
measure word before the food is one ("tablespoonful finely shredded or grated onion" is 1 tablespoon,
"ounce of melted butter" 1 ounce), except "tablespoonful each of" two foods, which stays unread; a second
size after "/" repeats the amount and leaves the name ("8 ounces / 1/2 pound fresh tortellini" is 8 ounces
of fresh tortellini).

Since checkpoint 4 (P2, P8, P11) a French cross-reference ("comme celui du N. 579") and a price ("twenty
cents' worth chicken gizzards") are no amount and "copperas one-half ounce" is half an ounce; the method fill
counts a sentence it reads as a proportion as an amount of the food, so a part amount of another sentence no
longer fills the line, reads "add to two quarts of" and "seven to eight cups of" as amounts, carries a
per-pound sentence past ";" and the point of "lb.", reads "for thirty pounds" as a proportion and takes
nothing for a utensil line ("tin plate") or from the vessel a food is put into ("Put into a tumbler ...
syrup"). Every amount the start-up repair would write passes the line guard of the rebuild
(`LineAmountGuard`, see `recipe-reprocess.md`): an empty amount is filled only from one mention grounded in
the line's own source text, and a stored amount is never emptied by rule. Since checkpoint 5 (Q1, Q11) a line
the classifier marked `reference` (a sauce another recipe makes) or `equipment` (boiling water) takes the
amount its method states, as an ingredient line does; a note, an instruction or an artefact never does. The
done line counts the guard's `guardKept`, `guardReplaced` and `guardHeld` with the held ones by reason, and
each held line is stored as a hold (`recipe."LineGuardHold"`, origin `startup-fill`), read with
`GET /api/RecipeReprocess/line-holds`.

**Held-out measure.** `line-holdout.tsv` holds every line the three checkpoint logs judged, split once by
a hash of the row id; rules are written against one half, and the other half's errors per class are
recorded in `line-holdout.json` (see `golden-sets.md`), so a parser change shows what it does to lines no
rule was written for before staging reads a sample.

```
POST /api/FoodCatalog/startup-fills/undo?sinceUtc=<utc time>&apply=false   # dry run: entries, lines it would restore, lines changed since
POST /api/FoodCatalog/startup-fills/undo?sinceUtc=<utc time>&apply=true    # restores each line that still holds what the repair gave it
```

So after any number of restarts every line undo restores everything it journaled, with nothing
skipped because of the repair. An order-only reprocess pass made after the fills does not stop
their undo either: it follows each line to its new position (`recipe-reprocess.md`, "Order of
undos").

Operator notes (staging checkpoint 2): prove this undo against the time of the build's own first
start (a copy that never ran an earlier build has no earlier fills to undo, production included):
dry run, apply, compare the line fingerprint with the one before the start, restart, and the refill
must give the same fingerprint again. On the 2026-11-20 production data the first start fills
about 3,530 rows (about 2,294 of them method fills), not the 1,887 of checkpoint 1. The undo does
not put back the recipes the first start cleared from Requires Revision; the refill makes them
consistent again. Operators still wait for the "Scraped recipe repair done" line
after a container start before running an apply or an undo: the repair saves a batch of up to 500
recipes at a time, and a line operation that writes to one of them while that batch is open can
be overwritten.

Not journaled, and why that is harmless for undos: the re-vet moves a recipe no person has
reviewed between `RequiresRevision` and `NonCurated` and rewrites `VettingIssues` (intended; it is
counted in the log line only, and no undo compares a recipe's status), and the entity decoding
rewrites name, description and step text of such recipes (no line undo reads them).

The stand-alone rule itself still misreads some methods: on staging 139 stand-alone water lines
that state no amount are stored in pieces, 64 of them in recipes whose method says "two or three
waters", "a spoonful or two of water" or "six of water". Those fills predate the journal and are
left as they are; `startup-fills.tsv` keeps two of them as `known`.

### Alias trust: which wordings an import may follow — BUILT ✅
An alias (`recipe."IngredientAlias"`) says a wording has been seen on a food; its
`SourceContext` says who put it there. An import links a line to a food without review, so it
follows an alias only when the context names a deterministic rule or a person
(`Nom.Data/Recipe/IngredientAliasTrust.cs`, a case-sensitive prefix test):

| Context | Written by | Trusted |
|---|---|---|
| `fdc:description` | a person's link approval that renamed the food; the USDA description it kept | yes |
| `retired:<id>` | `relink/retire`; the name of the duplicate food retired into this one | yes |
| `link:deterministic:staple`, `link:deterministic:name-match` | the link lane applying a rule's proposal | yes |
| `admin:link:<source>` | a person approving a link in Admin → Food Catalog, one at a time or as a group | yes |
| `deterministic:recheck:name-key`, `:staple`, `:name-match` | `aliases/classify` (below) | yes |
| `link:ai:<model>` | the link lane applying a model pick on its own | no |
| none | links applied before 2026-11-03 and the `cleanup/normalize` run of that night; origin unknown | no |

A new context that begins with `fdc:`, `admin:`, `deterministic:`, `retired:` or
`link:deterministic:` is trusted without further change, so name new writers with care.

**What an import does.** `RecipeScrapingService.FindOrCreateIngredientAsync` (URL import, pasted
data and `POST api/StagedImport`) decides the order in one place: a live ingredient of the
normalised name (a curated one first, then the oldest), then the food a trusted alias gives the
name to when exactly one live food holds it, then the food an applied link merged the retired
ingredient of that name into (the new line is journaled under that link, as a `Line` row of its
`LinkApply` journal, so `link-apply/undo` moves it back with the rest), and otherwise a new
uncurated ingredient. Until `aliases/classify?apply=true` has written its first journal entry
on a database, an alias with no context is followed too, as imports did before alias trust; so
a harvest between the deploy and the classification lands where it always did and creates no
row the classification cannot take back. That
ingredient waits for the link lane like any other new name: a rule's proposal is applied by the
lane, a model pick waits for a reviewer. A soft-deleted ingredient that still holds the name in
the unique index is renamed to `<name> (deleted #<id>)` first and the rename journaled
(`audit."AuditLogEntry"`: `EntityType` `Ingredient`, `ChangeType` `NameFreed`, old and new
name); it is not revived, because its applied link would keep the lane from proposing it again.
The photo import (`RecipeImportOrchestrationService.AddIngredientLinesAsync`) applies the same
test to the aliases it matches lines against. The recipe form selects a suggestion on save for
typed text that equals its alias only when the search result says the alias is trusted
(`MatchedAliasTrusted`). Ingredient search and the restriction resolver still read every alias:
a person picks from the first, and the second only ever adds restricted foods.

**Writers.** A link applied by a person writes `admin:link:<proposal source>`; the same link
applied by a lane writes `link:<proposal source>`, so a reviewed batch has to go through the API
as a person, not be marked in SQL and left to the lane. An alias the food already has keeps its
context unless the new one is trusted and the old one is not, or the new one is a person's
(`admin:`) and the old one a re-check (`deterministic:recheck:`); that change is journaled with
the link, so the classify undo leaves the approved alias alone. `cleanup/normalize` hands the
context of a quantity-prefixed alias to its normalised replacement and leaves USDA descriptions
and retired names alone.

**Rechecking the aliases with no context.** `POST api/FoodCatalog/aliases/classify?apply=false&sampleSize=50`
(curation-gated, a dry run unless `apply=true`) examines every live alias with no context on a
live food and marks the ones today's rules give to that same food: the alias is the food's name
in the singular or plural (`name-key`); it is a bare staple name and the food is that staple's
USDA entry (`staple`); the name matcher places it on the food (`name-match`); or it is the
food's name once the preparation words are off (`name-key`). An alias a staple or the matcher
gives to another food is counted as a conflict and stays as it is, as does one no rule covers.
An alias that names a utensil ("jelly-bag", "pudding cloth", "sieve") or holds "each of" ("each of
corn meal and flour") takes no rule and stays untrusted. Nothing is deleted. Each mark is one `audit."AuditLogEntry"` row: `EntityType`
`IngredientAlias`, `ChangeType` `AliasTrust`, `EntityId` the food, `PropertyName`
`SourceContext`, `OldValue` the alias text, `NewValue` the context written. Running it again
only looks at what is still without a context.
`POST api/FoodCatalog/aliases/classify/undo?sinceUtc=<time>` sets each alias marked at or after
the time back to no context and removes its journal entry; an alias whose context has changed
since is skipped and its entry kept. The same by SQL:
`UPDATE recipe."IngredientAlias" SET "SourceContext" = NULL WHERE "SourceContext" LIKE 'deterministic:recheck:%';`.

Measured on staging on 2026-11-19: of 32,841 live aliases with no context, the rules confirm
7,312 (name-key 4,967, name-match 1,845, staple 500), name another food for 875 and are silent
on 24,654. In the lines judged on 2026-11-12 a rule-confirmed alias was right for 98.8 % of
lines and an unconfirmed one for about half.

### Line labels and model quantities: veto and correction — BUILT ✅
The line classification lane labels unquantified lines as an ingredient or as something that
never had an amount (reference, equipment, note, instruction, artefact). A label other than
ingredient takes the line off the nutrition label, the missing-amount count and the curated
ingredient gate, so `Support/RecipeLineKindGuard.cs` vetoes it when the line states an amount
or its normalised wording is exactly a curated food's name (the name map line relinking uses).
A vetoed model answer is stored as `ingredient`. Any other model answer that a line is not an
ingredient is only journaled as `LineClassifySuggested` unless
`Curation:LineClassify:ModelMayExempt` is true (default false). Labels are journaled as
`LineKind[{ingredientId}:{sortOrder}]` with the old kind in `OldValue`.

`POST /api/FoodCatalog/labels/review?apply=false&shortLines=false&maxLines=5000&sampleSize=50`
(`RecipeLineLabelReviewService`) returns the labels already written to ingredient. By default
it covers the certain cases only; `shortLines=true` adds the wider rule (a line of 40
characters or fewer on a curated food), which also returns harmless lines such as "boiling
water". A line that reads as a heading or a note is never vetoed and so never returned
(`RecipeLineKindGuard.ReadsAsHeading`): a label before a colon not followed by an amount
("Piping tips: Wilton #125, ..."; "egg wash: 1 large egg ..." still counts), a line in capitals
("MEATBALLS"), a line that starts with "or" ("or 6 baked potatoes") and fat or liquid taken from
the dish itself ("dripping from the top of the soup"). Single food names that are headings or
serving notes ("Mashed Potatoes", "Pears", "Asparagus") have no such signal and are still
returned. The apply then runs the container re-read on exactly the lines it returned, which the
re-read skipped while they were labelled ("4 cans (13-3/4 ounces each) chicken broth" → 55
Ounce), and reports them as `Reread`; those are `LineReread` entries, so `reread/undo` with the
label batch's start time puts them back. The re-read and the label review can therefore run in
either order. The dry run reports both rules (`Certain`, `WithShortLines`) with lines, recipes,
approved recipes and `ApprovedStopPassing`: approved recipes that pass the line rules
(unquantified allowance, curated foods) now and would not after the change. Each change is a
`LineKindReview` audit entry; `POST /api/FoodCatalog/labels/undo?sinceUtc=<start>&apply=true` restores a
batch, skipping lines labelled again since and rows that now hold another line than the one the
review relabelled (an unfold or split that kept the row's place); their entries stay.

The AI repair lane accepts a count in pieces only when the quote states a number, the food has
a `GramsPerItem`, or "a"/"an" stands before one countable item ("a bay leaf", "a leg of mutton",
"a sandwich loaf"). It refuses (`RecipeRepairGrounding.PieceRefusal`, reason codes in brackets) a
measure the parser did not read as a unit before the first "with"/"or" ("a glassful of good red
wine", "one scruple of ginger", "a bunch of parsley"; `unread-measure`), "a"/"an" before a vague
amount ("a little butter", "a few", "a couple of", "a grain of"; `vague-amount`) or before a
preparation or utensil ("a white sauce", "a clean tin"; `not-an-item`), and a quote with neither
a number nor an article (`no-count`). The number is read only before the first "of", "with",
"or" or comma, so "1 large egg beaten with 1 Tablespoon milk" is one egg; an Italian number word
counts ("tre rossi delle uova", "due rossi", "mezza palla") and the Italian article "un"/"uno"/"una"
is read as "a" is ("una foglia di alloro" is one item; "un po' di sale", "una salsa" and "una tazza di"
are refused like "a little", "a white sauce" and "a glassful of"), and a measure after a ratio's "to a"
belongs to the other food ("three eggs to a pint of apples" is three eggs).
`POST /api/FoodCatalog/quantities/revert?apply=false` (`AiRepairRevertService`) checks every
journaled `AiRepair` piece fill with the same rule and puts back the quantity and measurement
its journal entry recorded, except that a fill whose quote the line parser now reads as a unit
other than a piece ("1 heaped tablespoonful", "1 level teaspoonful", "a tbp of lard", "A large
teacupful") takes that amount instead (`Reread`, and `RestoredUnit` on each sample). `reasons=vague-amount,unread-measure` limits the revert to those
reasons; the dry run still counts every reason (`ByReason`), the fills left alone because
their reason was not chosen (`NotChosen`) and the fills that stay, by why (`GroundedByReason`:
`states-a-count`, `weight-per-item`, `one-countable-item`); `ReasonMeanings` spells each code out. A fill journaled without a
line position (`Quantity[{ingredientId}]`) on a recipe with two such lines holding the fill is
left alone (`Ambiguous`). When the fill's food no longer has any line in the recipe (merged,
relinked or split since), the fill is looked for on the one line of the recipe that holds the
filled piece count and states no count of its own (`FoundMoved`); two such lines are
`Ambiguous`. Each revert is an `AiRepairRevert` entry in the `LineReread` value
format; `POST /api/FoodCatalog/quantities/undo?sinceUtc=<start>&apply=true` replays a batch forward.

**Order.** Run `quantities/revert` before `relink/verified`, `compound/apply` and any link batch
(`link-groups` apply, or the lanes' auto-apply): those move lines to other foods, and the revert
can only follow a moved fill when exactly one line of the recipe can be it. A bare seasoning
whose bracket states an amount ("Cloves (1½ oz.)", "salt (a small teaspoonful)") is not
acceptably unquantified; one with a price or a brand in the bracket still is.

Neither correction changes a recipe's curation status, and no undo does either: a recipe the
deterministic auto-approval job (`RecipeAutoApproveHostedService`) approved between an apply and
its undo stays approved after the undo, with the lines the undo put back (an uncurated food again,
or no amount). The AI pause switches do not hold that job, since it is not a model lane; the
platform feature `pause-auto-approve` does (`PUT /api/Pipeline/pause/auto-approve` with
`{"isEnabled": true}`, or Admin → Platform features; the pipeline gate reports it as
`autoApprovePaused`). Turn it on with `ai-pause-all` before a data window and off only once no
change made in the window will be undone. The job looks at the switch before each batch (every
`Curation:AutoApprove:PauseCheckSeconds`, default 30, while paused) and again before a batch saves
its approvals, so a batch already running when the switch goes on keeps none of them; wait for the
log line `Recipe auto-approval paused` before the first apply. It then resumes where it left off,
or from the first recipe if the API restarted meanwhile. Both touch `LastModifiedDate`, so the
nutrition sweep recalculates the labels. Order of work as for the other line lanes: staging
snapshot, dry run, note the UTC time, apply, check the samples in the app, prove the undo,
then production.

### Verified wording table — BUILT ✅ (table empty until the first judged batch)
`recipe."IngredientWording"` holds one reviewed verdict per wording, keyed by
`IngredientNameNormalizer.WordingKey` (the normalised name, lower-cased; no plural or
preparation-word folding):

| Verdict | Meaning | Effect |
|---|---|---|
| `food` | the wording names this one live, curated, non-branded food with nutrition | imports land on it; `relink/verified` moves existing lines to it |
| `none` | the catalog has no such food yet (white wine, brandy, sherry) | the link lane stops asking the model; the worklist for a USDA import |
| `unclear` | names no single food ("kosher salt and pepper", "cream or milk") | the link lane stops asking the model |

It is a table and not alias rows because a map from wording to food has to be a function (an
alias name can belong to several foods), every row needs an id for the journal, and aliases cannot
say "no such food". Aliases with no `SourceContext` are not a trusted source for it.

**The reviewed file.** Tab-separated, header
`wording verdict food_id food_fdc_id food_name lines_at_review batch_row reason`.
`db/verified-wordings.tsv` in the repository holds that header only, as the template: each reviewed
batch is its own file (the first, `w01`, 233 rows, is kept with the staging evidence, outside the
repository), and every load names that batch file, never the template. The food is
found by `food_fdc_id` when given (ids differ between staging and production), otherwise by
`food_id` together with the exact `food_name` (seed foods have no FdcId). Candidates come from
`ops/verified-wording-candidates.sql` (read-only; `psql -v limit=2000 -f ...`), which ranks
unresolved wordings by the recipes they are the only blocker of in the auto-approval sweep, then
by scraped lines, with the three commonest raw lines and the pending model pick for the judge.

**Load.** `POST api/FoodCatalog/wordings/load?batch=<name>&apply=false&replace=false`, body
`{"tsv": "<file contents>"}` or `{"rows": [...]}`, curation-gated, a dry run unless `apply=true`.
A body with no row at all (the header-only template, an empty file) is refused with 400 and the
message "The batch holds no wording row, so nothing would be loaded ...", so a wrong file name can
never pass as a load of 0 rows; read `rows` of the dry run against the batch file's row count before
the apply. A row is refused, with its reason in `problems`, when the wording is not in key form (unless the
same batch holds or held that wording, as an unload leaves it: the batch then reloads in the key form it was
loaded under, so a change to the name normalizer, such as reading "teaspn." and "tbp" as units, cannot
strand a batch the system loaded; staging checkpoint 3, N19), the verdict
is unknown, the food is missing, deleted, branded, without nutrition or not curated, the wording
is the name of another curated food, two rows give it different answers, or the table already
gives it another answer and `replace` is off. A USDA Foundation or SR Legacy food awaiting
curation is curated by the load (`promoted`); a branded food never is. Each row written is one
`audit."AuditLogEntry"` (`EntityType` `IngredientWording`, `ChangeType` `WordingVerify`,
`OldValue`/`NewValue` `verdict:foodId`), each food curated one more (`EntityType` `Ingredient`,
`ChangeType` `WordingCurate`, `9001` to `9003`). `busiestFoods` lists the foods receiving the most
wordings, the place to look for a wrong mass mapping. `GET api/FoodCatalog/wordings/export`
returns the live table as TSV for diffing against the file.

**Unload.** `POST api/FoodCatalog/wordings/unload?batch=<name>&apply=false&withLines=false` soft-deletes the
batch's rows (journaled with `NewValue` null) and returns to awaiting curation each food a load
curated that no remaining wording points at, unless the catalog admission rule admits it by itself (a
Foundation or SR Legacy food with no admission reason whose energy check passed: #279327, curated by
the w01 load before admission, was demoted by staging checkpoint 3's unload and never came back). A row the batch took over from an earlier batch with
`replace=true` goes back to that batch's verdict, food and note instead (`restored`); the replace
journals the earlier batch and note as `Batch` and `Note` rows beside the `IngredientId` row.
Unload replacing batches newest first. Loading the wording again revives its row. With
`withLines=true` every row's lines, sources, proposals, picks and import landings are first taken
back as a retract does (below, `lines` in the result), in the same transaction, and no rejection
memory is written: an unload is a rollback, not a verdict. Unload, load and `relink/verified?batch=`
again give the same rows, byte for byte, also for a row in an older key form: the relink finds its
source by the name as written and a line whose text ends with the wording after an amount
(`LineRelinkService.NamesWording`). A line whose move back would lower a recipe's diet flags stays where it
is with its entry (`lines.heldByDiet`, counted by the dry run too), so an unload and a reload that refuses
the row leave the lines as they were (staging checkpoint 5, Q21; `link-recheck.md`, "Every other path that
moves a line").

**Retract one wording.** `POST api/FoodCatalog/wordings/{id}/retract?reason=<why>&apply=false&evidenceKey=`
takes one live row back with everything it did, found by the journal rather than by time
(`LineRelinkService.UndoForSourcesAsync`): its sources (uncurated ingredients named with the
wording, by their `FoodRetire` entry into the wording's food or still live by name) get back the
`LineRelink` moves into the wording's food and come back themselves with the proposals the relink
withdrew with them (only the `ProposalWithdraw` entries written with the source's retirement, same
time and person; a pick catalog admission withdrew from the same ingredient stays withdrawn and
keeps its entry for `--admit --undo`); the picks withdrawn because the table judged the wording
(`WordingPickWithdraw`, matched by the pick's wording key) open again, except, in an unload that
puts a row back to an earlier batch's verdict, a pick that verdict contradicts. A withdrawn pick or
proposal whose proposed food may never be a link target (a prepared food, or a food deleted since)
stays withdrawn in a retract and an unload, so a reload that refuses the row for that reason does not
leave it open in the queue (staging checkpoint 4, P14: model pick #325288 of `franco-american
spaghetti` was reopened by the w01 unload and never withdrawn again); and each import
landing (below) goes to the food the import finds
without the table (`RecipeLineBuilder.FoodWithoutWordingAsync`: the ingredient of that name, the one
trusted-alias owner, an applied link's food, else a new uncurated ingredient, journaled as
`WordingRetract` with `EntityType` `Ingredient` and the wording as `NewValue`), journaled as
`WordingRetract` (`EntityType` `RecipeIngredient`, old and new `food@position`); a landing the import
would put on the same food without the table stays (`landingsKept`). Line entries of a recipe
rebuilt after them are skipped and keep their entries (`skippedRebuilt`, `landingsSkippedRebuilt`;
the dry run's `onRebuiltRecipes`): undo the rebuild first. Then the row is soft-deleted (`WordingRetract`,
`EntityType` `IngredientWording`, `NewValue` null) and the verdict, food, source, reason and
evidence key are kept in `recipe."IngredientWordingRejection"` (`WordingRetract`, `EntityType`
`IngredientWordingRejection`). The dry run counts the journal entries it found and writes nothing.
A food the load curated stays curated after a retract (the batch unload demotes it). Lines the
wording's earlier food got under another verdict are not moved.

**Rejection memory.** While a rejection row is live, a load refuses the same wording, verdict and
food (`the wording was retracted from this verdict and food`) when the rejection has no evidence key
or the row's `evidenceKey` equals it; another food, another verdict or new evidence goes through. So
`claret` retracted from Claret #287424 can be loaded onto red table wine at once, and a lane never
writes a retracted pair again on the same evidence. `load?overrideRejections=true` writes the pair on
purpose and lifts the memory (`WordingRejectionLift`, journaled): that is how a retract is undone,
followed by `relink/verified?batch=`; the result then equals the state before the retract. The
relink puts each landing a retract or an unload took off a food back onto it while the table gives
its wording that food again (`landingsReturned`, journaled as `WordingResolve` again, whatever the
line's text), and deletes an ingredient the retract created for landings that holds nothing once
they are back (`ingredientsRemoved`; no line, proposal, alias, nutrient, relink move or household
use). A landing on a recipe rebuilt after the retract stays (`landingsReturnSkippedRebuilt`).

**Existing lines.** `POST api/FoodCatalog/relink/verified?apply=false&maxLines=20000&sampleSize=50&batch=`
moves the scraped ingredient lines of every uncurated ingredient named with a `food` wording (of
that batch only, and withdrawing only that batch's contradicted picks, when `batch` is given) to
its food through the journaled `LineRelink` move (re-positioned, never folded), then retires each
such ingredient left with no line and nothing else pointing at it (`FoodRetire`, name tombstoned)
and withdraws its unreviewed link proposals (pending, or rejected by no person; `proposalsWithdrawn`,
each journaled as `ProposalWithdraw`, `EntityType` `FoodCatalogProposal`). It also withdraws every other pending, unreviewed
link proposal whose wording the table has judged and whose pick is not the table's food
(`picksWithdrawn`), each journaled as `WordingPickWithdraw` (`EntityType` `FoodCatalogProposal`).
Member-written recipes and non-ingredient lines stay. A line moves only when its own text,
normalised as the wording key is (singular and plural included), is the wording: stray lines an
import parked on the row ("one gallon of water" on `gelatine`) stay where they are, keep the row
from being retired, and are counted as `leftBehind` with `leftBehindSamples` in the dry run.
The dry run's `sourcesRetired` and `proposalsWithdrawn` are the totals of every batch the apply
needs, not of the first `maxLines` batch. Lines are moved in recipe, position and food order, so
two lines that land on the same food in one recipe take the same positions on every apply.
`relink/undo?sinceUtc=<T0>&apply=true` puts lines and ingredients back, opens the withdrawn picks again
(`picksRestored`) and brings back the proposals of retired ingredients with their status
(`proposalsRestored`) after every other step, only when their ingredient is back: when another food has
since taken the retired name, the ingredient and its proposals both stay withdrawn, are counted
`skipped` and keep their journal entries; run it before `wordings/unload`, and keep other relink work out of the
window, since the undo is by time. Proposals withdrawn by a release before v0.3.121 have no
journal entry and are not revived.

**Gates.** With `VerifiedWordings:ResolveOnImport=true` (default `false`),
`RecipeScrapingService.FindOrCreateIngredientAsync` looks the wording up first, before the name
and the trusted aliases, and lands it on its food while that food is live and curated; `none`,
`unclear` and a food that lost curation fall through to the usual order. Each line landed that way,
by an import or a rebuild (`RecipeLineBuilder`, shared by both), is journaled as `WordingResolve`
(`EntityType` `RecipeIngredient`, the recipe as `EntityId`, the wording key as `OldValue`,
`food@position` as `NewValue`), so a retract or an unload with lines takes it back; a rebuild's
entry carries the rebuild's time and is retired with the rebuild by `RecipeReprocess/undo`. Lines
landed before this journal existed (w01 with `ResolveOnImport` on) have no entry and stay on the
food when the wording is retracted. With
`VerifiedWordings:GateLinkLane=true` (default) the link lane takes no ingredient whose wording the
table has any verdict on, and `ProposeAsync` sets aside one handed to it before the staple list,
the name matcher or a model sees it, with a Rejected `deterministic:verified-wording` proposal:
the lane's source query compares the lower-cased raw name, `ProposeAsync` the wording key, so a
raw name such as "1 teaspoon vanilla" would otherwise come back every pass and hold a batch slot.
A food a wording points at cannot be retired by `relink/retire`.

**Rollout order.** `./db/apply.sh` (after `--dry-run`) is mandatory before a build that has
the table starts; there is no setting that makes the old schema safe. The same build maps
`measurement."Measurement"."IsContainer"`, and that table is shared by every measurement type, so
against the old schema every query that reads a measurement (recipe pages, the measurement
endpoints, the nutrition recalculation) fails with `42703 column does not exist`, whatever
`VerifiedWordings:GateLinkLane` says. With the gate on, every link pass also queries
`recipe."IngredientWording"` and fails, logged. Once the columns and table exist the API works
again without a restart.

**Rows the wording tiers write.** The deterministic wording tier lane (`wording-tiers.md`) writes
rows only through this loader, one batch per tier and pass (`wt-<tier>-<stamp>`), with source
`deterministic:<tier>` (`LoadAsync`'s `source`; a reviewed file keeps `verified:<batch>`) and the
evidence key `wording-tiers:lines-<line count>`, then relinks that batch alone. Its undo is the per-wording
retract with that evidence key, so the rejection memory keeps a retracted pair out until the
wording has twice the lines it was loaded on; a retract never demotes a food the load curated.

**Rows the wording readers write.** The wording reader lane (`wording-readers.md`) writes rows only
through this loader as well, one batch per frame tier and pass (`wr-read-<stamp>`, `wr-alcohol-<stamp>`),
with source `ai:readers:<hash>` (the readers' rules, prompts and the two reader models) and the evidence
key `wording-readers:lines-<line count>` in its decision, then relinks that batch alone and has the
sentinel read the moved lines. It writes `none` and `unclear` rows too, which move nothing. Its undo is
the per-wording retract with that evidence key. The verified-wording tier of the tiers reads its rows as
two readers' answer for other wordings of the same key; it does not read the tiers' own rows.

**Rename.** `POST api/FoodCatalog/relink/rename?id=<food>&name=<new name>` renames a live curated
food and sets `NameNormalized`; a soft-deleted holder of the name is tombstoned, a live holder
stops it (`heldByIngredientId`, `heldByLines`), and aliases of the new name come off other foods.
It journals as `FoodRetire` so `relink/undo` gives every name back. `relink/wordings` dry runs
now list `planned` (lines per food, wording and target) and `rejectedMoves`.

### Applied links: journal, undo and holds — BUILT ✅
Every USDA link applied through `FoodCatalogReviewService.ApplyProposalAsync`, by a person or a
lane, is journaled in `audit."AuditLogEntry"` (`EntityType` `FoodCatalogProposal`, `EntityId`
the proposal, `ChangeType` `LinkApply`; format in `Nom.Orch/Services/Support/LinkApplyJournal.cs`):
a `Proposal` header with the wording's food, name, status and source and whether it was merged;
one `Line` row per recipe line the merge moved (`recipe:food@position`, old and new); every open
link proposal the merge removed, in full; the USDA food's earlier curation status; any rename;
each alias added, brought back or given a trusted context, with its state before; and, when the
wording's food is kept, each nutrient row copied and each serving, density or item weight filled.
The header and the proposal's Applied status are saved in the merge's own transaction, and on
Postgres the whole apply (merge, renames, aliases, journal) is one transaction, so a failure
after the merge leaves no merged link without its journal.

`POST api/FoodCatalog/proposals/undo-applied?sinceUtc=<time>&source=<prefix>&systemOnly=false&apply=false&max=500&sampleSize=50`
(curation-gated, a dry run unless `apply=true`) takes back, newest first, the links applied at
or after the time: the wording's food comes back under its name, each line goes back to it at
its old position, the removed proposals are written back, the aliases and values go back to
what they were, the USDA food gets its earlier status unless another applied link still points
at it, and the proposal returns to Pending with ` · hold: undone` on its reason, so no lane
(staples, name matches, verified model picks) applies it again on its own; a person can. When a
person's approval gave the USDA food the wording's own name, that name comes off the USDA food
before the wording's food takes it back. Only the lines the undo moves get a new `LastModifiedDate`, plus every line of the USDA food
when the undo took nutrient values or measures off it (so its labels are recalculated). A removed
proposal written back keeps its original `CreatedDate`, `LastModifiedDate` and `CreatedByPersonId`
(journaled from this release; an older journal entry has no creator, so such a proposal gets the
person running the undo as creator but still its original dates), so the pipeline page's
`lastProposedAt` does not move.

A link with a line that is no longer where the link put it (moved by a relink, a compound split
or an unfold since, edited or deleted) is skipped and reported (`proposalsSkipped`, the sample's
`skipped` says how many lines) and nothing of it is written. Undo the later change first; or
pass `leaveMovedLines=true` to take the link back anyway, leaving those lines where they are
(`linesSkipped`) with their journal rows kept as `LinkApplyLeft`. A line an order-only pass of the
rebuild moved to another position is not "moved": the undo follows it through the order journal
and takes it back where it now stands. A link with a line on a recipe whose rows a rebuild rewrote
since the link is skipped like a moved one, its lines counted in `skippedRebuilt` (and the sample's
`linesSkippedRebuilt`), and nothing of it is written: undo the rebuild first and the link then comes
back whole. With `leaveMovedLines=true` those lines stay where the rebuild put them, their journal
rows kept as `LinkApplyLeft`, and the rest of the link is taken back. A rebuild that replaced only
the steps does not count. An alias changed since is left
where it is and counted. When another applied link still stands on the USDA food, its status row
is handed to the newest such link with a journal, whose own undo restores the status; links
taken back earlier in the same call do not count as standing, so the dry run reports what the
apply does. Each undone proposal leaves one `LinkApplyUndo` row and loses the `LinkApply` rows
it reversed. Skipped links stay Applied, do not count towards `max`, and are reported in
`proposalsSkipped`; `remaining` counts only links the call did not reach, so "repeat until
remaining is 0" ends. It does not run inside `relink/undo`: when both a relink and link applies
happened in the window, run `relink/undo` first so lines a relink moved after the link are back
on its food. A withdrawn (soft-deleted) proposal the merge removed comes back withdrawn.

The lanes apply exact name matches only under `IngredientLinking:AutoApplyNameMatches` (default
off), at most `IngredientLinking:MaxNameMatchAppliesPerDay` (200) a day and
`IngredientLinking:MaxNameMatchAppliesPerFoodPerDay` (25) per USDA food, the share checked within
each batch as well; staples are uncapped. A person's approval never renames the USDA food to a
name a soft-deleted food holds (the unique name index covers deleted rows).
Before a name match or a verified model pick is applied on its own, the hold rules in
`Nom.Orch/Services/Support/LinkAutoApplyGuard.cs` run (`IngredientLinking:HoldGuard`, default
on): the wording names more than one food; it is the name of another curated food; the USDA
description carries a state (dried, canned, cooked, frozen, ground, powder) the wording does not
while other USDA foods share its lead word; or, for a name match, the wording reads in the
description's own order ("cake chocolate"). A held pick gets ` · hold: <rule>` on its reason and
stays Pending for a person. `GET api/FoodCatalog/proposals/link-holds?sampleSize=50` counts what
the rules would hold across all pending name matches and model picks and writes nothing.
`IngredientLinking:AutoApplyVerifiedAi` defaults to off.

### Food-group enrichment (food group + IsWholeFood) — category only
`Nom.Import/Services/FoodGroupEnrichmentService.cs` (`--enrich-food-groups [--overwrite]`)
sets a food's group and whole-food flag from its stored USDA category alone, through
`CatalogAdmissionRule.GroupFor` and `EdibleFor`. It no longer classifies names by keyword
and no longer asks a model: both put foods in the wrong group (black pepper in Vegetables,
"Steak Sauce" in Beverages on the "tea" inside "steak", a canola blend in Vegetables), and a
wrong group is worse than none because household food-group minimums count it. A food
without a stored category is left alone. The name-keyword classifier
`FoodGroupHeuristics.ClassifyFoodGroup` is removed.

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
