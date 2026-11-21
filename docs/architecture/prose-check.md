# Prose check v2

A scraped recipe's method and description are shown only in our own words. The prose check decides
whether a text is our own: it compares a rewrite with the stored source (`recipe.ScrapedDocument`) by
rule, with no model. Version 2 (`ProseCheck`, `ProseCheck.Version = 2`) replaces the bare-number
comparison of `RewriteFidelity`. The re-check and the rewrite lane are to call the same function, so
they always agree. The re-check runs v2 (see "The withheld state" below), and so does the clean-room
rewrite lane (prose lane v2, `prose-lane.md`) on every rewrite before its judge reads it.

The check is text only: `ProseCheck.Check(source, rewrite, ingredientLines, stockIndex)`. The finding
lists every rule broken with its detail, the copy measures it computed, the check version and the
stock-index version it ran with.

## Rules

A rewrite passes only when every rule holds.

1. **Quantities, not bare numbers** (`ProseQuantities`). Each amount is read as a value or range,
   a unit family (time, temperature, length, volume, mass, count) and a unit.
   - "a" or "an" before a unit is 1; "a minute or two" is 1 to 2 minutes; "a half hour" and "half an
     hour" are 30 minutes; "1 and 1/2" and "one and a half" are 1.5; "1 hour 30 minutes" is one
     amount; "9 x 13-inch" is two lengths.
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
     3", "No. 156") are not amounts. A count of exactly one, and a spelled-out count before "sides",
     "edges", "ends" or "equal", is soft: it may be dropped or added ("in half" as "two equal
     parts"), but where both texts state a soft count before the same word, the source's must be
     kept ("two equal parts" as "three equal parts" is `lost-quantity`).
   - An amount in brackets right after another of its family ("350°F (175°C)", "9-inch (23 cm)") is
     a conversion and may be dropped; one stated must still match. Any other bracketed amount
     ("(350° F.)" alone, "1 can (15 oz)") is required.
2. **Amounts against the ingredient lines.** An amount up to 4 words before an ingredient's name
   (the last name word of a line, matched by the recipe's own line words) is compared with that
   ingredient's amounts only. If the method states an amount of that family for that ingredient,
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
     article, a heat level or "from", "off", "over" or "for" is a noun ("over medium heat"). A mixing
     verb followed by "in" ("stir in the flour") also adds.
   - Every ingredient a source step names must be in the rewrite (`lost-step`).
   - The source steps' amounts must appear in their order (`out-of-order`).
   - A method of 25 words or more must be rewritten at 0.4 to 1.3 times its length (`too-short`,
     `too-long`).
4. **Copy, ignoring stock phrases.** Words are lower-cased and every word holding a digit or a
   fraction is `#`; phrases are five words (`ProsePhrases`).
   - Distinctive containment, the share of the source's distinctive phrases (description and method)
     found in the rewrite, must be at most 0.12. It is counted when the source has 20 or more
     distinctive phrases; below that the run and sentence rules carry the check.
   - No run of copied distinctive phrases may be longer than 11 words.
   - No source sentence of 10 or more distinctive words may appear whole, or with 80 % or more of
     those words inside distinctive phrases the rewrite shares (`ProseCheck.SentenceCopiedShare`):
     a sentence copied with a word or two changed is still copied. This is what catches a short
     method below the containment floor.
   - Steps word for word equal to the source method are `unchanged`.
   All three copy rules report `copied-text`. The limits C = 0.12 and R = 11 are the design's
   starting values, to be set from the staging proof at the 98th percentile of clean-room output and
   kept below every judged wholesale copy (0.28).
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
is not written. With no index built the check treats every phrase as distinctive, which is the
strict reading.

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

Copyrighted source text is never committed. Cases from scraped recipes belong in a staging-only set
that refers to recipe ids.

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

`POST /api/ProseRewrite/recheck?version=2&apply=false&maxRecipes=500` checks every shown scraped
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

## Proving it on staging

1. Rebuild the stock index as a dry run: it must read about 21,954 documents on 17 sites (the
   2026-11-20 analysis), then apply.
2. Re-check dry run: compare with the analysis: of the 2,422 copied-only failures in the listed
   5,000, about 939 had at most 3 words outside phrases of 3 or more sites; v2 should no longer fail
   those for copying. Record the totals per reason and the approved count; read 60 failures and 20
   passes at random (wrong-amount and lost-step must be real).
3. Note the time T, apply with `maxRecipes=200`, and check as an anonymous user in the app at phone
   width: the recipe is found in search, its page shows key facts and the source link and no
   description or steps, and a meal plan can pick it. Then undo from T: `restored` 200, `skipped` 0,
   and a repeat dry run shows `remaining` 0.
