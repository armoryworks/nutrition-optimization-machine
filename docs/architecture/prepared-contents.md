# Prepared meals for restricted households: contents from an authoritative statement

`cleanup2/prepared-contents`. A household with a diet, an allergy or an avoided food gets a prepared meal
when, and only when, the meal's contents are known from an authoritative statement. Before this, such a
household got no generated prepared meal at all (`severe_restriction`, `diet_restriction`), because USDA's
SR Legacy prepared foods list no ingredients and a recipe is judged by its lines.

No model reads a statement, resolves an ingredient or decides a meal. Nothing here writes a nutrient value:
statements are evidence, and the branded lift still decides whether a branded meal's numbers are believed.

## Sources

| Source | `IngredientContents.Source` | Who may rely on it | How it arrives |
|---|---|---|---|
| The ingredient statement USDA FDC publishes for a branded food (`branded_food.csv`, column `ingredients`, CC0) | `fdc:{fdcId}` (`HouseholdId` NULL) | every household | `Nom.Import --import-fdc-branded` for new branded meals; `Nom.Import --prepared-contents <csv-dir>` for branded meals already in the catalog |
| A household-verified label's statement | `label:user:{personId}` (`HouseholdId` = the household, `VerificationId` = the label row) | that household only | the existing label path: the photo's text gives a draft (`IngredientStatementReader.Extract`, stored on `IngredientVerification.IngredientStatement`), the member checks it on the confirm sheet (`label-confirm-ingredients-input`) and `ConfirmAsync` reads it for a prepared food |

The label evidence stays evidence: it is never written to `nutrient.IngredientNutrient` or to a proposal
(`CLAUDE.md`, *Shopping List Verification*). Open Food Facts is not a source (ODbL, signal only).

SR Legacy prepared foods (Fast Foods, Meals/Entrees/Side Dishes, Restaurant Foods) have no FDC statement;
for them the household's label is the only source.

### Branded prepared meals

A branded food whose stored FDC branded category is one of the whole-meal categories is a prepared meal
(`PreparedFoods.BrandedMealCategories`, matched exactly as FDC spells them: Frozen Dinners & Entrees,
Ready-Made Combination Meals, Entrees, Sides & Small Meals, Prepared Subs & Sandwiches, Prepared Wraps
and Burittos, Sandwiches/Filled Rolls/Wraps, Frozen Breakfast Sandwiches, Biscuits & Meals, Breakfast
Sandwiches, Biscuits & Meals). It is classified from its category, never its name, and is a prepared food
everywhere (`PreparedFoods.IsPrepared`, `NotPrepared`, `Prepared`, `Meals`): never a recipe line's food,
never a food-group top-up. It is drawn by shuffle only once it is **Curated**, and a branded food is
curated only by the branded lift (two or more agreeing sources for its numbers). A statement is no
source for numbers, so this item adds no way around the lift.

## The reader (`IngredientStatementReader`, `contents-reader/3`)

Deterministic, pure, in `Nom.Orch/Services/Support`. It gives the named ingredients (with their parent
when they are sub-ingredients), the allergens the regulated "Contains:" line declares, and the allergens a
"may contain" or shared-facility line warns of.

| Text | Reading |
|---|---|
| `Ingredients:` label, closing period, `*` footnote markers, "organic", percentages | dropped |
| `( )`, `[ ]`, `{ }` | the parts of the ingredient before them, nested to any depth; the three kinds are equivalent |
| `Sauce: water, ...`, `Bread - water, ...` | a heading holding the items after it, up to the next heading or sentence; a heading followed at once by another heading is the product's title and names nothing |
| `contains 2% or less of:`, `less than 2% of`, `cured with:`, `may contain one or more of the following:` | markers, not ingredients and not allergen lines |
| `canola and/or soybean oil`, `palm or soybean oil`, `(corn, canola, and/or soybean)` | every alternative is named; a single-word alternative takes the last alternative's head word |
| `(a preservative)`, `(vitamin B1)`, `(for color)`, `added to prevent caking`, "no nitrites added except ..." | what an ingredient is for or a claim: names nothing |
| `PRE-COOKED CORN FLOUR - 100% GLUTEN FREE`, `Rice, gluten free, salt`, `Beef, non-GMO` | a claim ("gluten free", "all natural", "non-GMO", "kosher", "vegan", "no preservatives" and the like) names nothing, alone or after a spaced dash; so the name before the dash is an ingredient, not a heading (staging checkpoint 3, N4: `contents-reader/2` read "pre-cooked corn flour" as a heading and kept "gluten free" as the one item) |
| `Contains: milk, wheat, soy`, `Allergens: ...`, `CONTAINS MILK AND SOY INGREDIENTS` (also glued to the last period, also doubled) | declared allergens, each with its major-allergen class (`AllergenClasses`: milk, egg, fish, shellfish, tree nuts, peanut, wheat, soy, sesame) |
| `May contain peanuts`, `Made in a facility that also processes milk`, `Manufactured on shared equipment with products containing sesame`, `This product is made in a facility ...` | advisory allergens: everything after the warning's first lead ("may contain", "processes", "handles", "with", ...); a later lead or shared-facility clause in the same sentence separates allergens and never takes the place of the ones before it ("... processes peanuts, with strict allergen controls" keeps peanuts) |

**Unread** (contents unknown, never safe): empty; a placeholder ("N/A", "see package"); not text;
unbalanced brackets; an item of more than 12 words (a missing comma); a Contains line with no allergen; a
warning that names no allergen; an allergen line with no ingredient list.

The golden set is `prepared-contents.tsv` (47 rule-given rows, `ok`, `rule:construction`; see
`golden-sets.md`); its floor is every row.

On the 45,543 current US statements of FDC's whole-meal categories (December 2025 release) the reader reads
41,263 and leaves 4,280 unread (3,915 with unbalanced brackets, 326 with a run-on item, 39 empty or not
text). Only 394 carry a Contains line in FDC's field; the allergens are otherwise named in the list itself,
which the gate reads.

## Resolution (`PreparedContentsService`)

Each named ingredient is resolved by rule, as a recipe line's wording is, only to a **curated food that
is not itself a prepared food**: a verified wording that names a food (`IngredientWording`, the wording's
exact keys), then a food's exact name, then a trusted alias (`IngredientAliasTrust`). A key that names two
foods at one step resolves to neither. Otherwise it is a **term of label language**
(`StatementLabelTerms`, below), a **recognised term** (`IngredientStatementVocabulary.Terms`: water, salt,
additives, vitamins and minerals added by name, cultures, enzymes, flavourings, colours) or a **compound
known by its listed parts** (a name of at most six words whose parts the statement lists; longer names usually
hide a missing comma). Anything else is **unresolved**. A recognised term is judged by its text alone, exactly
as a recipe line naming it would be.

### Label language (`StatementLabelTerms`, `label-terms/2`; staging checkpoint 3, N5; checkpoint 4, P4)

**Label terms first (`label-terms/2`, staging checkpoint 4, P4).** A name the cited table reads is a label item
before it is a catalog food: `PreparedContentsService.Resolve` reads the label table first and keeps the catalog
food the name also resolves to on the item (`FoodId`), so the gate reads both what the term contains or may
contain and whatever the catalog food is restricted by. At checkpoint 4 a bare "vegetable oil" inside breadcrumbs
or a seasoning resolved to the catalog food Vegetable Oil first, and its "may be peanut, source not stated" was
never read, so meals 283237 and 283938 were offered to Peanut Allergy; the owner-reported decision holds unstated
oil sources on the safe side. A name the table does not read (a plain vocabulary term or a catalog-only food)
resolves as before, and a catalog-only food that is no longer curated still makes the meal unknown. The table
gained the composites of `diet-gate.md` (checkpoint 4 section); a stored row of `label-terms/1` is read again by
`--prepared-contents --reread`.

At checkpoint 3 none of the 96 read FDC statements on staging was fully known: the plain names a label uses
("wheat", "soybeans", "cream", "pasteurized milk", "enriched flour", "soybean oil", "basmati rice", "chick
peas", "potato starch", "pre-cooked barley") matched no exact key, so no household judged by contents could
ever be offered a branded meal. A name is now read as label language, by rule and with no model, against a
cited table (`nom-api/Nom.Orch/Contents/label-terms.tsv`, embedded): each term with its kind, its other
names (spellings, INS and E numbers), what it **contains** (words the restriction patterns read; a
major-allergen word is read with every word of its class) and what it **may** contain when the label does
not say, and its source:

| Kind | What it covers | Facts |
|---|---|---|
| food | single foods and the regulated common names (21 CFR standards of identity) | "enriched flour", "semolina", "farina", "spelt" contain wheat (21 CFR 137); "cream", "butter", "whey", "casein", "lactose", every standardized cheese contain milk (21 CFR 131, 133); "Litopenaeus vannamei" is shrimp; "vegetable oil" may be soybean, peanut or corn (21 CFR 101.4(b)(14); a highly refined oil is exempt from allergen labelling) |
| additive | functional additives with their INS / E numbers | free of the major allergens unless the table says: lysozyme (E1105) contains egg; a bare "lecithin" (E322) may be soy or egg; "mono and diglycerides" (E471) of unstated source may be animal fat (read as lard), "vegetable mono and diglycerides" may not; koji may be rice, barley or soybean; "rennet" and a bare "enzymes" (in a cheese, possibly animal rennet) may be veal, "animal rennet" contains veal, "microbial rennet" nothing; L-cysteine (E920) may be poultry or pork (feathers, hog bristles) |
| flavour | "natural flavor", "artificial flavor", "spices", "smoke flavor" | none: FALCPA (403(w)) makes a major allergen in a flavouring or spice declared by its source name, so they are judged by their own words, as `prepared-contents` already left them; "butter flavor" is read by its word. A bare "seasoning" is no spice (21 CFR 101.22(a)(2)): its parts that are not major allergens (barley malt, meat extracts) need not be named, so it is no term and stays unknown unless its parts are listed |
| composite | prepared foods whose name hides what they are made of ("goldfish crackers", "licorice", "Worcestershire sauce", "imitation crab", "pesto", "mayo", "satay", "groundnut") | what they are made of, from a standard of identity or the leading brand's label, and what they may be made of (miso may be barley or rice, the grain of its koji); the same rows give restriction types companion patterns (`diet-gate.md`, composite foods) |

The rule: a phrase that only says how or with what an ingredient is used names that ingredient ("fried in
vegetable oil", "cured with water", "including paprika", "contains wheat", "enriched with niacin", "vitamin d3
added"); a list joined by "and", "with" or "including" is known when every part is, and a part that is only a
qualifier ("liquid and hydrogenated soybean oil") or lacks its head ("arabic and guar gum", "vitamin a and
d3") takes it from its neighbour; qualifiers that change nothing a restriction reads are left out from the
front ("pasteurized", "expeller pressed", "pre-cooked", a colour, a rice variety) and forms from the end
("powder", "puree", "concentrate", "starch", "oil", "meat"); a word that may hide an ingredient ("breaded",
"imitation", "vegan", "seasoning blend", "seasoned", "sweetened") is never left out, so such a name stays unknown,
except that "seasoned" and "sweetened" are left out of a name whose parts the statement lists ("grilled seasoned
chicken breast (chicken breast, salt, spices)"), since the parts then name what they hide; a plural is read as
its singular and "E 471" or "INS 471" as `e471`. The item is stored as `label` with the terms it was read as;
the gate reads its own words as before and beside them the terms' facts. A term the table no longer holds
makes the item unknown. Golden set: `label-terms.tsv` (60 rule rows).

Measured on the 100 FDC statements of the branded whole meals staging held at checkpoint 3 (the selection
`--import-fdc-branded --limit 5000` makes from the December 2025 file, which staging held 99 of;
`nom-api/Nom.Api.Tests/Fixtures/fdc-meal-statements.tsv`), **with no catalog at all**: 97 read, **82 fully
known (84.5 %)**, against 0 of 96 before. The 15 left unknown are honest: missing commas ("salt caramel
color", "yeast extract granulated garlic"), typos ("xathan gum", "postassium chloride"), unnamed blends
("vegetable broth", "grilled ... vegetable blend"), "breaded chicken white meat chunks", "mini bun" and
reader fragments ("prevent caking"). For each default restriction type, the meals the gate can decide either
way (offered, or held because the contents name what it restricts) out of 100: most allergies 82 to 86
(for example Fish 83, Egg 85, Sesame 84, Milk 90, Soy 91, Wheat 95, Celiac Disease 95), diets 80 to 97
(Vegetarian 82, Vegan 94, Halal 82, Kosher 81; Vegetarian, Hindu Vegetarian and Pescatarian leave cheese meals
whose "enzymes" or "rennet" may be animal rennet unknown); the lowest are Peanut Allergy 67 and Nut Allergy 68,
because "vegetable oil" may be peanut oil and a label need not say. Unknown still keeps a meal out. The
figures are printed by `GoldenLabelTermsTests`, which also fails when a statement naming milk, gluten grains,
peanut, soy, egg or shellfish is offered to the matching allergy.

A row stores the statement, the outcome and reason, the reader version, each ingredient with how it was
known (`food` with its id, `term`, `compound`, `unresolved`), the allergens, the count of unresolved names
and a batch stamp. A newer row of the same food and household supersedes an older one; a pass writes a row
only when the source, text or reader version changed; `--prepared-contents --undo <batch>` takes a pass's
rows out, which brings the superseded rows back; `--prepared-contents --reread` reads every current row
again with the current reader, vocabulary and catalog (a wording load can resolve more names). A reread row
records the row it read again (`ReadFromId`), so undoing a pass also takes out every reread row that came from
one of its rows, however many rereads followed; undoing a reread brings back the rows it read again.
`--import-fdc-branded` prints the batch of the statements it stored (`Statement batch:`), which
`--prepared-contents --undo` takes back. `--purge-fdc` soft-deletes foods and leaves their statement rows; a
deleted food is never drawn, offered in the picker or counted on the Pipeline page, so those rows decide nothing.

## The gate (`PreparedContentsGate`)

A household whose restrictions are judged by contents (`RestrictedIngredientSet.ReadsContents`) or that
has a severe restriction (severity ≥ 4) has each prepared meal judged exactly as a recipe is judged by its
lines:

1. The meal itself in the restricted set (its own name, category or nutrients) holds it (`restricted_food`).
   While the diet gate's warm has not yet computed a food probe the restrictions need (`RestrictedIngredientSet.Pending`,
   `cleanup2/ck9-diet-speed`, see `diet-gate.md`) the foods are not all known, and every meal is held (`diet_gate_pending`).
2. The statement that decides is the household's own latest confirmed label, else the latest FDC statement.
   None → held (`contents_none`).
3. In either statement that was read, each named ingredient is a line: held when the curated food it
   resolved to is in the restricted set, when its text names a pattern criterion
   (`DietCriteriaMatcher.LineNames`), or, for a restriction with an alcohol criterion, when it reads as a
   drink (no amount is given, so the alcohol that stays cannot be counted; a wine vinegar is no drink).
   Each declared allergen is read with every word of its class against every pattern; each advisory
   allergen the same way against the criteria of severity 4 and 5 only (allergies and medical
   restrictions; a vegetarian diet is not held by a shared facility). A food the restrictions name directly
   (a restriction's own food, or a type's named-food criterion: `RestrictedIngredientSet.NamedFoods`) is read
   against the allergens too: a declared allergen, or at severity 4 and 5 an advisory one, holds the meal when
   its major-allergen class is one the food's name head names (the name before its first comma: "Peanuts, all
   types, raw" is peanut, "Nuts, cashew nuts, raw" is tree nuts) or when its own words name that head; and a
   recognised term or compound (no food id) whose text names that head holds it. A label term holds the meal
   by what it contains as by its own words ("enriched flour (wheat)", "sweet dairy whey (milk)"). Anything
   restricted → held (`contents_restricted`, with the names).
4. The deciding statement unread → held (`contents_unread`).
5. A named ingredient unresolved, resolved to a food no longer curated, or read as a label term whose possible
   source a restriction names and the label does not settle ("lecithin (may be soy, source not stated)") →
   held (`contents_unresolved`, with the names).
6. Otherwise offered.

A household with no such restriction is not judged by contents, as before.

## Where it is used

| Path | What it does |
|---|---|
| Shuffle (`LoadPreparedPoolsAsync`) | For a household judged by contents, the pool of each meal given to prepared meals is the curated prepared meals with a read statement that the gate offers, drawn at random three per cell and scored like recipes. A meal with none falls back to recipes with `severe_restriction` or `diet_restriction` as before. |
| Hand-add picker (`GET /api/MealPlan/prepared-check?householdId=&ingredientIds=`) | The plan dialog's food picker checks every prepared result and shows why one is held (`whole-food-result-held`). For a household with a severe restriction (severity 4 or 5, the severity the gate treats as severe) a held meal is marked `severe`, and adding it asks for an explicit "Add anyway" in a dialog that names the reason (`prepared-held-dialog`, `prepared-held-message`, `prepared-held-cancel-btn`, `prepared-held-add-btn`; Cancel is the default); a meal whose check has not come back is checked before it is added, and when that check fails nothing is added and the picker says so (`whole-food-error`). The server holds the same line: `POST /api/MealPlan` refuses a prepared meal it holds for a household with a severe restriction with 409 `restriction_violation:prepared_held_unconfirmed` unless the request carries `confirmHeld`, which the picker sends only after "Add anyway". For a household with only diets the reason is shown and adding asks nothing (lead decision on the owner question of `cleanup2/prepared-contents`). |
| Shopping list (`NeedsVerificationAsync`) | Ticking a prepared meal whose contents the household needs but no statement shows (none, unread or unresolved) asks for its label (`askContents`), so the household's own label can make it known. A meal a statement already shows to be restricted is not asked about. |
| Pipeline page (`pipeline-prepared-contents`) | Curated prepared meals by their current FDC statement, the one every household may rely on: read with every ingredient known, read with one unresolved, unreadable, no statement; how many have an FDC statement and, apart, how many a household label (a label serves only its household and moves no meal between the other counts). |

## Operator steps

1. `./db/apply.sh --dry-run`: new table `recipe."IngredientContents"` (with `ReadFromId` and its index) and one nullable `text` column
   `recipe."IngredientVerification"."IngredientStatement"`. Apply.
2. After the branded import and the branded lift have run on the target:
   `dotnet run --project nom-api/Nom.Import -- --prepared-contents <fdc-branded-csv-dir> --dry-run`, read the
   counts, the unread reasons and the most frequent unresolved names, then run it without `--dry-run` and keep
   the printed batch. `--prepared-contents --undo <batch>` reverses it.
3. After a wording load, and once after a release that raises the reader or label-terms version (stored as
   `ReaderVersion`, now `contents-reader/3 label-terms/2`): `--prepared-contents --reread --dry-run`, then
   apply. `--prepared-contents <dir> --all-branded` also stores the statements of every branded food in the
   catalog (not only the whole meals), so a recipe line linked to a branded food carries what its label names
   (`diet-gate.md`).
4. In the app (own staging account, S23 Ultra viewport): give a member Celiac Disease and Lunch to prepared
   meals; shuffle: lunches are only prepared meals whose statement reads gluten-free, or recipes with the
   notice. In the plan dialog's food picker a held meal says why. Tick a held SR prepared meal on the shopping
   list, photograph its label, check the ingredient list, save: the meal is offered to that household.
