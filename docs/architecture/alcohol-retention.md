# Alcohol retention: how much alcohol stays after cooking

Alcohol design A7 and A8, owner answer 6 (2026-11-20). Branch `cleanup2/alcohol-retention`.
Rules and a published table only; no model reads or writes anything here, and no food's stored
nutrition is changed.

## The table

`nom-api/Nom.Orch/Nutrition/usda-retention-r6-alcohol.tsv` holds the ten alcohol rows of the
USDA Table of Nutrient Retention Factors, Release 6 (2007), Nutrient Data Laboratory, ARS, page 12,
food group 14, retention codes 5001 to 5010 (the alcohol column, nutrient 221; every other column of
those rows is 100). They come from the analytical study of Augustin et al. 1992 (J. Am. Diet. Assoc.
92:486-488). Each row carries its title, publisher, year, page and locator; `AlcoholRetention.Problems`
refuses a row without them, and a test pins the ten percentages.

| Code | USDA description | Kept |
|---|---|---|
| 5001 | no heat, stored overnight | 70 % |
| 5002 | stirred into hot liquid | 85 % |
| 5003 | flamed | 75 % |
| 5004 | stirred, baked or simmered 15 min | 40 % |
| 5005 | 30 min | 35 % |
| 5006 | 1 hour | 25 % |
| 5007 | 1.5 hours | 20 % |
| 5008 | 2 hours | 10 % |
| 5009 | 2.5 hours | 5 % |
| 5010 | not stirred in, baked 25 min | 45 % |

No heat at all, and a recipe with no steps, keep 100 % (nothing heated, nothing lost); that is a
rule, not a USDA row.

## The step reader (`AlcoholRetention.Read`)

Input: the recipe's steps in order and the drink line. The steps are split into sentences.

1. **Find the drink.** The words that name it are the line's alcohol words (the alcohol sense rules'
   trigger, `AlcoholLineContext.Trigger`), and for a flavouring the word before it ("vanilla" for
   "vanilla extract"). The first sentence that names it is the drink's sentence; reading starts at
   the clause that names it (a clause ends at a comma or "then"), so "Simmer the onions 10 minutes,
   then add the wine" does not count the onions' 10 minutes.
2. **Drink never named.** Heat anywhere in the steps gives "up to" (85 % on the stove, 45 % if only
   the oven); no heat gives no heat (100 %).
3. **Heat after the drink.** Heating words: simmer, boil, stew, braise, reduce, poach, cook, heat,
   fry, sauté, sear, steam, scald, deglaze, blanch, grill, microwave, thicken, "over a low flame"
   (stove), and bake, roast, broil, oven (oven). "Remove from the heat", "preheat", "baking soda" and
   the like are not heating. A heating sentence's first time ("30 minutes", "1 1/2 hours",
   "1-1/2 hours", "1½ hours", "half an hour", "an hour and a half", "1 hour 15 minutes", "20 to 25
   minutes" read as 20, number words) is added up over the sentences that cook the drink: from its
   clause on, stopping before a sentence that starts a separate preparation ("Meanwhile boil the
   pasta", "In another pan", "separately", "While the stew simmers") and after a sentence that
   chills, cools, refrigerates, freezes, sets aside, reserves or serves the dish. Heat past that
   point is for something else ("Whip the cream with the brandy; chill. Bake the shortcakes for 20
   minutes." keeps all of the brandy), so it is not read; this can only raise the factor.
   - 15 minutes or more: the stirred-in rows by the largest time not above it (5004 to 5009); oven
     only and under 30 minutes: 5010 (45 %, the higher of 5004 and 5010).
   - Under 15 minutes, or heat with no time: "up to" the method's highest factor: 85 % (5002) on
     the stove, 45 % (5010) in the oven.
   - Flamed (flame, flambé, ignite, set alight, light the brandy): 75 % (5003), unless a timed heat
     after it gives less.
4. **No heat after the drink.** Off the heat in its sentence ("remove from the heat and stir in the
   sherry", "whisk in off the heat"), or stirred in right after a stove step: 85 % (5002). Stored
   overnight or for days: 70 % (5001). Otherwise 100 %.

The reader errs on the side of more alcohol: "up to" figures, the lower end of a range, seconds not
counted as time, a drink the steps do not name read as "up to", heat after a chilled, set-aside or
separate step not read. A dish whose drink is heated after a marinade is discarded still reads the
heating (the alcohol left in the dish is small either way). A sauce set aside and later returned to
the pot and simmered reads as not heated after the pause (more alcohol than stays, the safe side).
The stored basis says what was read: `no-steps`, `no-heat`, `overnight`, `hot-liquid`, `flamed`,
`timed:<simmer|bake>:<minutes>`, `up-to:<simmer|bake>[:<minutes>]`.

## Where it is stored and how the label uses it

Two nullable columns on `recipe."RecipeIngredient"`: `AlcoholRetention` (numeric(5,4), the share that
stays) and `AlcoholRetentionBasis` (varchar(64)). Null means not read: all of the line's alcohol
counts.

`RecipeNutritionService.RecalculateAsync` lowers each counted line's alcohol by what does not stay
and its energy by 6.93 kcal per gram of it (the FDC calorie factor for alcohol; never below 0).
Nothing else changes, and the food's stored values are never touched. Half a cup of white wine
(118 g, 12.2 g alcohol) simmered 30 minutes keeps 35 %: 4.3 g, and the recipe loses about 55 kcal.

## The run, its journal and its undo

`POST api/FoodCatalog/alcohol-retention?apply=false&maxRecipes=20000&afterRecipeId=0&sampleSize=50`
(curation-gated, `IAlcoholRetentionService.RunAsync`) reads every live line on a food that stores
alcohol, in recipe order, and writes the factor and basis only where they change, each with an
`AlcoholRetention` journal entry (`AuditLogEntry`, old and new `factor|basis`), then recalculates
those recipes. It answers the lines read, the lines changed, the recipes recalculated, the counts by
kind and a sample. A dry run writes nothing. Running it again writes only what changed (new lines,
edited steps).

`POST api/FoodCatalog/alcohol-retention/undo?sinceUtc=<stamp>&apply=false` gives each line back the
value it had before every entry at or after the time, newest first, skipping a line read again since
(its value is no longer the entry's new value) and a recipe rebuilt since, and recalculates the
recipes it changed. The dry run follows each line through the entries it would restore, so when two
runs changed the same line it counts both as restored, as the undo then does.

Operator notes (staging checkpoint 2): on the 2026-11-20 production data after the admission (66
more alcoholic foods than the design counted) the dry run reads about 14,048 lines in 12,832
recipes, not about 10,184 recipes. The factor and basis are kept by an order-only reprocess pass
and its undo, and the retention undo follows a line such a pass moved; run the retention again
after a full rebuild, which writes its lines without a factor.

## The recipe page note (A8)

`RecipeResponseModel.Alcohol` (`RecipeNutritionService.AlcoholNote`) counts the recipe's lines with
`RecipeAlcohol.Count`: drinks (a line on a food that stores more than 0.5 g alcohol per 100 g that is
not a flavouring), flavourings (a USDA Spices and Herbs food, or a food or line read as an extract or
flavouring, unless the line's own text reads as a drink), and drink lines that cannot be counted (a
drink line on a food with no alcohol figure, a drink line on a flavouring food such as "1/4 cup
brandy" on Vanilla Extract, or one with no gram weight). Under the nutrition label (`nom-nutrition-label`,
`data-testid="nutrition-alcohol-note"`, with `-drinks`, `-cooking`, `-flavouring`, `-uncounted`):

- "Contains alcohol: Dry White Wine. As written, about 3 g alcohol per serving."
- "Simmered about 30 minutes, so about 35% stays (USDA retention table): about 1.1 g per serving.
  Calories count only the alcohol that stays." ("Cooking time not stated, so up to 85% may stay";
  "Flamed, so about 75% stays"; "Not cooked after the alcohol is added, so all of it stays.")
- "Vanilla Extract adds a trace of alcohol (under 0.5 g per serving)."
- "The alcohol in “a glass of sherry” can't be counted yet, so a household that avoids alcohol is not
  offered this recipe."

The FDA-style label itself is unchanged.

## The diet gate (owner answer 6)

No Alcohol carries the alcohol nutrient row with `MaxAmountPerServing` = 0.5 g
(`DefaultRestrictionCriteria.MaxPerServingByName`; `EnsureAsync` moves an untouched uncapped default
row to it, and leaves an admin-edited type alone). Halal and Whole30 carry the uncapped row and
exclude a recipe with any drink line, however long it cooks: many Halal observers avoid wine as an
ingredient whatever the cooking (lead decision after wave 6, `cleanup2/ck3-small`; Halal was capped
from `cleanup2/alcohol-retention` until then, and `EnsureAsync` moves an untouched capped Halal row
back to the uncapped default). Flavouring extracts get the page note only, for all three.
So a beef stew with a cup of wine braised 2.5 hours (about 0.16 g per serving of 8) is offered to a
No Alcohol household and kept out for a Halal one (`diet-gate.tsv` dg-33 to dg-40).
`DietCriteriaMatcher`:

- A capped alcohol criterion forbids no food that stores an alcohol figure. A food with no alcohol
  figure whose own name reads as a drink is still forbidden, and the word patterns still flag names
  with no figure.
- `RetainedRecipesAsync` excludes (`RestrictedIngredientSet.RecipeIds`: meal plans, search,
  "Surprise me") every recipe where more than 0.5 g of its drinks' alcohol stays per serving (the
  label's servings, or the whole recipe when it gives none), or where a drink line cannot be
  counted. The whole-catalog count (`RecipeAlcoholAsync`) is kept in `DietGateCache` under
  `recipe-alcohol:<nutrient>`, warmed at start-up, and marked stale by `DietGateCacheInvalidator` on
  writes to recipe lines (which hold the retention), foods, nutrients, a recipe's servings
  (`recipe."Recipe"` inserts, deletes and updates of `Servings`, `RecipeServings` or `IsDeleted`)
  and measurements (`measurement."Measurement"`, whose factor and container flag weigh a drink line).
- The recipe page's diet matches name each drink line of an excluded recipe, with "About 3 g of
  alcohol per serving stays after cooking, more than 0.5 g.", or each drink line that cannot be
  counted.
- Flavourings never exclude (a note only). A line whose own text reads as a drink is never a
  flavouring, whatever its food.
- A type with an uncapped alcohol row (Whole30) forbids every alcoholic food and every drink line,
  however long it cooks; an uncapped row on the same type wins over a capped one.

## Golden set and scorer

`alcohol-retention.tsv` (74 rows: 64 rule-given, `rule:alcohol-retention`, `ok`; 10 realistic methods
judged by the builder, `agent-review-alcohol-retention-2026-11-21`, `agent`). No-model scorer lane
`alcohol-retention` in `model-lanes.json` (`required`, floor 100 %, no false accept; a false accept is
a factor below the row's). Record: `dotnet run --project nom-api/Nom.Import -- --golden-score --lane
alcohol-retention --record`; `GoldenAlcoholRetentionTests` checks every row, that every kind has a
rule-given row and every timed USDA row is reached, and that the record is current.

## Not built here

- No lane on the lane frame and no model auditor: the reader is rules over a published table, gated
  by its golden set at 100 %; its error is on the safe side by construction ("up to").
- The run is not scheduled; re-run it after imports and rebuilds (it writes only what changed).
- Retention is not read on every recalculation: a recipe saved after a run counts its new lines as
  written until the next run, so an undo stays an undo.
