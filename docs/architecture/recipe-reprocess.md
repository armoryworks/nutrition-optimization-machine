# Recipe reprocess: rebuilding lines from the stored source

A recipe's lines are built once, by the import. Every rule fix since (the parser, the name
normalizer, the drained-can rule, the heading pre-pass, the method fill) reached only new imports,
plus whatever a separate journaled repair patched in place. The reprocess builds a recipe's lines
again from the source it was imported from, **in place**: the recipe keeps its Id, so meal plans,
images, prose rewrites and curation stay where they are. Nothing is fetched from any site.

Code: `Nom.Orch/Services/Support/RecipeLineBuilder.cs` (shared with the import),
`Nom.Orch/Services/RecipeReprocessService.cs`, `Nom.Api/Controllers/RecipeReprocessController.cs`,
journal table `recipe."RecipeReprocessJournal"`. Tests: `RecipeReprocessTests`.

## One line builder for the import and the rebuild

`RecipeLineBuilder` is the line half of the import (`RecipeScrapingService`), split out so a rule
fix reaches both:

1. **Amount.** NOM's own parser (`IngredientLineParser`) reads the line's text. The scraper's
   reading is used only when NOM's parser reads nothing. A can or jar of a food used drained
   counts cans or jars. An amount whose measurement has no row is stored as no amount.
2. **Food.** The import's order: the verified wording (when `VerifiedWordings:ResolveOnImport` is
   set), a live ingredient of the normalized name, the one food a trusted alias gives the name to,
   the food an applied link merged the retired ingredient of that name into, else a new uncurated
   ingredient. A wording, an alias or an applied link never leads to a prepared food.
3. **Kind.** A line with no amount gets the kind the deterministic pre-pass reads on its face
   (`RecipeLineClassifier`: headings and yield notes are `note`, utensils `equipment`, lines with
   no letters `artefact`).
4. **Method fill (rebuild only).** A line with no amount that is the only line of its food and
   names that food takes the amount the method states next to it (`MethodAmountFinder`), as the
   start-up repair does, with the same checks: no amount from a ratio, a vessel, a length or
   washing water, and no piece count of a liquid or of a food with no weight per item. The import leaves this to the start-up repair, which journals each fill
   (`StartupFill`, undone by `startup-fills/undo`).

`RecipeLineBuilder.Version` (`line-builder/2` since the parser rules R1-R9 of
`cleanup2/parser-rules`) is stamped on every journal row. Raise it with any
change to a rule the builder applies, so the detector (a later item) can tell which recipes an
older version built.

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
- **Steps and prose** are not touched. `ProseWithheld` and the step stamps stay as they are.

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
(`RequiresRevision` or `NonCurated`, as the start-up repair does).

### Rule rejection

A recipe that is not curated (NonCurated or RequiresRevision, curation not completed) whose
source **and** rebuilt version both fail the text rules for "not a recipe" (placeholder copy, or
text with no real words) is set to `Rejected`, with `Rejected by rule: ...` added to its vetting
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
`NewValue`). So the recipe counts as held by a journaled operation (`RecipeLineHold`): the start-up
repair, the line-classify lane and the repair lane leave its lines alone, and the undo can restore
them however often the API restarts.

The rows are replaced, not edited, because the line key (recipe, food, position) changes with the
order: every row of the recipe, deleted ones too, is removed for good and the new rows inserted
with plain SQL, so the stamps they are given stand. A new row keeps the created date and creator of
the row it was paired with, and is stamped as last changed by the system person.

`POST /api/RecipeReprocess/undo?sinceUtc=&batchId=&kind=&apply=false` takes back the writes made
at or after `sinceUtc`, or of one batch, newest first, limited to one kind when given. A write is
restored only when the recipe's rows (deleted ones included) still equal `NewRows`; otherwise it
is skipped and its recipe listed. The recipe's state is restored only when it still equals
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

Each recipe's journal row, header and new rows carry the time that recipe was written, not the
start of the batch, so a lane entry written on a recipe while the batch was still working through
earlier recipes counts as older than its rebuild.

**Order of undos.** A rebuild replaces the rows that older line journals name. `relink/undo`,
`reread/undo`, `labels/undo`, `quantities/undo`, `startup-fills/undo` and the wording retract and
unload with lines (`LineRelinkService.UndoForSourcesAsync`) therefore skip an entry
whose recipe has a standing `Reprocess` header newer than the entry, and count it as
`skippedRebuilt`. To go back past a rebuild, undo the rebuild first; the older undo then works
again.

Nutrition is recalculated after every rebuild and every undo that restored lines.

## What is not here yet

- The detector (reasons D1-D8), the candidate table, the hosted lane on the lane frame, the daily
  cap and the line judge are the `reprocess-lane` item. Until then the operator names the recipes.
- The compound split rules are not repeated by the rebuild; recipes with a standing split are held.
- `undo-applied` (the USDA link undo) does not yet skip rebuilt recipes. Undo a rebuild before
  undoing a link applied before it.
