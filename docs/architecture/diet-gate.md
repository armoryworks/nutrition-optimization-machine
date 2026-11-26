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

## Staging checkpoint 6: words, dishes and the recipe's own text (cleanup2/ck11-diet-coverage)

Checkpoint 6 (R3, R5, R6, R7) found the gate reading parts of words ("til" in until, a nut in a maker's name),
missing what a dish holds by definition (six toasts, a panada, a Béarnaise with its accent), and offering a pie
whose lines left out the crust. `diet-patterns/6`, `diet-dishes/1`, `diet-vocabulary/2` and `recipe-text/1`:

- **A term is read where a word begins, and to where its meaning ends.** `Nom.Orch/Contents/diet-term-words.tsv`
  gives each term an end (`start`: any continuation, `flour` in flours and floury; `word`: the whole word and its
  inflections, `spot` but not spotted) and lists the compound words that are still the food (`in`: cornbread,
  buckwheat is `out`). Every word of the corpus a term starts is in the table; a word it leaves out becomes a named
  exception, so nothing substring matching rightly caught is lost unseen. Accents and ligatures fold on both sides
  (`DietPatternRule.Fold`: crème reads creme, œufs oeufs). A maker in the closing bracket of a name (a company word, or any
  bracket of a USDA branded food) is not read (`WithoutMaker`); a food whose name is its own USDA description keeps the spoken-order exceptions of a
  description. `Fixtures/diet-reading-register.tsv` holds every catalog name and description where the word rule
  and ilike disagree, pattern by pattern; `DietReadingRegisterTests` fails on any change until the register is
  regenerated from its output and the diff reviewed.
- **Dishes that hold a food by definition (`DietDishes`, `Contents/diet-dishes.tsv`).** Sauces, pastes, batters
  and preparations of the public-domain references (Escoffier, Beeton, Farmer, Webster) with what they are made
  of: a béchamel is a roux of butter and flour with milk, a forcemeat holds bread and egg, Marmite is barley yeast
  extract. A dish is resolved through other dishes and the label composites into composite companions, so every
  type that restricts a part holds the dish. Food names in the languages of the staged cookbooks (Italian,
  Spanish, French: farina, uova, burro, harina, beurre, oeufs) are vocabulary rows with basis `translation`.
- **The recipe's own text (R3, `DietRecipeText`).** For a book recipe (no web source) the method is read sentence by
  sentence, and the title for a dish made in a crust or a dough (pie, tart, pasty, dumpling, cheesecake; egg, fish
  and shellfish species), for a dish the title names as served with it ("Boiled leg of mutton, caper sauce"), and
  for every rule where the method is empty or a line sends the reader to another recipe ("Sauce made as directed in
  No. 1"). What a serving clause, an "instead of" or an imitation ("mock turtle") names is not read, nor what an
  accompaniment's title says it is for ("pour over the fish" under Sauce for Fish). A hold from the title or the
  method is an Unknown verdict for the severe types only, with its own reason (`DietFlagReason.Title`, `Method`),
  and the recipe page says it was "named in the title" or "named in the method". A recipe whose every line is an
  extraction placeholder ("Not specified in the text", "None provided") has no line and is unknown, and so is a book
  recipe built on another the lines do not give (`DietRecipeText.BuildsOnAnother`: "Make Biscuit dough", "Make like
  \"Brown Gravy\"", a line "batter as for suet pudding" or "ingredients for oyster sauce", a line that is only an
  amount, a method that says it is incomplete). Only a recipe imported from a book is read this way (`FromBook`: it
  has a scrape time and no web source); one a household writes in the app is read by its lines. The per-type counts
  (`DietStoredTypeCount`, `ops/diet-gate-count.sql`) give the recipes held for no line, an unknown food, the title and
  the method apart.
- **Measured, not built: an unrecognised line held for the severe types.** Holding a recipe whose line names no
  food word would have held 23,968 recipes; for Peanut Allergy 23,514 new holds against 3,476 held, far over the
  over-flag bar. It stays out; the proof draws below found the lines that matter by kind.
- **The proof loop.** Eighteen draws read locally with the build's reader against the read-only staging copy (30
  shown and 20 held per severe type, no recipe of an earlier sample): every miss and over-flag was fixed by kind
  before the next draw. Draws 1 to 17 each found at least one kind (draw 17: nouilles, an amount-only line, an
  incomplete method, "ingredients for oyster sauce", oyster mushrooms in a list, "the fish" of a potted-shrimp
  recipe); draw 18 found no miss and no over-flag of the reader's own, only two link errors and rows judged doubtful.
  The build report lists every draw and every link error found (links are listed, not patched).

## Staging checkpoint 7: safe only when understood (cleanup2/ck16-diet-known)

Four checkpoints failed diet coverage on words nobody had listed (checkpoint 7, S3: sponge-fingers, gaufrettes, a
skillet brownie, lo mein, chocolate chip cookies, Spry, balsamic, dried currants, dried cranberries). Listing does not
converge, so an unlisted word no longer means safe for a severe type. `diet-food-words/1`, `recipe-text/3`:

- **Three values for every line no curated food decides (`DietFoodWords`).** A line on a curated food is decided by
  that food, as before. A line on any other food (a wording food, a pending or retired food), or a line with no food,
  is read by its food words: its words without amounts and measures (the parser's own measure and number words), the
  grammar of English, and the line words of `Nom.Orch/Contents/diet-line-words.tsv` (chopped, divided, softened,
  garnish, optional: derived from the corpus, words that stand beside a curated food's own name in the lines of at
  least 20 curated foods, more often than they name one, that no severe criterion reads and that no 20 lines hold
  alone). For each severe set, each word of the live curated foods' names and USDA descriptions is:
  - **flagged** when the catalog knows it only on foods the set flags: on a flagged food whose name holds a term of
    the set, the term's one-word term; on one whose name holds none (flagged by an alias, a category or its contents),
    the words of its description's first part; and the one word of the second part of a description whose first part
    is a term of the set, when no clear curated food holds it ("Vinegar, balsamic": balsamic; "Cookies, brownies":
    brownie);
  - **clear** when it is known only on plain curated foods the set leaves clear. Plain foods are those of the USDA
    categories of single commodities (`DietFoodWords.PlainCategories`); a prepared mixture's clear verdict is no
    knowledge of its words, since its name does not list what it holds ("Restaurant, Chinese, vegetable lo mein" is
    clear only because its name says no noodle);
  - **unknown** otherwise: known on both sides, known only on prepared mixtures, or not known at all (sponge-fingers,
    gaufrettes, Spry).
  A line is flagged when a word is flagged, unknown when a word is unknown or the line names no food word at all ("one
  cupful of chopped", whose food the extraction lost), clear otherwise; a word that is a one-word term of the set is
  left to the term's rule, which reads the line first (so "peanut butter" stays clear for Milk Allergy).
- **Processing facts by kind.** The vocabulary's marker terms of two words give a kind: "dried apricot" (from
  "Apricots, dried, sulfured") makes "dried" with a head word of any curated food of Fruits and Fruit Juices a flag for
  Sulfite Sensitivity, on every line whatever its food, so dried currants linked to fresh currants and dried cranberries
  on Dried Cranberries are held. The vinegar and wine words come by the flagged rule above.
- **Severe types only.** A flagged word is a Contains verdict with its own reason (`DietFlagReason.FoodWord`); an
  unknown word an Unknown verdict with its own reason (`DietFlagReason.UnknownWord`), held for a severe set only and
  counted apart (`DietStoredTypeCount.RecipesUnknownWord`, `RecipesByFoodWord`; `ops/diet-gate-count.sql`
  `unknown_word`, `by_food_word`). A diet of choice reads as before. The recipe page says "Ingredient not understood"
  or "Named by an ingredient".
- **The words follow the catalog.** Each cycle the build reads the live curated foods (name, USDA description and
  trusted aliases) and builds every severe set's vocabulary from its stored facts; the signature of all of them is part
  of every recipe's fingerprint, and a cycle over the changes that finds the signature moved (a food curated, a curated
  food renamed or decided otherwise) runs a whole cycle, so every recipe is decided again under the new words. The
  food write trigger (`plan."DietGateFoodsWritten"`) now also fires on a change of `CurationStatusId`.
- **The recipe's own text follows the same rule (S4).** A book recipe whose method sends the cook to another recipe or
  preparation ("make the same as vanilla", "use recipe for Calumet One-egg Cake", "as for pie crust", "see page 34",
  "prepared as above", "according to directions previously given"), says it is incomplete anywhere, or breaks off
  ("Beat sixteen eggs very well in"), or whose lines only change another recipe's amounts (a line "only 1/2 cup of
  milk": 69144 Scones), builds on another and is unknown (`DietRecipeText.BuildsOnAnother`). A baked good of flour in
  the title (the Baked Products heads of the wheat vocabulary: bread, cake, biscuit, muffin ...) is read when no line
  names a flour, a meal or a starch ("Bread" whose lines are milk, sugar, nut meats and salt). A book method that names
  a food no line carries holds the recipe as unknown (Method, review ck16-2 and ck16-5; `DietFoodWords.HoldsInMethod`):
  - a word the catalog flags or knows on both sides, where it is what a flagged food is: the one word of its USDA
    description's first part ("Wafers, vanilla": wafer; "Candies, ...": candy), not a word of a first part of more
    words ("ice" of "Ice creams", "ring" of "Onion rings", "leavening" of "Leavening agents") or of the second part
    ("evaporated" of "Milk, evaporated", "mince" of "Pie, mince"), which a method uses as a verb or a tool ("mince the
    onion", "a buttered baking dish", "set on ice", "until evaporated", "a brick oven");
  - such a qualifying word too when a food word the set does not read as clear follows it right after ("slices of
    sponge cake", "hand sponge fingers with it", "dinner rolls"), and a second-part word that the corpus names as a food
    at one tenth of its method uses ("add cider, scalding hot" for Sulfite Sensitivity);
  - a word no curated food knows (or only a prepared mixture) that is the whole name of uncurated foods the corpus's
    lines are linked to, in at least 2 recipes and at least one fifth of the recipes whose method names it
    (`Nom.Orch/Contents/diet-method-foods.tsv`, derived, 1,216 words with the one-tenth rows: "rizotto" of 133979
    Poularde Valencienne, "pullet", "forcemeat", "cocoanut"; a word methods use far more than lines list is a tool or
    a thing the recipe makes, and is not read: pan, oven, cloth, paper, crust, icing, filling, stuffing, liquor,
    dripping, ice).
  The terms a method is not read for (`DietRecipeText.MethodSkips`) are left out of all three.
- **Holds by title or method alone (S5).** The method is no longer read in an optional addition, and only there
  (`DietRecipeText.WithoutOptional`, review ck16-1): what "if desired", "if liked" or "optional" follows is the phrase
  back to the last break ("Sift the flour with the salt and add sugar if desired" still reads the flour), what it leads
  is the phrase up to the next break ("Add, if liked, a little sherry and stir in the flour"), what "may be added" says
  of is its clause, and a variant ("another sauce", "variations") is its whole sentence; nor in a use clause ("This sauce is used for boiled chicken or fish"), "pass separately", a
  negation ("made without eggs") or a simile ("like a loaf of bread", "the size of a walnut", "come noci"), and an
  accompaniment's "pour/strain/serve over the ..." is read as what it goes with.
- **A statement that names its nut (R7).** A prepared meal's allergen that names a member of the tree nuts ("coconut",
  "cashews") is no longer read by the class's words that name no member ("tree nut", "nuts"), so Peanut Allergy's
  unnamed-nut term `% nut%` does not hold the six coconut and cashew meals (`IngredientStatementReader.GateWords`).
- **A drink offered as an alternative (S12).** A line's bracketed alternative ("beef stock (or I used dry red wine)") is
  read for drink alcohol (`DietCriteriaMatcher.LineCounts`), whatever the alias table holds.

The price is large and measured, not barred: on the read-only staging copy of 2026-11-25 every severe type holds
26,358 (Milk Allergy) to 50,430 (Sesame Allergy) recipes as unknown (Peanut Allergy 49,646, of them 43,265 by an
unknown word and about 1,640 by a method that names a food no line carries), against 1,171 to 67,197 flagged. The
work list of unknown words by frequency (`build/ck16-diet-known.unknown-words.tsv` in the audit evidence) is what the
wording and link lanes shrink: a wording linked to a curated food is decided by that food.

## Old cookbook words: the verified wording batch w02 (cleanup2/ck17-words)

The price above is cut by the verified wording table, not by a diet-side word list. Batch `w02`
(`nom-api/Nom.Api.Tests/Fixtures/VerifiedWordings/batch-w02.tsv`, old cookbook wordings only) gives each judged
wording either the curated food it is (`food`: "treacle" is molasses, "eschalots" are shallots, "Zucchero" is sugar,
"graham bread" is whole-wheat bread), whose lines `relink/verified` moves onto that food, or, when no single catalog
food is it, the USDA category and description head of the foods it is one of (`category`, `diet-food-words/2`):

- **What a category decides.** For each severe set, a group (the live curated foods of the category whose USDA
  description starts with the head at a comma or its end, `WordingCategory`) reads as **flagged** when every one of its
  curated foods is flagged, as **clear** when none is flagged, aliased or of unknown contents and the category is one
  of single commodities (`DietFoodWords.PlainCategories`), and as **unknown** otherwise. "isinglass" (Webster 1913:
  gelatin from the air bladders of sturgeon) is category 15, head "Fish": flagged for Fish Allergy, shown to Peanut
  Allergy. A flagged group flags the line; a clear group clears it unless its words are flagged; an unknown group adds
  nothing and the line is read by its words as before, so a group never holds a line its words understood ("chickens"
  stays shown to Egg Allergy although a curated batter-fried chicken is in the Poultry group).
- **Which lines.** Only a line on an uncurated row whose lower-cased name is the wording and whose own text names the
  wording as the verified relink reads it (`LineRelinkService.NamesWording`); a stray line an import parked on the row
  is still read by its words. A kind of the set ("dried" with a fruit) still flags such a line, and the line rules of
  the set read its text first, as for every line. Nothing moves and no nutrition is claimed.
- **Signature.** The category rows and every set's class of every group are part of the words signature, so loading,
  unloading or retracting a category row makes the next cycle a whole cycle.
- **What stays unknown.** A wording whose meaning the cookbooks leave open (stock, white stock, dripping, jelly, soup,
  forcemeat, consomme, marrow, "two cloves"), a consommé or a fumet even with its qualifier ("chicken consommé" is
  clarified with egg white, Escoffier's fish fumet is made with white wine, so the qualifier settles the meat or the fish
  but not what the cookbook adds), a sauce or composite the cookbook makes from another recipe (drawn butter,
  Espagnole, buttered crumbs), and things that are no food (saltpetre, alum, cochineal) get no row. Every row was judged
  twice, each time on five lines whose own text names the wording (a stray line an import parked on the row does not
  count), the second time from the definition and five other lines, and a row the two passes did not agree on was
  dropped. Measured on the staging copy: see the build report `build/ck17-words.md` in the audit evidence.

## The three-valued reading and its whole draws (cleanup2/ck17-diet-proof)

Version `diet-food-words/3` (the words of a set) and `recipe-text/4` (the book text rules). Every rule below is derived
from the catalog or the corpus, or is a kind a whole draw showed; no rule lists a food by hand.

- **Shapes and dish forms never clear a line alone** (`DietFoodWords.FormKind`). A shape of the catalog ("patty",
  "roll") is read as not understood when no other food word of the line is; a dish form is the whole first part of a
  prepared mixture's USDA description outside the single-commodity categories ("pie", "sauce", "pudding") and is read as
  not understood. Derived from the curated catalog when the words load.
- **A method dish no line carries** (`Contents/diet-method-dishes.tsv`, derived from the corpus: a word that ends at
  least 3 book titles that are two fifths of the book recipes whose method names it) holds the recipe as unknown when no
  curated food holds the word and the recipe's own title does not name it ("a garnish of rizotto").
- **Old spellings** (`Contents/diet-spellings.tsv`) map only by the derived rule: one letter from a curated catalog
  word (inserted, dropped, changed, or two neighbours swapped), the same first letter, and at least half of the
  spelling's lines on curated foods (at least 3) on foods that hold that word. Measured before adoption: it released
  153 to 517 type-recipes per type, all of them by spellings. A spelling is written in its own case ("Prepare Cocoanut
  Pudding." is read as "Prepare Coconut Pudding."), so the referral rules that need a capitalised recipe name still read
  it (review ck17dpr-1; on the staging copy the case changed no verdict).
- **The consommé of a clarifying book.** A line that names a consommé holds the recipe as unknown for a severe set that
  flags egg white when the recipe's own book (`SourceAttribution`) has a consommé recipe whose lines or method clarify
  it with egg white (`DietRecipeText.ConsommeClarifiers`). On the staging copy it holds 57 Egg Allergy recipes from 6
  books; a book whose consommé is strong broth (Francatelli) is not held. A write of a recipe's `SourceAttribution` makes
  it undecided, by the trigger and the EF clearer, since the hold reads it (review ck17dpr-2).
- **A dough the lines lost.** A title whose last word is a dough dish (`DoughTerms` and the derived
  `Contents/diet-dough-titles.tsv`, a word that ends at least 10 book titles of which nine tenths carry a dough line)
  while no line names one; a title's number in a series is dropped first ("RHUBARB PIE No. 2"). A method that bakes, boils or wraps
  the dish in a paste, a crust or a pastry, bakes it between two crusts, fills the lower crust or tops the pie "after the
  pie is baked", while no line names a paste, a crust, a dough or a shell (`DietRecipeText.PasteLost`).
- **A recipe built on another** (`DietRecipeText.BuildsOnAnother`) is held as having no readable ingredient list: a
  method that sends the cook elsewhere ("the same as given under No. 38", "see No. 185", "as directed on page 168",
  "Article 58", "as directed on package", "Prepare Duchess Potato.", "in a similar way to Cumberland pudding", "Make
  exactly as you would a beef-steak pie"), only varies another recipe ("Use Champagne instead of Madeira wine"), ends
  mid-sentence, begins by stirring something in ("Stir in the corn and bake"), or says it is incomplete; a line that is
  another dish's base ("dressing (such as is used for turkey dressing)", "batter as for suet pudding"); and melted or
  drawn butter measured by the pint, the gill or the boat, the English butter sauce of flour, butter and water ("three
  parts of a pint of melted butter").
- **A cake the method uses** ("spread between layers of cake", "dip small pieces of cake", "spread over hot cake") is
  read as the food, though a method's "cake" is otherwise a shape ("make into flat cakes"): a filling or an icing spread
  on a cake no line gives is unknown.
- **A line that leaves its food to the cook** ("Fillings of your choice", `DietRecipeText.LeavesChoice`) is not
  understood whatever food it is linked to.
- **A one-line recipe** whose method names a flagged, mixed or dish-form word no line carries is unknown: its line list
  is the dish's main food alone ("Layer sliced turnips with sauce and cheese, then bake").
- **A method's food words** are paired with the head of the words right after them ("soy bean sauce"), and a run of
  three words with no grammar is also read without its middle word.
- **A size given by a food** ("a walnut-sized piece of butter", "butter the size of an egg") is dropped from the text
  before it is read (`DietFoodWords.Respell`).

**The proof.** Whole draws on a local copy of the staging snapshot with w02 applied (30 shown and 20 held per severe
type, 10 of the shown weighted to recipes w02 releases, ids in no earlier sample, checkpoint, draw or fixture), read on
both sides and fixed by kind. Draws 6 to 17 are logged in `build/ck17-diet-proof.draws.tsv` in the audit evidence;
wrong flags stayed at 0 to 3 of 110 per draw. The stop rule (two consecutive whole draws with no miss among the shown)
is not reached, so the reading is not proven and is not to be released as proven: every draw from 6 to 17 found one to
five misses. Two known misses are catalog facts, not gate kinds, and are still shown: a curated condensed cream soup
read as clear for wheat, and the book's Protose linked to Fresh Coconut Meat. Since `cleanup2/ck18-composites` both are
held (next section).

**Price after w02**, type-recipes shown, flagged, unknown, of 116,557 live recipes: Celiac 26,194 / 55,427 / 34,936;
Milk 18,261 / 67,195 / 31,101; Peanut 50,701 / 2,984 / 62,872; Sesame 51,644 / 1,171 / 63,742; the build report has
every type, before and after w02.

## Made products by their brands' label statements (cleanup2/ck18-composites)

Draws 6 to 17 left two kinds of miss that no word rule could close: a curated made product whose category and name do
not say what it holds (`Condensed Cream Of Chicken Soup` read clear for Celiac Disease and Wheat Allergy), and a trade
name a model pick linked to a food it is not ("Protose", the vegetarian books' wheat-gluten and nut meat, on
`Fresh Coconut Meat`). Versions `diet-food-words/4` and `recipe-text/5`.

- **The branded consensus** (`BrandedConsensus`, `Contents/diet-branded-consensus.tsv`, `branded-consensus/2`). A
  curated food that is a made product (USDA categories Fats and Oils, Soups, Sauces and Gravies, Sausages and Luncheon
  Meats, Breakfast Cereals, Baked Products, Sweets and Snacks; a food of no USDA category; or any other category but the
  prepared and the American Indian/Alaska Native ones whose description holds a made marker: canned, dried, pickled,
  cured, sauce, paste, spread, mix, dressing, meatless ...) is matched to the US branded foods of FoodData Central that
  are the same kind. The rule: the product a branded description names (its first part, the flavour it repeats and an
  addition "with ..." left out) holds every word of the food's key and ends with the key's head; it holds no form word
  the food's description does not ("dried", "frozen", "instant", "pickled", "frosted" ...); the word before its head is
  no other food's head ("rice cakes" are no corn cakes) and names no class of the vocabulary the food's own words do not
  ("white clam sauce" is no white sauce, "cheddar popcorn" no popcorn; "mayo mayonnaise" stays); a later part of three or more words names no other food (a
  pizza that names the cheese); and it is not of a whole-meal category. Only the branded categories that hold a fifth of
  a food's matches count. A maker's own product (a description naming its brand in capitals) and a food with another
  food in it ("milk chocolate, with almonds") are not matched. Measured on 60 matches read by hand, 52 are the same kind
  (87 %; an earlier form of the rule 55 of 60; the class-word form of review ck18 again 52 of 60): the build report
  lists every read.
- **Foods made at home.** A food USDA describes as home-prepared, homemade, a home recipe, home preserved or prepared
  from a recipe is matched as any other, but takes from its brands only a *usually* row (what the dish is made of: the
  flour of a pie, the eggs of a sponge cake) and never the sulfite class. Where the brands are split, the split is what
  each maker adds, and a home recipe's own choice is in its own lines; a sulfiting agent is a preservative a maker adds,
  and a home kitchen does not (refrigerated mashed potatoes with sodium bisulfite are no home-prepared mashed potatoes).
- **Stating.** Each matched statement is read by the prepared-contents reader and the label terms with no catalog, as
  the gate reads a prepared meal's statement, against the default criteria of each vocabulary class's type; a "may
  contain" warning is no statement of contents; a declared allergen states only its own class (a "Contains: tree nuts"
  line is no statement of peanut); coconut does not state the tree nuts (the Tree Nut set does not read coconut); the
  sulfite class is stated by the sulfiting agents a label names, not by vinegar or wine.
- **The verdict per class.** From at least 10 read statements: three in four or more stating the class make the food
  *usually contain* it; more than one in ten and fewer than three in four make its contents for the class *split*;
  fewer leave it as its own data reads. Ten because below it one product moves the share by more than the whole clear
  band; one in ten because at the measured match precision (about one match in eight is another kind) mismatches alone
  can raise a clear kind toward a tenth, so a lower bar would hold foods on noise; three in four because the data is
  bimodal for the large classes (wheat, gluten: most foods under 5 % or over 90 %) and a true 85 % kind stays above it
  after that noise. No model reads or decides anything and no nutrition number is touched.
- **What the gate does with it.** Only a severe criterion (severity 4 or 5) reads the rows: a criterion reads a class
  when it names the class's probe word as a line would ("wheat"; "barley" for gluten, so Wheat Allergy does not read it;
  "almond" for the tree nuts, so Peanut Allergy's unnamed nut does not). A usually-contains food is flagged
  (`DietFoodFact.Source` `consensus`); a split food is unknown (`consensus-split`) for that set; a diet of choice reads
  nothing. A row applies to a curated food by its name and FDC id (catalog ids differ between databases), and is part of
  the food's fingerprint, so a changed file decides the food again. A consensus fact is no knowledge of a food's words:
  "soup" in an old book's "soup stock" is not a can of condensed soup. The line-move guard reads a split food as
  unknown, as the store does, so a relink, merge or undo that would move a line off it onto a clear food is held.
- **Unproven links.** A line on a curated food whose wording an applied model pick linked there (`ai:` link proposals
  not proven by a verified wording row) and whose every food word no curated food knows is read by its words, not as the
  food: "Protose, ½ pound." on `Fresh Coconut Meat` is not understood. The set of such links is part of the words
  signature. On the staging copy it held 49 to 134 type-recipes per type, where ck17's rule for every curated line held
  523 to 1,725.
- **Draw kinds.** A composite word (dressing, mayonnaise, margarine, sauce, catsup, gravy, mix ...) is not made clear by
  a curated food of a single-commodity category ("salad dressing" read clear for Egg Allergy); a line read by its words
  that carries a free-of marker ("Vegan Parmesan") is not understood; and the referrals of draws 18 to 21 (`BuildsOnAnother`).

The draws 18 to 21 and the price are in the build report `build/ck18-composites.md`. The stop rule is not met: each of
the four draws found four to six misses among the shown.

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
the database (next section). After staging checkpoint 6 proved the store, `cleanup2/ck11-diet-moves` removed the index
(`DietLineIndex`), the cache's peek path (`DietGateCache.PeekAsync`, `EnsureFreshAsync`, the warm of the food probes
and retained counts) and the old count (`DietGateCounts`). The build job reads lines with `DietLineRules` (the same
rules and seeded first-word check), and the golden tests compare the store with the in-line matcher only.

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
   rules (`DietLineRules`, the seeded first-word check); since `cleanup2/ck11-diet-moves` only the recipes it decides.
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

The job keeps the food and recipe fingerprints and the sets' food facts in memory between cycles; since
`cleanup2/ck11-diet-moves` it no longer keeps a reading of every recipe (R9). After a restart it reloads them from the
stored rows, re-reads the catalog and the lines, and decides nothing that did not change. That whole cycle is the
sweep. Between sweeps a cycle over the changes (`RunChangesAsync`, next section) reads only what a writer made
undecided.

When the job runs (since `cleanup2/ck11-diet-moves`):

- a whole cycle at start and every `DietGate:SweepSeconds` (900; was 300) as a backstop for a write made with the
  triggers disabled (`session_replication_role = replica`, a restore); every table the gate reads has its trigger, a
  measurement's category, factor and container mark included;
- a cycle over the changes within `DietGate:PollSeconds` (15) of a write to what the gate reads, by this process (the
  `DietGateCacheInvalidator` generation) or by any other (a committed row of the write log, `plan."DietGateWrite"`,
  written by the triggers in the writer's transaction);
- never sooner than `DietGate:MinIntervalSeconds` (20) after the last cycle.

On Postgres one API process builds at a time (`pg_try_advisory_lock`). A failed cycle is logged and noted on the
build (`Note`), and the job reloads its memory from the stored rows; nothing stored is lost. Each cycle logs one line:
`Diet gate build B (version) state: S sets; foods F of N decided in x s; recipes R of M decided in y s; cycle z s`.
Each set built logs its own line with its food and recipe times. `DietGate:Build=false` turns the job off, and
requests then read what is stored; the process then only trims the write log every `DietGate:SweepSeconds` of the rows
older than `DietGate:WriteLogKeepSeconds` (3600, at least 60), so a log no build reads does not grow without end
(`DietGateStoreBuilder.TrimWritesAsync`). A process that builds empties the log itself, so the trim never takes a row a
running build has not read yet unless that build has been down for longer than the age kept.

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
  old lines. Since `cleanup2/ck11-diet-moves` every other writer does the same in the database itself (next section).
- The food-group top-up and the prepared pool filter foods the same way on `DietFoodFact` and `DietFoodState`; a food
  the build has not decided is held by every set, as a recipe is (since `cleanup2/ck11-diet-moves`; before, only by a
  set that holds unknowns).
- The prepared check and the verification lane load the facts of the meals and of their statements' foods
  (`RestrictedIngredientSet.LoadFoodsAsync`).
- The recipe page reads the facts of the recipe's own foods (`DietGateStore.PageAsync`).
- `gate-counts` groups the stored rows.

**Unknown is held for a severe set, and counted (Q6).** These are unknown:

- a recipe with no live ingredient line;
- a recipe the build has not decided yet (made since the last cycle, or its lines written since), which every set
  holds, severe or not;
- a recipe with a line on a food the build has not decided;
- a recipe with a line on a food of unknown contents (since `cleanup2/ck16-diet-store-moves`: a wording food the link
  re-check took lines back to although the take-back would lower a flag, see "Catalog-wide writes");
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

## Every writer, every process (staging checkpoint 6, R12, R9; `cleanup2/ck11-diet-moves`)

Checkpoint 6 found the raw-SQL line writers (the rebuild, its undo and re-apply, the order-only pass, the re-check sets,
the relink undo, `Nom.Import`) never clearing `DietRecipeState`: the EF save interceptor never saw them, so moved
recipes were served from their old state, unheld, for up to 159 s (300 s bound), 17 of them with changed flags (8 that
gained Soy Allergy).

**Held, not stale, in the writer's own transaction.** `db/custom-objects.sql` carries statement-level triggers
(transition tables) on every table the gate reads, and `db/schema.sql` carries them by `sync-from-model.sh` (pgdiff
now diffs standalone sequences, functions and triggers, so `apply.sh` and `--check` carry them too):

| Table | Writes | Made undecided |
|---|---|---|
| `recipe."RecipeIngredient"` | insert, delete; an update that changes a line's food, order, text, amount, unit, kind, retention or liveness (a `LastModifiedDate`-only update does not) | the line's recipe (`DietRecipeState`) |
| `recipe."Recipe"` | an update of `Servings`, `RecipeServings`, `IsDeleted`, or the title and source a book recipe is read by (`Name`, `SourceUrl`, `ScrapedAtUtc`), or its book (`SourceAttribution`, which the consommé hold compares with the books that clarify their consommé with egg white: review ck17dpr-2) | the recipe |
| `recipe."RecipeStep"` | insert, delete; an update of a step's text, number or liveness, on a recipe with a scrape time (a book recipe's method is read, `DietRecipeText`) | the recipe |
| `recipe."Ingredient"` | delete; an update of name, liveness, USDA category or data type, or a weight | the food (`DietFoodState`); every recipe with a line on it is held through the write log (since `cleanup2/ck16-diet-store-moves`, see "Catalog-wide writes") |
| `recipe."IngredientAlias"` | insert, update, delete | the food; its recipes held through the write log |
| `nutrient."IngredientNutrient"` | a row of a nutrient some criterion reads | the food; its recipes held through the write log |
| `recipe."IngredientContents"` | a statement of no household | the food; its recipes held through the write log |
| `plan."RestrictionCriterion"` | insert, update, delete | nothing (a log row, so the job sees the criteria) |
| `measurement."Measurement"` | delete; an update of category, factor, container mark or liveness | every recipe with a line in that unit (retained alcohol weighs drinks by it) |

Every trigger also logs what it touched in `plan."DietGateWrite"` (a recipe id, a food id, or neither for a criterion
or a new food), the write log every process's build job polls. A log row is a row of the writer's own transaction, so
the job sees it exactly when the write commits; a rolled-back write takes its clearing and its log rows with it. (The
first version moved a sequence instead; a sequence moves when the statement runs, not when it commits, so a poll could
read the count of a write still open, run its cycle before the commit, and then see no reason to run again until the
sweep, review CK11-DM-1.) The EF interceptor stays (it is what the in-memory tests see). Requests hold an
undecided recipe or food for every set, so a moved recipe is held from the writer's commit until the next cycle.

**The cycle over the changes.** `DietGateStoreBuilder.RunChangesAsync` first reads the committed rows of the write log
(at most `WriteBatch`, 100,000), then the foods and recipes the active build has no state for, the logged foods and
recipes, and the recipes with a line on those foods; it loads the catalog for those foods and the foods of those
recipes only (`DietFoodCatalog.LoadAsync` with `foodIds`), reads the lines of those recipes only, decides each whose
fingerprint moved or that has no state, and at the end removes the log rows it read. It falls back to a whole cycle
when the build or a set is not complete or a type's criteria changed; a whole cycle consumes the log the same way. A
write that commits while a cycle runs keeps its log row, so the next poll runs again: a recipe whose state the cycle
built from the lines before that commit is decided again then (its fingerprint moved although it has a state), and a
recipe the commit left undecided is held until then.

**No verdict from older lines (since `cleanup2/ck12-links-small`).** Before, a recipe whose state the cycle built from
the lines before such a commit was served from that verdict until the next poll (15 to 35 s). Now each log row carries
the writer's transaction (`Tx xid8 DEFAULT pg_current_xact_id()`), and the cycle takes a snapshot
(`pg_current_snapshot()`) before it reads the lines. In the transaction that stores a chunk's states, after the insert,
one statement removes again the state and the flags of every recipe of the chunk with a log row whose transaction that
snapshot could not see (`pg_visible_in_snapshot`): a write that committed between the reading and the storing. Such a
recipe stays undecided, so every set holds it, and the next cycle decides it from the new lines; the cycle line counts
them (`N written while read left for the next cycle`).

That statement sees only committed log rows. A writer whose transaction is still open when the chunk is stored (its
trigger ran before the store, found no state row to remove, and its log row is not committed yet; a long batch apply
is the usual case) commits after it, and nothing it ran can see the new state row (review ck12ls-r1). So each stored
state also carries `ReadXmin`, the xmin of the cycle's snapshot (the oldest transaction still running when it began to
read the lines), and requests read a state only while no log row for its recipe has a `Tx` at or above it
(`DietGateStore.Standing`, used by `WithoutHeld`, the page reading and the counts): from the writer's commit, when its
log row becomes visible, the recipe is held as undecided, and the next cycle decides it and removes the row. Before
that commit the stored verdict agrees with every committed line. The test is conservative (a transaction at or above
the xmin may have committed before the snapshot), which can only hold a recipe until the next cycle. No lock is taken:
a writer never waits for the build, and the build never waits for a writer. In-memory contexts (the tests) compare the
log row ids seen before the reading instead of a snapshot and store no `ReadXmin`.

**Memory (R9).** The API held 1.1 to 1.9 GiB after the build and logged `High memory usage detected` up to 38 times a
minute. Two causes: the build job kept a reading of every recipe (its rules, drink lines with their text, foods) in
memory between cycles, and every cycle (every ~31 s while the API wrote anything) loaded the whole food catalog and every
line again. Now the job keeps no recipe reading, reads only the recipes it decides, and a cycle over one change reads a
few hundred rows; the whole catalog is read at start and once per sweep. The memory warning of
`ContainerSecurityMiddleware` was logged on every request while the managed heap was above 500 MB; it is now logged at
most once a minute.

**Measured** (`DietGateStorePostgresTests`, the generated production-sized data on a throwaway `postgres:16` made from
this branch's `db/schema.sql`):

| Measure | Before (ck10) | Now |
|---|---|---|
| First build, 102 sets (the larger pattern sets of ck10-diet-coverage) | 46 to 55 s on ck10's sets | 128.5 s (foods 69.6, recipes 58.7) |
| Whole cycle after a restart, nothing changed | 6.6 to 8.8 s (every recipe re-read by the matcher) | 2.8 s |
| Whole cycle, nothing changed | — | 3.9 s |
| One food changed (whole cycle) | 1.8 to 3.0 s | 1.9 s |
| Cycle over the changes: 8 raw-SQL writes (order, insert, delete, servings, rename, alias) | — | 0.21 s, 2 foods and 18 recipes, 122 foods read |
| Cycle over one line change, five times | — | 0.11 to 0.14 s; managed heap 259 -> 260 MiB |
| Raw update of 200,086 lines' order, with and without the triggers | — | 3.26 s against 2.98 s |
| Raw update of their `LastModifiedDate` only (nothing cleared) | — | 4.35 s against 3.45 s |

The sweep stays as a backstop only (every table the gate reads has a trigger, the measurement table since review
CK11-DM-4); at 900 s it reads the catalog four times an hour instead of twelve (and instead of every ~31 s while the
API wrote). 900 s is the right interval: the sweep now catches only a write made with the triggers off, and raising it
further gains little, since a whole cycle with nothing changed costs about 3 s.

## Catalog-wide writes (staging checkpoint 7, S2, S6; `cleanup2/ck16-diet-store-moves`)

Checkpoint 7 measured the cycle over the changes after each catalog-wide write (the admission, its undo and re-apply, the
prepared contents, the alcohol re-check set with its 92 new foods and 245 alias rows, the w01 unload): `recipes 116639 of
122580 decided in 315.9 s` where a whole cycle decides `122103 ... in 66.7 s`, so 58 to 95 % of recipes were held for
every restricted household for 4 to 6 minutes after each write (never stale). Two causes, both fixed:

- **One by one.** The cycle over the changes read its recipes one at a time; the whole cycle reads them on every core
  (`Parallel.For`). The API ran at about 500 % CPU during the build, which is the factor of five. The cycle over the
  changes now reads on every core too (`ReadScope`), and above `DietGateStoreBuilder.LargeScope` (5,000) recipes it reads
  every line in windows of recipe ids as the whole cycle does (`windowed` in the report and `(read in windows)` in the
  cycle line), so its cost is about a whole cycle's whatever the scope.
- **Recipes read again for a food whose decision did not move.** A write to a food (`DietGateClearFoods`: a rename, an
  alias, a criterion nutrient, a statement, a category, a weight) removed the state of every recipe with a line on it.
  Now it removes the food's state and logs the food and, for each recipe with a line on it, a row with both ids
  (`plan."DietGateWrite"` with `RecipeId` and `IngredientId`); the recipe's state stays. `DietGateStore.Standing` holds
  such a recipe from the write's commit, as it holds any logged recipe, so nothing is served from the food's old facts.
  The cycle decides the food first; only when its decision moved for some set (a fact changed, or for the first time),
  or what weighs a drink's retained alcohol moved (the capped nutrient, a weight, its name, category or liveness), are
  its recipes read again (`FoodsMoved`). Otherwise the cycle removes the log rows and the recipes stand again unread
  (`RecipesConfirmed`, `N held by a food write released unread` in the cycle line). Which catalog writes change a diet
  fact is measured by that line on every cycle, not assumed: the admission's 12,341 foods (energy check, categories,
  descriptions) are expected to move few facts; the prepared contents and the re-check sets' new aliases move some.

The cycle line now reads: `changes cycle: N sets; foods F of T decided in X s, M moved a decision; recipes R of T decided
in Y s (read in windows), C held by a food write released unread, L written while read left for the next cycle`.

**Measured** (`DietGateStorePostgresTests.A_catalog_wide_food_write_is_decided_within_a_whole_cycles_time_and_one_that_moves_no_decision_releases_its_recipes_unread`,
the production-sized generated data): 12,000 foods carrying more than 90 % of the recipes are given an alias no pattern
reads (every recipe on them held at the commit, released unread by the next cycle, faster than a whole cycle), then
renamed with " wheat" (every recipe on them decided again, windowed, no slower than 1.2 times a whole cycle that decides
every food and recipe, plus 5 s). The test could not run in the build of this branch (no Postgres was available to the
builder); the operator runs it (operator steps below). Expected on staging: the admission apply, its undo and the
re-apply each decided within about 2 minutes (a whole cycle's 124.5 s at most; far less where few facts move), and the
alcohol set's alias rows within about 1 minute.

**Shuffle with a restriction (S6).** The per-type round measured a shuffle median of 0.445 s with a restriction against
0.10 s without (checkpoint 6: 0.120 s), Walnut Allergy once 1.07 s while the nutrition sweep rewrote labels. A shuffle
runs one random draw per (recipe type, meal type) pool, and a second when the meal-affine draw is short; with a
restriction each draw also evaluates the gate over every visible recipe (`ORDER BY random()` must see the whole filtered
set): the anti-join on the set's flags, the semi-join on `DietRecipeState` with, since `cleanup2/ck12-links-small`, the
anti-join on the write log inside it (`Standing`), and for a severe type the curated-only test over every line. Searches
pay this once per call, a shuffle once per pool (six to twelve times), which is why search stayed at its old median and
shuffle did not. A restricted pool is also smaller, so the second, fallback draw runs more often. Not changed here: the
cost could not be measured on this branch (no Postgres), and every cheaper form found either serves a verdict read
before the request's own snapshot (a list of held recipes read once per shuffle) or changes which recipes a shuffle
draws. `DietGateStorePostgresTests.A_whole_shuffle_with_any_one_restriction_answers_under_a_second` measures the whole
shuffle per type against one without a restriction; if it fails, the next step is one query that draws every pool's
candidates at once.

**A food of unknown contents (S15).** A food whose contents a rule marked not known (an alias of its own name with the
context `deterministic:recheck:contents-unknown`, written by the link re-check) is decided `Unknown` for every set whose
criteria do not count it; a recipe with a line on it is unknown (`UnknownFood`), held by every set that holds unknowns
(a severe type, or a member at severity 4 or 5) and shown by a diet of choice, and the food-group top-up holds the food
for such a set too. The move guard reads the mark the same way (`LineMoveDietGuard`): a move off such a food to one
that does not flag a type is a lowering move and is held, by an undo too unless the operator chose `restoreLowered`.

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

### Every writer, every process (since `cleanup2/ck11-diet-moves`)

Schema: the write log `plan."DietGateWrite"` (identity `Id`, `RecipeId`, `IngredientId`, `WrittenAt`, and since
`cleanup2/ck12-links-small` the writer's transaction `Tx`), eleven functions
in `plan` (`DietGateClearRecipes`, `DietGateClearFoods` and one per table, `DietGateStepsWritten` since the wave 21
merge), twenty-two triggers (`DietGate*Inserted`,
`*Updated`, `*Deleted`, `DietGateCriteriaWritten`, and `DietGateUnitsUpdated`/`DietGateUnitsDeleted` on
`measurement."Measurement"`) and the table `recipe."RecipeNutritionCheck"`, all additive. On staging first:

1. `./db/apply.sh --dry-run` shows exactly those: the two tables with their primary keys (the log's identity
   sequence `DietGateWrite_Id_seq`), eleven `CREATE OR REPLACE FUNCTION`, and twenty-two `CREATE TRIGGER`. Apply; a second
   dry run says nothing to do.
2. Deploy. `DietGate__SweepSeconds` defaults to 900 (was 300); leave it unset. The cycle line now says `whole cycle`
   or `changes cycle`.
3. Run a raw-SQL line writer (the order-only pass, a rebuild apply, a re-check set) while sampling
   `plan."DietRecipeState"` of the touched recipes: the states fall at the writer's commit (checkpoint 6 saw them never
   fall), the recipes leave every restricted search at once, and a `changes cycle` line follows within 15 to 35 s
   deciding them. `SELECT count(*) FROM plan."DietGateWrite"` rises at each commit and falls back to 0 after that
   cycle. Then hold a raw writer's transaction open across a `changes cycle` line and commit after it: the next poll
   runs another cycle that decides the written recipes (review CK11-DM-1). With `DietGate:Build=false` nothing reads
   the log; that process trims it every `DietGate:SweepSeconds` of the rows older than `DietGate:WriteLogKeepSeconds`
   (3600) and logs `Diet gate write log: N rows older than 3600 s removed`.
3a. Since `cleanup2/ck12-links-small` (column `plan."DietGateWrite"."Tx"`, `xid8`, default `pg_current_xact_id()`; the
   dry run shows one `ALTER TABLE ... ADD COLUMN`): commit a raw line write while a cycle is between reading and
   storing (a long whole cycle after a restart is the easiest window). The cycle line says `1 written while read left
   for the next cycle`, the recipe has no `plan."DietRecipeState"` row from that commit until the next `changes cycle`,
   and it is held for every restricted search meanwhile. Since review ck12ls-r1 (column `plan."DietRecipeState"."ReadXmin"`,
   `xid8`, and the index `IX_DietGateWrite_RecipeId`; the dry run shows one `ALTER TABLE ... ADD COLUMN` and one
   `CREATE INDEX`): open a raw writer's transaction, write a line of a recipe that has no state, let a `changes cycle`
   line pass (it decides the recipe from the older lines), then commit: the recipe leaves every restricted search at the
   commit, although its `plan."DietRecipeState"` row is still there, and the next cycle stores it again.
4. Writes of another process (`Nom.Import --admit`, the prepared-contents re-read): the same, without waiting for the
   sweep.
5. `relink/food?fromId=279327&toId=148&apply=false`: the 25 Franco-American lines held (`heldByDiet` 25, Nightshade
   Sensitivity among the types), nothing written; `relink/wordings` dry run the same. The w01 round trip: no Soy Allergy
   flag falls during the trip (139211, 139725 stay flagged). `GET proposals/290250/apply-preview`: not applied, the two
   grape-tomato lines held or Nightshade in `raisedTypes`; approving it leaves it pending.
6. The link re-check alcohol set dry run (`link-recheck/7`, signature moved): the brandy, rum and wine groups off Coffee
   Cream Liqueur and the Madeira and sherry groups off Red Wine Vinegar are no longer `diet flags kept`; the liqueurs
   (elderflower, Kahlúa, Grand Marnier) still are. "large chicken thighs" off Breaded Chicken Thighs moves.
7. After a start, watch `recipe."RecipeNutrition"`: no rewrite storm (checkpoint 6: about 40,000 inserts a minute for
   over an hour). The first sweep logs `N recipes recalculated, M unchanged by their inputs` with M large; later
   sweeps find only recipes whose inputs changed. `docker stats`: the API's memory after the build and at rest, and
   the count of `High memory usage detected` lines (at most one a minute).

### Safe only when understood (since `cleanup2/ck16-diet-known`)

1. `./db/apply.sh --dry-run`: the only delta is the body of `plan."DietGateFoodsWritten"` (it now compares
   `CurationStatusId` too). Apply.
2. The version moves (`diet-food-words/1`, `recipe-text/3`), so the first start builds every set beside the active
   build. Then `GET /api/DietAdmin/gate-counts`: per severe set `recipesUnknownWord` and `recipesByFoodWord`
   (`psql -f ops/diet-gate-count.sql`: `unknown_word`, `by_food_word`), against the build report's table.
3. Curate or rename one curated food (or undo such an edit) and read the next cycle line: it is a whole cycle, since
   the words moved.
4. Walk on the recipe page (S23 viewport) one recipe held "Ingredient not understood" and one "Named by an ingredient"
   for a severe restriction, and the same recipes for a diet of choice (shown).

### Old cookbook words (since `cleanup2/ck17-words`)

1. `./db/apply.sh --dry-run`: two nullable columns on each of `recipe."IngredientWording"` and
   `recipe."IngredientWordingRejection"` (`FdcCategoryId`, `CategoryHead`). Apply. The version moves
   (`diet-food-words/2`): the first start builds every set beside the active build.
2. `POST api/FoodCatalog/wordings/load?batch=w02&apply=false` with the batch file's text: rows, added, unchanged and no
   problem, against the build report. Then `apply=true`.
3. `POST api/FoodCatalog/relink/verified?batch=w02&apply=false&maxLines=20000`: `remaining`, `heldByDiet`, `leftBehind`
   against the build report; then `apply=true` until `remaining` is 0. Note the time `T0` before the first apply.
4. After the next whole cycle, `GET /api/DietAdmin/gate-counts`: `recipesUnknownWord` falls per severe set as the report
   says. Read one isinglass recipe for Fish Allergy (held, "Named by an ingredient") and for Peanut Allergy (shown).
5. Undo: `relink/undo?sinceUtc=T0&apply=false`, then `apply=true` (lines back), then
   `wordings/unload?batch=w02&apply=false` and `apply=true` (rows out, category rows included); one wording alone:
   `wordings/{id}/retract`.

### Made products by their brands (since `cleanup2/ck18-composites`)

No schema change. The versions move (`diet-food-words/4`, `recipe-text/5`), so the first start builds every set beside
the active build, a whole cycle.

1. To derive the consensus again (after a new FDC branded release, or after curated foods were renamed): verify the
   release's sums with `ops/fdc-releases.sha256`, then `dotnet run --project nom-api/Nom.Import --
   --branded-consensus <fdc-branded-csv-dir> --out nom-api/Nom.Orch/Contents/diet-branded-consensus.tsv [--matches
   <file>] [--tallies <file>]` against any copy of the catalog (it reads, it writes only files). Commit the file with
   the counts it prints; read 60 random matches before adopting a changed matching rule.
2. After the deploy, `GET /api/DietAdmin/gate-counts` per severe set against the build report's price table, within a
   few hundred. Walk 124035 Curried Chicken Soup for Wheat Allergy (held, "Named by an ingredient") and 97533 PROTOSE
   HASH for Peanut Allergy (held, "Ingredient not understood").

### Catalog-wide writes and unknown contents (since `cleanup2/ck16-diet-store-moves`)

The function `plan."DietGateClearFoods"` changes (no table, column or trigger): `./db/apply.sh --dry-run` shows one
`CREATE OR REPLACE FUNCTION plan."DietGateClearFoods"`; apply; a second dry run says nothing to do. On staging first:

1. With `NOM_DIET_STORE_PG` set to a throwaway `postgres:16` made from this `db/schema.sql` and `db/seed.sql`, run
   `DietGateStorePostgresTests` (all eight): the new catalog-wide test prints the whole cycle, the alias cycle (no recipe
   read) and the rename cycle (windowed, at most 1.2 times the whole cycle plus 5 s); the shuffle test prints the whole
   shuffle per type (under 1 s). Keep the output under `staging/`.
2. On staging, while sampling the touched recipes as in checkpoint 7 (`gsampler.sh`): run the admission apply, its undo
   and the re-apply, the prepared contents and the alcohol re-check set. At each commit `DietRecipeState` keeps its rows
   but `Standing` holds the recipes (`stale states served 0`); the next `changes cycle` line names `M moved a decision`
   and `C held by a food write released unread`, and its recipes are decided or released within about 2 minutes
   (checkpoint 7: 5 min 44 s to 6 min 10 s). Write down M for each write: it is the measure of which catalog writes
   change a diet fact.
3. The per-type round (`pertype.sh`) after the admission's cycle: shuffle median and slowest; compare with search.
4. The link re-check sets (dry run first): `diet flags kept` no longer appears; `unknownGroups` and
   `foodsMarkedUnknown` count the take-backs that go to a food of unknown contents (expect about checkpoint 7's 109
   holds: alcohol 63, prepared 16, fresh-herb 1, free-of 29, plus the Spanish rice and other severe flags a dish name
   used to account for). After the apply: `shaoxing wine` is off Red Wine Vinegar, the four Spanish rice lines of 40810,
   80445, 90494, 90497 are on a food of unknown contents and Soy Allergy still holds those recipes (as unknown). The
   run's undo by stamp now holds the lines on foods of unknown contents (`heldByDiet`, `marksKept`); the exact earlier
   state takes `restoreLowered=true`, whose dry run lists the moves (`restoredLowering`).
5. Before the first food write after the deploy: `SELECT count(*) FROM plan."DietRecipeState" WHERE "ReadXmin" IS NULL`.
   Such states (stored before the column existed) are not removed by a food write any more; `Standing` holds them while
   any write is logged for their recipe (review ck16-r5), so the count is for the record, not a blocker.
