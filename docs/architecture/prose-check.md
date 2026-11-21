# Prose check v3

A scraped recipe's method and description are shown only in our own words. The prose check decides
whether a text is our own: it compares a rewrite with the stored source (`recipe.ScrapedDocument`) by
rule, with no model. Version 2 replaced the bare-number comparison of `RewriteFidelity`; version 3
(`ProseCheck.Version = 3`, the staging checkpoint 2 fix round, "Check v3" below) replaces v2's copy
rules and corrects its coverage and quantity reading. Its review (CK4P) added the close-stretch rule,
stopped stock phrases from excusing a copy and put back four lost-step readings before v3 ever ran,
so the number stayed 3: no recipe carries a v3 stamp and no run was recorded with it. The re-check and the rewrite lane call the same
function, so they always agree. The re-check runs it (see "The withheld state" below), and so does the
clean-room rewrite lane (prose lane v2, `prose-lane.md`) on every rewrite before its judge reads it.

The check is text only: `ProseCheck.Check(source, rewrite, ingredientLines, stockIndex)`. The finding
lists every rule broken with its detail, the copy measures it computed, the check version and the
stock-index version it ran with. The index is recorded only: no rule reads it (see the copy rules).

## Rules

A rewrite passes only when every rule holds.

1. **Quantities, not bare numbers** (`ProseQuantities`). Each amount is read as a value or range,
   a unit family (time, temperature, length, volume, mass, count) and a unit.
   - "a", "an" or "another" before a unit is 1 ("another minute" is 1 minute); "a minute or two" is
     1 to 2 minutes; "a half hour" and "half an hour" are 30 minutes; "1 and 1/2" and "one and a
     half" are 1.5; "1 hour 30 minutes" is one amount; "9 x 13-inch" is two lengths; "5 or so
     minutes" is 5 minutes.
   - A bare number from 200 to 550 after "to" or "at" in a sentence about an oven, heating, baking,
     roasting, broiling or frying is a temperature in degrees: "preheat the oven to 425" equals "425
     degrees" and "425°F", "bake at 350 for 30 minutes" states 350 degrees.
   - Degree signs are one form: "350ºF" (the ordinal sign º, which is a letter) and "350℉" read as
     "350°F".
   - Two amounts are equal when they are of one family and agree within 2 % at both ends once
     converted: ¼ lb is 4 oz, 2 pints are 4 cups, 175 °C is 350 °F. "350 degrees" with no scale
     equals 350 °F or 350 °C.
   - A unit changed inside a family is `changed-unit` (½ tablespoon to ½ teaspoon, 30 seconds to 30
     minutes). A range collapsed, widened or narrowed is `changed-range`. A source amount the rewrite
     lacks is `lost-quantity`; an amount the source, its title and its ingredient lines lack is
     `added-quantity`.
   - Amounts are matched one for one: an amount the source states twice ("bake 10 minutes, turn and
     bake 10 minutes more") must be stated twice, or the second is `lost-quantity`. An oven
     temperature restated counts once.
   - Step markers ("2.", "Step 3", "2In a bowl", a trailing "3.") and cross-references ("as in step
     3", "No. 156") are not amounts. In a source whose method is one numbered block ("1. Preheat
     ...2. Toss ...3. Melt ..."), the next number of the list is a marker too where it stands after a
     word and before a capital with no full stop of its own ("chile flakes 4. Pour"). A count of exactly one, and a spelled-out count before "sides",
     "edges", "ends" or "equal", is soft: it may be dropped or added ("in half" as "two equal
     parts"), but where both texts state a soft count before the same word, the source's must be
     kept ("two equal parts" as "three equal parts" is `lost-quantity`).
   - An amount in brackets right after another of its family ("350°F (175°C)", "9-inch (23 cm)") is
     a conversion and may be dropped; one stated must still match. Any other bracketed amount
     ("(350° F.)" alone, "1 can (15 oz)") is required.
2. **Amounts against the ingredient lines.** An amount up to 4 words before an ingredient's name
   (the last name word of a line, matched by the recipe's own line words) is compared with that
   ingredient's amounts only; an amount in brackets right after an ingredient's name belongs to that
   ingredient ("a little milk (about a tablespoon) with the egg" is the milk's tablespoon). If the
   method states an amount of that family for that ingredient,
   the rewrite's must equal it, or it is `wrong-amount`: where the method and its own lines
   disagree, the method's amount is the one to keep, and two amounts swapped between ingredients
   ("1 cup flour with 2 cups milk" as "2 cups flour and 1 cup milk") are two `wrong-amount`s. If the
   method states none, the line's amount, the sum of that ingredient's lines, or a method amount next
   to no ingredient is allowed; any other amount of the line's family is `wrong-amount`, and a method
   amount that belongs to another ingredient counts as added. The ingredient lines are the source's
   `recipeIngredient` (`ProseCheck.SourceLines`).
3. **Coverage.**
   - Every cooking verb of the source must be kept (`lost-step`). Verbs come from the synonym groups
     of `prose-verbs.tsv` (compiled into Nom.Orch): "heat", "warm" and "preheat" are one action,
     "heat" and "mix" are not. A word may be in several groups ("whisk" mixes and beats); a source
     verb is kept when the rewrite holds any word of any of its groups. A word right after an
     article, a heat level or "from", "off", "over" or "for" is a noun ("over medium heat"); in a
     source, one after "is", "are", "was", "were", "has", "have", "had", "get" or "become" (adverbs
     skipped) is a state and asks for nothing ("until they are covered", "until the cheese has
     melted"), while a state in a rewrite ("once the frosting has chilled") keeps its group. A mixing
     verb followed by "in" ("stir in the flour") also adds.
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
     way, with the heat "turned off", or by taking the food off or away from the heat ("take the pan
     off the heat", "remove from the heat"). Taking food off the heat in a source asks for nothing,
     since moving the pan is not a separate cooking step.
   - A form marked `*` in `prose-verbs.tsv` only keeps its group in a rewrite and is never asked of
     a source: "clean*" keeps "wash" ("Clean the potatoes") though "comes out clean" asks for no
     washing; likewise "coat*" (grease), "liner*" (line), "combine*" (add), "absorb*" (soak),
     "roll*" (shape), and "heat*" and "warm*" for the generic "cook" (a source "cook" is kept by
     "heat", a source "heat" is not kept by "fry"). "cube" cuts, except after "ice", "into", a
     number, "inch", "sugar", "bouillon" or "stock" ("ice cubes", "1-inch cubes"). "turn" alone is not
     a verb of the lexicon ("turn brown", "turn out"). "puree" mixes as well as mashes. "sprinkle" and
     "scatter" also add, and "add" keeps them ("sprinkle on the radishes" as "add the radishes").
     "spread*" only keeps "add": a source "spread" asks for spreading (smearing), which pouring does
     not keep, except "spread ... out" (up to six words on), which lays food out and asks only for
     placing it.
   - Every ingredient a source step names must be in the rewrite (`lost-step`), under its name or a
     listed other name (green onion and scallion, cilantro and coriander, chickpea and garbanzo,
     zucchini and courgette, eggplant and aubergine, shrimp and prawn).
   - The source steps' amounts must appear in their order (`out-of-order`).
   - A method of 25 words or more must be rewritten at 0.4 to 1.3 times its length (`too-short`,
     `too-long`).
4. **Copy, on the whole text.** The rewrite's description and steps are read as one stream of
   words and the source's description and method as another (`ProseCheck.CopyTokens`), so where
   the steps begin and end does not matter: a one-block source cut into steps, or two steps merged,
   is measured the same. Words are lower-cased, apostrophes dropped, and every amount the quantity
   reader finds is one token `#` however it is written ("½ cup", "0.5 cup", "half a cup", "1/2 c."),
   a bracketed conversion left out; any other number or number word is `#` too. A common run is a
   stretch of words the two streams share in order. In weighing a run, a word of the recipe's
   ingredient names and an amount count half (`ListWordWeight` 0.5), so a shared ingredient
   enumeration ("walnut oil, cider vinegar, maple syrup, shallot, tarragon, grainy mustard, garlic,
   salt and pepper") is not a copy of expression.
   - **Run:** no common run may weigh 14.5 or more (`CopiedRun`).
   - **Share:** in a rewrite of 40 words or more (`MinimumWordsForShare`), the words that sit in
     common runs of 7 or more words (`SharedRun`), weighed the same way, must make up less than 0.37
     of the rewrite (`MaximumCopiedShare`). This catches a copy whose runs are broken by a changed
     word now and then.
   - **Close stretch:** no stretch of the rewrite may follow the source in order with a score of 26 or
     more (`CloseStretchLimit`), where each word kept in order adds its weight (as above) and each word
     changed, added or dropped costs 2 (`StretchPenalty`). It is a local alignment of the two streams
     (Smith-Waterman), so it needs no run of any length and no step boundary: a stretch grows while at
     least two of every three words are kept in place. This catches the close paraphrase that changes
     one word in five or six, evenly, so that no run reaches 7 words and no sentence keeps 0.9 of its
     words (review CK4P-1); it needs a stretch of about 50 words at one word in six, 65 at one in
     five.
   - **Description:** the rewrite's description may share no run weighing 10 or more with the source
     description (`DescriptionRun`): a description is a sentence or two, so 10 is most of a sentence.
   - **Sentence:** no rewrite sentence of 15 words or more (`SentenceWords`) may match a source
     sentence at 0.9 of its words in order (`SentenceSimilarity`, the longest common subsequence over
     the longer of the two).
   - Stock phrases count like any other words. The limits were set with no stock excuse, which is
     how CI measures the bars, so the configuration that runs is the one measured: an excuse for runs
     made only of stock phrases let a copy split into common baking phrases pass (review CK4P-3).
   - Steps word for word equal to the source method are `unchanged`.
   All copy rules report `copied-text`. The limits are set on the 80 recipes read at staging
   checkpoint 2 (see "Check v3").
5. **Form.** No step marker (`step-marker`) and no empty step (`empty-step`).

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
- CI requires 0 false accepts on fault rows and at most 5 % false rejects on faithful rows
  (`Check_v2_lets_no_constructed_fault_through_and_rejects_at_most_5_percent_of_faithful_rewrites`),
  and a fault row for every rule.
- `prose-check.tsv`: the 80 recipes read against their source at staging checkpoint 2 (40 the v2
  re-check failed, 40 it passed), one row each, referred to by staging recipe id: the source and
  rewrite excerpts the verdict needs (cut down by keeping a rewrite sentence and its matching source
  sentence together, and only while the check's finding and v2's staging reason stay the same), the
  ingredient lines it reads, `expect` (`passed`, or the reason the check must give: `copied-text`
  for a copy), `verdict` (the operator's reading), `v2` (v2's reasons on staging) and, for the 15
  verdicts v2 got wrong, `rule` (the rule behind the error). Rows the operator judged are `agent`
  (`agent-operator-checkpoint2-2026-11-21`); the 14 it left doubtful were judged in this fix round
  by the same standard as the judge ("same actions, same amounts, nothing missing, nothing added"),
  `agent` (`agent-review-ck4-2026-11-21`), with the reason in `note`. One row is `known` (baseline
  1, 24316): a mixture made in one step and never added in the last, which a per-recipe ingredient
  check cannot see. CI requires that no copy row passes, that at most 1 in 20 of the check's failures
  is false, and that no verdict changes under a stock index holding every phrase of both texts
  (`GoldenProseCheckTests`).

Copyrighted source text is never committed. The excerpts of `prose-check.tsv` are masked: every word
the check reads keeps its form (the verbs of `prose-verbs.tsv`, units, numbers and number words,
the function words the rules look at, the listed ingredient names) and every other word is replaced
by a stable pseudo-word (`qlefor`), the same word always by the same pseudo-word, with its plural
or "-ly" ending kept. The `rule` and `note` cells are masked the same way inside quotation marks, so
no quoted source wording decodes a pseudo-word. Before the file was written each row was checked in
both forms: the finding (reasons, copy measures, close stretch) is identical, so the rows test exactly
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
- **A holding state, never an end.** Every withheld recipe is work for the rewrite lane (prose lane
  v2, `prose-lane.md`), approved first; a recipe the lane gives up on is counted by failure class on
  the Pipeline page and taken again when the lane version changes. The Pipeline page shows the count ("Prose withheld (approved)"); the goal is
  none.

### Re-check, apply and undo

`POST /api/ProseRewrite/recheck?version=3&apply=false&maxRecipes=500` checks every shown scraped
recipe of unknown licence (neither quarantined nor withheld) against its source with the current
stock index. It reports the totals by reason (`failedByReason`: `wrong-amount`, `lost-step`,
`copied-text` ...), by status and for approved recipes, the samples (approved first), and lists the
recipes skipped because a person edited a step or ended the withhold. Only the current check version runs; any other
`version` is refused. With `apply=true` it withholds up to `maxRecipes` failures, approved first, in
transactions of 500, each journaled as `ProseWithhold` (old check version, the finding, the check and
stock versions), and stamps the passing recipes with the check version. An apply is refused while no
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
`ProseWithholdUndo`, so a repeat dry run shows nothing left (`remaining` 0). It still takes back
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
2. Re-check dry run (`version=3`): expect about 11,800 failures (41 to 69 % of the shown recipes),
   mostly `copied-text`, then `lost-quantity`, `wrong-amount` and `lost-step`. Record the totals per
   reason and the approved count; read 40 failures and 40 passes at random against their source:
   no pass may copy its source, and at most 2 of the 40 failures may be false.
3. Note the time T, apply with `maxRecipes=200`, and check as an anonymous user in the app at phone
   width: the recipe is found in search, its page shows key facts and the source link and no
   description or steps, and a meal plan can pick it. Then undo from T: `restored` 200, `skipped` 0,
   and a repeat dry run shows `remaining` 0.
