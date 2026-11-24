# Recipe reprocess: rebuilding lines from the stored source

A recipe's lines are built once, by the import. Every rule fix since (the parser, the name
normalizer, the drained-can rule, the heading pre-pass, the method fill) reached only new imports,
plus whatever a separate journaled repair patched in place. The reprocess builds a recipe's lines
again from the source it was imported from, **in place**: the recipe keeps its Id, so meal plans,
images, prose rewrites and curation stay where they are. Nothing is fetched from any site.

Code: `Nom.Orch/Services/Support/RecipeLineBuilder.cs` (shared with the import),
`Nom.Orch/Services/RecipeReprocessService.cs`, `Nom.Api/Controllers/RecipeReprocessController.cs`,
journal table `recipe."RecipeReprocessJournal"`; the lane: `ReprocessDetectorService`,
`ReprocessLaneService`, `Support/ReprocessRuleAudit.cs`, `OllamaLineJudge.cs`,
`Nom.Api/Services/ReprocessHostedService.cs`, table `recipe."ReprocessCandidate"`; the
re-extraction: `ops/cookbook-extract.py`. Tests: `RecipeReprocessTests`, `ReprocessLaneTests`,
`ReprocessLaneRebuildTests`, golden `recipes-rebuild.tsv`.

## One line builder for the import and the rebuild

`RecipeLineBuilder` is the line half of the import (`RecipeScrapingService`), split out so a rule
fix reaches both:

1. **Amount.** NOM's own parser (`IngredientLineParser`) reads the line's text. The scraper's
   reading is used only when NOM's parser reads nothing. A can or jar of a food used drained
   counts cans or jars. An amount whose measurement has no row is stored as no amount.
2. **Food.** The import's order: the verified wording (when `VerifiedWordings:ResolveOnImport` is
   set), a live ingredient of the normalized name, the one food a trusted alias gives the name to,
   the one curated food the wording names with a hyphen for a space or a space for a hyphen ("sun
   dried tomatoes" is Sun-Dried Tomatoes) where those find none or an uncurated one, the food an applied
   link merged the retired ingredient of that name into, the curated food the first alternative of an
   "or" line names where nothing carries the whole wording, else a new uncurated ingredient. The first
   alternative is read with the words the alternatives share: when the last alternative has words after
   its first and the first is not closed off by a comma or by an amount opening the next alternative,
   "lemon or lime juice" is lemon juice (curated Lemon Juice, or no food), never the fruit Lemon, and
   "chicken or vegetable broth" is chicken broth, never Chicken; "lemon or lime" is Lemon, and "1 teaspn.
   ground coriander, or ¼ level teaspn. ground anise seed, ..." is ground coriander. A wording, an alias or an applied link never leads to a prepared food. A line whose
   text states only an amount and names no food (`IngredientNameNormalizer.NamesOnlyAnAmount`: a
   number alone, "Grammi circa 100", "Grammi 80", "2 cups"; a count word that can be a food, such as
   "6 cloves", is not read so) keeps the food the rebuild finds it on, or a food already under the
   line's own text ("grammi circa 100"); this holds for a count line such as "N. 6" (six yolks) too,
   which keeps the food it is on. Otherwise it is not built: the rebuild counts it in `counts.dropped`
   (a stored row whose line is not built is dropped, as a row with no source line is, and a source line
   that is not built counts even when no stored row is paired with it, so the detector's structure reason
   and the order-only guard see it) and the import logs it. No food is ever created under an amount, by the import or by a rebuild. Two bare numbers
   joined by "or" ("two or three nutmegs", "five or six quarts") are a range, read by its lower end as
   the parser reads any range, never an alternative. In an "or" line whose food is
   named only in a later alternative that states its own amount, that amount is the line's ("pinch
   stevia OR 2 tbsp maple syrup" on maple syrup is 2 tbsp). A clove counts garlic or shallot: on the
   spice cloves ("a couple of cloves") the count is in pieces.
3. **Kind.** A line with no amount gets the kind the deterministic pre-pass reads on its face
   (`RecipeLineClassifier`: headings and yield notes are `note`, utensils `equipment`, lines with
   no letters `artefact`).
4. **Method fill (rebuild only).** A line with no amount that is the only line of its food and
   names that food takes the amount the method states next to it (`MethodAmountFinder`), as the
   start-up repair does, with the same checks: no amount from a ratio, a vessel, a length or
   washing water, and no piece count of a liquid or of a food with no weight per item. A ratio anywhere
   in a sentence ("To every quart allow ..., half a teaspoonful of mace", "per cup of vegetable", "on
   each plate", "to a pound of pulp") leaves every amount of that sentence alone; a range ("six or
   eight pounds") gives its lower end; a line naming its food with an "or" ("stewed or canned
   tomatoes") takes the amount the method states before those same words. The import leaves this to the start-up repair, which journals each fill
   (`StartupFill`, undone by `startup-fills/undo`).

`RecipeLineBuilder.Version` (`line-builder/2` since the parser rules R1-R9 of
`cleanup2/parser-rules`; `line-builder/3` since the checkpoint-1 fixes of `cleanup2/ck1-parser-catalog`:
hyphenated mixed numbers after the name, a measure "and a half", trailing size words in the name,
and the method fill's own-word, teacupful, per-item ratio, temperature, measure-as-pieces,
alternative and fold rules; `line-builder/4` since the second checkpoint-1 round of
`cleanup2/ck2-undo-reprocess`: "circa" before an Italian amount, no food named by a number, the
alternative that names the food, the spice cloves, the classify lane's kinds kept on unchanged text; `line-builder/5` since its review: a line that
states only an amount is never a new food, not even under its own text; `line-builder/6` since the wave-6
merge, which joins those rules with `cleanup2/container-weighing`'s, a jar read as Jar and a bottle as
Bottle instead of Package, raised on that branch from the same base as `line-builder/4`;
`line-builder/7` since `cleanup2/parser-size-first`: a size before one container with no count of
its own, "1 lb. can tomatoes", "1 quart can", "½ pint can", "15-ounce can", "1 lb. 13 oz. can", is one
container of that size, so a drained such line counts one can and the line's stated size is
weighed instead of an assumed one; "2 lb. cans" does not say how many and still reads 2 pounds;
`line-builder/8` since the checkpoint-2 round of `cleanup2/ck4-parser-method`: two bare numbers joined
by "or" are a range and never an alternative (M18), an amount after the name may be followed by a
description ("Egg white, 1, partly beaten.", M20), two measures joined by "and" are added up ("one
quart and a pint" is 6 cups), the method fill's ratio is read per sentence, its range by the lower end
and an "or" line by its own words (M4, M5, M19), a count line naming no food keeps its food and a line
left out is counted as dropped, the curated food spelt with a hyphen or named by an "or" line's first
alternative is chosen over a new junk food, and "d'" and "spicchio" are taken off an Italian name (M17);
`line-builder/9` since the checkpoint-3 round of `cleanup2/ck6-parser-rebuild`: a cross-reference to
another recipe of the book ("descritto al N. 579") is no count and leaves the name, "- 6 numero" counts
six, a bare "tablespoonful" before the food is one, a second size after "/" ("8 ounces / 1/2 pound") is
not part of the name, the method fill reads "to two pounds of", "to one of" and "for half a pint" as
proportions, reads a range whatever number of the food's words follow it ("make 4 or 5 cups of cold
boiled sweet potatoes" is 4 cups), counts pieces from "ten or a dozen" (a dozen is a count, not a
measure) and takes nothing a liquid dissolves, softens or steeps another ingredient in, the curated food
spelt with a hyphen inside a word ("Semi-Sweet") is found from the joined word, and a first alternative of
more than one word is read alone when the shared words name no food (N1, N14, N16);
`line-builder/10` since the checkpoint-4 round of `cleanup2/ck9-amounts`: every line built from a stored
row passes the line guard below, a sentence the method fill reads as a proportion still counts as an
amount of the food, so a part amount of another sentence no longer fills the line ("To six pounds of
peaches allow two pounds of sugar" beside a syrup's three-quarters of a pound), "add to two quarts of"
and "seven to eight cups of" are amounts and not proportions, a per-pound sentence goes on past ";" and
past the point of "lb.", "for thirty pounds" is a proportion, a utensil line ("tin plate") and the
vessel a food is put into ("Put into a tumbler ... syrup") give nothing, a French cross-reference ("comme
celui du N. 579") and a price ("twenty cents' worth") are no amount, "one-half ounce" is half an ounce and
"cup of the soup stock" is one cup (P2, P8, P11);
`line-builder/11` since the checkpoint-5 round of `cleanup2/ck10-amounts`: the line guard below as revised
for Q12 and Q1 (an empty rebuilt row never replaces a stored amount, amounts are read in the line's own
source text, a count of a measure and the first part of "a pound and a half" give way, a reference or
equipment line takes the amount its source states) and every held line stored as a hold;
`line-builder/12` since the checkpoint-6 round of `cleanup2/ck11-lines`: an ordinal fraction before a measure is
read whole in the method fill ("one-sixth of a pint" is a third of a cup, never a pint: R1, 133802), one it
cannot join to its measure ("the sixth of a pint", "a sixth of pepper") gives nothing, the parser reads sevenths,
ninths, tenths, twelfths and sixteenths, a source entry with no letter or digit ("&nbsp;") is no line, a new
food's name drops a metric weight glued to its number ("115g mozzarella" is "mozzarella"), and the guard below
takes the amounts R2 found held although the method proves them);
`line-builder/13` since the staging read of `cleanup2/ck12-lines-proof` (no new rule in the builder itself; the
parser, the method finder and the guard it calls changed): the parser reads a spelt fraction with its own number
("essence violets, one-quarter ounce" is a quarter ounce, not one and a quarter; "one pound and a quarter", "a pint
and a third", "an eighth of a cupful", "half a quarter of a pint", "a third of a yeast-cake"), a half-pint added to
quarts as one more cup, and gives no amount for a spelt fraction of another amount ("a third as much soda", "one
fourth of the flour", "two-thirds Java coffee"), a peck or bushel, a vessel filled in part ("a teacup two thirds
full"), a size under an inch and a recipe number ("forcemeat No. 1", "sirup No. 3 or 4"); the method finder gives none
for a line that names what its food is for ("oil for frying"), an oven or fat temperature ("to 350C"), one piece
named in the singular for a plural line ("on a corn tortilla") or a fraction it cannot read; and the guard reads
under `reprocess-audit/9` (below)
is stamped on every journal row. Raise it with any
change to a rule the builder applies: the detector reads again every recipe an older version read,
and the lane's signature changes, so its tiers go back to shadow until they qualify under the new
rules.

## Line guard

The rebuild can never make a line's amount worse by rule (`LineAmountGuard`, no model, since staging
checkpoint 4, P11; revised at checkpoint 5 for Q12 and Q1). Every amount is read against the line's
**own source text**: the line alone when the line states an amount (the parser reads one, or it opens
with a number), otherwise the line and the method sentences that name the line's food or its own
wording. Each line built from a stored row is compared with that row, and the verdict carries a reason
(`LineAmountGuard.Reasons`):

- **same** (`same`): the amount is unchanged (two empty amounts are the same whatever their unit);
- **kept** (`stored-grounded`): the stored amount stands in the line's own source text
  (`ReprocessRuleAudit.GroundedIn`) and the rebuilt row has an amount that text does not ground, or none
  while the line itself grounds the stored amount: the stored amount and kind stay;
- **replaced**: the rebuilt amount stands in the line's own source text with a unit A3 passes, and the
  stored row contradicts that text: it is empty on a line of a kind that takes an amount (`fill`: an
  ingredient, a line of no kind, a `reference` such as a sauce another recipe makes, or `equipment` such
  as boiling water; since Q1 a reference or equipment line the method states an amount for takes it); it is
  not grounded there (`stored-ungrounded`); its unit fails A3 ("1 teaspn. salt" stored as a piece, `unit`);
  it is a count in pieces of a measure that text names for the food ("a large tablespoon of butter", "two
  large spoonfuls of butter", "1 tbp chopped pickle", "one pony of maraschino" stored as pieces,
  `count-of-measure`), the rebuilt amount being that measure as the line parser reads the phrase, and the
  text not also stating the count as a count of the food ("Beat 2 eggs and brush with 2 tablespoons of the
  beaten egg" holds the two eggs); or it is the first part of an amount that text states in the same measure ("a pound
  and a half of grated bread", "two pounds and a half of butter", "1-1/4 cups", `added-part`);
- **held**: every other difference keeps the stored amount and kind and is marked `held` in the line's
  `changes`, with the rebuilt amount in `heldQuantity`, `heldMeasurementId`, `heldUnit`, `heldLineKind` and
  `heldFromMethod` and the reason in `guardReason`: `rebuilt-empty` (since Q12 a rebuilt row with no amount
  never replaces a stored row that has one, whatever the grounding says, and the stored one is not grounded),
  `rebuilt-empty-method` (as before, the stored amount grounded only by a method sentence naming the line's
  food or wording, whose number may be another food's: "yellow wax half a pound, Sack six spoonfuls" grounds
  half a pound of Sack; held so the line judge sees it), `rebuilt-ungrounded` (the rebuilt amount does not stand in the line's own source text),
  `kind` (an empty note, instruction or artefact line: headings and method text take no amount; since
  `line-builder/12` such a line that is no heading, yield or utensil on its face takes a measure a method
  sentence states once, right before its own words, read the same again by the guard: `method-measure`, a
  replace, as "one and one-half cups of boiling water" on a boiling-water line the line lane filed as a note;
  since `line-builder/13` a note or instruction that is no fold of lines also takes the measure its own line
  states of the food it names: `line-measure`, a replace, as "White-wine vinegar 3½ quarts" filed as an
  instruction; an artefact never; a count of pieces never does), and
  `both-grounded` (both stand and differ, as "four or five oranges" or "two pounds or two quarts").

The run counts them per recipe and in total, for rebuilt and for unchanged recipes alike (`guardKept`,
`guardReplaced`, `guardHeld`, `guardHeldByReason`), and each changed line carries its verdict in `guard`
and its reason in `guardReason`. An order-only result whose lines change only order or are held stays in the
order tier. The start-up fill uses the same guard and lets an amount stand only where the guard would
replace: it never empties a stored amount ("two of sugar" stays two pieces), and its done line counts
`guardKept`, `guardReplaced` and `guardHeld` with the held ones by reason.

**Holds are stored** (`recipe."LineGuardHold"`, since `line-builder/11`): each held line with the stored row
(food, amount, unit, kind), the rebuilt row (food, amount, unit, kind, whether read from the method), the
source line, the reason, the rule version and the writer (`rebuild` on an apply, including an apply that
changes nothing because every difference is held; `startup-fill` on each start). There is one open hold per
line and writer: a writer that reads the line again updates it, and resolves it (`released`) when it no longer
holds the line. A line is the same line by its food and its source text wherever it stands (since
`cleanup2/ck11-lines`, staging checkpoint 6 R22: after the order-only pass the next start released 152
start-up fill holds and made 152 new rows for the same lines): its hold moves with it to its new position,
unless another open hold of the writer stands there or another held line takes its old place, and only then
is a line matched by position alone; a rebuild apply resolves the start-up fill's open holds of its recipe (`rebuilt`). A hold
writes nothing to the recipe. A dry run stores none. An undo of a rebuild resolves that recipe's open
rebuild holds (`undone`) in the undo's transaction; the next start-up fill reads the recipe again and records
its own. A worker that takes a hold still first checks that the row carries the stored amount. `ResolvedAt` and `Resolution` are for
the line judge or a later rule that works the queue.

```
GET /api/RecipeReprocess/line-holds                                                   # open holds by reason, in all and per writer; resolved by resolution
GET /api/RecipeReprocess/line-holds/list?reason=<r>&origin=<o>&afterId=0&take=100     # the holds themselves, in id order (resolved=true for worked ones)
```

Measured at the checkpoint-4 round: on the held-out half of `line-holdout.tsv` (90 scored lines, the
amount the checkpoint build left as the stored one and the current reading as the rebuilt one) the guard
gives 78 same, 4 kept, 6 replaced and 2 held, no line worse than the build left it and 6 better
(`line-holdout.json`, `guard`). Replayed on the 80 rebuilt recipes the checkpoint-4 operator read, from the
log's lines and the step sentences it quotes: of 103 lines with a stored row, 20 same, 11 kept, 46
replaced and 26 held; the 2 wrong amounts end as before (one held, one unchanged), 7 of the 8 emptied
stated amounts are kept and the eighth was wrong before too; no replaced line is one the reading judged
right before.

Measured at the checkpoint-6 round (`line-builder/12`, `reprocess-audit/8`, R1 and R2): the 197 start-up fill
holds of checkpoint 6 (`ck6/holds.csv`, with the full method; the 22 `both-grounded` ones not replayed, their stored
amount not exported) replayed against `/11`: of 175, **40 held lines are now filled, every one right** by a reading
of its sentence (10 of them among the 32 holds the checkpoint read, 2 among its Q1 re-test (78123, 82513), with the operator's one doubtful one, "Half a
Pint of Pippin Jelly", read as right), the other 135 held as before. The 90 start-up fills the checkpoint judged: only
133802 changes, from 2 cups to a third of a cup. The 133 changed lines of the 80 rebuilt recipes the checkpoint
read: no verdict changes (one hold changes its reason, `rebuilt-empty` to `rebuilt-empty-method`). A reading
scan of every line of a staging copy (847,401 lines; the line parser, then the method finder where the start-up
fill would ask it, old and new side by side) changes 190 readings and none wrongly: 39 new readings, every one an
ordinal fraction of a measure read whole ("one-sixth pint of white wine", "one-fifth pint of brandy", "four-fifths
ounce"); 22 changed (18 lines the parser now reads, "one-tenth ounce cassia" a tenth of an ounce and no longer one
piece, and 4 method readings corrected: three wrong pints, 133802 among them, and "two-fifths of a tablespoonful");
129 withdrawn, all wrong or not one amount: 126 counts of pieces that were proportions ("two-thirds potatoes and
one-third fish", "three-quarters full of shaved ice", "a quarter one of pepper", "quarter the apples"), "the fourth
of a pound of brown sugar" read as a pound, and two lines that took one of two stated amounts (two of the 126,
"juice of a quarter of lemon" and "the halves of an English walnut", were near right and are now left to the line
judge). The list is in the `cleanup2/ck11-lines` build report.

Measured at the checkpoint-5 round (`line-builder/11`): on the held-out half of `line-holdout.tsv` the guard
gives 78 same, 0 kept, 5 replaced and 7 held, none worse, 5 better and **0 replaced wrong**
(`line-holdout.json`, `guard`; `/10` gave 4 kept, 6 replaced, 2 held, 6 better: one right emptying is now held
by the Q12 rule, and the four kept against an empty rebuild are held as `rebuilt-empty-method`). Replayed on the 78 recipes the checkpoint-5 operator read (`ck5-op2-rebuild-read78`, every line with
the method), the 49 lines whose stored and rebuilt amounts the log records: **39 replaced, 0 kept, 10 held, 0
replaced wrong** (one doubtful, as before: "1 3 or 4 lb. chicken" at the upper end of the range), against 29
replaced and 20 held at `/10`. The two wrong replaces of Q12 (88262 curry powder, 99026 sweetbreads) keep
their right stored amounts, held as `rebuilt-empty-method`; the 6 held right fixes (7 lines: 39382, 44526, 83759, 88893, 127680, 138515 twice)
are replaced, and 8 more held lines are filled or corrected right (four reference lines: currie sauce, Espagnole's
own tablespoon, cream (or milk), Béchamel; "a scant cup of water" twice; "a small tablespoonful of sifted flour";
"a can of tomatoes"). Three emptyings the reading judged right or doubtful (103590, 110439, 46916) are now
held (`rebuilt-empty-method`), so the wrong stored amounts wait in the queue: an empty rebuild never replaces. The 42 kept lines of the reading carry no
rebuilt amount in the log and are not replayed; by the rules a kept line can only stay kept or become held.

Measured at the staging read of `cleanup2/ck12-lines-proof` (`line-builder/13`, `reprocess-audit/9`, read-only on
the staging copy after checkpoint 6, with the start-up fill's own `Fill` and guard run locally over every scraped
recipe, the rows put back to their state before checkpoint 6's first start). `reprocess-audit/9` grounds an amount
after a leading "optional" or "~", with entities and vulgar fractions read, "saltspoonful", "teacupful" (also "tea
cup"), "a bunch", "a head" or "a stalk" of the food, an article before up to three words that describe the food ("a
well-beaten egg", "a small chopped onion"; "a chopped half-onion" is no one), "dozen" after a number as its measure
("one dozen and a half" is 1.5 dozen) and a number hyphened to its measure ("one-half-pint"); A3 takes a count in
pieces for the juice of a number of fruit ("juice from 2 limes"; not the juice of a can), reads a line's measure
outside its brackets only ("Juice from 1/2 lemon (about 2 teaspoons)" is a count), and lets cooked, cold or chopped
bird meat be measured by volume. A slice a method sentence names is still no amount: half of those read are one per
serving ("a slice of buttered toast" under each egg, "between every two pancakes"). The checkpoint's 3,630 fills
it could place are all reproduced by the checkpoint build; this build changes 2 of them (133802 and "essence
violets, one-quarter ounce", 1.25 to 0.25 ounce), drops 14 (proportions read as pieces, "the fourth of a pound",
amounts that were one of two stated, "a tumbler two-thirds full", "Heat oil in a pan to 350C" as 350 cups of oil,
one piece per serving for a plural line; three of the 14, "a beaten egg" twice and one olive, were right and are left
to the line judge)
and makes 706 more fills over all recipes (the start reads only the recipes no line operation holds). Of six
fresh draws of 60 fills, the second, third and fourth found one wrong each, fixed by kind; the last two found
none, and every fill the guard changes let pass was read. The start-up fill's holds fall from 654 to 179 over all recipes and,
in the recipes checkpoint 6's start read, from 190 to 54 (`rebuilt-ungrounded` 43, `both-grounded` 6, `kind` 5); of
40 fresh holds read, 16 are right to hold and no held wrong amount passes. The rest are slices the method names,
both-grounded counts against a measure the line states ("3-4 tablespns.", "a couple of cloves" against a stored 1),
"wine glass" spelt apart and bird meat whose method alone says it is cooked. The draws are in the
`cleanup2/ck12-lines-proof` build report.

## Rebuild

`POST /api/RecipeReprocess/rebuild?apply=false` with `{ "recipeIds": [...] }` (at most 500;
`CanManageCuration`; a dry run unless `apply=true`).

- **Source.** The newest stored record (`scraper-recipe`, `staged-cookbook`), else the newest
  `jsonld` document read through the scraper's `POST /api/parse` (no fetch). A recipe with no
  source (typed in by a person), with a source that does not read, or with one that has no
  ingredient line is held (`no-source`, `source-unreadable`, `source-empty`).
- **Pairing.** Each source line is paired with the stored row of the same text (entities, spacing
  and case normalized), then with a `" + "` fold that holds it as one of its parts (a fold pairs
  with each part).
- **Kind of a paired line.** A line built from the same text as its row (entities, spacing and case
  aside) keeps the row's kind, the line-classify lane's as a rule, over the pre-pass's, while it has
  no amount or the same amount as before; a line that now reads an amount it lacked loses it. A
  recipe not yet curated that would fail vetting only because the rebuild left a line without the
  kind its row had (the parts of a `" + "` fold, for example) stays `NonCurated`.
- **Food of a paired line.** The line keeps the food it is on now, so links, relinks and approvals
  made since the import stand ("the rebuild follows the current links"; wrong links are re-decided
  by the applied-link re-check, which runs after the rebuild). It gives way only when that food is
  retired, a prepared food, an uncurated food whose name carries an amount or unit
  (`IngredientNameNormalizer.IsResidue`, e.g. "2 cups flour"), or an uncurated food where the
  import now finds a curated one. The line then takes the food the import finds. A prepared food
  whose whole name is the line's text ("1 hamburger" on a food renamed "Hamburger") is the one food
  the import's name match still finds, and no other food can take that name, so such a line stays
  on it: the answer lists it as `prepared-food` and counts it (`onPreparedFood`), the approval gate
  holds the recipe, and renaming the food is for a person.
- **Kind of a paired line.** A rebuilt line with no amount and no deterministic kind keeps the kind
  the lanes gave the row of the same text.
- **Order.** The source's order (`SortOrder` 0, 1, 2 ...).
- **Steps and prose** are not touched, except for a newer extraction of a book (below).
  `ProseWithheld` stays as it is.
- **A newer extraction of a book.** When the source is a `staged-cookbook` record of version 2 or
  later (a re-extraction attached as the next version, `ops/cookbook-extract.py`) and its steps differ
  from the live steps, the rebuild replaces the steps too: steps 1, 2, 3 ... with the texts the import
  would store, each stamped as last changed by the system person, keeping the type and creation stamps
  of the live step at its place. The step key is (recipe, step number), so every step row, deleted
  ones too, is replaced, as the line rows are, and the journal keeps them all (`OldSteps`,
  `NewSteps`). A step a person changed (stamped by anyone but the system person) holds the recipe
  (`person-step-edit`), and so does a standing rewrite of its steps by the clean-room prose lane
  (`prose-rewritten`, until the lane's undo takes it back), so a newer extraction never puts source
  text back over a rewrite. A web or JSON-LD source never replaces steps: the prose lane owns them.

The answer lists, per recipe, each line before and after with what changed (`amount`, `food`,
`order`, `kind`, `text`, `added`, and `prepared-food` for a line left on a prepared food), and the counts per batch (including `dropped` rows, lines
`filledFromMethod` and `newFoods` the apply would create).

### Guards

Each keeps the stored lines and is reported as `held` with its reason:

| Reason | Rule |
|---|---|
| `person-edit` | the recipe was saved in the editor (`Recipe.Version` above 1). Since `cleanup2/ck11-lines` (staging checkpoint 6, R10) a save that leaves the recipe as it was keeps its version, and a save that brings it back to what it was before the person's first edit (an edit and its revert, nothing else written to the recipe in between) puts that version back: the first edit of a recipe at version 1 or below records the version and a fingerprint of the content before it (name, description, servings, the lines in order with food, amount, unit, text and kind, the step texts) as a `PersonEditBase` audit row. A save rewrites a step's `Summary` only for a step whose text changed, cut to its 255 characters (R4: a step over 255 characters made every save of the recipe fail, and every save overwrote every step's `Summary`). The cut never splits a character outside the basic plane, so an emoji at the 255th place is left out whole. Each step `Summary` an edit replaces is recorded once against the fingerprint it belongs to (`PersonEditBase`, entity `RecipeStep`, property `Summary[n]`), and the revert that puts the version back puts those summaries back too |
| `rejected` | the recipe is Rejected |
| `compound-split` | a compound split or alternative pick (`LineSplit`, `LineAlternative`) stands on the recipe; the rebuild does not repeat those rules yet, so it would join the split again |
| `planned-food-change` | the recipe is in a meal plan from today on and a line would change its food (amounts, order and kinds may change) |
| `approved-fewer-amounts` | an approved recipe would end with fewer lines with an amount |
| `approved-fewer-curated` | an approved recipe would end with fewer lines on a curated food |
| `approved-vetting` | an approved recipe would fail a vetting rule it did not fail before (issues compared by kind, numbers aside, so one that already failed a rule may not fail a further one) |
| `approved-gate` | an approved recipe would newly fail the approval gate (an uncurated counted line, a prepared food) |

An approved recipe keeps its status. Other recipes not yet curated are vetted again
(`RequiresRevision` or `NonCurated`, as the start-up repair does), except that kinds the rebuild
dropped never move a recipe to `RequiresRevision` (see "Kind of a paired line").

### Rule rejection

A recipe that is not curated (NonCurated or RequiresRevision, curation not completed) whose
source **and** rebuilt version both fail the text rules for "not a recipe" (placeholder copy, or
text with no real words), and in neither of which any step is real method text (words and no
placeholder copy), is set to `Rejected`. A recipe cut short ("(Recipe incomplete, steps cut off)"
after three real steps, recipe 22541 at checkpoint 1) has real steps and is only vetted. A rejected recipe gets `Rejected by rule: ...` added to its vetting
issues and a `RuleReject` row in `audit.AuditLogEntry` (entity `Recipe`). The rejection is its own
journal row of kind `rule-reject` with the state before and after and no rows; when the lines change
too, a `rebuild` row before it holds them. So `undo?kind=rule-reject` takes back every rule
rejection and leaves the rebuilt lines, and an undo of the whole batch takes back both. Since
`cleanup2/ck11-lines` (staging checkpoint 6, R19) `undo?kind=rebuild` takes a rebuild run's rule
rejections too, since a rebuild run writes them, and its dry run and result count them apart in
`restoredByKind` (`rule-reject`); before, it restored the lines and left those recipes Rejected
without saying so.

### Idempotence

A rebuilt recipe rebuilt again is `unchanged` and nothing is written. A recipe the import created
from a stored record rebuilds to the same lines (as long as its method fills nothing).

Operator note (staging checkpoint 2): a dry run of 500 recipes takes about two minutes on the
staging copy. Before any apply, read the rebuild's "X or Y" ranges (a range raised to its upper
number), the method fills it loses and the amounts after a name with a trailing description: the
checkpoint read 8 of 60 rebuilt recipes wrong on these (M18 to M20, the builder fixes are their own item), over
the 1 in 20 the stop rule allows.

## Order only

`POST /api/RecipeReprocess/rebuild?orderOnly=true&apply=false` pairs each live row with its
source line by text (a fold by its first part) and sets `SortOrder` from the source, nothing
else: food, amount, kind and text stay. A row with no source line holds the recipe
(`order-unpaired`). Same journal, kind `order`. Meant for the 105,775 recipes whose lines all sit
at position 0. Its header is `ReprocessOrder`, which does not hold the recipe: the line-classify,
repair and start-up lanes keep working on it, and its undo then skips a recipe they changed. It is
not a rebuild for the older line undos: it moves positions and nothing else, so each of them
follows a line through the standing order-only writes made after its entry (below, "Order of
undos") instead of skipping it. It also keeps each row's alcohol retention factor and basis, which
the journal's rows carry since `cleanup2/ck4-undo-ops` (before, an order-only pass dropped them).
Budget two calls of 500 per 1,000 recipes (preview and apply; 209 each for the 104,275 order-lost
recipes on staging) and expect about 6.6 % `order-unpaired`.

## Duplicate retire

`POST /api/RecipeReprocess/duplicates?apply=false` with `{ "recipeIds": [...], "anyAttribution": false }`
(all recipes when the list is empty). Only imported recipes take part (scraped, or with a stored
source document): a recipe a person typed in is never grouped. Copies are grouped by the same source
URL, else by the same name (case and spacing ignored) and attribution, and only copies that carry an
attribution (the site or book) are grouped by name; `anyAttribution=true` groups by name alone, for a
book imported twice under two attributions (Artusi). In each group the copy kept is the one that
is referenced (meal plan, household recipe or cookbook, rating, comment, note, share token,
shopping item, a child recipe), then approved, then older. Every other copy is soft-deleted when
its lines and the kept copy's agree both ways at 80 % or more (fold parts count); otherwise it is
held (`lines-differ`), as is a copy that is itself referenced (`referenced`) or edited
(`person-edit`). Journal kind `duplicate-retire`, with the kept recipe, and a `DuplicateRetire`
row in `audit.AuditLogEntry`.

## Journal and undo

Every write is one transaction and one row of `recipe."RecipeReprocessJournal"`: `RecipeId`,
`BatchId` (one per call), `Kind` (`rebuild`, `order`, `rule-reject`, `duplicate-retire`),
`RuleVersion`, `SourceDocumentId`, `OldRows` and `NewRows` (jsonb: every column of every line
row, deleted rows included), `OldRecipe` and `NewRecipe` (jsonb: curation status, vetting issues,
deletion), `KeptRecipeId`, `UndoneAt`. An audit row cannot hold a recipe's rows (4,000
characters), so the journal is its own table.

A write that changes lines also writes a header in `audit.AuditLogEntry` (entity
`RecipeIngredient`, the recipe as `EntityId`, change `Reprocess`, `journal=<id>;batch=...` in
`NewValue`). So the recipe counts as held by a journaled operation (`RecipeLineHold`): the repair
lane leaves its lines alone, and the undo can restore them however often the API restarts. The
start-up repair leaves a recipe with a standing `Reprocess` header alone altogether (no fill, no
step split, no entity decoding, no re-vetting): the rebuild applies the same rules and journals
them, and a newer rule reaches the recipe by the next rebuild. The line-classify lane is let through
the header and through the applied-link line rows and `WordingResolve` rows the header names
(`links=`, written when a rebuilt line followed a USDA link or a verified wording); a link row the rebuild did not write still holds the recipe. It labels the rows a rebuild left without a kind, which the undo tolerates (above), so
rebuilt recipes get the model's kinds as well as the deterministic ones.

The rows are replaced, not edited, because the line key (recipe, food, position) changes with the
order: every row of the recipe, deleted ones too, is removed for good and the new rows inserted
with plain SQL, so the stamps they are given stand. A new row keeps the created date and creator of
the row it was paired with, and is stamped as last changed by the system person.

`POST /api/RecipeReprocess/undo?sinceUtc=&batchId=&kind=&apply=false&batchSize=500&maxBatches=&runId=` takes back the writes made
at or after `sinceUtc`, or of one batch, newest first, limited to one kind when given (`rebuild` includes the
same runs' `rule-reject` rows). A write is
restored only when the recipe's rows (deleted ones included) still equal `NewRows`, and its steps
`NewSteps` when it replaced them; otherwise it is skipped and its recipe listed. A kind the
line-classify lane gave since to a row the write left without one does not count as a change: that
lane only labels unclassified rows and no undo reads its labels, so the rows are restored as they
were before the write. The recipe's state is restored only when it still equals
`NewRecipe`; otherwise the lines are restored and the state kept (`stateKept`), except that a
recipe approved since the write (Curated now, not Curated after the write) is skipped: it was
approved on the rebuilt lines, and an approved recipe must not get back lines that may fail
vetting or the gate. The header is relabelled `ReprocessUndone` (which holds nothing, so the recipe
is free again), and so are the applied-link line rows (`LinkApply`, `Line`) the rebuild wrote for a
line that followed a USDA link and the `WordingResolve` rows it wrote for a line it put on a food
through a verified wording, which its header names (`links=`): the recipe is no longer held
through them, the link's own undo no longer finds a line "moved since" there, and a wording retract
no longer moves a line the rebuild undo already put back. The journal row
gets `UndoneAt`, and a `ReprocessUndo` row is written (entity `Recipe`). A second undo finds
nothing to do.

**Foods a rebuild created.** Each new uncurated food a rebuild creates is journaled as
`ReprocessFoodCreate` (entity `Ingredient`, the food's id, `journal=<id>;batch=<id>`, its name). The
undo of that rebuild removes the food again (a hard delete, so the catalog is as before) when, with
the undone recipes' rows back, nothing refers to it: still uncurated under the same name, no other
journal entry on it, and no recipe line (deleted rows included), alias, proposal, nutrient, wording or
rejection, verification, extras, measure, container size or household, plan, pantry or shopping row.
The entry is relabelled `ReprocessFoodCreateUndone`. A food something refers to is kept and counted
(`foodsKept`); the dry run counts both (`foodsRemoved`, `foodsKept`). Foods created before this
journal existed (the 18 of checkpoint 1) are not touched. A food is kept too while a standing journal
would put a line back on it: the rows before of another reprocess not undone in the same call, or the
place before of a line relink or an applied link. Undoing that later write first, or both in one call,
frees it.

Each recipe's journal row, header and new rows carry the time that recipe was written, not the
start of the batch, so a lane entry written on a recipe while the batch was still working through
earlier recipes counts as older than its rebuild.

**Order of undos.** A rebuild replaces the rows that older line journals name. `relink/undo`,
`reread/undo`, `labels/undo`, `quantities/undo`, `startup-fills/undo` and the wording retract and
unload with lines (`LineRelinkService.UndoForSourcesAsync`) therefore skip an entry
whose recipe has a standing `Reprocess` header newer than the entry, and count it as
`skippedRebuilt`. A rebuild that replaced only the steps and left the rows as they were (its rows
before equal its rows after) does not count, and its own undo restores the steps and the recipe's
state without touching the rows, so the two undos work in either order. To go back past a rebuild, undo the rebuild first; the older undo then works
again.

An order-only write (`ReprocessOrder`) does not count as a rebuild (staging checkpoint 2, M25: the
relink undo skipped 20,737 lines and the start-up fill undo 3,379 of 3,530 as rebuilt after the
order-only pass). Every older line undo (relink with its unfold, split and alternative entries, the
link re-check's line moves, re-read, labels, quantities, start-up fills, alcohol retention, the
wording retract, unload and reload, and `proposals/undo-applied`) follows its line through the
standing order-only writes made after its entry (`RecipeOrderTrail`: the write's rows before name
the line by food and position, its rows after by the same food, text and creation time) and puts
it back where it stands now; a line one of those writes did not keep is skipped as moved, not as
rebuilt. What the undo puts back it also carries into the order-only write's own rows (its rows
before take the line as it was before the older write, at the place it had then; its rows after
take it as it stands now), so the order-only undo still finds its rows as written and, run after
the older undo, returns each line to its first place: the older write, the order-only pass and
both undos round-trip to the first rows exactly. The wording retract and reload journal a new entry
for each line they move, so they follow an order-only write but do not carry into it: undo them
first (newest first) before undoing the order-only pass.

An order-only undo run alone puts the positions back even where a later step changed the recipe's
lines otherwise (staging checkpoint 3, N33: run alone it skipped 22,666 of 97,400 recipes whose food
links a later relink, refill or re-check set had changed, not their positions). An order move and a
link or amount change on the same line are independent: when every live row now is a row the pass
left, known by its text and creation time, and none was added or removed since, the undo gives each
row the position the pass found (a row whose food is already taken at that position by another row
goes to the next free one) and keeps the later change (`positionsFollowed` in the result). The move
back is journaled as an order write of its own, batch `rp-order-follow-<id of the pass's journal row>`,
and the pass's row is marked undone: the later step's undo follows its line through that write as it
follows any order-only write (`RecipeOrderTrail` lets only entries made after the pass follow it, since
an older entry's line already stands where it stood before the pass), and carries what it puts back
into it. Undoing that write (`RecipeReprocess/undo?batchId=rp-order-follow-<id>`) sets the pass's
positions again and marks the pass standing, so the pass can be undone once more the usual way. That
write is an undo, not a step: only an undo naming its batch takes it back, and an undo by `sinceUtc`,
by kind or of the lane's batches never selects it, so running the same undo again, or a wider one,
leaves the pass undone. A recipe a later step added a line to or took one from (an unsplit, a refold, a
rebuild) is still skipped: undo that step first. So is a recipe with a later reprocess write that still
stands (a rebuild that kept every row, a second order-only pass, another pass's order undo write): its
own undo restores rows as written and does not follow lines through an order write, so moving them
would leave it impossible to undo; undo it first, or in the same run.

Nutrition is recalculated after every rebuild and every undo that restored lines (not one that only
followed positions), as before the batches: a label also reads the foods' current nutrients, their
active container sizes and the recipe's servings, which may have changed since the label was last
calculated (a catalog fill calculates no label again), so an undo whose lines are unchanged in content
can still change the label's values. The undo calculates the labels of a batch's restored recipes in a
few reads and one save (`IRecipeNutritionService.RecalculateManyAsync`, the same calculation as one
recipe's), inside the batch's transaction before its progress row: a batch is committed with its labels
or not at all, so a restart during a batch leaves no label calculated from the lines it replaced. A
concurrency conflict with another label writer on the same rows (the API's own, staging checkpoint 5,
Q16: one `DbUpdateConcurrencyException` on `recipe."RecipeNutrition"` during the order-only undo) is
calculated again from fresh reads, up to four tries a little apart, logged at information level. Any other
failure, or a conflict that outlasts the tries, is logged as a warning and leaves the labels of that batch
as they were (the recipes are then calculated one at a time, so one recipe's failure keeps no other label
back).

**Batches, progress and stopping** (staging checkpoint 4, P20: the order-only undo applied about 80
recipes a minute, about 20 hours for the 97,400 recipes of the pass; the client gave up after an hour and
the API went on until it was restarted). The undo takes its entries in batches (`batchSize`, default
500, at most 2,000), newest first as before. Each batch reads its journal rows, recipes, lines, steps,
later standing writes and headers in a few set-based reads, judges each entry exactly as the
row-by-row undo did (against the rows, steps and state the entries before it left), and an apply then
writes the whole batch in one transaction: one delete and one insert (`unnest` of typed arrays) for
the lines of every recipe it restores, one save for the journal, header, link and audit rows, and a
progress row. The generated copy of the pass in `ReprocessUndoBatchTests` (160 recipes: order-only
pass, rebuilds with created foods, steps and states, rule rejections, second copies, later relinks,
amount changes, hand-added and deleted lines, classifier kinds, approvals) was undone by the
row-by-row undo of `93bb4b15` and its every line, step, recipe, food, journal, audit row and label value
is `Fixtures/ReprocessUndo/row-by-row-undo.tsv`; the batched undo leaves exactly that at batch sizes
500, 7 and 1, and when stopped and continued. The rule version the pass stamps on its journals and audit
rows is `{line-builder}` in the file and reads as the current `RecipeLineBuilder.Version`, since the undo
never judges by it. The copy ends with a catalog fill that changes foods'
nutrients and calculates no label (review CK9US-1), so a label the undo does not calculate again shows.

- An apply answers its `runId` (`ru-<time>-<guid>`). Its rows are in the audit log under entity
  `RecipeReprocessUndoRun`, property the run id: `ReprocessUndoRun` (the filter and the entries at the
  start), one `ReprocessUndoBatch` per batch (its counts and the last entry id reached, written in the
  batch's transaction), and `ReprocessUndoRunEnd` (`finished`, `stop-requested`, `client-gone` or
  `max-batches`) when a call ends.
- `GET /api/RecipeReprocess/undo/progress?runId=` (the newest operator run when no id is given; the
  reprocess lane's own undo also writes runs, which are read by their id) answers the
  state (`running`, `stopped`, `finished`, or `interrupted` when the API stopped during the call),
  entries at the start, batches, entries reached and remaining (counted now), the counts so far, the
  last entry id, the time of the last batch, the working time (each batch's own time, summed, so the
  time between a stop and the call that goes on is not counted) and entries per minute of that time.
- `POST /api/RecipeReprocess/undo/stop?runId=` (the newest operator run when no id is given) asks the
  run to stop: it ends before its next batch, and
  a call whose caller went away (the request aborted) ends the same way. Every batch written is whole,
  so the state is consistent wherever it stops.
- `POST /api/RecipeReprocess/undo?runId=<id>&apply=true` goes on with a stopped or interrupted run: it
  uses that run's filter and takes the entries below the last one reached, so an entry the run skipped
  is not judged again; `maxBatches` limits a call. A finished or running run is refused (409).
- One apply runs at a time on a database: an operator apply is refused (409) while another apply, the
  operator's or the lane's, is going in the API, and the lane's own undo waits until it ends. The check
  and the run's start row are made under one lock, so two applies sent at the same moment cannot both
  start. The entries are listed when the run starts, and each batch reads only those still standing: an
  entry something else took back in between is counted `alreadyUndone` and not judged again.
- A dry run (`apply=false`) runs in the same batches, writes nothing and answers no run id.

**Counts of a rebuild.** `counts.added` counts every built line with no stored row of its own: a
source line new to the recipe, and the second and later parts of a `" + "` fold taken apart again (the
first part keeps the fold's row). So `linesAfter - linesBefore = added - dropped` always holds (staging
checkpoint 3, N14: five recipes gained a row while `added` stayed 0).

## The detector

`ReprocessDetectorService` reads every recipe with a stored source again with the current line
rules, as a dry run of the rebuild, and stores what it finds in `recipe."ReprocessCandidate"`, one
row per recipe: `Reasons` (bits), `RuleVersion` (the builder's version it read with),
`SourceDocumentId` (the newest source it read), `Tier`, `Priority`, `DetectedAt`, and what the lane
did (`Outcome`, `OutcomeReason`, `OutcomeAt`, `JournalId`). A recipe is read again only when
`RecipeLineBuilder.Version` changes or a newer source is attached; a recipe a person edited and a
rejected one are not read. Nothing is written to recipes.

**Operator run.** `POST /api/RecipeReprocess/detect?afterRecipeId=0&maxRecipes=500&apply=false&sampleSize=20`
(curation-gated) runs the same reading for an operator, without the lane, its pause switches or any
model (staging checkpoint 2, M15: the detector ran only inside the lane, which waits on
`ai-pause-all` and whose batches call the line judge). The answer gives `read`, `candidates`,
`approvedCandidates`, the tier split (`orderTier`, `rebuildTier`), `byReason` (D1 to D8),
`heldByReason` from the rebuild's dry run, `lastRecipeId` (page with `afterRecipeId=<lastRecipeId>`
until `read` is 0) and up to `sampleSize` candidates with their changed lines. A dry run writes
nothing and reads every sourced recipe, read before or not, so it can be repeated; with
`apply=true` it reads only the recipes the current rules have not read and stores their candidate
rows as the lane's first sweep would, writing nothing to recipes. A call of 500 takes about two
minutes on the staging copy (the rebuild's dry run). D8 comes from `duplicates` (dry run).

| Code | Reason | From the dry run |
|---|---|---|
| D1 | order lost | the stored lines all at position 0, a `" + "` fold (a row whose text no source line holds as it is; "salt + pepper to taste" printed so in the source is no fold, recipe 32080, M16), a changed order, or (with no newer extraction) a new or dropped line from a merge or split |
| D2 | amount | a line whose own text the parser now reads another amount or unit from |
| D3 | method fill | an amount the line's text does not state (filled from the method, now or before) that changes |
| D4 | food | a line whose food gives way (retired, prepared, uncurated junk, uncurated where a curated food is found) |
| D5 | kind | the deterministic kind differs (a colon heading) |
| D6 | compound | a stored line the compound rules read as whole foods sharing a product (`or-shared-head`, R9) that is not on that product |
| D7 | source | new, dropped or reworded lines or steps from a newer extraction of the book |
| D8 | duplicate | a copy the duplicate retire would retire (marked after each sweep) |

D6 and D8 alone are not rebuilt: the compound rules and the duplicate retire deal with them. A
candidate whose only change is its order goes to the `order` tier (the light order-only pass, R4);
every other one to `rebuild`. Priority: an amount, a method fill or a food 300, a newer source 200,
a kind or compound 100, order alone 0; an approved recipe 50 more when it has any change but order.

## The lane (`ReprocessHostedService`, `ReprocessLaneService`)

Lane `reprocess` on the lane frame (`lane-frame.md`), tiers `rebuild` and `order`, behind the lane
gate (`ai-pause-all`, `ai-pause-reprocess`, quiet hours). Off unless `Reprocess:Enabled=true`.

- After each start and every `Reprocess:DetectEveryHours` (24) the detector sweeps, in batches of
  `Reprocess:DetectBatchSize` (200).
- Between sweeps each tier takes up to `Reprocess:BatchSize` (100) candidates, highest priority first,
  within its daily cap: `Reprocess:MaxPerDay` (500) rebuilds and `Reprocess:OrderMaxPerDay` (5,000)
  order restores in 24 hours, counted from the lane's decisions.
- A tier writes only while the frame holds it live. In shadow, and while paused (probation), it
  previews the rebuild and records the decision; a candidate decided in shadow waits for the live
  tier, which takes it again. Every write is under a batch id starting `rl-`.
- Every decision is a `curation."LaneDecision"` row (subject `recipe:<id>`, stratum the main reason)
  with up to six changed lines and their rule-check failures in its signals. A batch is journaled as
  a `ReprocessBatch` row (entity `ReprocessLane`) with its figures.

**Rule checks** (`ReprocessRuleAudit`, `reprocess-audit/6`, written apart from the builder, no
model) on every changed line: A1 the amount stands in the line's text, or for a method fill in a
method sentence that names the food, as a number that text states times an old measure that same
text names (a quart is 4 cups, a gill half a cup, a pound 16 ounces), within a range it states
("2 to 3", "2-3", "2 or 3"), or as two numbers it states one after the other multiplied or added
("2 cans of 15 oz"), or as pounds and ounces it states added up in ounces, alone or times a number
stated before them ("1 lb. 13 oz. can" is 29 ounces, "2 cans (1 lb. 4 oz. each)" 40), or as two
measures joined by "and" added up, in the smaller measure or the first ("one quart and a pint" is 6
cups, "1 pound and 2 ounces" 1.125 pounds; `reprocess-audit/5`, as the builder now reads such lines); "a" or "an" counts as one only before a measure or the food, and a number between
two unrelated numbers ("cook 10 minutes, then 2 more onions") grounds nothing; since `reprocess-audit/6` a
measure the text opens with counts one ("pinch of salt"), "fourth", "sixth" and "eighth" are fractions ("one-sixth
pint" is a third of a cup), a sum of two numbers is also read times the text's measure ("a pint and a half" is
3 cups), a cross-reference ("al N. 579", "du N. 579") and a price ("twenty cents' worth") ground nothing, and the
point of a measure's abbreviation ("1/2 pt. of cream") ends no method sentence; since `reprocess-audit/7` the point
of an initial ("J. P. Smith's Curry Powder") ends none either, a method sentence counts when it names the food or
the line's own wording (a line "sweetbreads" on the USDA food "Veal, variety meats and by-products, thymus, raw"),
and "a" or "an" counts one also before a can or before a size word and a measure ("a scant teaspoonful", "a large
tablespoon", and a line opening "scant cup of"); since `reprocess-audit/8` an ordinal fraction is read whole with
the "of a" after it ("three-quarters of a pint" is 0.75 pint, so the "a" no longer grounds a whole pint; "two and
one-half pounds" is 2.5), a spoon measure spelt with a hyphen or a space ("table-spoonful", "tea spoonsful") is a
measure, and a count of a measure may be a fraction, a vulgar fraction, "half a" or "a couple of" ("1/2 pony", "half
a pony", "a couple of tea-spoonsful"). A line that starts with
a number or "two cups of" stored with no amount fails. A2 a line moved to another food names that food. A3 pieces only of a food with a weight
per item and never of a liquid or of a line that states a measure; no volume of a whole bird; a
volume of a seasoning or leavener (salt, pepper, a ground spice, baking powder or baking soda, yeast;
a bare "soda" is a drink) of at most 4 cups and of a dry staple (flour, sugar, meal, rice, oats,
crumbs, starch, cocoa) of at most 16 cups, matched on the food's name before any comma, or after a
USDA class word on the part that follows it ("Spices, pepper, black", "Leavening agents, baking soda",
"Cereals, oats, ..."; "Sugars, granulated" is matched as sugar), so a line linked to a catalog food is
checked as well as a new food named from its wording ("1 gallon of salt" fails, "1 quart salt" for a
brine passes; `reprocess-audit/3`, since A6 reads energy only and salt has none; `reprocess-audit/4`
since `cleanup2/parser-size-first`, as a line in pieces that starts "1 lb. can" is now read as a
can like "1 (16 oz) can" and so does not fail A3 for its measure, and A1 grounds pounds and ounces
added up, so "1 lb. 13 oz. can peaches" moving from 1 pound to 29 ounces passes). A4 a
recipe fails no more checks after than before. A5 (live) ten rebuilt recipes rebuilt again change
nothing. A6 the kcal per serving of the rebuilt lines (grams by the label's own conversion, the food's
Calories per 100 g, ingredient lines only, the recipe's servings) lies in 30 to 2,500, or no further
outside that band than the stored lines were; a recipe with no servings or no counted line is not
read. A4, A5 and A6 each count as one more failing line. O1 (order tier) the rows of an order restore,
read in their new order, follow the source's lines (each row's text, or a part of a `" + "` fold,
found on a later source line than the row before it); each restore counts as one checked line.

**Faults** (pause a live tier; keep a shadow tier in shadow): the checks pass on less than 98 % of a
batch's changed lines (at least 50), or less than 99 % in shadow; more than 5 % of a batch's approved
recipes held (at least 20); a rebuild that is not idempotent; the line judge unreachable, or unreadable
on more than a fifth of its sample.

**Audit and bars.** The rebuild tier's auditor is the line judge (`OllamaLineJudge`, reader B of the
roster, `mistral-nemo` by default, `Reprocess:JudgeModel` to change it): it reads up to three changed
lines of a sampled decision with the source line and the method sentences that name the food, one
line to a call, the stored and rebuilt rows shown as A and B in an order that does not tell which
is which. It reads the line (its kind and the amount the source states, copied) and names the right
row; a choice that contradicts its own reading (a kind other than the line's, an amount where it
reads none, a Piece count of a measure, no amount where the other row's amount stands in its
reading) is corrected by rule, and the rebuilt row is called better only when the stored row
contradicts the reading or the rebuilt row's amount stands in it, else unclear. The result is the
rebuilt row better, the same, worse or unclear. A recipe is flagged when a line is worse;
the flag counts as an error only when the rule audit confirms it (a check failing on a changed line,
A4, A6 or O1). A model set by `Reprocess:JudgeModel` must be of reader B's family and of neither reader
A's nor the auditor's; otherwise the API refuses to start. The combined check's own score comes from the committed `line-judge` golden record
(`golden-sets.md`), built into the API: without a record of the current prompt and judge model, at or
above its floor, the tier cannot go live. Sample 30 % of written decisions (at least 30). Bars:
qualify on a Wilson lower bound of 98 % (at most one worse in about 300), pause when a full window of
300 audited rebuilds shows more than 3 % confirmed worse (the observed-error rule, LQ-4). The order
tier's auditor is an exact rule (a decision is wrong when a line changed anything but its position,
or when its rows do not follow the source's order, O1), 10 % sample, qualify 99 %, pause above 2 % over 400.

**Undo.** `ReprocessLaneUndo` takes back the tier's own batches (`rl-`) since the last clean window,
newest first, through the journal (rule rejections and rebuilds in one pass by kind `rebuild`, each
counted once; order restores for the order tier), and marks those candidates `undone`: they wait for the next change of the line rules. An
operator's rebuild (`rp-`) is never the lane's to undo.

The Pipeline page shows the frame rows of both tiers and a "Recipe rebuild" block: candidates
waiting by tier, outcomes, reasons with their waiting counts, each tier's last batch with its rule
share and faults, and the judge's record problem.

## Re-extracting a book (`ops/cookbook-extract.py`)

Run on the harvest host, where the book texts are; it fetches nothing and asks only the model host
the operator names. Chunks start at recipe titles (numbered, capitals or title case after a blank
line) and hold whole recipes up to `--limit` characters (4,000), so a recipe is never cut at a chunk
edge; one recipe longer than `--hard-limit` is cut at paragraph ends and its records are marked
`split`. Every record is checked against its own recipe in the chunk (from the title that names it
to the next title; a chunk holding several recipes and no title naming the record drops it) or is
dropped to `--rejects` with its reason. Fidelity: each ingredient line appears in the recipe after
normalization, else one line or sentence of the recipe holds all its numbers and words; each step
likewise, number words included, so the method a rebuild fills amounts from states no amount the book
does not; and every line of the recipe's ingredient list (a paragraph of short lines stating amounts)
is extracted. With `--translate` (Artusi) the model writes English and quotes the Italian of each
ingredient and step: the quote must lie in the recipe, an ingredient's quote must name a food and not
only an amount, the English must carry exactly the quote's numbers and units (grammi is grams,
cucchiaio a tablespoon; one is not compared, since an article reads as one in one language only) and
name the same common foods (burro and butter, zucchero and sugar, uova and eggs ...), two English
lines may not come from one Italian line, and every line of the Italian ingredient list must be
translated with its whole amount. Each kept record may carry `replacesRecipeId`, from the first
attach's log (`--recipe-map`, by the quoted title, else the name) or a CSV (`--pin-csv`); a record
whose title and name point to two recipes, and every record of a recipe two records point to, stays
unpinned and is logged. `recipe-source-attach.py`
sends that as a pin: with `newVersion`, `POST /api/RecipeSource/attach` stores a pinned record as the
next version on that recipe only, when it already holds a record of that format, whatever the line
agreement (a translation does not repeat the old lines); two records of one batch pinned to the same
recipe are both `doubleMatch` and neither is stored. The detector then finds D7 and the lane
rebuilds lines and steps.

## What is not here yet

- The compound split rules are not repeated by the rebuild; recipes with a standing split are held.
- `undo-applied` (the USDA link undo) skips a link with a line on a recipe rebuilt after it
  (`skippedRebuilt`, the link stays Applied with its journal) and follows a line an order-only pass
  moved; undo the rebuild first, then the link, or pass `leaveMovedLines=true` to leave the rebuilt
  lines where they are and take the rest back.
- A6 reads energy only: a wrong amount of a food with no calories (salt, water, baking powder) or one
  that keeps the serving inside 30 to 2,500 kcal passes it.
- A held recipe is retried when the line rules change, not after a catalog change.
