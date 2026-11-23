# Prose check v4

A scraped recipe's method and description are shown only in our own words. The prose check decides
whether a text is our own: it compares a rewrite with the stored source (`recipe.ScrapedDocument`) by
rule, with no model. Version 2 replaced the bare-number comparison of `RewriteFidelity`; version 3
(the staging checkpoint 2 fix round, "Check v3" below) replaced v2's copy rules. Version 4
(`ProseCheck.Version = 4`, the staging checkpoint 3 round, "Check v4" below) separates the two
questions v3 mixed: whether the text is **copied**, a rule of its own that must never miss, and
whether it is **faithful**, decided by rule only where a rule is reliable. The weak signals that made
v3 fail faithful rewrites (verbs said another way, steps out of order, negation) no longer fail a
recipe: they mark it **unsure**, a third outcome that withholds nothing. The re-check and the rewrite
lane call the same function, so they always agree. The re-check runs it (see "The withheld state"
below), and so does the clean-room rewrite lane (prose lane v2, `prose-lane.md`) on every rewrite
before its judge reads it.

The check is text only: `ProseCheck.Check(source, rewrite, ingredientLines, stockIndex)`. The finding
lists every rule broken with its detail, the copy measures it computed, the check version and the
stock-index version it ran with, and its outcome (`Finding.Outcome`):

| Outcome | When | What happens |
|---|---|---|
| `failed` (`Finding.Failed`) | a copy rule, a fact rule or a form rule is broken | the re-check withholds the prose; the lane tries again |
| `unsure` (`Finding.Unsure`) | only weak signals (`ProseCheck.UnsureReasons`: `lost-step`, `out-of-order`, `added-quantity`, `too-short`, `too-long`) | the prose stays shown, is marked `Recipe.ProseUnsure` and counted on the Pipeline page; it is the prose judge's to read (in the lane, the judge reads it like any rewrite) |
| `passed` (`Finding.Passed`) | no fault at all | shown |

The index is recorded only: no rule reads it (see the copy rules).

## Rules

### 1. Copy (fails the rewrite)

The rewrite's description and steps are read as one stream of words and the source's description and
method as another (`ProseCheck.CopyTokens`), so where the steps begin and end does not matter. Words
are lower-cased, apostrophes dropped, and every amount the quantity reader finds is one token `#`
however it is written ("½ cup", "0.5 cup", "half a cup", "1/2 c."), a bracketed conversion left out;
any other number or number word is `#` too. Every word counts in full, ingredient names and amounts
included (v3 weighed them at half, which let copied sentences through an ingredient list: checkpoint
3, N9). The rewrite's sentences are put in the order of the source passages they follow most closely
(`ProseCheck.InSourceOrder`: each placed where its best close stretch ends in the source, sentences
that follow none last), so no measure depends on the order of the rewrite's sentences either: a copy
whose sentences are shuffled, cut one per step or merged into one step measures the same. Each test
looks for passages anywhere in the two streams:

- **Run:** no common run (words the two streams share in order) of 16 words or more (`CopiedRun`).
- **Close stretch:** no stretch of the rewrite that follows the source in order with a score of 44.5
  or more (`CloseStretchLimit`), or of 15 or more (`MinimumStretchForShare`) that reaches 0.4 of the
  rewrite's word count (`CloseStretchShare`, for short methods). Each word kept in order adds 1 and
  each word changed, added or dropped costs 1.5 (`StretchPenalty`): a local alignment of the two
  streams (Smith-Waterman), so it needs no run of any length. It catches the close paraphrase that
  changes a word every few words, and a one-block source cut into short sentences nearly word for word
  (checkpoint 3, 36371).
- **Share:** in a rewrite of 40 words or more (`MinimumWordsForShare`), the words that sit in common
  runs of 7 words or more (`SharedRun`) make up less than 0.42 of the rewrite (`MaximumCopiedShare`).
- **Description:** the two descriptions share no run of 11 words or more (`DescriptionRun`).
- **Sentence:** no rewrite sentence of 15 words or more (`SentenceWords`) matches a source sentence at
  0.92 of its words in order (`SentenceSimilarity`, the longest common subsequence over the longer of
  the two).
- Stock phrases count like any other words (review CK4P-3): the limits were set and are measured with
  no stock excuse.
- Steps word for word equal to the source method are `unchanged`.

All copy rules report `copied-text` (`Finding.Copied`). How the limits were set and what they miss is
in "Check v4".

### 2. Facts (fail the rewrite)

Only the losses a rule reads reliably fail a rewrite:

1. **Quantities, not bare numbers** (`ProseQuantities`). Each amount is read as a value or range,
   a unit family (time, temperature, length, volume, mass, count) and a unit.
   - "a", "an" or "another" before a unit is 1 ("another minute" is 1 minute); "a minute or two" is
     1 to 2 minutes; "a half hour" and "half an hour" are 30 minutes; "1 and 1/2" and "one and a
     half" are 1.5; "1 hour 30 minutes" is one amount; "9 x 13-inch" is two lengths, and so is "a 9x13
     dish" with no unit (inches); "5 or so minutes" is 5 minutes; "half a large onion" and "half of
     the cheese" are a soft count of ½, equal to "½ large onion"; "6 or 3 strands" (a smaller number
     after "or") is two counts, not a range; "envelope" and "pkg" are packages.
   - A bare number from 200 to 550 after "to" or "at" in a sentence about an oven, heating, baking,
     roasting, broiling or frying is a temperature in degrees: "preheat the oven to 425" equals "425
     degrees" and "425°F", "bake at 350 for 30 minutes" states 350 degrees.
   - Degree signs are one form: "350ºF" (the ordinal sign º, which is a letter) and "350℉" read as
     "350°F".
   - Two amounts are equal when they are of one family and agree within 2 % at both ends once
     converted: ¼ lb is 4 oz, 2 pints are 4 cups, 175 °C is 350 °F. "350 degrees" with no scale
     equals 350 °F or 350 °C.
   - A source amount the rewrite lacks is `lost-quantity`; a unit changed inside a family is
     `changed-unit` (½ tablespoon to ½ teaspoon, 30 seconds to 30 minutes); a range collapsed,
     widened or narrowed is `changed-range`.
   - Amounts are matched one for one: an amount the source states twice ("bake 10 minutes, turn and
     bake 10 minutes more") must be stated twice, or the second is `lost-quantity`. An oven
     temperature restated counts once, and an amount "on each side" (or "per side", "on both sides")
     stands for two statements ("sear 1 to 2 minutes, turn and sear 1 to 2 minutes more" as "sear 1 to
     2 minutes on each side").
   - An amount in a cook's first-person note in brackets ("(I used two small chillies)", "(I cooked
     6.)") is not required. A first-person sentence outside brackets is an instruction like any other
     ("I stir in 2 tablespoons of flour" still asks for the amount).
   - Step markers ("2.", "Step 3", "2In a bowl", a trailing "3.") and cross-references ("as in step
     3", "No. 156") are not amounts. In a source whose method is one numbered block ("1. Preheat
     ...2. Toss ...3. Melt ..."), the next number of the list is a marker too where it stands after a
     word and before a capital with no full stop of its own ("chile flakes 4. Pour"). A count of exactly
     one, and a spelled-out count before "sides", "edges", "ends" or "equal", is soft: it may be
     dropped or added ("in half" as "two equal parts"), but where both texts state a soft count before
     the same word, the source's must be kept ("two equal parts" as "three equal parts" is
     `lost-quantity`).
   - An amount in brackets right after another of its family ("350°F (175°C)", "9-inch (23 cm)") is
     a conversion and may be dropped; one stated must still match. Any other bracketed amount
     ("(350° F.)" alone, "1 can (15 oz)") is required.
2. **Amounts against the ingredient lines.** An amount up to 4 words before an ingredient's name
   (the last name word of a line, matched by the recipe's own line words) is compared with that
   ingredient's amounts only; an amount in brackets right after an ingredient's name belongs to that
   ingredient ("a little milk (about a tablespoon) with the egg" is the milk's tablespoon). If the
   method states an amount of that family for that ingredient, the rewrite's must equal it, or it is
   `wrong-amount`: where the method and its own lines disagree, the method's amount is the one to
   keep, and two amounts swapped between ingredients ("1 cup flour with 2 cups milk" as "2 cups flour
   and 1 cup milk") are two `wrong-amount`s. If the method states none, the line's amount, the sum of
   that ingredient's lines, or a method amount next to no ingredient is allowed; any other amount of
   the line's family is `wrong-amount`. An amount of the rewrite's description that the source's
   description or title states is never wrong ("between two cookies" against a yield of 30 cookies,
   checkpoint 3, 51581). The ingredient lines are the source's `recipeIngredient`
   (`ProseCheck.SourceLines`).
3. **Ingredients.** Every ingredient the source method names must be named somewhere in the rewrite
   (`lost-ingredient`), under its name or a listed other name (green onion and scallion, cilantro and
   coriander, chickpea and garbanzo, zucchini and courgette, eggplant and aubergine, shrimp and prawn).

### 3. Weak signals (mark the rewrite unsure, never fail it)

- **Verb coverage** (`lost-step`). Every cooking verb of the source should be kept. Verbs come from the
  synonym groups of `prose-verbs.tsv` (compiled into Nom.Orch): "heat", "warm" and "preheat" are one
  action, "heat" and "mix" are not. A word may be in several groups ("whisk" mixes and beats); a source
  verb is kept when the rewrite holds any word of any of its groups. A word right after an article, a
  heat level or "from", "off", "over" or "for" is a noun ("over medium heat"); in a source, one after
  "is", "are", "was", "were", "has", "have", "had", "get" or "become" (adverbs skipped) is a state and
  asks for nothing ("until they are covered", "until the cheese has melted"), and one after "not",
  "never", "without" or "don't" is negated and asks for nothing ("soft but not melted"), while a state
  in a rewrite ("once the frosting has chilled") keeps its group. A mixing verb followed by "in" ("stir
  in the flour") also adds.
  - An accompanying "-ing" form of the source is a step to keep ("whisking constantly", "stirring
    often"); an "-ing" form naming a thing is not ("the whole baking sheet", "boiling water", "the
    serving dish": one followed by sheet, dish, pan, tray, tin, powder, soda, bowl, spray, plate,
    platter, board, rack, paper, mat, stone, spoon, basket, bag, water, liquid, time or temperature).
  - In a source, these ask for nothing: an "-ed" form right after an article or "on", "onto",
    "into", "in", "with", "to" ("on lined trays"), a liquid soaking in ("letting it soak in", "until
    the stock soaks into the barley"), and the second word of a compound naming how a food was made
    ("home-cut", "sun-dried"). A prepared pan ("a well-greased pan", "a parchment-lined sheet")
    still asks for greasing and lining.
  - Turning or switching off the heat, the burner or the stove ("turn off the heat", "switch the
    burner off") is the off-heat action (`ProseVerbLexicon.OffHeatGroup`). A rewrite keeps it the same
    way, with the heat "turned off", or by taking the food off or away from the heat. Taking food off
    the heat in a source asks for nothing.
  - A form marked `*` in `prose-verbs.tsv` only keeps its group in a rewrite and is never asked of
    a source: "clean*" keeps "wash"; likewise "coat*" (grease), "liner*" (line), "combine*" (add),
    "absorb*" (soak), "roll*" (shape), and "heat*" and "warm*" for the generic "cook". "cube" cuts,
    except after "ice", "into", a number, "inch", "sugar", "bouillon" or "stock". "turn" alone is not
    a verb of the lexicon. "puree" mixes as well as mashes. "sprinkle" and "scatter" also add, and
    "add" keeps them. "spread*" only keeps "add": a source "spread" asks for spreading, except
    "spread ... out" (up to six words on), which asks only for placing it.
  - Verb coverage is a weak signal because a verb said another way ("prepare a baking sheet with
    parchment" for line, "form" for shape, "mix with salt and pepper" for season) is not a lost step:
    it produced 7 of checkpoint 3's 9 false failures.
- **Order** (`out-of-order`): the source steps' amounts should appear in their order.
- **Added amounts** (`added-quantity`): an amount the source, its title and its ingredient lines lack.
  A changed amount fails as `wrong-amount`, `changed-unit` or `changed-range`; a bare added one is
  left to the judge.
- **Length** (`too-short`, `too-long`): a method of 25 words or more rewritten outside 0.4 to 1.3
  times its length.

### 4. Form (fails the rewrite)

No step marker (`step-marker`) and no empty step (`empty-step`).

Any change of a rule or a limit raises `ProseCheck.Version`.

## The stock-phrase index

`recipe.ProseStockPhrase` (`Hash`, `SiteCount`, `Version`) holds every five-word phrase that 3 or
more recipe sites use (`ProseStockIndex.StockSites`): a phrase 2 or more sites use besides the one a
source came from is stock, not copying ("bring a large pot of", "preheat the oven to #"). The hash is
the 64-bit FNV-1a of the phrase's words joined by single spaces.

`ProseStockService` builds it from every stored source document: the description and each method
step of the JSON-LD, phrases inside one step only, each site (the source host without "www.")
counted once per phrase. The same documents always give the same index. A write adds the whole
index as the next version (the primary key is `Version`, `Hash`) and keeps the earlier versions, so
a check run recorded with stock version N can be repeated against version N
(`IProseStockService.LoadAsync(N)`) after the documents have changed. It journals one
`ProseStockRebuild` entry in `audit.AuditLogEntry` (`EntityType` `ProseStockPhrase`, old and new
version and counts). An index that would not change, or one with no phrases, is not written and keeps
its version.

```
POST /api/ProseRewrite/stock/rebuild?apply=false   # dry run: documents, sites, per-site counts, phrases, stock phrases, added, removed, versions
POST /api/ProseRewrite/stock/rebuild?apply=true
POST /api/ProseRewrite/stock/restore?version=N&apply=false   # dry run of the undo
POST /api/ProseRewrite/stock/restore?version=N&apply=true
```

Curation-gated (`CanManageCuration`). The undo of a rebuild is a restore: it writes the phrases of
version N again as the next version and journals one `ProseStockRestore` entry, so a version number
always names one index and is never reused. A version that does not exist or equals the current index
is not written. Since the check's review (CK4P-3) no copy rule reads the index; it is still built and
its version recorded with each re-check run and lane decision, and the re-check still asks for one
before an apply.

## Golden sets

- `prose.tsv`: column `check2` is the v2 outcome (`passed`, a reason the check must give, `!reason`
  for one it must not give, `!quantity` for no amount reason), `status2` its status, `lines` the
  ingredient lines, `stock` `none` to run with an empty index. Rows come from the public-domain
  recipes of the v1 rows, constructed recipes of our own text (`k1`-`k3`) and constructed sentences
  (`e-`, `f-`). Fault rows are made by one deterministic edit of a faithful rewrite and are rule-given
  (`rule:construction`, `ok`); the faithful rewrites themselves were written by a model operator
  (`agent-review-construction-2026-11-20`, `agent`).
- `prose-stock.tsv`: the frozen stock phrases the golden rows use, constructed (no staging data).
- `prose-verbs.tsv`: the verb lexicon itself.
- CI requires that the check refuses (fails) every fault row whose reason a fail rule decides, that
  it marks every fault row of a weak signal at least unsure (such a row passes the lane's rule gate
  and only the judge can refuse it: today `k1-heat-mix`, `k1-added` and `k1-order`), that it rejects
  at most 5 % of faithful rows
  (`The_check_refuses_every_constructed_fault_its_rules_decide_marks_every_weak_signal_fault_unsure_and_rejects_at_most_5_percent_of_faithful_rewrites`),
  and a fault row for every rule.
- `prose-check.tsv`: the 160 recipes read against their source at staging checkpoints 2 and 3 (at
  each, 40 the re-check failed and 40 it passed), one row each, referred to by staging recipe id: the
  whole stored source description and method and the whole shown rewrite (check v4's length and share
  tests read the whole text, so the excerpts of the v3 rows were replaced by the whole texts), the
  ingredient lines it reads, `expect`, `verdict` (the operator's reading), `v2` and `v3` (the reasons
  of the check that ran at that checkpoint) and, for the 27 verdicts v2 or v3 got wrong, `rule` (the
  rule behind the error and the v4 rule that fixes it). `expect` is `passed` (met by a rewrite the
  check does not fail: passed or unsure), `unsure` (a real loss only a weak signal shows: met by a
  rewrite the check does not pass clean) or the reason the check must fail it with (`copied-text` for a
  copy). Rows the operators judged are `agent` (`agent-operator-checkpoint2-2026-11-21`,
  `agent-operator-checkpoint3-2026-11-21`); the doubtful ones were judged by the same standard as the
  judge ("same actions, same amounts, nothing missing, nothing added") and, for copying, as the
  operators read it: a source sentence kept whole, its frame together with the list it carries
  (ingredient names and amounts count), word for word or with a word or two changed, or a kept
  description, is a copy (ck2-76289); sentence-by-sentence light edits that rephrase each sentence's
  frame, with short stock sentences kept, are not (ck2-65767, ck3-39145), `agent` (`agent-review-ck4-2026-11-21`, `agent-review-ck6-2026-11-21`), with the
  reason in `note`. CI requires that the copy rule catches every copy row and no `passed` row, that
  no copy row passes, that at most 1 in 20 of the check's failures is false (each on all 160 rows and
  again on the 127 rows the operators read, whose verdicts were given before v4's limits were set),
  that no weak signal alone
  fails a row, and that no verdict changes under a stock index holding every phrase of both texts
  (`GoldenProseCheckTests`).

Copyrighted source text is never committed. The texts of `prose-check.tsv` are masked: every word
the check reads keeps its form (every word of the check's own vocabulary: the verbs of
`prose-verbs.tsv`, units, numbers and number words, the function, first-person and negation words the
rules look at, the listed ingredient names) and every other word is replaced by a stable pseudo-word
(`qlefor`), the same word always by the same pseudo-word, a plural, "-ies" or "-ly" form following
its stem's pseudo-word. The `rule` and `note` cells are masked the same way inside quotation marks, so
no quoted source wording decodes a pseudo-word. Before the file was written each row was checked in
both forms: the finding (outcome, reasons, fault count, longest run, close stretch, copy share, kept
sentences, length ratio) is identical, so the rows test exactly
what the readable text tests, while no source sentence can be read from them. The readable rows are
kept with the staging evidence, outside the repository. The examples in this document and in the
unit tests are constructed or public-domain text, not the wording of a scraped source.

## The withheld state

A scraped recipe whose shown description and steps fail the check has its prose **withheld**
(`Recipe.ProseWithheld`). This replaces the old remedy of putting it back into quarantine
(`ContainsSourceProse`), which hid the whole recipe and dropped approved recipes out of search and
meal plans.

- **Never served.** No endpoint serves a withheld recipe's stored description or steps, to anyone,
  author and admins included: detail, the recipe lists, search (with steps), suggestions, plans,
  cookbooks, the curation queue, bulk export (JSON, CSV and the PDF print) and the image finder's
  work items. The redaction is `ProseWithholding` (or the same test on `ProseWithheld` inside a query
  projection); `ProseWithheldEndpointTests` walks each of those endpoints with a withheld recipe and a
  shown one, so an endpoint that serves nothing cannot pass. The text stays in the database and in
  the journal, for the undo.
- **Still usable.** Visibility does not test `ProseWithheld`: search, suggestions, shuffle and meal
  plans keep the recipe, and its curation status is left as it is. Auto-approval skips a recipe that
  is not yet approved while it is withheld (`RecipeAutoApprovalPolicy`).
- **Key facts and the source link** stand in for the method. The detail returns `keyFacts`, built by
  rule from the stored source method (`ProseKeyFacts`): each time, temperature and pan size in step
  order with its verb ("oven 350°F · 9 x 13-inch pan · bake 30 to 40 min · cool 30 min"), written in
  one fixed form so no source wording is carried over. The recipe page shows them with "Full method
  at example.com" and the note "We're writing this method in our own words."
  (`data-testid` `recipe-method-withheld`, `recipe-key-facts`, `recipe-key-fact`,
  `recipe-method-source-link`, `recipe-method-withheld-note`).
- **Edits.** A save from the edit form, which never saw the withheld text, keeps the stored
  description when it sends a blank one and the stored steps when it sends none. When a person
  writes both their own steps and their own description (or the stored one is empty), the withhold
  ends, journaled as `ProseWithholdEnd`. A step whose text a person changes or adds carries that
  person as its last editor (a save that sends a step unchanged leaves its editor as it was), and a
  recipe with a `ProseWithholdEnd` row counts as edited by a person, so neither the re-check nor the
  marker cleanup touches the person's own method.
- **Unsure is not withheld.** A recipe the check is only unsure of (weak signals only, see "Rules")
  stays shown. The re-check marks it `Recipe.ProseUnsure` and journals each mark and each clearing as
  `ProseUnsure` (old value, then "true | check N stock M: finding" or "false | check N stock M: why":
  "passed", "failed and withheld: finding", "failed, not withheld in this run: finding", or "not
  checked, steps edited by a person" / "not checked, withhold ended by a person"); a later pass, a
  failure (withheld in that run or not, so a recipe that fails is never counted as unsure and carries
  no mark back when the lane releases it), a person's edit of a step, or the undo clears or restores
  the mark. The Pipeline page counts the
  marked recipes ("Prose the check is unsure of, shown (approved)", `data-testid`
  `pipeline-metric-recipes-prose-unsure`); they are the prose judge's to read when a judge reads
  re-check results, and stay shown and counted until then.
- **A holding state, never an end.** Every withheld recipe is work for the rewrite lane (prose lane
  v2, `prose-lane.md`), approved first; a recipe the lane gives up on is counted by failure class on
  the Pipeline page and taken again when the lane version changes. The Pipeline page shows the count ("Prose withheld (approved)"); the goal is
  none.

### Re-check, apply and undo

`POST /api/ProseRewrite/recheck?version=4&apply=false&maxRecipes=500` checks every shown scraped
recipe of unknown licence (neither quarantined nor withheld) against its source with the current
stock index. It reports the failures by reason (`failedByReason`: `copied-text`, `lost-quantity`,
`lost-ingredient`, `wrong-amount` ...), by status and for approved recipes, the samples (approved
first), the unsure recipes (`unsure`, `unsureApproved`, `unsureByReason`, `unsureSamples`), and lists the
recipes skipped because a person edited a step or ended the withhold. Only the current check version runs; any other
`version` is refused. With `apply=true` it withholds up to `maxRecipes` failures, approved first, in
transactions of 500, each journaled as `ProseWithhold` (old check version, the finding, the check and
stock versions), marks the unsure recipes (`markedUnsure`, `clearedUnsure`), and stamps the passing
and unsure recipes with the check version. An apply is refused while no
stock index has been built. An apply that leaves no failure shown is recorded as a full run
(`ProseRecheckRun`). A recipe the check itself fails on counts as a failure with the reason
`check-error` and is withheld like any other (its prose was never checked, so it is not left shown
under a recorded full run); it is also counted in `errored` and listed in `errors` (recipe and error,
the first 1,000), and the run goes on. The full-run journal names the count, and the weekly job logs
the recipes as a warning. The prose lane then rewrites them like other withheld recipes; a recipe the
lane itself fails on is counted in its `errors` and goes to the back of its queue, and the sweep goes
on. One such recipe used to stop the
whole run (checkpoint 1, K6: an ingredient line stating a size of two lengths, "foil (12 x 18
inches)", gives two amounts over one span, and cutting the span twice ran past the shortened
line); amounts that share or overlap a span are now cut once (`ProseQuantities.WithoutAmounts`).

`POST /api/ProseRewrite/recheck/undo?sinceUtc=&apply=false` releases the recipes withheld since that
time with the check version they had, unless their prose has been replaced since (a later
`ProseRewrite` journal row, or the withhold already ended). Each release is journaled as
`ProseWithholdUndo`, so a repeat dry run shows nothing left (`remaining` 0). It puts every unsure mark
set or cleared since that time back as it was (`restoredUnsure`, journaled as `ProseUnsure`). It still takes back
recipes an earlier re-check put into quarantine (`ProseRecheck` rows). Releasing is the only way to
show withheld text again.

### The weekly job

`ProseRecheckHostedService` runs the re-check with apply when a full run is due: when none is
recorded, when the check or the stock index has a version the last full run did not use (so a deploy
with a new check runs it), or every `ProseRecheck:IntervalDays` (default 7). It never runs before a
stock index exists. It is off until `ProseRecheck:Enabled=true` (turn it on after the first withhold
has been applied by hand), looks every `ProseRecheck:CheckMinutes` (default 60), and the platform
feature `pause-prose-recheck` holds it. A run already going asks the switch before it saves each
batch of 500; once it is on, that batch and the rest are not withheld, no full run is recorded, and the
log says "Prose re-check paused ... after withholding N". Turn the pause on, and wait for that line or
for the next check to log the pause, before a release with the undo, or the run withholds those recipes
again.

### Step markers

`POST /api/ProseRewrite/markers?apply=false&maxRecipes=1000` takes the step markers the check reports
(`step-marker`) out of the steps of scraped recipes: "1. Heat", "Step 2: Mix", "2In a bowl",
"hours.2.Stir", "Toss to mix. 3.", "4) Fold". Nothing else in the step changes; a step that held only
a marker is removed. Steps a person edited, and steps too long for the journal to hold, are left
alone. Each step changed is journaled as `ProseMarkerStrip` on the recipe, with the property
`RecipeStep:{number}` and the whole old text. `POST /api/ProseRewrite/markers/undo?sinceUtc=&apply=false`
puts back every step whose text has not changed since, journaled as `ProseMarkerStripUndo`. The
samples (old and new text of each step) cover only recipes whose prose is shown: the steps of a
withheld or quarantined recipe are stripped and counted but their text is never served.

A number after a reference abbreviation is not a marker: "N. 8", "No. 2", "Art. 47", "Fig. 15"
(also "Nos.", "Nr.", "Arts.", "Figs.", "p.", "pp.", "Vol.", "Chap.", "Ch.") keep their number, and
the check reads them as cross-references. The abbreviations are matched by case: "Fig." and "Figs."
only with a capital, because "the figs. 2. Place them" ends a sentence on the fruit (and "figs. 2
cups" states an amount); the others also in lower case ("no. 3", "pp. 12"). The period of the abbreviation used to pass for a sentence
end, so the dry run on staging (checkpoint 1, K5) stripped Artusi's counts and cross-references
("Uova, N. 4." to "Uova, N.", "stampo del N. 8." to "del N.") and those of the Woman's Institute
books ("Art. 47", "Fig. 15"): 73 of its 1,000 samples. The dry run scans every scraped recipe, books
and myplate included, so its total is larger than the analysis figure of about 3,624 steps on 753
recipes, which counted only shown recipes of unknown licence; `stepsByLicence`, `recipesByLicence`,
`stepsShownUnknownLicence` and `recipesShownUnknownLicence` split the count so the two can be
compared.

## Check v4

Staging checkpoint 3 ran v3's re-check over 21,377 shown recipes: 16,620 failed (77.7 %, expected 39
to 69 %), still led by `lost-step`. The operator read 40 failures and 40 passes against their source
on recipes checkpoint 2 had not read: 9 of the 40 failures were false (N8: verbs said another way,
a negated verb, steps in source order read as out of order, "half a large onion" against "½") and 3
of the 40 passes copied their source (N9). Both bars missed for the second time, so v4 is not v3
patched again: the copy question and the faithfulness question are answered apart, and only the
first and the reliable part of the second fail a recipe.

### The copy measure, its limits and its error

What the copy rule measures is passages of the rewrite that reproduce the source: a long common run,
a close stretch (a local alignment that tolerates a changed word every few words), a large share of
words in 7-word runs, a run shared by the descriptions, or a long sentence kept nearly word for word.
Every word counts in full; the two texts are each one stream; the rewrite's sentences are placed in
the order of the source passages they follow (`InSourceOrder`); stock phrases are not excused. Each
limit sits midway between the highest score of a faithful read recipe and the lowest score of a
copied read recipe that only that test catches:

| Test | Limit | Highest faithful of the 160 | Lowest copy only it catches |
|---|---|---|---|
| common run | 16 words | 15 (an ingredient enumeration in source order) | 16 (four recipes, 37386 and 23988 among them) |
| close stretch, penalty 1.5 | 44.5 | 34 | 53 (36371) |
| close stretch over the rewrite's length (stretch of 15 or more) | 0.4 | 0.31 | 0.5 (36371, and the constructed close paraphrase `k4-swap` of `prose.tsv`) |
| share of words in 7-word runs, 40 words or more | 0.42 | 0.38 | 0.45 (24168) |
| description run | 11 words | 9 | 12 (40965) |
| sentence of 15 words or more | 0.92 | 0.90 | 0.94 (59854) |

The penalty 1.5 separates best: at 2 the highest faithful stretch is 28 against 38 for 36371, at 1.5 it
is 34 against 53.

Error on the 160 read recipes (whole stored texts; the golden rows give the same findings, see "Golden
sets"): of the 45 copies (the 39 the operators read as copying, all 9 copies that passed v2 or v3
among them, and 6 of the 19 recipes checkpoint 3 left doubtful, judged in this round), the copy rule
catches 45; of the 95 faithful rewrites (the operators' clean passes and false failures, and 13
doubtful ones judged faithful), it flags none. The limits are a fit to these rows; the margins of the
run test (15 against 16 words) and the sentence test (0.90 against 0.92) are thin, so the next
staging reading is the held-out test.

The doubtful rows were labelled by the builder after seeing v4's output, so the bars are measured
again on the 127 rows the operators read alone (labels given before v4's limits were set): 0 of 38
copies pass, 0 of 48 failures are false. Every bracket of the table but one is an operator row; the
run test's faithful 15 is two doubtful rows (enumerations in source order with the sentence frame
rephrased). On operator rows alone its gap is 14 (ck3-34248) to 16, so 15 or 16 both separate them,
and four operator copies (76309, 51162, 23988, 37386) are caught by the run test alone at exactly
16: a copy with a 15-word run and every other measure below its limit would pass. The run test
counts words, not frames, so it cannot tell a kept sentence frame around a 16-word enumeration
(ck2-76289, a copy, also caught by the sentence test) from a rephrased one (ck2-24316 and ck2-33306,
judged not copies but failed by it; both have a real loss and fail correctly as recipes).

On constructed near-copies of the 160 sources (`ck6` synthetic set, outside the repository: the source
method itself given back with one word in N replaced by a new word, evenly or at random):

| Words changed | Methods of 80 words or more | Shorter methods |
|---|---|---|
| none, steps shuffled, cut one sentence per step, merged into one step, or amounts retyped | 160 of 160 each | |
| 1 in 6 | 125 of 125 (even and random) | 32 and 30 of 35 |
| 1 in 5 | 123 and 124 of 125 | 31 and 27 of 35 |
| 1 in 4 | 105 and 103 of 125 | 21 and 20 of 35 |
| 1 in 3 | 24 and 49 of 125 | 0 and 1 of 35 |

A text with every third word changed is no longer a copy of its wording, which is why the measure lets
most of those through. The copy verdict of each of the 160 read rewrites is the same when its
sentences are shuffled one per step and when its steps are merged into one: the measure depends on
neither step boundaries nor sentence order.

### Faithfulness by reliable rules, the rest unsure

Fact rules fail a recipe only for an amount, temperature or time of the source missing or changed, and
for an ingredient of the source method named nowhere in the rewrite. The quantity reader was fixed for
"half a" thing, pan sizes with no unit, "6 or 3" alternatives, an amount on each side, amounts in a
cook's bracketed first-person note, and an amount of the rewrite's description that the source's
description states. Verb coverage, step order, added amounts and length mark the recipe unsure: shown,
marked `Recipe.ProseUnsure` (journaled as `ProseUnsure`) and counted on the Pipeline page ("Prose the
check is unsure of, shown"), and the prose judge's to read when a judge reads re-check results; until
then they stay shown and counted. In the rewrite lane an unsure rewrite goes to the judge like one that
passed, and the rule-only rendering (no judge) is written only when the check finds no fault at all.

### Error rates on the 160 read recipes

| Check | Failures | False failures | Copies passed | Unsure |
|---|---|---|---|---|
| v3 | 70 | 11 (15.7 %) | 5 of 45 | (none) |
| **v4** | **62** | **0** | **0 of 45** | 11 |

The 11 unsure: 8 faithful rewrites with a verb said another way, and 3 with a real verb-level loss
(a cover before chilling, turning off the heat), which are the judge's to confirm.

### What share of rewritten recipes each outcome takes

Weighting each checkpoint's 40 failures and 40 passes by the counts of its staging run:

| Basis | Failed | Unsure | Passed |
|---|---|---|---|
| checkpoint 3 (v3 run: 16,620 failed, 4,757 passed) | 57 % (46 to 69) | 18 % (7 to 28) | 25 % (19 to 32) |
| checkpoint 2 (v2 run: 19,113 failed, 2,264 passed) | 53 % (39 to 67) | 5 % (0 to 11) | 43 % (29 to 56) |

So on the shown recipes about 55 % are expected to fail (about 12,000), most as copies (45 of the 62
failures read are copies), and 5 to 18 % to be marked unsure. Both failure samples were drawn from the
re-check's samples, which list approved recipes first; the v4 dry run's own counts replace these
estimates.

## Check v3

Staging checkpoint 2 ran v2's re-check over 21,377 shown recipes: 19,113 failed (89 %). The operator
read 40 failures and 40 passes against their source: 9 of the 40 failures were false (finding M11)
and 6 of the 40 passes still copied their source (M12). Each wrong verdict and the rule behind it:

| Recipe | v2 verdict | Rule behind the error | v3 |
|---|---|---|---|
| 61968 | lost-step | a state after "are" ("until they are covered with ...") read as the verb cover | state context after a form of be, get, have, become |
| 64255 | lost-step | "cook" not kept by "heat"; a result participle ("coating all of ...") read as grease; "sprinkle on" not kept by "add"; "green onions" not kept by "scallions" | "heat*" keeps cook; "coat*" only keeps grease; sprinkle also adds; ingredient other names |
| 32578 | lost-step | "wash" not kept by "clean"; "spread ... out over a sheet" not kept by "place" | "clean*" keeps wash; "spread ... out" only places |
| 32333 | lost-step | a liquid soaking into a grain read as the verb soak | a liquid soaking in asks for nothing |
| 36138 | lost-step | "line" not kept by "a silicone liner"; the noun "puree" read as the verb mash, which blending did not keep | "liner*" keeps line; puree also mixes; "the whole baking sheet" asks for no baking |
| 33374 | lost-step | the noun "ice cubes" read as the verb cube | "cube" after "ice" is the noun |
| 24078 | lost-quantity, added-quantity | "350ºF (180ºC)" with the ordinal sign º (a letter) read as two bare counts; "letting it soak in" read as a soak step | degree signs made one form; soaking in asks for nothing |
| 32520 | lost-quantity, lost-step | a one-block source: a list number "4." after a word with no full stop read as an amount; "home-cut" read as the verb cut | the next list number is a marker; the second word of a "made how" compound asks for nothing |
| 85135 | added-quantity, copied-text | "another minute" not read as 1 minute; a 10-word ingredient enumeration counted as a copied sentence | "another" is 1; ingredient names weigh half; a kept sentence counts from 15 words |
| 34051 | passed (copy) | stock-phrase exclusion cut a 32-word verbatim run into runs under 11 distinctive words | runs on the whole text; stock phrases count like any other words |
| 65868 | passed (copy) | runs were measured inside one rewrite step, so a 23-word run over a step boundary counted as two | one stream of words, step boundaries ignored |
| 38555 | passed (copy) | two common cooking phrases split a 19-word run | as 34051 |
| 35641 | passed (copy) | sentence after sentence of common baking language, each run split by stock phrases | as 34051 |
| 51795 | passed (copy) | a one-block source cut into steps nearly verbatim; without its stock phrases the containment stayed under 0.12 | one stream; share of words in 7-word runs |
| 33922 | passed (copy) | as 51795 | as 51795 |

Two other v2 faults turned up in the right failures: two amounts equal in value and span (the two
lengths of "9x9-inch") were one record, so the second was never matched and was reported lost (51574,
23915), and a bracketed amount after an ingredient ("water (about a teaspoon)") was compared with
another ingredient's line (76309). Both are fixed.

The review of v3 (CK4P) found four more faults before it ran, each with the case that shows it:

| Finding | Rule behind the error | Now |
|---|---|---|
| CK4P-1 | a close paraphrase changing one word in five or six, evenly, keeps no 7-word run and no sentence at 0.9, so the run, share and sentence rules all miss it | the close-stretch rule (score 26); `prose.tsv` row k4-swap |
| CK4P-2 | every source "-ing" form was skipped, "cube" and "turn" were out of the lexicon, and "spread" was kept by any adding verb, so a dropped "whisking constantly", "cube the butter", "turn off the heat" (35195) or "spread the icing" passed | accompanying "-ing" forms are steps, "-ing" forms naming a thing are not; "cube" is back with its noun contexts; the off-heat action; "spread*" only keeps "add" |
| CK4P-3 | a run or sentence made only of stock phrases did not count, but CI measured the limits with no index, so under the deployed index a copy split into common phrases could pass where CI said it failed | no copy rule reads the index; CI checks that no verdict changes under an index holding every phrase |
| CK4P-4 | `rule` and `note` cells of `prose-check.tsv` quoted source wording beside its masked form, which decoded pseudo-words in every row; unit tests and this document quoted source sentences | quotes are masked; examples are constructed or public-domain |

**Why these measures and limits.** Copying is a property of the text, not of its step boundaries or
of how its amounts are typed, so the measures read one stream per side with every amount one token.
The limits sit midway between the closest clean paraphrase and the closest copy of the 80 read
recipes, measured with an empty stock index (the strictest reading):

| Measure | Highest clean or faithful rewrite | Lowest copy it alone must catch | Limit |
|---|---|---|---|
| Weighted run | 13.5 | 15.0 (35641) | 14.5 |
| Share of words in 7-word runs | 0.332 (32520) | 0.418 (33922; 0.451 51574) | 0.37 |
| Description run | 8.0 | 11.5 (40965) | 10 |
| Sentence similarity (15+ words) | below 0.85 | 0.94 (59854, 76289) | 0.9 |
| Close stretch (full texts) | 21.5 (65767), then 16 | 30.5 (the CK4P-1 probe), 29.5 (k4-close) | 26 |

The 7-word share also catches the constructed close paraphrase of `prose.tsv` (k4-close, 0.586),
which an 8-word share would miss (0.333). The close stretch was measured on the full stored texts of
the 80 recipes (the excerpts are shorter and score lower: their highest clean row is 18.5), with a
penalty of 2 per changed word, which separated the two sides best of the penalties tried (1.5: the
clean rows reach 24.5 against the probe's 35; 2.5 and 3: 26 against 19.5 and 21.5 against 18, and
fewer synthetic paraphrases caught). On synthetic close paraphrases
of the 80 sources, one word in N replaced at even spacing, v3 as first built caught 0 of 80 at one in
four and five and 2 of 80 at one in six; with the stretch rule the check catches 43, 74 and 77 of 80
(every source of 80 words or more at one in five and six; the misses are short methods, where the run
and sentence rules have little room). At random spacing (where some runs survive) the figures are 58,
69 and 79 of 80 before and 63, 74 and 79 after. Ingredient names and amounts weigh half so that a shared
enumeration of the ingredient lines, which is a list of facts, never makes a run on its own.

**Error rates on `prose-check.tsv`** (80 rows; a copy row is one whose `expect` is `copied-text` or
`unchanged`, 20 rows):

| Check | Failures | False failures | Copies passed | Other expected failures passed |
|---|---|---|---|---|
| v2 as run on staging (stock index 1) | 40 | 15 (37.5 %) | 6 of 20 | 0 |
| v2 on the excerpts with an empty stock index | 60 | 31 (51.7 %) | 1 of 20 | 1 |
| v3 as first built, on the excerpts (as in CI) | 29 | 0 | 0 of 20 | 2 (24316, 35195) |
| v3 after its review, on the excerpts (as in CI) | 30 | 0 | 0 of 20 | 1 (24316, `known`) |
| v3 after its review, on the full stored texts | 30 | 0 | 0 of 20 | 1 (24316) |

The v2 false failures counted here are the 9 the operator read as wrong and 6 of the 14 doubtful
rows judged faithful in this round. Both bars hold: no copy passes, and 0 of 30 failures are false
(the bar is at most 1 in 20). The limits were set on these same rows, so the rates are a fit, not a
held-out measure; the synthetic paraphrases above and the operator's reading of a fresh sample on
staging are the checks that do not reuse them. The `known` row is a mixture made in one step and
never added in the last: a per-recipe ingredient check cannot see an ingredient dropped from one
step while named in another, and a per-step check would fail faithful rewrites that merge or split
steps. No copy is among the rows the check misses.

**What share of the 89 % is expected to fail under v3.** Of the 40 failures read, 24 still fail
(60 %, 95 % interval about 45 to 74 %): 14 copy their source and 10 lose or change an amount or a
step. Of the 40 passes, 6 (15 %) now fail as copies. Applied to the staging run that is about 11,500
of the 19,113 failures and about 340 of the 2,264 passes, some 11,800 recipes or 55 % of the 21,377
(about 41 to 69 %), instead of 89 %. The 40 failures were drawn from the re-check's samples, which list
approved recipes first, so approved recipes are over-represented in the reading; read the v3 dry run's
own samples before applying. These remaining failures are true faults of the old paraphrase lane, and
every class of them is one the clean-room lane removes by construction: it never sees the source's
wording (copies), puts every amount back from a masked token (lost and changed amounts) and must keep
every lexicon verb and ingredient of each step (lost steps; the lane's fact-sheet and form checks read
the same lexicon, the off-heat action included, and a rule-built sheet names it "turn off the heat").
Withheld is the holding state while the lane works them off.

## Proving it on staging

Operator notes (staging checkpoint 2): the re-check's samples are failures only, capped at 5,000
(a larger `sampleSize` still returns 5,000), so the passes can be listed only after an apply has
stamped them: any apply stamps `ProseCheckVersion` on every passing recipe, and the withhold undo
leaves those stamps. Plan the reading of passes accordingly (apply 200, read, undo). Walking the
JSON, CSV and PDF export of a withheld recipe needs the RecipeBulkOperations capability on, and the
cookbook and meal-plan walk a test household with a plan holding the recipe. A fingerprint of
`recipe."RecipeStep"` must order by (`RecipeId`, `StepNumber`): the rebuild writes the steps back
with plain SQL, and staging's copy holds `Id` 0 on every row, so a fingerprint ordered by `Id` is
not stable.

1. Rebuild the stock index as a dry run: it must read about 21,954 documents on 17 sites (the
   2026-11-20 analysis), then apply.
2. Re-check dry run (`version=4`): expect about 55 % of the shown recipes to fail (46 to 69 %, about
   12,000), mostly `copied-text`, then `lost-quantity`, `wrong-amount` and `lost-ingredient`, and 5 to
   18 % unsure (`unsureByReason` led by `lost-step`); `failedByReason` holds no `lost-step`,
   `out-of-order`, `added-quantity`, `too-short` or `too-long`. Record the totals per reason and the
   approved count; read 40 failures, 40 passes and 20 unsure at random against their source: no pass
   and no unsure recipe may copy its source, and at most 2 of the 40 failures may be false. Any wrong
   verdict becomes a row of `prose-check.tsv`, masked the same way, the readable row to the staging
   evidence.
3. Note the time T, apply with `maxRecipes=200`, and check as an anonymous user in the app at phone
   width: the recipe is found in search, its page shows key facts and the source link and no
   description or steps, and a meal plan can pick it. Then undo from T: `restored` 200, `skipped` 0,
   and a repeat dry run shows `remaining` 0.
