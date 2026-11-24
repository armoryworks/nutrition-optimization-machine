# Diet gate: what a restriction's criteria count

A household restriction saved from the UI names a restriction type ("Halal", "Celiac Disease",
"Peanut Allergy"); the type's rows in `plan."RestrictionCriterion"` say what it forbids. Meal
planning, "Surprise me" and search read the household's or person's set
(`HouseholdRestrictionResolver`); the recipe page and the severe-conflict filter read the matches of
one recipe (`RecipeOrchestrationService.GetDietMatchesAsync`). Both go through
`DietCriteriaMatcher`, so they agree.

A prepared meal has no lines; for a household judged by contents its named ingredients and allergens,
read from an authoritative statement, are judged by the same criteria as a recipe's lines
(`PreparedContentsGate`, `prepared-contents.md`).

A criterion is one of:

- an exact food (`IngredientId`);
- a nutrient (`NutrientId`): the alcohol rows of No Alcohol, Halal and Whole30, read as in
  `alcohol-context.md`;
- a word pattern (`\mrum\M`), matched on word boundaries (`~*`);
- a name pattern with `%` (`%pork%`, `%flour%`, `%peanut%`), read by words (below). These are most
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
(`DietCriteriaMatcher.LineNames`: the name pattern names it by words, or a word pattern matches it
and the alcohol rules count it). So "1/4 lb bacon or butter" keeps its recipe from a Halal
household, and so does "4 slices bacon" still linked to Olive Oil in a recipe whose butter line was
the alias's only reason; "1 cup butter" alone is offered. No recipe the old rule flagged through an
alias loses its flag while one of its lines names the restricted thing.

The household set carries these recipes in `RestrictedIngredientSet.RecipeIds` (with the drink
lines); the recipe page names such a match by the line's text. The severe set
(`ResolveSevereForPersonAsync`, severity 4 and 5) reads the same rule, so search hides them for a
person with an allergy or intolerance.

## Every line is read by its own text (staging checkpoint 2, M27)

Checkpoint 2 found a Peanut Allergy household offered recipe 75943, whose line
`canola, (vegetable or peanut oil for frying)` sits on the uncurated wording food `canola`: no
pattern names that food, and the rule above read lines only in recipes touching an alias-only food.
Since `cleanup2/ck4-diet-containers` every live line of the catalog is read by its own text, for
every restriction type, whatever food the line is on: a line whose text names a name pattern of
the type keeps its recipe out of plans and search and flags it on the page
(`DietCriteriaMatcher.LinePatterns`, `TypeProbe.LineRecipes`). The paths by which such a line
escaped before, each closed and each with a golden row or a test:

| Path | Example | Now |
|---|---|---|
| A food no pattern names, curated or an uncurated wording food | `canola, (vegetable or peanut oil for frying)` on `canola` (dg-41) | out by the line |
| A wrongly linked food in a recipe touching no alias-only food | `4 slices bacon` on Olive Oil (dg-45; the case this document used to leave out) | out by the line |
| An alternative in parentheses or after "or" | `2 tbsp vegetable oil (or melted butter)` (dg-43), `2 cups water or chicken broth` (dg-46) | out by the line |
| A note or garnish line | `For serving: chopped peanuts` on a wording food (dg-44) | out by the line; the probe reads every live line, whatever its `LineKind` |
| A line on a retired (soft-deleted) food | `2 tbsp roasted peanut oil` on `groundnut oil (deleted #...)` | out by the line; the recipe page reads the line's food name with the query filter off, so the line is not dropped from its matches either |

A line has no other way to escape: every line carries a food (`IngredientId` is part of the line's
key), its text is the whole raw line with its parentheses and alternatives (a compound split keeps
each part's text on its own row), and the probe reads lines, not foods. Word patterns are read the
same way for a type with no alcohol criterion (an admin's `\mtripe\M`); a type with the alcohol
criterion (No Alcohol, Halal, Whole30) leaves its drink words to the alcohol rule
(`alcohol-context.md`, `alcohol-retention.md`), so a drink a capped type lets through once it cooks
off is not kept out by its word. A synonym the patterns do not list ("groundnuts" for peanuts) is
not read, by the food or by the line: it belongs in the type's patterns (stop rule below).

Cost: one more probe per type, a single scan of the live lines with every name pattern of the type
joined in one regular expression (`~*`, no join; since `cleanup2/ck5-diet-patterns` the word rule's
`DietCriteriaMatcher.LineRecipeSql`, below), returning the distinct recipe ids; the answer is part of the type's cached probe and refreshed with it. On the
checkpoint copy (about 843,000 live lines) a type with many patterns (Whole30, Vegan) reads every line
once; the warm computes the types with a word or nutrient criterion and every type a restriction
names, as before. Operator step 2 below times it.

## A name pattern is read by words (cleanup2/ck5-diet-patterns)

Since every line is read, the crude ILIKE reading of the `%` patterns hit line text as it had long hit
food names: `%ham%` in "graham", `%egg%` in "eggplant", `%corn%` in "peppercorn", `%rice%` in
"licorice", `%oat%` in "goat", `%cola%` in "chocolate", `%icing%` in "slicing", `%wine%` in "kitchen
twine", `%lard%` in "collard greens", `%butter%` in "peanut butter", `%milk%` in "coconut milk". Since
`cleanup2/ck5-diet-patterns` a name pattern is read by words, the same way in a food's name, an alias,
a recipe line and a prepared meal's statement (`DietPatternRule`, `diet-patterns/5` since `cleanup2/ck10-diet-coverage` and `/4` from `cleanup2/ck9-diet-terms`, see below; `/3` from `cleanup2/ck6-prepared-diet`, which added exceptions scoped to types; `/2` from the ck5 review):

- the term counts where it begins a word, whatever follows: plurals, stems and compounds ("eggs",
  "anchovies", "buttermilk", "cornstarch", "eggnog");
- inside a word it counts only as a whole part of a compound: at least three letters before it, and
  after it nothing, an inflection (s, es, ed, ing) or at least three more letters ("shortbread",
  "popcorn", "horseradish", "monkfish"). So "goat" is not oat, "spears" not pear, "slicing" not icing,
  "avocados" not cod, "chocolate" not cola, "twine" and "swine" not wine, "champagne" not ham;
- a term followed by "less" or "free" ("eggless", "flourless", "peanut-free") says the food is absent,
  but "free-range", "free range" and "free-run" describe the food and leave it counted ("1 egg
  free-range" still names egg);
- a compound food that holds the term but is not its food is a named exception with a cited reason
  (`DietPatternRule.All`, about 130 phrases): "graham" is not ham, "buckwheat" not wheat (Celiac
  Disease Foundation), "peanut butter" not dairy (21 CFR 164.150), "coconut milk" not milk, "water
  chestnut" not a tree nut, "nutmeg" and "coconut" not tree nuts (FDA, Questions and Answers Regarding
  Food Allergens, edition 5, 2025), "scalloped potatoes" not scallops, "oyster mushroom" not an oyster,
  "shellfish" not a fish for Fish Allergy (FALCPA names them apart), "sweetbreads" not bread. An
  exception blanks only its phrase: "1 cup coconut milk or whole milk" still names milk;
- a term of two words matches across any spaces or punctuation ("brazil-nut"), but an exception's
  words only across spaces or hyphens: "2 cups cooked rice, flour for dusting" is two foods, not rice
  flour, and still names flour ("chopped peanuts, butter" still names butter, "shredded coconut, milk
  chocolate" still names milk);
- a criterion that asks for a substring on purpose keeps the ILIKE reading: a pattern that starts or
  ends with `%%` (`%%nut%%`, which ILIKE reads exactly as `%nut%`), or one with `_` or a `%` inside its
  term. Anchors keep their meaning: `oat%` reads the term only at the start of the text, `% oat%` only
  where a word begins.

The rule never reads more than ILIKE did, apart from the separators of a two-word term
(`GoldenDietPatternTests`). An exception is listed only where every default type that holds its term
either does not restrict the compound or still names it by another of its own patterns, and the
reason says which ("coconut milk" stays out of Coconut Allergy plans by `%coconut%`, "turkey bacon" out
of Vegetarian ones by `%turkey%`); a test checks each pattern a reason cites. Where an exception would
otherwise clear a food a type means, the type holds a companion pattern
(`DefaultRestrictionCriteria.Companions`, each with its reason): `%hamburger%` beside `%ham%` for the
meat types (hamburger is chopped beef under 9 CFR 319.15(b)), `%buckwheat%` beside `%wheat%` for Paleo
and Whole30 (pseudo-cereals), `%root beer%`, `%ginger beer%`, `%birch beer%` and `%maltodextrin%`
beside `%beer%` and `%malt%` for Paleo and Whole30 (added sugar; maltodextrin for Grain-Free too),
`%licorice%` beside `%rice%` for Paleo, Whole30, Grain-Free and Carnivore (licorice candy is made with
wheat flour), `%soy milk%`, `%soymilk%`, `%oat milk%`, `%soy yogurt%`, `%honeycrisp%` and
`%pearl barley%` for the two FODMAP types, `%eggplant%` for Jain, `%goldfish cracker%` for the vegan
types (cheddar), `%scallopini%` (veal) for the meat types holding `%scallop%`, and for db/seed.sql's
Pancreatitis and Gallbladder Disease the nut, seed and cocoa butters and turkey and beef bacon, which
they restrict for fat, not for dairy or pork. The companions are part of the defaults, and
`DefaultRestrictionCriteria.EnsureAsync` adds them on every start to a type that holds the pattern
however its criteria were seeded or edited (an admin-edited Vegetarian gets `%hamburger%`), at that
pattern's severity, unless the type already has a row for the companion, an admin's deleted one
included. The start-up log names the types it gave companions and the seeded or edited types it left
without default top-up. Every compound a shipped type (the defaults and db/seed.sql's types) lets
through by an exception is listed in `nom-api/Nom.Api.Tests/Fixtures/diet-pattern-clears.tsv`; a change
to the exceptions, the defaults, the companions or the seed shows there as a diff a person reviews
(`DietPatternCompanionTests`). Carnivore's patterns name food groups
("vegetable", "fruit", "rice"), not every plant, so a plant compound an exception clears ("vanilla
bean") was held for it only by a look-alike; that is a gap in its patterns, as "groundnuts" is
for the peanut types, not something the look-alike should cover.

Exceptions scoped to types (`cleanup2/ck6-prepared-diet`). An exception that is right for some types only
names them (`DietPatternException.Types`) and is read only for their patterns (`DietPatternRule.For(pattern,
typeName)`; the criterion carries its type's name, `DietDataCriterion.TypeName`): "sweet potato" is no
nightshade (a morning glory) for Nightshade Sensitivity and Nightshade-Free, while Jain still avoids it as a
root and Carnivore as a plant; "sugar snap" peas hold no added sugar for Sugar-Free, while Carnivore means
them; "licorice root" is the herb, cleared for the gluten types, which hold `%licorice%` for the candy. An
admin's type of another name reads none of them (the stricter reading). `ops/diet-gate-count.sql` reads every
pattern without type-scoped exceptions, so its figures for those types count those lines as kept out.

Composite foods (`cleanup2/ck6-prepared-diet`). ck5 left two gaps on the unsafe side: Milk Allergy and
Dairy-Free never read "Goldfish crackers" (cheddar), the gluten types never read "licorice" candy (wheat
flour). The class is closed by the composite rows of the label-terms table (`StatementLabelTerms.Composites`,
see `prepared-contents.md`): each composite food (Goldfish crackers, licorice, Worcestershire sauce, Caesar
dressing, pesto, mayo, custard, hollandaise, ranch dressing, Alfredo, béchamel, tempura, brioche, challah,
naan, pretzels, croutons, cookies, biscuits, pastry and pie crust, muffins, pancakes, waffles, crêpes,
dumplings, wontons, udon, ramen, soba, matzo, phyllo, roux, baklava, doughnuts, teriyaki, hoisin, ponzu, shoyu,
soy sauce, tamari, miso, za'atar, gomasio, furikake, dukkah, tahini, hummus, halva, lox, caviar, bottarga,
dashi, bonito, surimi and imitation crab, scampi, langoustine, satay, groundnut, frangipane, amaretti,
gianduja, orgeat, romesco, nougat, marzipan, praline, Nutella, quiche, frittata, soufflé, carbonara,
zabaglione and the cheeses, yogurt dishes and milk sweets whose names do not say so) lists what it is made
of, from a standard of identity or the leading brand's label. A type one of whose patterns names one of
those contents holds the composite's patterns (`DefaultRestrictionCriteria.CompositeCompanionsFor`), at that
pattern's severity, unless its own patterns already name it: Milk Allergy, Dairy-Free, Vegan and the other
dairy types hold `%goldfish%`; Celiac Disease, Gluten Sensitivity, Gluten-Free and Wheat Allergy hold
`%licorice%` and `%liquorice%`; Fish Allergy holds `%worcestershire%` and `%imitation crab%`; the peanut
types `%satay%` and `%groundnut%` (the "groundnuts" gap ck5 named). The companions of the defaults are part
of `ByName` (`CompositeCompanions`); `EnsureAsync` gives every type them on start, seeded, edited and
admin-named types alike, from the type's own patterns, with the note `composite food, by %x% (label-terms/3)` (`/2` before `cleanup2/ck10-diet-coverage`, `/1` before `cleanup2/ck9-diet-terms`),
and respects an admin's deleted row. A composite whose name another food begins with is left out ("edam"
would name edamame, "romano" romano beans; since `label-terms/3` "flan" and the wheat term "cake" are read with "flank" and "rice cake" as named exceptions); "ranch" alone is not a
composite name, only "ranch dressing". Some readings are wider than a person would choose and lie on the
safe side: a type holding `%bean%` (Carnivore, the FODMAP types) holds soy sauce, tamari and miso through
"soybean".

Admin-named types (`cleanup2/ck6-prepared-diet`). A type an admin creates under a new name gets the
companions of the default type it was copied from: the defaults none of whose own patterns it lacks, the
largest of them (`DefaultRestrictionCriteria.OriginsOf`; Vegan and Ethical Vegan hold the same patterns, so a
copy of either gets both's companions). A type that holds no default whole gets none of the exception
companions, only the composite foods its own patterns restrict. The start-up log names the admin types it
read as copies (`admin types read as copies of a default: ...`).

Branded foods with a read statement (`cleanup2/ck6-prepared-diet`). A food that is not a prepared meal and
carries a read FDC ingredient statement (`recipe."IngredientContents"`, `--prepared-contents <dir>
--all-branded`) counts for a type when its statement names what a pattern of the type restricts, as
`PreparedContentsGate.RestrictedNames` reads a prepared meal (its named ingredients, what their label terms
contain, its Contains line); so a recipe line linked to a branded "snack crackers" food whose label names
cheddar cheese stays out of a dairy type though neither the line nor the food's name says so. It is part of
the type's cached probe, which a write to a statement now marks stale. Today no branded food is a recipe
line's food (`catalog-branded`), so this changes nothing until one is.

Alternatives. A line that offers a restricted food beside another ("chicken broth or vegetable broth",
"2 tbsp vegetable oil (or melted butter)") keeps its recipe out for every type: allergies and
intolerances, where the unsafe side must not open, and diets too. For a diet the recipe page, the
plan's shopping list and its nutrition follow the food the line is linked to, and nothing tells which
alternative the cook picks; religious diets (Halal, Kosher) are severity 3, and "bacon or butter"
offered to a Halal household is the failure the gate exists to prevent. The cost is a vegetarian
recipe that offers chicken broth as an alternative leaving vegetarian plans; `ops/diet-gate-count.sql`
counts such recipes per type (`gained by line text only`).

The whole-catalog line probe stays one scan of the live lines with no join
(`DietCriteriaMatcher.LineRecipeSql`): a line must first hold some pattern's term (all expressions
joined); a term with no exceptions then counts as it is, and a term with exceptions counts when it is
still found once its exceptions are blanked out of the line (`regexp_replace`), as
`DietPatternRule.IsMatch` reads it in memory. The expressions are written once in two dialects (.NET and
PostgreSQL's advanced expressions, which differ only in their letter classes); a test reads every
golden line with the PostgreSQL ones in Tcl's regular-expression engine, from which PostgreSQL's is
taken, when `tclsh` is installed. Food names and aliases are probed by the PostgreSQL expression
(`~*`) and their exceptions read in memory.

## Staging checkpoint 4: the unsafe and over-flagging side (cleanup2/ck9-diet-terms)

Checkpoint 4 read 127 recipes through the API for six types and found four misses on the unsafe side and six
over-flags (P3, P4, P12, P17, P18, P19). `diet-patterns/4` and `label-terms/2`:

- **"free from" and "free of" something else (P12).** "bread free from crust", "stale bread free of crust" and
  "fish free from bones" were read as "bread-free" and "fish-free", so five recipes were not kept from Celiac
  Disease, Wheat Allergy and Fish Allergy. The "free" negation now holds only for "<term>-free" or "<term> free"
  as a label of the food itself: "free" followed by spaces or a hyphen and "from" or "of" is no negation, in both
  dialects. "peanut-free", "egg free noodles", "wheat-free, of course" and "egg free often" still read as absent.
  A unit test reads every named exception the same way: "<term> free from lumps" names the term, the phrase
  before "free from" is still its exception, and the term after the phrase still counts.
- **"flower", the old spelling of flour (P12).** The types that hold `%flour%` (Wheat Allergy, Celiac Disease,
  Gluten Sensitivity, Gluten-Free, Grain-Free, Paleo, Whole30) hold the companion `%flower%`. The blossoms are
  named exceptions of `%flower%` with their reasons: cauliflower, sunflower, safflower, cornflower and corn
  flower (corn types hold them by `%corn%`), elderflower, orange flower and flower water, the edible blossoms
  (rose, squash, zucchini, pumpkin, banana, chive, lavender, chamomile, hibiscus, nasturtium, jasmine),
  flower pepper, petals, buds, flowerets and "flowering". A blossom the list lacks stays held, on the safe side.
- **Cream tartar and USDA descriptions in reverse (P18).** "cream tartar" (also "cream-tartar"), the older
  spelling of cream of tartar, is a named exception of `%cream%`. A USDA description alias (`fdc:description`)
  is read as it stands, and is cleared only when a named exception blanks out of its order of speech
  (`DietPatternRule.IsDescriptionMatch`, `SpokenOrder`: "Flour, almond" also reads "almond Flour") every place
  the term holds there, so "Flour, almond", "Flour, coconut", "Flour, buckwheat" and "Cheese, goat" no longer
  flag their foods for the gluten types or Halal while "Flour, wheat, all-purpose" still does. The order of
  speech never decides where a term begins or ends: `% oat%` and `oat%` still read "Cereals, oats, regular and
  quick" as written (review CK9T-5), and a substring pattern has no exceptions to clear. A wording alias or a
  recipe line is never reordered: "flour, almond extract" is two foods. `ops/diet-gate-count.sql` reads a
  `fdc:description` alias the same way on its rule side (`rule_alias`), so the drops the order of speech makes
  are counted and sampled with the other rule drops (review CK9T-3).
- **Peanut Allergy (P3).** Peanut Allergy holds the companion `%nut butter%`: a nut butter that names no nut may
  be peanut butter. Reese's (Pieces, Peanut Butter Cups) is a composite of peanut, milk and soybean, so the
  peanut, dairy and soy types hold `%reese's%`. A tree-nut butter whose name holds "nut butter" (hazelnut nut
  butter) is held for Peanut Allergy too, on the safe side.
- **Marshmallows (P17).** Marshmallows are a composite of gelatin and corn syrup, so every type that restricts
  gelatin (the meat types, Halal, Kosher) holds `%marshmallow%`, and so do the corn and sugar types. Marshmallow
  creme is a composite of egg white and corn syrup (the leading labels name no gelatin); the gelatin types still
  hold it by `%marshmallow%`, on the safe side, and "marshmallow cream" is a named exception of `%cream%`.
- **Prepared foods the link re-check creates (P19).** Composite rows for tortellini, ravioli, spaghetti
  (21 CFR 139.110, so Wheat Allergy now holds `%spaghetti%`), zwieback, potstickers, taquitos, Spanish rice,
  pizza dough and corned beef hash, and the wonton split into the filled wonton (pork or shrimp, on the safe
  side) and the empty wrapper (a named exception of `%wonton%`, held by `%wonton wrapper%` where wheat or egg
  is restricted). Pasta composites contain "pasta" and zwieback "bread", so the types that hold `%pasta%` or
  `%bread%` (Carnivore among them) keep them. See `link-recheck.md` for the guard that keeps a take-back from
  lowering a recipe's flags.
- **An exception phrase inside a longer name (review CK9T-4).** "peanut butter cups", "peanut butter cookies"
  and "coconut cream pie" hold an exception phrase ("peanut butter", "coconut cream") inside a food made with
  the restricted thing, so they are composite rows: peanut butter cups of peanut, milk and soy (the milk
  chocolate), peanut butter cookies of peanut, wheat flour, butter and egg (butter and egg on the safe side),
  and coconut cream pie of coconut, milk, egg and wheat flour.
- **Over-flags left on the safe side.** `%spaghetti%` and `%pasta%` still hold "spaghetti sauce" and "pasta
  sauce"; granola is held for Celiac Disease (oats).

## Staging checkpoint 5: a derived and measured vocabulary (cleanup2/ck10-diet-coverage)

Five checkpoints extended the severe types' patterns by example and each fresh sample found new misses
(checkpoint 5: Celiac Disease showed 16 of 32 wheat recipes drawn by word, Q5; Egg Allergy missed Allemande
sauce and Sulfite Sensitivity maraschino cherries, Q19; the sulfite types offered prepared meals whose statements
name sodium bisulfite, Q7). `diet-vocabulary/1`, `diet-patterns/5` and `label-terms/3` stop adding terms one by
one:

- **Derived from the catalog by category (`DietVocabulary.Derive`).** For each class (wheat, milk, egg, soy,
  peanut, tree nut, fish, shellfish, sesame, sulfite) the rule takes the head term of every food of the frozen
  USDA catalog (`gate-catalog.tsv`) in the categories that carry it: Baked Products and Cereal Grains and Pasta
  for wheat; the dairy entries of Dairy and Egg Products for milk; the `Fish,` entries of Finfish and Shellfish
  Products for fish and its `Crustaceans,` and `Mollusks,` entries for shellfish; the `Nuts,` entries of Nut and
  Seed Products for tree nuts; the soy, peanut and sesame foods of their categories. The head is the first part
  of the description after the class's lead part and any brand written in capitals, its last word singular.
  Coating and treatment words give a term of their own: `batter` (breaded and battered foods), `meatless` (the
  meat analogues of the legume category, soy protein), `dried <fruit>` (sulfured fruit). The committed file
  `Nom.Orch/Contents/diet-vocabulary.tsv` keeps every derived term with the categories and the number of foods
  that gave it; `DietVocabularyTests` checks the file against the catalog row for row.
- **Decided, never guessed.** Each term is `in` (its pattern joins the class's defaults), `covered` (another
  pattern of the class already reads it) or `out` (free of what the class restricts, with a cited reason:
  sorghum, millet, teff, quinoa and the other gluten-free grains, corn tortillas and taco shells, leavening
  agents, coconut, a maker's name). The test reads each decision against the class's reference type: an `in`
  term is read by its own pattern and no other, a `covered` one by another, an `out` one by none, so the named
  exception that takes it out (`sorghum flour`, `rice noodle`, `corn tortilla`) is in force.
- **Label names from an authority (`basis` `label`).** The names a label uses for each class: the sulfiting
  agents of 21 CFR 182 (sulfite, bisulfite, metabisulfite, sulfur dioxide, sulfured), FDA's other names of
  sesame (benne, gingelly, til, sesamol, tehina), and FARE's names for egg (albumin, ovalbumin, lysozyme, yolk
  ...), milk (curd, lactose, lactalbumin, half-and-half, creme fraiche, pudding), soy (TVP, okara, yuba, bean
  curd), peanut and tree nuts (arachis, goober, a nut a line does not name, nutmeat, pignoli, filbert ...),
  shellfish (escargot, cockle, langouste ...); the pasta shapes of 21 CFR 139.110 and the breads of 21 CFR
  136.110 for wheat; "cereal" for the gluten types only (a ready-to-eat cereal is made with wheat, barley malt
  or oats). The prepared-meal gate reads a statement's named ingredient by the same patterns, so a statement
  naming sodium bisulfite is held for the sulfite types.
- **Classic composites (`label-terms.tsv`).** Sauces and pastries whose name hides what they are made of, cited
  from public-domain references: Allemande, velouté, espagnole, Mornay, suprême and mousseline sauces, crème
  anglaise, pastry cream, sponge cake, ladyfingers, angel food and pound cake, madeleines, choux pastry,
  popovers, panettone and pandoro (Italian decree of 22 July 2005), biscotti, scones, graham, Bisquick, pizza
  rolls, stuffing, egg rolls, French toast, spaetzle, pierogi, empanadas, samosas, calzones, flan, crème
  brûlée, tartar and rémoulade sauce, bouillabaisse, maraschino cherries, and the peanut candy bars.
- **Fortified wines (Q13).** Sherry, port, Madeira, Marsala, vermouth, claret, champagne, Shaoxing and the other
  semi-generic wine names of 27 CFR 4.24(b)(2), and hard cider, are companions of `%wine%` for the sulfite types
  and Histamine Intolerance, so the re-check guard reads a take-back of a sherry line off a wine vinegar by the
  line's own words. A companion an alias would add to a food whose name already holds its pattern adds no
  restricted word (`LinkRecheckRules.RestrictedWords`): "dry sherry" may still become an alias of a dessert wine.
- **The over-flags of Q19.** A USDA description is read in each order of speech, each later part before the
  first (`DietPatternRule.SpokenOrders`: "Tortillas, ready-to-bake or -fry, corn" is corn tortillas, "Squash,
  winter, spaghetti, raw" spaghetti squash), and cleared when a named exception blanks the term out of any of
  them; the count script reads the same orders. Sorghum flour, broccoli flower clusters, fruit butters, flax and
  chia eggs and meatless bacon (for the meat and pork types) are named exceptions. A statement that lists the
  parts of an ingredient is decided by them (Q8): "vegetable oil (soybean and/or canola oil)" names its oils, so
  what a bare vegetable oil may be is not read (`PreparedContentsGate.UncertainNames`), but only a part that names a
  source decides and only for its own occurrence (`prepared-contents.md`).
- **Review of the derived terms.** A short term read where a word begins also reads the words that begin with it,
  and a compound reading reads it inside a longer word, so each such word is a named exception: "piece" (`%pie%`),
  "unpeeled" (`%eel%`), "Tillamook" and "till" (`% til%`), "for stuffing" (the stuffing composite). An exception
  of a fish term whose phrase is no animal at all (linguine, lingonberry, pikelet, the moringa drumstick, San
  Francisco, shadow) holds for every type the fish patterns feed, the vegetarian and vegan types too; only a
  phrase that is meat (linguica, carpaccio, poutine, spotted dick) stays scoped to Fish Allergy. A bird's
  drumstick is read by its bird where the line names one, as before the fish vocabulary (review CK10C-3 to
  CK10C-7).
- **Measured (`diet-coverage.tsv`, `GoldenDietCoverageTests`).** Per severe type at least 150 foods of the frozen
  catalog, seven a category and 25 from the categories that carry the class, read by name and USDA
  description, and constructed lines: no row that names what the type restricts is missed, and under 5 % of
  each type's clear rows are flagged. Prepared foods are kept as unasserted rows (their ingredient statement
  decides), and so are foods whose contents their name does not say (a branded cereal's grains, a candy bar's
  soy lecithin): name reading cannot know them, and the report counts them.

## Cost and cache

The whole-catalog answer of each type (its foods, its alias-only foods and the recipes their lines
keep out) is one value of `DietGateCache`, computed once and refreshed when a food, alias, nutrient
or recipe line is written, as `alcohol-context.md` describes. A name pattern costs a name probe
(the word rule's expression, `~*`, on `recipe."Ingredient"."Name"`, which the trigram index serves,
with its exceptions read in memory; ILIKE for a substring pattern), an alias probe that reads
each alias's source, the category probe for `%pork%`, and, for a type with alias-only foods, one
probe per pattern over the lines of the recipes that touch them (`EXISTS` on the alias-only foods,
no join). Since `cleanup2/ck6-prepared-diet` (staging checkpoint 3, N2: the warm ended about three minutes
after a start) the name patterns of one type are probed together, one name query and one alias query with
every expression joined, and each pattern is read in memory on the rows they return, instead of two queries
per pattern (Vegan holds about 70). The start-up warm (`DietCriteriaMatcher.WarmAsync`) computes the types
with a word or nutrient criterion and every type a restriction names, each at full severity and at severity 4
and above (the set a person's severe check reads, which was computed in line before), up to four probes at
once on their own contexts (`WarmParallelism`), so the log line "Diet gate: N catalog probes computed and
kept" counts those probes plus the drink lines. A household set asked for during the warm never gets a
partial answer: a probe that is not kept yet is computed by the first reader, and every other reader of the
same probe, the warm included, waits for it (`DietGateCache.GetAsync`; tested). The recipe page reads only the recipe's
own foods and lines and is not cached; it names a line kept out by its own text by that text.

The paragraph above is how the in-line matcher still works (tests on an in-memory database, the recipe page's scoped
probes). Since `cleanup2/ck9-diet-speed` the API never computes a probe for a household set; see the next section.

## The line index: no request probes the catalog (staging checkpoint 4, P1, P5, P23)

Since `cleanup2/ck10-diet-store` no request reads the line index or the kept probes: the gate's results are stored in
the database (next section). The build job still reads lines and their rules with the index's matcher, and the
index itself stays as the oracle the tests compare against.

On the production-sized copy (843,636 live lines) the joined line probe (`LineRecipeSql`) of a type with a large
severe set ran past Npgsql's 30 s command timeout: the warm never finished, and search, plans and the prepared check
answered HTTP 400 after 43 to 236 s for Celiac, Wheat, Milk, Egg, Lactose, Vegetarian, Vegan and Paleo (P1). The read
is now split so that no user request runs a probe:

- **Lines: `DietLineIndex`**, kept in the API's memory. For every pattern criterion of every restriction type (a
  name pattern keyed by its pattern and the type its exceptions are scoped to, or a word pattern) it keeps the
  recipes with a live line whose own text names it, read by `DietCriteriaMatcher.LineNames` (the matcher's own
  reading, not a SQL translation of it); beside them the lines whose text reads as a drink (`LineCounts`) and the
  recipes on each food. A pass reads `recipe."RecipeIngredient"` in windows of 2,000 recipe ids (one bounded range
  query on the primary key each, `DietGate:Window`), and reads a line for a rule only when it holds the rule's first
  word, an ordinal check on the lower-cased line; a line with a character a match without case may read as an ASCII
  letter (the long s, the Kelvin sign, the Turkish i's) is read for every rule, so the prefilter never drops a match.
  A recipe whose lines carry the same fingerprint (foods, order and text) as the last pass read keeps its reading and
  is read only for rules newer than its reading, so a later pass costs one read of the lines plus the changed recipes.
  The reading is published as an immutable snapshot every 10 s during a pass and at its end, so a reader never waits.
- **Foods: the food probes stay SQL** (names and aliases, which the trigram indexes serve) but are read with
  `DietGateCache.PeekAsync`: the kept value, fresh, or stale while one refresh runs apart; never computed in line.
  `DietCriteriaMatcher.FoodsProbeAsync` keys them `foods-only:` (no line read). The retained alcohol of a capped
  alcohol criterion is counted on the index's drink lines (`RetainedPeekAsync`). Each count is kept with the published
  reading it was made on and is fresh only while that reading is still the published one, so the warm counts again
  after every pass, and a count a request refreshed on an earlier or part-read reading is served only while it is made
  again and holds every recipe above the recipe id that reading had read (review CK9S-1).
- **The warm: `DietGateHostedService`** (replacing the start-up `Task.Run`). At start it computes the food probe of
  every type with a pattern or nutrient criterion, at full severity and at 4 and above, the types a restriction names
  first (`WarmFoodsAsync`), then one index pass, then the retained counts. Every `DietGate:PollSeconds` (30) it runs
  again when `DietGateCache.Generation` moved (a write to a food, alias, nutrient, line, a recipe's servings or a
  measurement), when a reader met a rule the index has not read (a criterion an admin added or edited), or after
  `MaxAge` (15 min, for writes from another process), never sooner than `DietGate:MinIntervalSeconds` (60) after the
  last run. `DietGate:Warm=false` turned it off. Since `cleanup2/ck10-diet-store` the setting is `DietGate:Build`.

**The safe side while something is not read.** `HouseholdRestrictionResolver` (on a relational database, with
`DietGateCache.Shared` and `DietLineIndex.Shared`) reads only what is kept. A type whose food probe is not kept yet
makes the set `Pending`: every recipe is held (`RecipeIds` gets every recipe id) and `PreparedContentsGate` holds
every prepared meal (`diet_gate_pending`). Each rule knows the recipe id up to which it has been read; every recipe
above the lowest such id the set needs (`ReadUpTo`: every recipe before the first pass reaches it, a recipe made since
the last pass, or every recipe for a rule not read at all) is held. So before the warm ends a request answers at once
and never errors, a recipe not yet read is held for a severe restriction and never shown as fine, and a non-severe
restriction shows it flagged. One gap is accepted: a recipe already read whose line changes keeps its old reading until
the next pass (at most about `MinIntervalSeconds` plus one pass after the write), as the cached probes did before.

**Cost.** On generated data of the production copy's size and shape (843,636 lines over 122,580 recipes, every
default type's 738 rules; `DietLineIndexTests`) the first pass reads every line in about 5 s on the desktop (32
threads; the matching runs in parallel), every type's read (its line recipes, its named recipes and its alias-line
recipes) takes under 15 ms, and a pass after 1 % of the recipes changed reads only those, in about 1 s. Memory: the
recipe ids per rule, one fingerprint and the matched rule keys per recipe, the drink lines and the recipes per food,
tens of megabytes. On the server the pass adds the line read (about 70 range queries of about 12,000 rows each); the
warm's log line gives both times (operator steps).

**Results are the matcher's.** `GoldenDietGateTests` and `GoldenDietPatternTests` resolve every golden row a second time
through a warmed index (`DietLineIndexTests.AssertIndexAgreesAsync`) and require the same foods and recipes, for the
household and for the person's severe set; the production-sized test checks 300 recipes per type line by line against
`LineNames`.

**Aliases never read a measure (P5).** "wine glassful of salt" (Salt) and "Wineglass Water" (Water) were trusted
aliases (`deterministic:recheck:name-key`, `:staple`), so Sulfite Sensitivity's `%wine%` counted Salt, Water, Milk and
Olive Oil and hid 71 % of recipes. A pattern now reads an alias with every measure named after a drink or a food
blanked out (`DietMeasureWords`: wine, claret, sherry, port, liqueur, cordial, whisky, brandy, champagne and beer
glasses and spoons; tea, coffee and breakfast glasses, cups and spoons; saltspoons; one word or two, with "ful" and
plurals). A drink's cup is no measure: "claret cup", "champagne cup" and "wine cup" are punches, so such an alias
still counts for the drink (review CK9S-3, golden dg-64). The alias
re-check (`IngredientLinkService.RecheckRule`) gives no rule to an alias that holds one. A recipe line is read as
before ("a wineglassful of water" still holds its recipe for `%wine%`, the safe side). Aliases already marked keep
their mark; the gate no longer reads the measure in them.

**The count (P23).** `GET /api/DietAdmin/gate-counts` (curation admins) reads the same kept values for every type
with a criterion, at full severity and severe: foods counted, recipes by food, recipes kept out as a whole (a line's
own text, a drink line, retained alcohol, or not yet read), both together, the read-up-to id and whether the type is
pending, with the index's time, recipes, lines and rules. Only recipes that are not deleted are counted, as the SQL
count does, and `recipes` gives their total, the base of a share such as Sulfite's 71 % (review CK9S-4); the index's
own recipe count also holds the live lines of deleted recipes. It answers in seconds and needs no database scan.
`ops/diet-gate-count.sql` stays for what only it does (the crude-versus-word-rule comparison and the samples); it now
blanks measures in an alias as the gate does, and it still takes about 40 minutes on the copy, so it is run alone
(nothing else on that database) and only for a release that changes the rule itself. It cannot read the gate's
stored results: they live in the API's memory, not in the database.

## The stored results: the gate in the database (staging checkpoint 5, Q2, Q3, Q4, Q6, P23)

Checkpoint 5 showed the in-memory gate failing a second time:

- The warm took 948 to 972 s, almost all of it food probes, and it could not be timed per type (Q3).
- After the warm, refreshes of stale probes ran with no bound. Under ordinary load (an import, the API's own
  nutrition rewrite, `gate-counts` itself) they passed Npgsql's 30 s command timeout.
- A failed refresh dropped the value, so large restricted households got no recipes, plans or prepared meals for
  minutes at a time (Q2).
- Search took 1.0 to 1.6 s with no restriction and up to 11.3 s with one (Q4).

Since `cleanup2/ck10-diet-store` the results live in the database, in six tables of the `plan` schema:

- **`DietGateBuild`**: one build per rule version. The version (`DietGateStore.Version`) joins the store's own
  `diet-store/1` with the pattern, label-term, measure-word, alcohol-context and statement-reader versions. A build
  is `building`, `active` (the one requests read) or `retired`, and it records its timings and its last cycle.
- **`DietGateSet`**: one restriction set per type and reading:
  - `all`: the type's nutrient, word and name-pattern criteria, which a household's plans read;
  - `severe`: those of severity 4 and 5, which a person's search reads, stored only when that list differs.

  A request finds a set by `CriteriaKey`, the SHA-256 of its criteria as the gate reads them (ids, severities,
  patterns, nutrient, cap and the type an exception is scoped to). `Severe` says whether a criterion is of severity 4
  or 5. The row records the set's food and recipe times and counts from when it was built. `State` is `building`,
  `complete` or `superseded`: when a type's criteria change (a deploy's `DefaultRestrictionCriteria.EnsureAsync`, or
  an admin), the active build keeps the type's old set, marked `superseded`, until the set of the new criteria is
  complete, and removes it at the end of that cycle (review CK10S-2).
- **`DietFoodFact`** (`SetId`, `IngredientId`): a food the set counts (`Verdict` 1) with:
  - the criterion that decided it (the highest severity, the lowest id among equals);
  - every criterion that counts it;
  - how it was counted: `name`, `alias`, `category`, `contents`, `nutrient` or `drink-name`.

  A food a pattern matches only through an alias that does not count has `Verdict` 3. A decided food with no row is
  free.
- **`DietFoodState`** (`BuildId`, `IngredientId`): that the build decided the food, with the fingerprint of what the
  decision read. That is the name, liveness, USDA category, data type, weights, aliases with their sources, the
  criteria's nutrient rows, and the latest FDC statement's id, outcome, reader version and change time.
- **`DietRecipeFlag`** (`SetId`, `RecipeId`): a recipe the set flags, with the food and criterion that decided it.
  `Verdict` 1 (contains) gives its `Reason`:
  - 1: a line's food;
  - 2: a line's own text names a pattern read in every line;
  - 3: a line on an alias-only food, beside a line that names a pattern;
  - 4: a drink line on a food not counted;
  - 5: retained alcohol over the cap.

  `Verdict` 2 (unknown) gives 6 (no live ingredient line) or 7 (a line on a food the build has not decided). A
  decided recipe with no row is free.
- **`DietRecipeState`** (`BuildId`, `RecipeId`): that the build decided the recipe, with the fingerprint of its live
  lines (food, order, text, amount, unit, kind, retention) and its servings.

**The build job.** `DietGateStoreBuilder`, run by `DietGateHostedService`, brings the current version up to date in
one cycle:

1. It reads the food catalog once: foods, aliases, the criteria's nutrient rows and the latest statements. It loads a
   statement's text only for a food it decides.
2. It decides every food whose fingerprint changed, and every food for a set not yet built, with `DietFoodReader`.
   The reader reads as the matcher's food probe (`DietCriteriaMatcher.ProbeAsync`) does, in memory:
   - a name by `DietPatternRule.IsMatch`;
   - an alias by `IsAliasMatch`, with its measures blanked;
   - the USDA category;
   - the statement by `PreparedContentsGate.RestrictedNames`;
   - a nutrient above 0.5 g per 100 g, and a drink name.

   Each food is decided from its own data only, so a batch of changed foods is decided as the whole catalog would
   decide them.
3. It reads every live line in windows of 2,000 recipe ids. Each changed recipe's lines are read with the line
   index's matcher (`DietLineIndex.Rules`, the seeded first-word check).
4. Per set, it decides every recipe whose lines or servings changed, every recipe on a changed food, and every recipe
   for a set not yet built. It counts retained alcohol for those recipes only (`RecipeAlcoholAsync` with a scope).
   It writes 5,000 recipes per transaction, so a recipe's flags and state are replaced together, with `COPY` on
   Postgres. A recipe is changed when its stored state is missing or its fingerprint differs from the stored one, read
   from `DietRecipeState` window by window, so a state another process removed is decided again.
   The fingerprints of the foods decided in the cycle are stored only after their recipes are (review CK10S-1). A
   cycle that fails between the two keeps the old fingerprints, so the next cycle, in this process or after a
   restart, sees those foods as changed again and decides their recipes.
5. A new rule version creates a new build beside the active one. Requests keep reading the active build until the new
   one is complete. The old build is then retired, and its rows are removed set by set.

The job keeps what it decided in memory between cycles. After a restart it reloads that from the stored rows, re-reads
the catalog and the lines, and decides nothing that did not change.

When the job runs:

- at start;
- within `DietGate:PollSeconds` (15) of a write this API made to a food, alias, nutrient, statement, line, recipe
  servings or measurement (the existing `DietGateCacheInvalidator` generation);
- every `DietGate:SweepSeconds` (300) in any case, for writes of another process (`Nom.Import`, SQL);
- never sooner than `DietGate:MinIntervalSeconds` (20) after the last cycle.

On Postgres one API process builds at a time (`pg_try_advisory_lock`). A failed cycle is logged and noted on the
build (`Note`), and the job reloads its memory from the stored rows; nothing stored is lost. Each cycle logs one line:
`Diet gate build B (version) state: S sets; foods F of N decided in x s; recipes R of M decided in y s; cycle z s`.
Each set built logs its own line with its food and recipe times. `DietGate:Build=false` turns the job off, and
requests then read what is stored.

**What a request reads.** No request runs a pattern, a probe or a build, waits on one, or loses a value because a
cycle failed:

- `HouseholdRestrictionResolver` reads the active build's id and the set ids of the household's types
  (`DietGateStore.SetsAsync`, two small indexed queries). Its `IngredientIds` and `RecipeIds` hold only the foods the
  restrictions name directly.
- Search, "Surprise me", plan generation and shuffle filter recipes with `DietGateStore.WithoutHeld`: one
  `NOT EXISTS` on `DietRecipeFlag` by (`SetId`, `RecipeId`), plus `EXISTS` on `DietRecipeState`: a recipe the active
  build has not decided is held by every set (review CK10S-3).
- A save that writes a recipe's line or servings removes that recipe's `DietRecipeState` rows in the same save
  (`DietRecipeStateClearer`, an EF save interceptor registered in the API and in `Nom.Import`). The edited recipe is
  then held by every set until the build has decided it again, instead of being read with the decision made on its
  old lines. A write that does not go through the EF change tracker (`ExecuteUpdate`, raw SQL, another tool) is
  picked up by the next sweep.
- The food-group top-up and the prepared pool filter foods the same way on `DietFoodFact` and `DietFoodState`.
- The prepared check and the verification lane load the facts of the meals and of their statements' foods
  (`RestrictedIngredientSet.LoadFoodsAsync`).
- The recipe page reads the facts of the recipe's own foods (`DietGateStore.PageAsync`).
- `gate-counts` groups the stored rows.

**Unknown is held for a severe set, and counted (Q6).** These are unknown:

- a recipe with no live ingredient line;
- a recipe the build has not decided yet (made since the last cycle, or its lines written since), which every set
  holds, severe or not;
- a recipe with a line on a food the build has not decided;
- a prepared meal the build has not decided.

A set holds unknowns when one of its criteria is of severity 4 or 5, or when a household member holds the type at
severity 4 or 5. Such a set keeps them out of search, plans and prepared meals (`diet_gate_pending`), and the recipe
page names them "Ingredients not known". A diet of severity 3 holds what contains its foods and shows what it cannot
read. A type whose criteria changed reads its previous set until the new one is built, so a deploy or an admin edit
never empties the gate. A type with no set at all (no build yet, or a type the build has never decided) holds every
recipe, and the recipe page names it "Not checked yet". `gate-counts` gives, per set, `recipesUnknown` and the flags
among decided recipes, `recipesNotDecided` (held by every set) and `recipesHeldOnlyForUnknown`: the price of the safe
side.

These are not unknown:

- a line on an uncurated wording food, which is a decided food like any other (the line's own text is read for it as
  before);
- a recipe whose lines leave out the restricted food (Macaroni and Cheese with no pasta line), which is decided by its
  lines. The title is not read.

**Budgets.** `DietGateStorePostgresTests` runs against a throwaway `postgres:16` made from `db/schema.sql` and
`db/seed.sql`, with `NOM_DIET_STORE_PG` set; otherwise it is reported skipped. Its data is generated: 88,000 foods
(44,000 aliases, 2,000 read statements), 122,000 recipes (26 with no line), 850,000 lines and 94 restriction types (102
sets, more than production's 79).

| Measure (desktop, `postgres:16` with `shared_buffers=1GB`) | Budget | Measured |
|---|---|---|
| First build (all foods and recipes, 102 sets) | 20 min | 46 to 55 s (foods 18 to 22 s, recipes 28 to 34 s) |
| Slowest set to build (Halal, `all`) | per-type time logged | foods 2.5 to 3.0 s, recipes 0.1 s |
| Rows stored | — | 822,451 food facts, 4,030,767 recipe flags (flags table with indexes 422 MB) |
| Cycle after a restart, nothing changed | — | 6.6 to 8.8 s, nothing decided |
| Cycle after one food changed | within a minute (with the 15 s poll) | 1.8 to 3.0 s, 1 food and its 10 recipes decided |
| `gate-counts` | seconds | 0.7 to 0.8 s |
| `ops/diet-gate-count.sql` (default) | seconds | 1.8 s |
| Search, any one restriction ("chicken" or empty query, 94 types) | under 1 s | slowest 468 ms |
| Search without a restriction | — | "chicken" 163 ms, empty query 342 ms |
| Search with a restriction, median over types | no slower than without | "chicken" 163 ms, empty 386 ms (worst type 115 ms above the unrestricted empty query) |
| Gate's part of a shuffle (household set, 30-recipe draw, top-up foods), 94 types | under 1 s | slowest 109 ms |

The search figures include a fix outside the gate. Search used to page with split-query includes, and each split
query ran the ranked, paged subquery over every recipe again (about 290 ms each on this data, about 2 s for an empty
query). That is why checkpoint 5 measured search at 1.0 to 1.6 s with no restriction at all (Q4). Search now picks
the page's ids in one query and loads the includes for those ids only.

**The results equal the in-line matcher's on every golden diet row.** `GoldenDietGateTests` and
`GoldenDietPatternTests` build the store on each row's database. They compare the recipes and foods the store lets
through with the matcher's, for the household and for the severe person (`DietGateStoreTests.AssertStoreAgreesAsync`).
The recipe page and the severe conflicts of `diet-gate.tsv` are then read from the store. `DietGateStoreTests` also
checks `DietFoodReader` against `ProbeAsync` for every default type in both readings. It uses a generated catalog of
names, USDA descriptions, trusted and untrusted aliases, alcohol figures, categories and statements.

**The count (P23).** `GET /api/DietAdmin/gate-counts` reads the stored rows. Per set it gives the foods, the recipes
by food, by text, unknown, not decided, held and held only for unknowns, and the live recipe total. `GET
/api/DietAdmin/gate-build` gives the builds with their timings and every set's food and recipe seconds (Q3).
`ops/diet-gate-count.sql` reads the same rows by default, in seconds. The crude-versus-word-rule audit it ran before
still runs with `-v audit=1` (about 40 minutes, alone, and only for a release that changes the rule).

## Golden set

`diet-gate.tsv` (see `golden-sets.md`): one food with its aliases and sources and one recipe line on
it under one default restriction; the recipe is `out` (kept out of plans, flagged on the page and,
at severity 4 or 5, hidden from search and a severe conflict) or `in`. The rows carry the K9 class
to Halal, Whole30, Kosher, Celiac Disease, Lactose Intolerance, Milk Allergy and Peanut Allergy in
both directions: the line the alias came from stays out, a line that does not name the restricted
thing is offered, and foods counted by name, category, USDA description, an admin's alias and a
retired duplicate's name stay out. `GoldenDietGateTests` also checks that no row the old rule kept
out is offered while its line names a pattern. Rows dg-41 to dg-50 (staging checkpoint 2) carry the
line rule above: a line naming the restricted thing on a food no pattern names, an alternative in
parentheses, a note line, the bacon line on a wrong food, a diet's alternative, each with a row the
other way, and `ratafia cakes` (almond biscuits) read as no drink line for No Alcohol and Halal.
Rows dg-51 to dg-59 carry the word rule end to end on foods named graham crackers, eggplant, coconut
milk (with its USDA description), peanut butter, buckwheat groats and hamburger. Rows dg-60 to dg-63
(staging checkpoint 4, P5) put Salt, Water and Milk with an alias made of a wine glass or a coffee cup
under Sulfite Sensitivity and Caffeine Sensitivity (offered), and a wine line on Water (kept out). Row dg-64 (review
CK9S-3) keeps a food whose trusted alias is "Wine Cup", a punch, under Sulfite Sensitivity. Rows dg-65 to dg-70
(checkpoint 4, P18) put almond, buckwheat and coconut flour with their reversed USDA descriptions and Cream
Of Tartar with its "cream tartars" alias on the gluten and dairy types (offered), and keep all-purpose flour
by its description and a "coconut flour or flour" line out. Row dg-71 (review CK9T-5) keeps an oats
description out of Celiac Disease, as `% oat%` reads it as written. Rows dg-cov-01 to dg-cov-10 (checkpoint 5,
Q19) put corn tortillas, apple butter, broccoli florets, sorghum flour, meatless bacon and spaghetti squash with
their USDA descriptions on the types that over-flagged them (offered), and keep flour tortillas, meatless bacon
for Soy Allergy, pizza rolls and maraschino cherries out.

`diet-coverage.tsv` (`GoldenDietCoverageTests`): the severe types measured on a judged sample, see the
checkpoint 5 section above.

`diet-patterns.tsv` (`GoldenDietPatternTests`): one recipe line under one default restriction type, on
a food no pattern names, `hit` or `clear`; a food named by the same text must be counted the same way.
Every type that ships by default holds a line that names its food and a look-alike, and every line the
crude reading flagged and the rule clears must be a reviewed `clear` row: no line that names a
restricted food by a pattern loses its flag. Unit tests (`DietPatternRuleTests`) check that every
exception blanks its phrase and leaves the term beside it.

## Operator steps

Before the change is relied on, on staging (read-only), count it. Since checkpoint 2 pass every
type with a criterion (`-v names=...`); the default list now names No Alcohol too:

```
psql "$STAGING_URL" -v sample=20 -f ops/diet-gate-count.sql > dg.tsv
```

The script runs in a read-only transaction. By default it reads every restriction type that has a
criterion, No Alcohol included (`-v names=Halal,No Alcohol` limits it to the types named, plus every
type a restriction uses). Reference rows that share a name (Halal 2011 and 60901, Kosher, Vegan and
others) are read as one type: their criteria are taken together and the type prints once, with its ids
in the `summary` row. Each pattern is matched against the food names, aliases and lines once, however
many types share it, and each set is counted once for all types. Should it still run long on a full
copy, run it in the background and read the file afterwards:
`nohup psql "$STAGING_URL" -v sample=20 -f ops/diet-gate-count.sql > dg.tsv 2> dg.err &`
(it holds only a read-only transaction; `pg_stat_activity` shows it while it runs). For each type it
prints one `summary` row:
foods counted before and after, alias-only foods, recipes kept out before and after, how many of
those the line rule keeps, recipes that lose the flag (and how many are approved) and recipes newly
kept out (the category and the line rule); then, for each severity 4 or 5 type, up to `sample` lost
recipes with their lines on alias-only foods, approved ones first, the rest spread over the whole set
in a fixed pseudo-random order (`-v seed=...` draws another sample). Beside the name patterns it applies the criteria the
change leaves as they were, the same way before and after: the word patterns (by name, and by an
alias with no drink word, which after the change also needs a trusted source), the nutrient
criteria with no per-serving cap (a stored figure above 0.5 g per 100 g; for alcohol not a spice or
a flavouring, plus a food named as a drink with no alcohol figure) and, for a type with the alcohol
criterion, every recipe with a drink line. A capped alcohol criterion (No Alcohol since
`cleanup2/alcohol-retention`, see `alcohol-retention.md`; Halal was capped too until
`cleanup2/ck3-small` made it uncapped again) counts a food named as a drink with no
alcohol figure and every recipe with a drink line, since the script cannot read how much alcohol
stays after cooking; a stored figure of any nutrient criterion, capped or not, decides a word
pattern's food as in the code. So a No Alcohol recipe whose cooked-off wine line would let it back
is still counted as kept out, in both columns; a Halal one is kept out by the code as well. A Halal
recipe whose butter alias goes but whose
brandy line stays is not counted as lost, and a line that names a word pattern counts as "kept by a
line". The script cannot run the alcohol reader, so a drink word is read by the shared drink-word
probe (`AlcoholLineContext.DrinkNamePattern`, kept equal to the code by a test) less a short list of
words that make a name or line no drink (vinegar, extract, juice, non-alcoholic and the like); the
rows of types with only name patterns, which are all the severity 4 and 5 defaults, do not depend on
it. Read every
`lost` sample line of the allergy and intolerance types: a line that names the restricted thing by a
word the patterns lack stops the release, and the word is added to the type's patterns.

Since `cleanup2/ck5-diet-patterns` each summary row also gives the word rule beside the crude reading:
`word rule: foods` (foods the type counts by the rule, and how many the crude reading counted that the
rule does not), `recipes` kept out by the crude reading (foods, alias lines, drink lines and every line)
`->` by the rule, `dropped` (and approved), `added` (a two-word term across punctuation, or a pattern
the release added, once it has run), `gained by line text only` (recipes only a line's own text keeps
out under the rule, and how many are approved), `patterns read as substring` (a pattern written to ask
for one: `%%`, `_` or an inner `%`) and `patterns the table lacks (read by ILIKE)` (a pattern the rule
reads by words that the script's table does not hold, such as an admin's; its figures are the ILIKE
reading, not the rule's, and it should be 0 unless an admin wrote criteria). The table (`rule_pats`)
holds the expressions of every default, companion and db/seed.sql pattern exactly as the code writes
them for PostgreSQL (a test keeps them equal).
For every type, not only the severe ones, it prints up to `sample` of each: `rule-drop` (a recipe the
rule lets back, with the lines and foods the crude reading read), `rule-food` (a food it no longer
counts) and `rule-line` (a recipe gained by line text only, with its lines and their foods). Read every
`rule-drop` and `rule-food` sample of the allergy and intolerance types: a line or food that names the
restricted thing stops the release, and the phrase that hid it comes out of the exceptions (or a word
goes into the type's patterns). The rule adds one ILIKE pass per distinct pattern (its first word, then
the expression on the lines that hold it) over the live lines and the food names.

Each summary row also gives `any-line` (recipes a line keeps out by its own text, whatever its food)
and `gained by any line` (those of them the food rules alone did not keep out, and how many are
approved), and for each severity 4 or 5 type up to `sample` such recipes as `line` rows, each line
with the food it sits on (`[on canola]`). Recipe 75943 must be among Peanut Allergy's gained recipes.
Read every `line` sample of the allergy types: a line that names the allergen only in a way that
plainly is not it ("peanut-free") is the one over-flag to report; it keeps the recipe out, the safe
side. The `line` samples are spread like the `lost` ones (the same `seed`), and a type with two
reference rows of one name prints once, as above. The line read adds one ILIKE pass per distinct
pattern over the live lines, a few minutes on the checkpoint copy beyond its 14.

### The line index (since `cleanup2/ck9-diet-speed`)

No schema change. After the deploy, on staging first:

1. Read the API log after the start: one `Diet gate: N food probes and M retained counts computed; line index read
   R of R recipes (L lines, K rules, K new) up to recipe X in P s; warm W s` line. The budget: W within minutes (the
   production-sized test reads the lines in about 5 s on the desktop; the server adds about 70 range reads), and no
   `Failed executing DbCommand` from the diet gate. A `Diet gate warm failed` warning repeats every 30 s until it
   succeeds; meanwhile every pattern restriction holds every recipe.
2. During the warm (right after the start) and after it, with a temporary Celiac Disease (60800) restriction on the
   API person: `POST /api/RecipeSearch/search {"query":"bread","pageSize":5}` answers 200 at once every time, with no
   result during the warm (every recipe held) and the gluten-free results after it, each under one second. Repeat for
   Wheat Allergy, Milk Allergy, Egg Allergy, Lactose Intolerance, Vegetarian, Vegan and Paleo (checkpoint 4's list),
   a plan generation for one of them and a prepared check (held as `diet_gate_pending` during the warm).
3. `GET /api/DietAdmin/gate-counts` (seconds): the per-type figures the checkpoints follow from now on, beside the
   index's recipes, lines and read-up-to id; no type `pending` once the warm is done. Sulfite Sensitivity no longer
   holds about 71 % of recipes (P5); its recipes by food must not include Salt's or Water's.
4. Write one recipe line (any edit) and confirm the next warm line reads `1 of ...` recipes about a minute later.
5. `ops/diet-gate-count.sql` remains the crude-versus-rule audit for a release that changes the rule itself. Run it
   alone, with nothing else running on that database (about 40 minutes on the production-sized copy); a checkpoint
   that does not change the rule reads step 3's figures instead.

### The stored results (since `cleanup2/ck10-diet-store`)

Schema: six new tables in `plan` (`DietGateBuild`, `DietGateSet`, `DietFoodFact`, `DietFoodState`, `DietRecipeFlag`,
`DietRecipeState`), additive. On staging first:

1. `./db/apply.sh --dry-run` shows exactly those six tables with their primary keys, the two identity sequences and
   the indexes `IX_DietFoodFact_IngredientId`, `IX_DietGateBuild_State_Id`, `IX_DietGateSet_BuildId_CriteriaKey`
   (unique) and `IX_DietRecipeFlag_RecipeId`. Then apply.
2. Deploy. The setting `DietGate:Warm` is gone. `DietGate:Build` (default on), `PollSeconds` (15), `SweepSeconds` (300)
   and `MinIntervalSeconds` (20) replace it.
3. Read the API log after the start. Expect one line per set built (`Diet gate build 1: set S (type T, all) built: ...`)
   and one cycle line: `Diet gate build 1 (diet-store/1;...) active (switched): N sets; foods ...; recipes ...; cycle
   W s`. The budget is W under 20 minutes; the generated production-sized test takes about a minute. Until that line
   appears, every pattern restriction holds every recipe (no build is active).
4. `GET /api/DietAdmin/gate-build`: the build `active`, its `totalSeconds`, and every set's `foodSeconds` and
   `recipeSeconds` (Q3).
5. `GET /api/DietAdmin/gate-counts`, or `psql -f ops/diet-gate-count.sql` (seconds): per set the foods, recipes by food
   and by text, `recipesUnknown`, `recipesNotDecided` and `recipesHeldOnlyForUnknown`. Expect about 26 unknown recipes
   (the live recipes with no line) held for each severe set and none held for unknowns by a diet. Compare the other
   figures with the checkpoint 5 figures of the in-memory gate (the same matcher, so equal within the lines changed
   since). Calling either now starts nothing.
6. With a temporary restriction on the API person, repeat checkpoint 5's per-type pass (79 type ids: search
   "chicken", shuffle, prepared check). Every call should answer under 1 s, with no type `diet_gate_pending` and no
   shuffle that creates 0 meals for lack of a value. Check that 141871 (Maple Pecan Sticky Buns) and 141878 are hidden
   from a Celiac search and shown to a Vegetarian one. Load the API at the same time (an admission import, the
   nutrition rewrite after a start): the answers do not change.
7. Edit one food's name to one a restriction reads (for example append "wheat") and one recipe line. The edited
   recipe leaves every restricted search at once (it is not decided until the next cycle). Within about 15 s the next
   cycle line reads `foods 1 of ...` and `recipes K of ...`, and a search reflects both.
8. Restart the API: the cycle line reads `foods 0 of ...; recipes 0 of ...` within seconds of the catalog read, and
   requests answer throughout the restart (they read the stored rows).
9. A release that raises a rule version builds a second build. `gate-build` shows it `building` beside the `active`
   one, requests keep answering from the active one, and after the switch the old build is `retired` and its rows are
   gone. A release that changes only default criteria (for example the coverage branch's pattern lists) builds the new
   sets in the active build: `gate-build` shows the changed types' old sets `superseded` and their new sets `building`,
   no restricted search, plan or prepared check returns `diet_gate_pending` meanwhile, and at the end of the cycle the
   superseded sets are gone.
10. `ops/diet-gate-count.sql -v audit=1` is the crude-versus-rule audit of a rule change, as before (about 40 minutes,
   alone).
