# Diet gate: what a restriction's criteria count

A household restriction saved from the UI names a restriction type ("Halal", "Celiac Disease",
"Peanut Allergy"); the type's rows in `plan."RestrictionCriterion"` say what it forbids. Meal
planning, "Surprise me" and search read the household's or person's set
(`HouseholdRestrictionResolver`); the recipe page and the severe-conflict filter read the matches of
one recipe (`RecipeOrchestrationService.GetDietMatchesAsync`). Both go through
`DietCriteriaMatcher`, so they agree.

A criterion is one of:

- an exact food (`IngredientId`);
- a nutrient (`NutrientId`): the alcohol rows of No Alcohol, Halal and Whole30, read as in
  `alcohol-context.md`;
- a word pattern (`\mrum\M`), matched on word boundaries (`~*`);
- a name pattern with `%` (`%pork%`, `%flour%`, `%peanut%`), matched as ILIKE. These are most
  rows of `DefaultRestrictionCriteria`: every allergy (severity 5), intolerance (4) and diet (3).

## A food counts by what is its own (staging checkpoint 1, K9)

Aliases in `recipe."IngredientAlias"` hold whole recipe lines: a line once linked to a food left its
wording there ("bacon or butter" on Butter, "1 cup sugar and 2 cups flour" on Sugar, "vegetable or
peanut oil" on Vegetable Oil). Checkpoint 1 found the alcohol words reading such aliases and keeping
about 4 in 10 recipes out of No Alcohol, Halal and Whole30 plans, every recipe with sugar or milk.
The name patterns had the same flaw: "bacon or butter" on Butter kept every butter recipe from a
Halal household, and "vegetable or peanut oil" every vegetable oil recipe from a household with a
peanut allergy.

Since `cleanup2/ck2-diet-gate` a name or word pattern counts a food by:

- its own catalog name;
- its USDA category, for the patterns of `DietCriteriaMatcher.CategoryPatterns`: every food in
  Pork Products (category 10) counts for `%pork%`, so a pork cut whose catalog name does not say
  pork still counts. Only a category every food of which is the restricted thing is listed;
  Dairy and Egg Products (eggs), Finfish and Shellfish Products (both) and the grain categories
  (rice, corn) are not;
- an alias a rule or a person gave it (`IngredientAliasTrust`): its USDA description
  (`fdc:description`), an admin's alias (`admin:`), a deterministic rule's (`deterministic:`,
  `link:deterministic:`) or the name of a duplicate retired into it (`retired:`).

An alias with no source, or one a model pick left (`link:ai:...`), never counts: it is a wording
seen on a recipe line, and that line decides for itself. A word pattern's alias also needs a
trusted source, and as before must hold no alcohol word.

## The line decides for the line

A food a pattern of a type matches only through an alias that does not count is *alias only* for
that type. A recipe with a live line on an alias-only food stays out of the plans and flagged when
any of its live lines, on any food, names a pattern of the type by its own text
(`DietCriteriaMatcher.LineNames`: the ILIKE pattern matches the line, or a word pattern matches it
and the alcohol rules count it). So "1/4 lb bacon or butter" keeps its recipe from a Halal
household, and so does "4 slices bacon" still linked to Olive Oil in a recipe whose butter line was
the alias's only reason; "1 cup butter" alone is offered. No recipe the old rule flagged through an
alias loses its flag while one of its lines names the restricted thing.

The household set carries these recipes in `RestrictedIngredientSet.RecipeIds` (with the drink
lines); the recipe page names such a match by the line's text. The severe set
(`ResolveSevereForPersonAsync`, severity 4 and 5) reads the same rule, so search hides them for a
person with an allergy or intolerance.

What this does not do: a line that names the restricted thing on a food matched by no pattern at
all, in a recipe that touches no alias-only food ("2 slices bacon" linked to Olive Oil with nothing
else), was never caught by the name patterns and is not caught now; reading every line of the
catalog for every pattern of every type costs a scan per type and is left to a later item. A
synonym the patterns do not list ("groundnuts" for peanuts) on an alias-only food is cleared with
it: the alias that named peanuts no longer decides for "groundnuts". Both are measured on staging
before the change is relied on (operator steps below).

## Cost and cache

The whole-catalog answer of each type (its foods, its alias-only foods and the recipes their lines
keep out) is one value of `DietGateCache`, computed once and refreshed when a food, alias, nutrient
or recipe line is written, as `alcohol-context.md` describes. A name pattern costs a name probe
(ILIKE on `recipe."Ingredient"."Name"`, which the trigram index serves), an alias probe that reads
each alias's source, the category probe for `%pork%`, and, for a type with alias-only foods, one
probe per pattern over the lines of the recipes that touch them (`EXISTS` on the alias-only foods,
no join). The start-up warm (`DietCriteriaMatcher.WarmAsync`) computes the types with a word or
nutrient criterion and every type a restriction names, so the log line "Diet gate: N catalog probes
computed and kept" counts those types plus the drink lines. The recipe page reads only the recipe's
own foods and lines and is not cached.

## Golden set

`diet-gate.tsv` (see `golden-sets.md`): one food with its aliases and sources and one recipe line on
it under one default restriction; the recipe is `out` (kept out of plans, flagged on the page and,
at severity 4 or 5, hidden from search and a severe conflict) or `in`. The rows carry the K9 class
to Halal, Whole30, Kosher, Celiac Disease, Lactose Intolerance, Milk Allergy and Peanut Allergy in
both directions: the line the alias came from stays out, a line that does not name the restricted
thing is offered, and foods counted by name, category, USDA description, an admin's alias and a
retired duplicate's name stay out. `GoldenDietGateTests` also checks that no row the old rule kept
out is offered while its line names a pattern.

## Operator steps

Before the change is relied on, on staging (read-only), count it:

```
psql "$STAGING_URL" -v sample=20 -f ops/diet-gate-count.sql
```

The script runs in a read-only transaction. For every default type with name patterns that it names
(`-v names=...` to change the list) and every type a restriction uses, it prints one `summary` row:
foods counted before and after, alias-only foods, recipes kept out before and after, how many of
those the line rule keeps, recipes that lose the flag (and how many are approved) and recipes newly
kept out (the category and the line rule); then, for each severity 4 or 5 type, up to `sample` lost
recipes with their lines on alias-only foods. It counts the name patterns alone (not the alcohol or
nutrient criteria), so a lost recipe with a drink line is still out of a No Alcohol plan. Read every
`lost` sample line of the allergy and intolerance types: a line that names the restricted thing by a
word the patterns lack stops the release, and the word is added to the type's patterns.
