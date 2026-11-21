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

## The reader (`IngredientStatementReader`, `contents-reader/2`)

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
| `Contains: milk, wheat, soy`, `Allergens: ...`, `CONTAINS MILK AND SOY INGREDIENTS` (also glued to the last period, also doubled) | declared allergens, each with its major-allergen class (`AllergenClasses`: milk, egg, fish, shellfish, tree nuts, peanut, wheat, soy, sesame) |
| `May contain peanuts`, `Made in a facility that also processes milk`, `Manufactured on shared equipment with products containing sesame`, `This product is made in a facility ...` | advisory allergens: everything after the warning's first lead ("may contain", "processes", "handles", "with", ...); a later lead or shared-facility clause in the same sentence separates allergens and never takes the place of the ones before it ("... processes peanuts, with strict allergen controls" keeps peanuts) |

**Unread** (contents unknown, never safe): empty; a placeholder ("N/A", "see package"); not text;
unbalanced brackets; an item of more than 12 words (a missing comma); a Contains line with no allergen; a
warning that names no allergen; an allergen line with no ingredient list.

The golden set is `prepared-contents.tsv` (42 rule-given rows, `ok`, `rule:construction`; see
`golden-sets.md`); its floor is every row.

On the 45,543 current US statements of FDC's whole-meal categories (December 2025 release) the reader reads
41,263 and leaves 4,280 unread (3,915 with unbalanced brackets, 326 with a run-on item, 39 empty or not
text). Only 394 carry a Contains line in FDC's field; the allergens are otherwise named in the list itself,
which the gate reads.

## Resolution (`PreparedContentsService`)

Each named ingredient is resolved by rule, as a recipe line's wording is, only to a **curated food that
is not itself a prepared food**: a verified wording that names a food (`IngredientWording`, the wording's
exact keys), then a food's exact name, then a trusted alias (`IngredientAliasTrust`). A key that names two
foods at one step resolves to neither. Otherwise it is a **recognised term**
(`IngredientStatementVocabulary.Terms`: water, salt, additives, vitamins and minerals added by name,
cultures, enzymes, flavourings, colours) or a **compound known by its listed parts** (a name of at most six
words whose parts the statement lists; longer names usually hide a missing comma). Anything else is
**unresolved**. A recognised term is judged by its text alone, exactly as a recipe line naming it would be.

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
   recognised term or compound (no food id) whose text names that head holds it. Anything restricted → held
   (`contents_restricted`, with the names).
4. The deciding statement unread → held (`contents_unread`).
5. A named ingredient unresolved, or resolved to a food no longer curated → held (`contents_unresolved`,
   with the names).
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
3. After a wording load: `--prepared-contents --reread --dry-run`, then apply.
4. In the app (own staging account, S23 Ultra viewport): give a member Celiac Disease and Lunch to prepared
   meals; shuffle: lunches are only prepared meals whose statement reads gluten-free, or recipes with the
   notice. In the plan dialog's food picker a held meal says why. Tick a held SR prepared meal on the shopping
   list, photograph its label, check the ingredient list, save: the meal is offered to that household.
