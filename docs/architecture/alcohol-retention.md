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
   "vanilla extract"); a line with no alcohol word on an alcoholic food ("1 teaspoon vanilla" on
   Vanilla Extract) is found by its first word that is no amount, unit or modifier ("vanilla"). A
   step that names the drink only as "liquor", "spirits" or "alcohol" with no determiner ("add the
   spice and liquor") names it; "the liquor", "their liquor" and "oyster liquor" are cooking liquids
   and do not. The first sentence that names it is the drink's sentence; reading starts at
   the clause that names it (a clause ends at a comma or "then" when what follows names a verb), so
   "Simmer the onions 10 minutes, then add the wine" does not count the onions' 10 minutes, and
   "Stew in water, white wine, sweet herbs" still stews the wine.
2. **Drink never named.** When a step names some other drink word of the same kind (the trigger
   again: "When cold, pour the spirits over", "the wine", "their liquor"; a wine word for a wine, a
   spirit word for a spirit, and "spirits", "liquor" or "alcohol" for any drink, so "Serve with white
   wine sauce" never places the brandy of the line), the drink may have gone in there, so the
   reader reads from each such step and from "up to" below, and keeps the highest factor; the
   mix-all step is not used. When no step names any drink word and a step mixes all the ingredients together ("Mix all the ingredients
   well together", "Combine all the ingredients", "Whisk in the remaining ingredients", "fold it all
   in"; a step that mixes the dry ingredients does not count), the drink is in that mixture and reading starts there: the USDA
   rows 5004-5009 measure exactly that, a drink stirred into a mixture that is then baked or simmered.
   Otherwise heat anywhere in the steps gives "up to" (85 % on the stove, 45 % if only the oven); no
   heat gives no heat (100 %). Every reading of this step is an **estimate** (`Unnamed`), and so is
   every "up to" reading: the run counts them (`estimated`, `unnamed`) and marks each sample, so a
   figure read from a step that names the drink is never mistaken for one placed by rule.
3. **Heat after the drink.** Heating words: simmer, boil, parboil, stew, braise, seethe, coddle,
   fricassee, reduce, poach, cook, heat,
   fry, sauté, sear, steam, scald, deglaze, blanch, grill, microwave, thicken, "over a low flame"
   (stove), and bake, roast, broil, oven (oven). "Cook" and "heat" in a sentence that bakes, roasts
   or names the oven are the oven's heat ("cook in a moderate oven"). "Blanched" describes a
   ready food ("almonds blanched and shredded"), not heat on the dish. "Every 30 minutes" ("basting
   every 30 minutes") is how often, not how long, and is not a time. "Remove from the heat", "preheat", "baking soda" and
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
   - Flamed (flame, flambé, ignite, set alight, set fire to, ablaze, light the brandy): 75 % (5003),
     unless a timed heat after it gives less.
4. **No heat after the drink.** Frozen after the drink ("freeze", "into the freezer", "until
   frozen"): 100 %, whatever came before, since a frozen or uncooked dish keeps all its alcohol. Off
   the heat in its sentence ("remove from the heat and stir in the sherry", "whisk in off the heat"),
   or stirred in right after a stove step: 85 % (5002), unless the sentence says the mixture is cold
   before the drink goes in ("When cold, add the brandy"). Stored overnight or for days: 70 % (5001).
   Otherwise 100 %.

The reader errs on the side of more alcohol: "up to" figures, the lower end of a range, seconds not
counted as time, a drink the steps do not name read as "up to", heat after a chilled, set-aside or
separate step not read. A dish whose drink is heated after a marinade is discarded still reads the
heating (the alcohol left in the dish is small either way). A sauce set aside and later returned to
the pot and simmered reads as not heated after the pause (more alcohol than stays, the safe side).
The stored basis says what was read: `no-steps`, `no-heat`, `overnight`, `hot-liquid`, `flamed`,
`timed:<simmer|bake>:<minutes>`, `up-to:<simmer|bake>[:<minutes>]`.

### Staging checkpoint 2: the doubtful items, decided by the table (`alcohol-retention/3`)

| Item (checkpoint 2, section 20) | Decision and the USDA row | Golden row |
|---|---|---|
| A drink the steps never name read at the stove's 85 % in an oven dish (17180 card cakes, vanilla) | "Cook in a quick oven" is the oven: up to 5010, 45 % | ar-067 |
| The same in a two-hour boil (24825 Hunting Pudding, brandy) | The first step mixes all the ingredients, so the brandy is in the mixture boiled 2 hours: 5008, 10 % | ar-068; ar-020's expected value changed by hand (up to 85 % to 5006, 25 %) for the same reason |
| Plum Pudding (23122) "add the spice and liquor" | "liquor" names the brandy; boiled six hours: 5009, 5 % | ar-069 |
| Vanilla in baked batters at 85 % (24490, 23938) | The line "1 teaspoon vanilla" is found by "vanilla"; baked 18 minutes: 5010, 45 % | ar-066 |
| `simmer:1` on a roast (22351) | Kept: heat under 15 minutes with an untimed roast is no timed row, so the highest row of the heat seen, 5002 (85 %), stays; a basting frequency is no longer read as a time (ar-070) | ar-071 |
| Wrong (safe side): "Set fire to the rum" read as no heat (22994) | Flamed: 5003, 75 % | ar-065 |
| 22883 codfish soup, wine `no-heat` | Not a reader fault: the stored steps were cut in the store | none |

Review of the branch (`alcohol-retention/4`): a drink the steps name only as "the spirits" and pour
over the cold cake after a mix-all step and a three-hour bake was put in the mix-all step and read
at 5 % (timed bake, 5009), an understatement for a No Alcohol household. A step naming any drink
word now keeps the mix-all step out and the highest reading wins: ar-074 (no heat, 100 %), ar-075
(another drink word, no heat), ar-076 (a drink word in a timed boil may not lower the reading below
the "up to" row, 85 %).

### Staging checkpoint 3: four factors too high (`alcohol-retention/5`)

40 recipes read on staging, 4 wrong, all on the high side (N20). The staging step texts were not
read again for this fix (no staging access in the build); the golden rows are written for the rule in
the shape the log gives.

| Item | Cause and decision | Golden row |
|---|---|---|
| 17180 card cakes, vanilla `up-to:simmer` 85 % where the only heat is "bake in slow oven" | A made thing such as "boiled icing" read as heat on the dish; it is no heat, so the oven's up-to row, 5010, 45 % | ar-077 |
| 24825 HUNTING PUDDING, brandy `no-heat` although step 1 mixes all and step 4 boils two hours | "Tie it up in a clean cloth" was read as a separate preparation and ended the reading; a clean cloth, towel, napkin or bag wraps the dish. 5008, 10 % | ar-078 |
| 24040 zucchini bread, vanilla `up-to:bake` 45 % with "Bake for 50-55 minutes" | The range is read (the time reader takes "50-55 minutes" as 50); the reading had ended before the bake, at "In a separate bowl, whisk the flour ...", after only "Preheat the oven" had been seen as oven heat. Preheating cooks nothing, and a separate bowl of the dry ingredients (flour, dry ingredients, baking powder or soda, with no heat of its own) is the batter's other half. 50 minutes is row 5005, 35 %: the row at or below the time, as for every timed reading (the log's 25 % is the nearer row) | ar-079 |
| 72005 Real Christmas Pudding, rum `up-to:simmer` 85 % where the soaked fruit and its liquor are boiled five hours | A step that names the drink only as "the liquor" made the reader keep the highest of every reading and the up-to row. A drink that soaks an ingredient (soak, steep, macerate, plump) goes where that ingredient goes: the method is read from that step to its end, past the rest that soaks it. 5009, 5 % | ar-080, ar-081, ar-082 |

ar-083 keeps a separate preparation with heat of its own (a hard sauce beside boiled cream) separate.

Review of the branch (CK6-1, CK6-2), same version: the soak is read like any other reading, up to a
separate preparation ("Meanwhile, in a saucepan ..."), passing only the pause in its own sentence and
the sentences right after it that only rest it overnight (ar-084, ar-087). When the steps name the drink
only by a drink word, the soak's reading is one of the readings from each step that names it, and the
highest is kept (ar-085: soaked fruit baked, then the cake brushed with more of the liquor, is no heat).
A separate bowl of dry ingredients is passed over only when the next step stirs it into a mixture the
drink's own sentence names ("the egg mixture" after "beat the eggs ... and vanilla"); into any other
mixture it ends the reading as before (ar-086: a bourbon glaze, then a batter baked 30 minutes, is no
heat).

### Staging checkpoint 4: four readings wrong, one on the low side (`alcohol-retention/6`)

40 fresh recipes and the four N20 examples read on staging (P15); in three of the examples the drink
is named in no step. Golden rows are step texts written for the rule in the shape the log gives.

| Item | Cause and decision | Golden row |
|---|---|---|
| 17180 card cakes, vanilla `up-to:simmer` 85 % | "almonds blanched" read as stove heat; "blanched" is a ready food. Up to the oven, 5010, 45 % | ar-088 |
| 24825 HUNTING PUDDING, brandy `no-heat` 100 % | "Serve with white wine and butter sauce" named a drink word, so its no-heat reading won; a wine word does not place a spirit. Mix-all, boiled two hours, 5008, 10 % | ar-089 |
| 24040 zucchini bread, vanilla `up-to:bake` 45 % | "whisk in the remaining ingredients" was no mix-all step. Baked 50 minutes, 5005, 35 % | ar-090 |
| 70087 Timbale of Chicken, sherry `up-to:simmer` 85 % | No step names the sherry or mixes all the ingredients, so nothing places it: the safe-side "up to" row stays and the reading is counted as estimated (the log's 25 % needs a placement the steps do not give) | ar-091 |
| 70187 Ice Pudding, brandy 85 % (low side) | Added "when cold" to a boiled custard and frozen: no heat, 100 % | ar-092, ar-093 |
| 73328 BEEF VINGRETTE, white wine `no-heat` 100 % | The comma before "white wine" ended the clause; a clause starts only where a verb follows. "Stew" heats it: up to 5002, 85 % | ar-094 |

ar-095 to ar-097 cover braise, seethe and fricassee as stove heat.

### Staging checkpoint 5: six readings wrong (`alcohol-retention/7`)

40 fresh recipes read 26 right, 6 wrong and 8 doubtful (Q18). The heat that counts is the heat of the step in which
the drink goes in and of the later steps of the same part of the dish; where that is unknown (no step places the drink)
the safe-side "up to" factor stays and the reading is counted as estimated, as before (ar-091). Golden rows are step
texts written for the rule in the shape the log gives (no staging text copied).

| Item | Cause and decision | Golden row |
|---|---|---|
| 22358 wine boiled with the broth, chicken simmered 12 to 15 minutes, read `up-to:simmer:12` 85 % | A range read at its lower end, under 15 minutes. A range after heat the steps give no time for counts at its upper end: 15 minutes, 5004, 40 % | ar-ck10s-01 |
| 17337 vanilla in a butter cake baked 1¼ hours, read `up-to:bake` 45 % | "one and a quarter hours" (and "an hour and three quarters", "two and a quarter hours") were no time; with the vanilla named, 75 minutes, 5006, 25 %. With no step naming it the safe side stays | ar-ck10s-02 |
| 74304 a glass of white wine stewed with celery till tender, read `up-to:simmer` 85 % | A sentence that only states how long ("It will take about an hour") now times the heat of the sentence before it: 5006, 25 %. A time of standing, resting, chilling, setting, soaking or rising ("It needs to stand two hours") is no heat and adds nothing (review CK10S-1) | ar-ck10s-03, ar-ck10s-10 |
| 74359 white wine in the stewpan the ham boils in for 1¾ hours, read `up-to:simmer` 85 % | "h" is an hour ("1 3/4 h"): 105 minutes, 5007, 20 % when a step names the wine; unnamed it keeps the safe side | ar-ck10s-04 |
| 70088 sherry in a sauce made on the fire, read `no-heat` 100 % | "on the fire", "on a slow fire", "on the stove" are stove heat like "over the fire": no time, up to 5002, 85 % | ar-ck10s-05 |
| 24434 vanilla in an uncooked icing, read `hot-liquid` 85 % from the frying of the dough (the low side) | The sentence before a drink's own counts as its heat only when the drink's sentence does not start a part of its own ("For the icing, ...", "Make the sauce", "Glaze:"); such a sentence after the drink's also ends its segment. No heat, 100 % | ar-ck10s-06, ar-ck10s-09 |
| 74884, 74588 (doubtful) a drink stirred into a hot sauce, tossed till hot | "into the hot ...", "on the boiling ...", "till hot" is a hot liquid: 5002, 85 %. Only the part of the clause before a serving word counts: "serve with hot plum pudding" names the dish the cold mixture goes with, not a liquid the drink goes into (review CK10S-4) | ar-ck10s-07, ar-ck10s-08, ar-ck10s-11 |

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
kind, the lines read as estimates (`estimated`: the drink named in no step or an "up to" heat;
`unnamed`: the drink named in no step) and a sample, each sample marked `estimated` and `unnamed`;
the log line carries both counts. The journal keeps the factor and basis the undo matches on, as
before. A dry run writes nothing. Running it again writes only what changed (new lines,
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

`alcohol-retention.tsv` (118 rows since `alcohol-retention/7`: 108 rule-given, `rule:alcohol-retention`, `ok`, ar-ck10s-01 to ar-ck10s-11 from staging checkpoint 5 and its review; 10 realistic methods
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
