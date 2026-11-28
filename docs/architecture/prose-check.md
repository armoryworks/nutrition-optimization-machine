# Prose check v8

A scraped recipe's method and description are shown only in our own words. The prose check decides
whether a text is our own: it compares a rewrite with the stored source (`recipe.ScrapedDocument`) by
rule, with no model. Version 2 replaced the bare-number comparison of `RewriteFidelity`; version 3
(the staging checkpoint 2 fix round, "Check v3" below) replaced v2's copy rules. Version 4
(`ProseCheck.Version = 4`, the staging checkpoint 3 round, "Check v4" below) separates the two
questions v3 mixed: whether the text is **copied**, and whether it is **faithful**, decided by rule
only where a rule is reliable. The weak signals that made v3 fail faithful rewrites no longer fail a
recipe: they mark it **unsure**, an outcome that withholds nothing. Version 5 (the staging checkpoint 4
round, "Check v5" below) makes each fact rule earn the right to fail a recipe (see "Fact classes").
Version 6 (the staging checkpoint 5 round, "Check v6" below) gave the whole-text copy measures two
lines each and a **copy-unsure** band withheld like a copy. Version 7 (`ProseCheck.Version = 7`,
`cleanup2/ck11-prose`, "Check v7" below) follows the owner's answer of 2026-11-23 on the copy standard:
the unit judged is the **sentence**, read in its step and in the whole text; a recipe mostly made of
copied sentences is withheld, one with some is withheld too until the rewrite lane has reworded those
sentences, its work list (outcome **repair**), since a recipe is shown only when all its sentences pass;
the copy-unsure band is gone; and an amount the recipe's own ingredient line contradicts
(`contradicts-lines`) fails any shown text, description included, old paraphrases included.
Version 8 (`ProseCheck.Version = 8`, `cleanup2/ck15-prose`, "Check v8" below) keeps v7's rules but stops
sorting the earlier paraphrases by them: the re-check withholds every earlier paraphrase by its origin until the
reword lane has reworded its whole method, the check's copy verdict on old text only orders the lane's queue, and
the lane's own new text must also pass a strict copy check (`ProseRebuilt`) where a synonym swap in the source's
frame is a copy. Where the sections below say a recipe with copied sentences is withheld for repair, v8 withholds
every earlier paraphrase instead.
The re-check and the rewrite lane call the same function, so they always agree. The re-check runs it
(see "The withheld state" below), and so does the reword lane (prose lane v3, `prose-lane.md`) on every
reworded sentence and on the whole text before its judge reads it.

The check is text only: `ProseCheck.Check(source, rewrite, ingredientLines, stockIndex)`. The finding
lists every rule broken with its detail, the sentence reading (`Finding.Sentences`), the whole-text
copy measures (recorded only), the check version and the stock-index version it ran with, and its
outcome (`Finding.Outcome`):

| Outcome | When | What happens |
|---|---|---|
| `failed` (`Finding.Failed`) | mostly copied (`copied-text`) or word for word (`unchanged`), an amount the recipe's own line contradicts (`contradicts-lines`), a form rule broken, or a fact rule whose class is proven (`ProseCheck.ProvenClasses`) | the re-check withholds the prose; the lane rewords it from its source as a whole |
| `repair` (`Finding.Repair`) | nothing fails, but some sentences are copies (`copied-sentences`) | withheld in the same batch as the failures (`maxRecipes` caps both); the re-check marks `Recipe.ProseCopiedSentences` with their count (journaled `ProseCopiedSentences`) and the Pipeline page counts it; the lane rewords those sentences only and shows the recipe when all of it passes |
| `unsure` (`Finding.Unsure`) | only weak signals (`ProseCheck.UnsureReasons`) and faults of fact classes not proven | the prose stays shown, is marked `Recipe.ProseUnsure` and counted; it is the prose judge's to read |
| `passed` (`Finding.Passed`) | no fault at all | shown |

A finding can be both `repair` and `unsure`; its outcome then reads `repair` and it is marked both ways.
The index is recorded only: no rule reads it.

## Rules

### 1. Copy, sentence by sentence (`ProseSentences`)

Each sentence of the rewrite (its description, then each step; split after `.`, `!`, `?` or `;`) is set
beside the source passage it follows most closely: one source sentence, two adjacent ones it may merge,
or the part of one it may split off (the longest common subsequence of their words, `#` for every
amount as in the old measures, over the longer of the sentence and the source span it covers). Its words
are then read with the facts set aside (`ProseSentences.IsFact`): amounts and numbers, units, every word
of the recipe's ingredient lines, and the craft's own words (`ProseSentences.CraftWords`: heat and its
levels, the oven and its scale, vessels and tools, signs of doneness such as golden, tender, set).

| Term | Rule | Line |
|---|---|---|
| Copy (`Sentence.Copy`) | a sentence of 10 words or more (`MinWords`) that is the source's sentence with single words swapped, dropped, added or moved: no change longer than 2 words (`MaxEdit`), at least 0.6 of it in the source's order (`MinSimilarity`), and at least 6 of the source's own words beyond the facts kept (`MinFrame`); or any sentence holding a word-for-word run of 8 such words (`MinFrameRun`) | as set out |
| Kept nearly word for word (`Sentence.NearVerbatim`) | a sentence of 5 words or more, at least 0.8 of it the source's in order, no change longer than 2 words | as set out |
| Mostly copied (`Reading.Mostly`) | it holds a copy and at least 0.4 of its sentences are copies or kept nearly word for word (`MostlyCopied`), or at least four sentences and half of them are kept nearly word for word (`MostlyVerbatimSentences`, `MostlyVerbatim`: a method made of the source's sentences, however short) | `copied-text`, withheld |
| Some copied | not mostly, but one copy or more | `copied-sentences`, shown, the lane's work list |

So a sentence that is mostly facts (an oven temperature, a short list of foods into a bowl) keeps too
few of the source's own words to be a copy; a sentence rebuilt from understanding (another frame,
clauses rebuilt, merged or split) changes more than single words; and a text that is the source's
sentences one after another is the source's text even where each is short. Steps word for word equal to
the source method stay `unchanged`. Stock phrases count like any other words.

The six whole-text measures of v6 (`ProseCheck.CopyMeasures`: run, stretch, stretch share, share,
description run, sentence) are still computed and journaled with every withhold, but no line is drawn
on them.

### 2. Facts (fail the rewrite while their class is proven)

Each fact reason is a class of its own: `lost-quantity`, `wrong-amount`, `changed-unit`,
`changed-range` and `lost-ingredient` (`ProseCheck.FactClasses`). A fault of a class fails the
rewrite only while the class is proven on its judged rows (see "Fact classes"); otherwise it marks
the rewrite unsure. The rules:

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
     2 minutes on each side"). An amount followed by "each" ("1 teaspoon each of thyme, sage and
     parsley") stands for one statement per item of its list. An amount the source restates after
     "the" ("the 8x8 pan") may be matched again.
   - Utensils (since v5). A length or capacity followed, through words such as "round", "baking",
     "deep" or "inch", by a cooking vessel (pan, dish, skillet, tin, pot, saucepan, mold, ramekin,
     casserole, sheet, tray, cutter, slow cooker) is the vessel's size: required, but stated once
     however often the source restates it. A size of a holding vessel (measuring cup, bowl, jug,
     pitcher, mug, plate, platter, container: "a 4-cup measuring cup") is never an amount, required or
     compared. A count before "at a time" or before a tool ("two forks", "two spatulas") is soft, so
     "two at a time" and "in pairs" agree. Two counts joined by "or" ("four or six pieces") are kept
     by either. A ratio ("50/50"), a percentage and an ordinal ("2nd") are not amounts. Counts of two
     different count words ("a pinch" and "a slice") are never equal.
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
2. **Amounts against the ingredient lines.** An amount is paired only with an ingredient named
   in its own clause: up to 4 words after it, before the next amount and before a comma, full stop,
   colon, semicolon or bracket (since v5). A time or a temperature is never an ingredient's amount,
   nor is a utensil size, and an ingredient's name followed by "water" or "liquid" ("1/2 cup of pasta
   water") names that liquid, not the ingredient. An amount up to 4 words before an ingredient's name
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
   An extract, essence or leaf is also kept by the word before it in its line ("vanilla" keeps
   "vanilla extract"); words in brackets of the source method ask for no ingredient, and a line's
   name stops before "for" ("sauce for serving" is a sauce).

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

No step marker (`step-marker`) and no empty step (`empty-step`). These are facts of the rewrite's
shape, not readings of its meaning, so they are not classes and fail it as before.

### Fact classes

A fact class fails a recipe only while its judged rows prove it, the way a link re-check class
writes (`link-recheck.md`). Every row of `prose-check.tsv` on which the check fires a fact class
judges that class in its `classes` cell: `right` when at least one of the class's faults on the row
names a real loss or change of that kind, `false` when none does, `doubtful` when the reader cannot
tell. A class is proven (`ProseCheck.ClassMeetsBar`) when at least 40 checkpoint-judged rows
(`ProseCheck.ClassMinimumJudged`, status `agent`) judge it and at least 95 % of them
(`ProseCheck.ClassPrecisionBar`) and of all its rows are right; a doubtful row counts as judged and
not right; every rule-given row (status `ok`) must be right and none counts toward the minimum.

The figures are read from a committed record, `scores/prose-check-classes.json`, built into Nom.Orch
and written only by the scorer:

```
dotnet run --project nom-api/Nom.Import -- --prose-classes [--rows] [--record]
```

It checks every judged row as the re-check does, prints each class's rows, precision, doubtful rows
and whether it fails recipes, and with `--record` writes the record (refused under CI, and while any
firing is unjudged). `ProseCheckClassRecord.Evaluate` never trusts the record's own flags: it decides
each listed class again from its counts, and a record of another check version proves nothing.
`GoldenProseCheckClassTests` re-scores the rows and refuses a record that differs, so a class is
proven by the data, never by an edit. A change of the check or of the judged rows means scoring and
committing the record again.

Faults of a class not proven are kept in the finding (`Finding.UnprovenReasons`), named in the
re-check's `unsureByReason` and journal, and the recipe is marked `Recipe.ProseUnsure`; in the
rewrite lane the judge reads such a rewrite like any unsure one.

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
- `prose-check.tsv`: the 320 recipes read against their source at staging checkpoints 2 and 3 (at
  each, 40 the re-check failed and 40 it passed), 4 (40 it failed) and 5 (40 it failed, 40 it passed
  and 40 it was unsure of; `ck5-`, column `v5`), one row each, with the copy judgement of each in
  column `copy` (`copy`, `doubtful` or `not`; see "Check v6"), referred to by staging recipe id: the
  whole stored source description and method and the whole shown rewrite (check v4's length and share
  tests read the whole text, so the excerpts of the v3 rows were replaced by the whole texts), the
  ingredient lines it reads, `expect`, `verdict` (the operator's reading), `v2`, `v3` and `v4` (the
  reasons of the check that ran at that checkpoint), for the 35 verdicts v2, v3 or v4 got wrong, `rule`
  (the rule behind the error and the rule that fixes it), and `classes` (the judgement of each fact
  class the check fires on the row, see "Fact classes"). `expect` is `passed` (met by a rewrite the
  check does not fail: passed or unsure), `unsure` (a real loss only a weak signal shows: met by a
  rewrite the check does not pass clean) or the reason the check must give (`copied-text` for a
  copy), failing the rewrite, or only marking it unsure while the reason is a fact class not proven. Rows the operators judged are `agent` (`agent-operator-checkpoint2-2026-11-21`,
  `agent-operator-checkpoint3-2026-11-21`); the doubtful ones were judged by the same standard as the
  judge ("same actions, same amounts, nothing missing, nothing added") and, for copying, as the
  operators read it: a source sentence kept whole, its frame together with the list it carries
  (ingredient names and amounts count), word for word or with a word or two changed, or a kept
  description, is a copy (ck2-76289); sentence-by-sentence light edits that rephrase each sentence's
  frame, with short stock sentences kept, are not (ck2-65767, ck3-39145), `agent` (`agent-review-ck4-2026-11-21`, `agent-review-ck6-2026-11-21`), with the
  reason in `note`. CI requires that the copy rule catches every copy row and no `passed` row, that
  no copy row passes, that at most 1 in 20 of the check's failures is false (each on all 200 rows and
  again on the rows the operators read, whose verdicts were given before the limits were set, with
  the committed proven classes and again with every fact class taken as proven),
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
  one fixed form so no source wording is carried over. Since staging checkpoint 8 a temperature never takes the verb of
  work on a pan ("line 450°" was a pan lining read as an oven), and how long the dish keeps is no fact (a time in a
  storing sentence after its storing word, or days and weeks with "store", "keeps", "lasts", or "up to" in the cold: "cover 4 days", "chill 2 weeks"; a bake time before "storing" and "marinate up to 2 days" stay facts). Since staging
  checkpoint 9 (U16) the most heat a pan, paper or dish is safe up to is no fact ("baking paper ... safe up to 450 F"
  gave a bare `450°`), when only joining words ("up to", "for temperatures of") or units stand between the
  word of being safe and the temperature; a heat the sentence names after an oven-safe pan stays a fact ("In an
  oven-safe skillet, bake at 400°F"), and a time of thawing is named "thaw", never by a freezing word of its sentence ("After freezing
  it ..., let it thaw for about 10-15 minutes" gave `freeze 10 to 15 min`). The
  ingredient lines are served with their HTML entities read (`ProseWithholding.Shown`: "&frac14;" is ¼), and so are
  the drink lines of the alcohol note and the diet match (U15: the note served "1 &frac12; ounces clear rum"). The recipe page shows them with "Full method
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
- **Mostly copied is withheld; some copied sentences are withheld until repaired.** From check v7 the
  copy-unsure band is gone (the `Recipe.ProseCopyUnsure` column with it). A recipe mostly made of
  copied sentences fails as `copied-text` and is withheld; its `ProseWithhold` journal row reads
  "check N stock M: failed: finding | measures ...", the finding naming how many sentences are copied
  or kept. A recipe with only some copied sentences is withheld as well (the owner's answer of
  2026-11-23: a recipe is shown when all its sentences pass), in the same ordered batch as the failures,
  so `maxRecipes` bounds both; its `ProseWithhold` row reads "check N stock M: repair: ...", and the
  re-check marks `Recipe.ProseCopiedSentences` with the count and journals each mark and each clearing
  as `ProseCopiedSentences` (old count, then "n | check N stock M: withheld until reworded: the
  reading: the sentences"); the dry run counts `repair`, `repairApproved` and `repairSentences`, the
  apply `withheld` (all), `withheldRepair`, `markedRepair` and `clearedRepair`, and `remaining` counts
  failures and repairs not yet withheld. A recipe beyond the batch is neither withheld nor marked. A
  later pass, a whole withhold, a person's edit (journaled as not checked) and a rewrite the lane
  writes clear the count; a facts-only withhold keeps it, so undoing that withhold shows the recipe
  still marked; and the undo puts every count back as it was (`restoredRepair`). The Pipeline page
  counts these recipes ("Withheld until the rewrite lane rewords their copied sentences (approved)",
  `data-testid` `pipeline-metric-recipes-prose-repair`; a facts-only withheld one is not counted, as the
  lane rewords it as a whole). The reword lane takes them with the withheld recipes and rewords only
  those sentences.
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

## Check v8 (2026-11-24, `cleanup2/ck15-prose`): old text withheld by origin, new text checked strictly

The proof of check v7 (`cleanup2/ck12-prose-proof`) found that no word-order rule can tell an earlier paraphrase's
light edit from a rebuilt sentence at the bars: v7 marks about 37 % of the copied sentences, and of 40 recipes it
called clean, 15 held copied sentences. So the earlier paraphrases are no longer sorted by the check.

**Old text, by origin (`ProseRecheckService`).** Every web recipe whose shown method is an earlier paraphrase of its
stored source is withheld (`ProseWithheld`) until the reword lane has reworded its whole method. An earlier
paraphrase is a scraped recipe of no clear licence (anything but `PublicDomain`, `CreativeCommons` or
`UserSubmitted`), not quarantined, with a stored JSON-LD source, whose steps no person wrote or edited (and whose
withhold no person ended), and whose text the reword lane did not write (its latest `ProseRewrite` state row, not
undone, is tagged with a lane version starting `v4-`, `ProseLanePrompts.IsWholeMethodTag`). The rule:

- The check still reads every earlier paraphrase, shown or already withheld, but its verdict (`mostly`, `some`,
  `none`, or `check-error`) only orders the lane's queue: the count of sentences it reads as copied or kept nearly
  word for word is stored as `Recipe.ProseCopiedSentences` (the queue figure, journaled as `ProseCopiedSentences`
  "n | check 8 stock M: queue figure: ..."). The lane takes approved withheld recipes first, then the others, each
  by that figure, most first. No showing decision depends on it, and the repair-by-sentence mode is gone. The figures
  of recipes already withheld are set only by an apply that withholds every earlier paraphrase still shown, so a
  trial apply of a batch writes no more than its batch.
- The apply withholds approved recipes first, then the most copied, in batches of 500, each journaled as
  `ProseWithhold` "check 8 stock M: earlier paraphrase, withheld until reworded as a whole: <verdict>: <finding>";
  a withhold clears an unsure mark (journaled). The undo by stamp (`POST /api/ProseRewrite/recheck/undo`) releases
  the recipes and puts every queue figure back.
- The result names what is exempt and why: `exemptClearLicence`, `exemptLaneWritten`, `skippedEditedByPerson`,
  `skippedNoSource`; `byCopy` gives the verdicts, `examinedWithheld` the already-withheld paraphrases read for their
  figure, `queueFigures` the figures that change.
- The version moved to 8 so the weekly job runs the new rule after a deploy (`Due`: the last run's check version
  differs). The text rules are v7's; the v8 candidate of `cleanup2/ck12-prose-proof` (`fa19192d`) was never adopted.

**On the staging copy** (read-only role, 2026-11-24): 122,213 scraped recipes not quarantined. Exempt: 100,836
public domain (clear licence); among the 21,377 of unknown licence none has a step a person edited, none a withhold a
person ended, none lacks a stored source, and none was written by the lane. So every one of the 21,377 is an earlier
paraphrase: the 8,727 shown now (1,221 approved) are withheld by the apply, and the 12,650 already withheld (12,581
facts-only, 69 permission-needed; 2,308 approved) get their queue figure. The check's verdict on them: shown 2,331
mostly, 4,424 some, 1,972 none; already withheld 3,983 mostly, 5,431 some, 3,236 none. 610 quarantined recipes stay
quarantined and are the lane's as before. 6,278 of the shown recipes are from the forbidden sources the forbidden-source
removal takes out; if it runs first, the apply withholds 2,449.

**New text, strictly (`ProseRebuilt`, `rebuilt/1`).** The lane's own reworded sentences pass only when clearly
rebuilt, since a false copy verdict only costs a retry. Each sentence is set beside every source passage (one source
sentence or two adjacent ones) with words in a plain form (`ProseRebuilt.Plain`: the closed list of equivalents and
the stem of `fa19192d`, extended with a few pairs such as according/per, direction/instruction, ahead/advance). It is
a copy when, beside some passage, half or more of its frame (every word but the facts: amounts, units, the words of
the ingredient lines and the craft words) follows the passage in order; or, with two or more words of its own wording
(the frame without function words), half or more of that wording follows in order; or, with three or more of its own
words in the passage in any order, those are 60 % or more of its own wording. A sentence whose own wording is at most
three words of the closed formula list (`ProseRebuilt.FormulaWord`) passes unless half or more of all its words follow
a passage in order: a formula the lane rewords passes, a formula kept word for word does not.

- **The line**, from the judged sentences of the two proof draws (144 recipes, 1,415 step sentences, 526 judged
  copies; description sentences left out, since the lane's description is built by rule): no sentence judged a copy
  passes. The price on old text: 828 of the 889 step sentences judged not copies are refused.
- **On text it will really see** (`ck15-prose.md`): 21 staging methods rewritten by hand both ways. Every lightly
  edited recipe is refused (148 of 149 light sentences refused; the one that passes is "Serve right away."). Of
  109 sentences rebuilt by hand in a first round, 51 are refused (47 %); rewriting the refused ones again, 16 of 116
  are refused in the second round and 8 in the third, so 14 of 21 recipes pass whole within the lane's three rounds.
- `ProseReword.SentenceProblems` uses it for each reworded sentence (reason `copy`, "the wording follows the
  published sentence too closely; ..." with which measure fired and the writer's own words that keep the published
  order, `ProseReword.Followed`), and the gate refuses a text with any sentence it reads as a copy. The line is not
  moved by the lane's prompt rounds.
- The golden rows of `prose-check.tsv` hold it: every row judged to hold a copied sentence must be refused (all 450
  are, including the rows judged to hold none: the retry cost on old text).

**`contradicts-lines` on new text: not adopted.** The proof found that 14 of 20 right firings of a lines class were
rewrite faults (1 tsp to 1 tbsp) that `contradicts-lines` misses, because an amount the source states anywhere
exempts it. Narrowing the exemption to the same food or to an amount before no food fires on 71 more recipes of the
staging copy. Judged: the 20 of the proof's 40 lines rows it fires on (16 right, 3 false, 1 doubtful) and a fresh 40 of
the 71 (seed `ck15p-lines`, proof rows, judged draws and golden rows left out: 27 right, 12 false, 1 doubtful): 43 of
60, 72 %, far under 95 %. The false firings are amounts per item ("a slice", "each", "per lemon", "on each salad",
"half of the onions": 7), another food sharing the head word or the sentence ("lime juice" read as the orange juice
line, sugar sprinkled "on the rhubarb", stock for the rice, yeast for the starter, apple cider mix as the apple line:
6), a list "each of salt and pepper" read as the first food (1) and the source's own ambiguity (2 doubtful). So the
exemption stays as v7 has it; the case where the source disagrees with its own lines stays out. The judge, which
reads every reworded step, is what catches a changed amount in new text.

## Check v7

Built on `cleanup2/ck11-prose` (2026-11-24) on the owner's answer of 2026-11-23: "halfway between strict
and lenient. The entire text needs to be read for context, then the paragraph, then the sentence. Then
rewording can happen". Under v6 the copy measures were read on the whole text; checkpoint 6 (finding R8)
found 19 of 40 passes copying their source and 12 of 20 shown fact-unsure recipes copying, under the
strict standard. The owner then chose a standard between the two.

- **The sentence rule** (above) replaces the two lines of v6 and the copy-unsure band. Its terms were
  set on the checkpoint 6 sample, read sentence by sentence beside the closest source sentence (100
  recipes; the 30 v6 copies by their sentence table), and the line between "some" and "mostly" on all
  450 judged rows (`The_mostly_line_sits_where_the_judged_rows_put_it`).
- **Golden rows.** `prose-check.tsv` has a third judgement, `sentences`, beside `expect` (written under
  the lenient standard of the earlier checkpoints) and `copy` (the strict standard of checkpoints 5 and
  6): `mostly` (more than half of the sentences copied or kept nearly word for word), `doubtful` (two in
  five to half), `some` or `none`, with that count. The 130 checkpoint 6 samples are new rows `ck6-`,
  masked as before (the craft words kept, as the check reads them; findings and every sentence's copy
  and near-verbatim flags identical in both forms; the longest run of kept words 30), with `classes`
  `copy-only`: their fact firings are not judged and they are left out of the class scores. All rows
  are `agent`.
- **Bars.** On all 450 rows: no recipe judged mostly copied is shown (0 of 63); of the 132 the check
  withholds as copied (131), 3 are judged to hold some copied sentences or none (2.3 %, bar 1 in 20) and
  65 are doubtful. On the 100 checkpoint 6 recipes read sentence by sentence: 0 of 7 mostly copied shown,
  1 of 11 withheld judged some or none (shown, not held: a sample of 11).
- **`contradicts-lines`.** An amount the text puts before a food whose single ingredient line states
  another amount of the same kind, where the source method states none for it and the amount is not
  the method's, fails the text whatever the fact classes prove (checkpoint 6: a shown step with 4 eggs
  and 1 cup sugar where the lines say 2 eggs and 2/3 cup). So does an amount the source method does not
  state that is more than its single line holds, even where the method states another amount for that
  food, and either case in the description (the description's amounts the source's own description
  states excepted). On the judged rows it fires on 5 rows, all judged right (ck4-37498 newly, judged
  `wrong-amount`), and on the checkpoint 6 madeleines. An amount the source method itself states is not
  held against the lines: on the judged rows that reading fired on 9 more rows, of which about 7 read an
  amount against the wrong line (a brine's salt, pastry squares against sheets, broth against chicken),
  and the lane, which keeps the source's amounts, could never repair them. Other wrong amounts stay
  `wrong-amount`, a class that fails only while proven.
- **Estimate on the staging copy** (the 130 samples applied to the checkpoint 6 dry run of 21,377; a
  read-only pass was not possible from this build): about 6,100 recipes withheld as mostly copied
  (29 %), about 10,200 withheld for their copied sentences, the lane's work list (48 %), about 5,100
  shown with none (24 %).
- **Measured on the staging copy** (`cleanup2/ck12-prose-proof`, 2026-11-24, read-only, with the build
  run locally over every examined web recipe, 21,377, of which 12,650 were already withheld
  and 8,727 shown): 6,314 mostly copied (29.5 %), 9,855 with some copied sentences (46.1 %; 4,732 with
  one, 2,656 with two, 1,365 with three, 639 with four, 463 with five or more), 5,208 with none
  (24.4 %); outcomes failed 6,513, repair 9,701, unsure 3,144, passed 2,019. The estimate held.
- **The proof on fresh draws did not pass.** Under the owner's standard (a sentence with single words
  swapped, dropped or moved, synonyms included, is a copy; a formula or a list of foods is not; a
  merged or rebuilt sentence is NOM's own), 120 recipes drawn fresh and read blind beside their source,
  sentence by sentence, gave for check v7: 11 judged mostly copied that are not withheld as a whole (10
  marked only for some sentences, 1 shown with none), 5 of the 28 it withholds as mostly copied judged
  to hold only some (18 %), and of the sentences judged copies of 8 words or more it marks 37 %, with
  88 % of its marks right. The rule reads words in order, so a light edit made of synonyms ("lower" for
  "reduce", "pour in" for "add", "loosen" for "release") falls under its similarity line, while a
  sentence rebuilt around the same facts can sit above it; no line on these measures meets the bars
  (no recipe judged mostly copied outside the mostly group, at most 2 of 40 in it judged some or none,
  the marked sentences right in 19 of 20 recipes). A candidate with equivalent words, stray-match
  trimming and merge detection reached 5 missed and 22 % (60 % of copies marked, 76 % of marks right)
  and was not adopted. The proof report is `build/ck12-prose-proof.md` with the staging evidence.
- **The re-check reports both withhold figures.** `withheldIfRepairWithheld` (failures and recipes
  with some copied sentences, as built) and `withheldIfRepairShown` (failures alone, were those recipes
  shown while the lane rewords them), each with its approved count; a report only, nothing depends on
  it. Both count the recipes shown now and the recipes already withheld: a run reads the withheld ones
  too (`examinedWithheld`, leaving out those a person edited or released and those with no stored
  source) for these figures alone, and never releases or changes them, so the figures stay whole after
  an apply. `alreadyWithheldIfRepairWithheld` and `alreadyWithheldIfRepairShown` give the part already
  withheld. On the staging copy: 16,214 and 6,513 of 21,377 (9,444 and 4,109 of the 12,650 already
  withheld, 6,770 and 2,404 of the 8,727 shown).

## Check v6

Staging checkpoint 5 ran v5's re-check dry run over 21,377 shown recipes: 7,208 failed (33.7 %, all
`copied-text`), 9,082 were unsure (42.5 %) and 5,087 passed (23.8 %). The operator read 40 of each:
the failures were right (0 false), but 4 of 40 passes copied their source (60214, 24134, 31704,
63162: a light edit of every sentence), 11 more were doubtful, and 2 of the 3 unsure recipes read in
full were copies (31735, 62797; finding Q9, high, and Q10). The copy measures of the passes (runs 5
to 17, 5-gram shares 0.02 to 0.33) overlap those of the failures (11 to 38, 0.09 to 0.47), so no
single line separates copies from faithful rewrites: a light edit of every sentence stays under each
line of v5 (run 16, stretch 44.5, share 0.42, description run 11, sentence 0.92) while a failure of the
same closeness meets one.

v6 therefore draws two lines per measure from the judged rows and calls the band between them
copy-unsure:

- **Judged rows.** All 120 checkpoint 5 rows were added to `prose-check.tsv` (`ck5-`), masked as
  before, with a `copy` judgement on every one of the 320 rows: `copy`, `doubtful` or `not`. Old rows
  take it from `expect` (a copy reason is `copy`, anything else `not`), except 7 checkpoint 2 to 4 rows
  judged on facts whose copy measures v5 already failed and which follow the source with light edits
  (`doubtful`). New rows keep the operator's reading where the operator read them (copy, clean, right);
  the 14 rows the operator left doubtful and the 37 unsure rows the operator judged by the measures
  only were read in full by the ck10 builder (`agent-review-ck10-2026-11-23`): copy when the text
  follows the source sentence after sentence with a word or two changed, a long sentence kept nearly
  whole or the description kept; doubtful when several sentences, often with their lists, are light
  edits and the rest rephrased; not otherwise. Two rows the operator called clean after reading two
  steps (37510, 36162) were read in full and are doubtful. Totals: 117 copies, 25 doubtful, 178 not.
- **Copied lines** (`CopiedLimits`): just above the highest value of each measure on any row judged
  not a copy or doubtful (run 16, stretch 56.5, share 0.432, description run 15, sentence 0.95), so no
  judged non-copy is called copied.
- **Copy-unsure lines** (`CopyUnsureLimits`): chosen so that every row judged a copy or doubtful
  reaches at least one line with a margin (`CopyUnsureMargin` 0.9: its value times 0.9 is still at or
  over the line), with as few rows judged not a copy over a line as the lines allow (a search over the
  lines each copy row could set). Taking the lowest value any copy has on every measure at once, as the
  plain reading of "below every copy" would, withholds all 178 non-copies: a copy caught only by its
  description has a run of 6, and one caught only by its run has a description run of 0.
- **The price.** 94 of the 178 rows judged not a copy (52.8 %) land in the band and are withheld; 24 of
  the 51 of checkpoint 5 (47 %). On the rows: 0 of 142 copies and doubtful pass the copy rule, 0 of 79
  failed or copied verdicts are false, no non-copy is copied.
- **Staging estimate** (checkpoint 5's samples applied to its run; the 40 passes were drawn from the
  2,843 certain passes, approved or with an id up to 50,971, so the pass figures lean on those): of
  the 7,208 failures 34 of 40 stay copied and 6 become copy-unsure; of the 9,082 unsure 24 of 40 become
  copy-unsure and 16 stay unsure on facts; of the 5,087 passes 29 of 40 become copy-unsure and 11 pass.
  About 6,100 copied (29 %), 10,200 copy-unsure (48 %), 3,600 unsure on facts and shown (17 %) and
  1,400 passed (6.5 %): about 16,300 withheld (76 %), against 7,208 under v5.

**Stretch share (review CK10-R1).** v5 also called a stretch of 15 or more that covers 0.4 of the
rewrite's words copied; v6 as first built dropped it, so a short method of short sentences kept but
for one word (an 18-word drink method: run 9, stretch 15.5, no share under 40 words, no sentence of
15 words) passed. The stretch share puts it back as a sixth measure: its copy-unsure line is v5's 0.4,
above every row judged not a copy (highest 0.309), so it adds nothing to the price; its copied line is
0.41, just above the highest doubtful row (0.409). On the rows it moves one copy from copy-unsure to
copied (79 failed or copied verdicts, 0 false) and the constructed close paraphrase `k4-swap` of
`prose.tsv` back to `copied-text`; the checkpoint 5 rows and so the staging estimate are unchanged.

The two lines are a fit to these rows; the next staging reading is their held-out test. The golden
test `The_two_copy_lines_sit_where_the_judged_rows_put_them` refuses lines that no longer sit where the
rows put them (a copied line at or under a non-copy's value, or a copy or doubtful row that reaches no
copy-unsure line by the margin) and prints the price; adding judged rows means setting the lines again.

**Facts on the new rows.** Every fact firing on the 120 rows was judged (`classes`). Of the 34
`lost-quantity` firings, 25 are right, 7 false (an aside or tip, a bracketed conversion, a range
restated, a "1-cup measuring cup", an example pan) and 2 doubtful; `lost-ingredient` 1 of 6 right
("vegetables of your choice", "ingredients", "a splash of olive oil" read as olives); `wrong-amount`
5 of 6, `changed-range` 1 of 2. The committed record (`--prose-classes --record`, check 6) now holds
`lost-quantity` at 55 of 65 checkpoint-judged rows (84.6 %, below 95 %), `wrong-amount` 10 of 11,
`lost-ingredient` 3 of 8, `changed-range` 4 of 5: no class is proven, so fact faults still only mark a
recipe unsure. With every class taken as proven 7 of 132 verdicts would be false, so that figure is
shown by the golden test and no longer held as a bar. Fact-unsure recipes stay shown, marked
(`ProseUnsure`), counted, and are the prose judge's queue.

## Check v5

Staging checkpoint 4 ran v4's re-check over 21,377 shown recipes: 12,880 failed (60.3 %), 7,208 of
them as copies. The operator read 40 failures against their source: 31 right, 1 doubtful and 8 false
(finding P7, over the bar of 2), every false one from a quantity rule. The check had now failed four
fresh readings on rule detail, so v5 changes how a rule earns the right to fail a recipe (see "Fact
classes") and fixes the quantity pairing where the cause was clear:

| Recipe | v4 verdict | Rule behind the error | v5 |
|---|---|---|---|
| 51378 | lost-quantity | a pan size the source states twice was required twice | a cooking vessel's size is stated once |
| 33472 | lost-quantity | the size of a measuring cup read as an amount | a holding vessel's size is no amount |
| 34862 | lost-quantity | "two at a time" required as a count; "in pairs" kept it | a count before "at a time" or a tool is soft |
| 36132 | lost-quantity | "1 teaspoon each of" three herbs did not stand for three teaspoons | an amount before "each" stands for every item of its list |
| 32341 | wrong-amount | the time of a bracketed alternative paired with the ingredient after the first time | a time is never an ingredient amount |
| 32888 | wrong-amount | one ingredient's cooking time paired with an ingredient of a later clause | an amount pairs only within its own clause |
| 33731 | lost-quantity | "a slice" matched against the source's "a pinch" (two counts of one, of different words) | counts of different count words are never equal |
| 36164 | wrong-amount | "1/2 cup of pasta water" read as pasta | an ingredient name before "water" or "liquid" names the liquid |

The false sub-reasons of right failures are fixed too: "vanilla" now keeps "vanilla extract", a
ratio ("50/50") is no amount, and a liquid ("2 quarts of water") is no longer paired with a dough's
line. The 40 recipes are rows of `prose-check.tsv` (`ck4-`), and every fact firing on the 200 rows
is judged.

Per class on the 200 rows (the record of this version; doubtful counted against):

| Class | Rows (checkpoint-judged) | Right | Doubtful | Precision | Fails recipes |
|---|---|---|---|---|---|
| `lost-quantity` | 31 | 30 | 1 | 96.8 % | no (below 40 rows) |
| `wrong-amount` | 5 | 5 | 0 | 100 % | no |
| `changed-range` | 3 | 3 | 0 | 100 % | no |
| `lost-ingredient` | 2 | 2 | 0 | 100 % | no |
| `changed-unit` | 0 | 0 | 0 | - | no |

No class has 40 judged rows: the readable rows kept outside the repository are all in the file, so
no fact class fails a recipe in v5, and every fact fault marks it unsure. The precision is measured
after the v5 fixes on the rows that motivated them, so it is a fit; the next staging reading, with
every fact firing it reads judged and added, is the held-out test and the way a class reaches 40.
The copy rule keeps its own bar: 0 of the 66 copies pass (the 45 of checkpoints 2 and 3 and the 21 of
checkpoint 4), and no faithful row meets it.

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
2. Re-check dry run (`version=6`): with no fact class proven (the committed record), only copies,
   copy-unsure and form fail: expect about 6,100 `copied-text` and about 10,200 `copy-unsure`
   (`copyUnsure`, `copyUnsureApproved`) of 21,377, about 3,600 unsure on facts and about 1,400 passed
   (see "Check v6"). Record the totals per reason and the approved counts; read 40 failures, 40
   copy-unsure, 40 passes and 40 unsure at random against their source: no pass and no unsure recipe
   may copy its source, and at most 2 of the 40 failures may be false. Judge the copy-unsure ones too
   (copy, doubtful or not): that is the held-out price. Any wrong verdict becomes a row of
   `prose-check.tsv`, masked the same way, the readable row to the staging evidence.
3. Note the time T, apply with `maxRecipes=200`, and check as an anonymous user in the app at phone
   width: the recipe is found in search, its page shows key facts and the source link and no
   description or steps, and a meal plan can pick it. Then undo from T: `restored` 200, `skipped` 0,
   and a repeat dry run shows `remaining` 0.
