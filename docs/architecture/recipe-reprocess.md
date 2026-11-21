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
   the food an applied link merged the retired ingredient of that name into, else a new uncurated
   ingredient. A wording, an alias or an applied link never leads to a prepared food. A line whose
   text states only an amount and names no food (`IngredientNameNormalizer.NamesOnlyAnAmount`: a
   number alone, "Grammi circa 100", "Grammi 80", "2 cups"; a count word that can be a food, such as
   "6 cloves", is not read so) keeps the food the rebuild finds it on, or a food already under the
   line's own text ("grammi circa 100"); otherwise it is not built. No food is ever created under an
   amount, by the import or by a rebuild. In an "or" line whose food is
   named only in a later alternative that states its own amount, that amount is the line's ("pinch
   stevia OR 2 tbsp maple syrup" on maple syrup is 2 tbsp). A clove counts garlic or shallot: on the
   spice cloves ("a couple of cloves") the count is in pieces.
3. **Kind.** A line with no amount gets the kind the deterministic pre-pass reads on its face
   (`RecipeLineClassifier`: headings and yield notes are `note`, utensils `equipment`, lines with
   no letters `artefact`).
4. **Method fill (rebuild only).** A line with no amount that is the only line of its food and
   names that food takes the amount the method states next to it (`MethodAmountFinder`), as the
   start-up repair does, with the same checks: no amount from a ratio, a vessel, a length or
   washing water, and no piece count of a liquid or of a food with no weight per item. The import leaves this to the start-up repair, which journals each fill
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
Bottle instead of Package, raised on that branch from the same base as `line-builder/4`)
is stamped on every journal row. Raise it with any
change to a rule the builder applies: the detector reads again every recipe an older version read,
and the lane's signature changes, so its tiers go back to shadow until they qualify under the new
rules.

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
| `person-edit` | the recipe was saved in the editor (`Recipe.Version` above 1) |
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
rejection and leaves the rebuilt lines, and an undo of the whole batch takes back both.

### Idempotence

A rebuilt recipe rebuilt again is `unchanged` and nothing is written. A recipe the import created
from a stored record rebuilds to the same lines (as long as its method fills nothing).

## Order only

`POST /api/RecipeReprocess/rebuild?orderOnly=true&apply=false` pairs each live row with its
source line by text (a fold by its first part) and sets `SortOrder` from the source, nothing
else: food, amount, kind and text stay. A row with no source line holds the recipe
(`order-unpaired`). Same journal, kind `order`. Meant for the 105,775 recipes whose lines all sit
at position 0. Its header is `ReprocessOrder`, which does not hold the recipe: the line-classify,
repair and start-up lanes keep working on it, and its undo then skips a recipe they changed. The
older line undos skip it like a rebuild, since its keys changed.

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

`POST /api/RecipeReprocess/undo?sinceUtc=&batchId=&kind=&apply=false` takes back the writes made
at or after `sinceUtc`, or of one batch, newest first, limited to one kind when given. A write is
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

Nutrition is recalculated after every rebuild and every undo that restored lines.

## The detector

`ReprocessDetectorService` reads every recipe with a stored source again with the current line
rules, as a dry run of the rebuild, and stores what it finds in `recipe."ReprocessCandidate"`, one
row per recipe: `Reasons` (bits), `RuleVersion` (the builder's version it read with),
`SourceDocumentId` (the newest source it read), `Tier`, `Priority`, `DetectedAt`, and what the lane
did (`Outcome`, `OutcomeReason`, `OutcomeAt`, `JournalId`). A recipe is read again only when
`RecipeLineBuilder.Version` changes or a newer source is attached; a recipe a person edited and a
rejected one are not read. Nothing is written to recipes.

| Code | Reason | From the dry run |
|---|---|---|
| D1 | order lost | the stored lines all at position 0, a `" + "` fold, a changed order, or (with no newer extraction) a new or dropped line from a merge or split |
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

**Rule checks** (`ReprocessRuleAudit`, `reprocess-audit/3`, written apart from the builder, no
model) on every changed line: A1 the amount stands in the line's text, or for a method fill in a
method sentence that names the food, as a number that text states times an old measure that same
text names (a quart is 4 cups, a gill half a cup, a pound 16 ounces), within a range it states
("2 to 3", "2-3", "2 or 3"), or as two numbers it states one after the other multiplied or added
("2 cans of 15 oz"); "a" or "an" counts as one only before a measure or the food, and a number between
two unrelated numbers ("cook 10 minutes, then 2 more onions") grounds nothing. A line that starts with
a number or "two cups of" stored with no amount fails. A2 a line moved to another food names that food. A3 pieces only of a food with a weight
per item and never of a liquid or of a line that states a measure; no volume of a whole bird; a
volume of a seasoning or leavener (salt, pepper, a ground spice, baking powder or baking soda, yeast;
a bare "soda" is a drink) of at most 4 cups and of a dry staple (flour, sugar, meal, rice, oats,
crumbs, starch, cocoa) of at most 16 cups, matched on the food's name before any comma, or after a
USDA class word on the part that follows it ("Spices, pepper, black", "Leavening agents, baking soda",
"Cereals, oats, ..."; "Sugars, granulated" is matched as sugar), so a line linked to a catalog food is
checked as well as a new food named from its wording ("1 gallon of salt" fails, "1 quart salt" for a
brine passes; `reprocess-audit/3`, since A6 reads energy only and salt has none). A4 a
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
lines of a sampled decision with the source line and the method sentences that name the food, and
calls the rebuilt row better, the same, worse or unclear. A recipe is flagged when a line is worse;
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
newest first, through the journal (rule rejections, then rebuilds; order restores for the order
tier), and marks those candidates `undone`: they wait for the next change of the line rules. An
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
