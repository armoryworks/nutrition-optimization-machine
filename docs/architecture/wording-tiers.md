# Wording tiers: deterministic wording rows on the lane frame

The first half of the one decision pipeline (plan C1): every in-use uncurated wording is keyed by the
harder normalizer and asked of the deterministic tiers in a fixed order. No model is asked and no
tier ever supplies a number. Every answer passes the one link gate (`LinkGate`, see `link-gate.md`)
and, when its tier is live on the lane frame (`lane-frame.md`), is written as a row of the verified
wording table through the verified wording loader, so one per-wording retract takes any row back
(`food-catalog-ingestion.md`, verified wording table). The wording readers (two blind models) are
the second half and decide what no tier answers.

Code: `Nom.Orch/Services/Support/WordingTiers.cs` (the tiers, pure),
`WordingHeadDefaults.cs` (the data-given head defaults), `IngredientNameNormalizer.TierKey` (the
harder key), `Nom.Orch/Services/WordingTierLaneService.cs` (the lane and its undo),
`OllamaWordingTierJudges.cs` (the audit readings), `Nom.Api/Services/WordingTierHostedService.cs`.

## The key

`IngredientNameNormalizer.TierKey` is the normalised name with its preparation words (`Bare`), its
notes of purpose ("for garnish", "for the pan", ", divided", ", separated", "plus more") and the old
cookbooks' filler ("the", "nice", "good", "best", "fine", "remains of", "rest of", "a quarter of a
pound of") taken off, lower-cased. A heading line ("For the frosting:", "Filling:") has no key and
names no food. The table itself stays keyed by `WordingKey`; the harder key is only what the tiers
decide on.

## The tiers, in order

| Tier | Answers when | Frame policy |
|---|---|---|
| `verified-wording` | another wording with the same key has a live `food` row in the verified wording table that no tier wrote (a `deterministic:<tier>` row is one tier's answer, not two readers', and only that tier's undo reaches it); a `none` or `unclear` row for the key stops the pipeline with no answer | T1 |
| `alias` | a trusted alias (contexts `admin:`, `deterministic:`, `retired:`, `link:deterministic:`; never an alias with no context) with the same key belongs to one food | T1 |
| `staple` | the key is a staple name (`StapleFoods`) | T1 |
| `name-key` | the key, singular or plural, is the key of exactly one curated food's name; a name that is its own key ("Ham") owns it before a name that only comes to it once words are taken off ("Ham, minced"), and two foods of one rank leave it to neither (`WordingTiers.KeyMap`) | T2 |
| `alcohol-context` | every line of the wording has an alcohol word and they share one drink sense (`AlcoholLineContext`, read with the recipe's title, other lines, steps and source); the answer is the first ready food of that sense's table, which lists the drink's own kind first and after it only foods the alcohol design holds nutrition-equivalent; ready = live, not branded or prepared, curated or a pending SR Legacy or Foundation food with no admission reason, core nutrients and at least 20 rows (so Claret, with 6, never) | T2 |
| `head-default` | the key is one word and every line's unit class gives the same data-given default (below) | T2 |

The first tier that answers decides. When a later tier names another food the pair is held
(`tiers-disagree`): two deterministic rules that disagree are not a decision.

A wording that names more than one food (`LinkAutoApplyGuard.IsCompound` on the key: and, or, with,
&, +, a second food after a comma) gets no row. The gate holds compound wordings by rule, and the
judged pairs call "A or B" linked to A wrong (111 of the 120 false accepts of an early build that
wrote such rows were "butter or lard" to Butter and the like). Compound lines are decided one line at
a time by the compound line rules (`CompoundLineSplitter`, `compound/apply`, journaled and undone per
line), which now also resolve a part by the harder name key ("the remains of cold ham or tongue"
moves to Ham), links design B11.

## Head defaults from data

`WordingHeadDefaults` is built once a day from the lines of approved recipes on curated foods: each
line of 60 characters or less whose key is one word counts toward that head, in its unit class
(spoon: teaspoon, tablespoon, pinch, dash, drop; volume; mass; count; container; none when the line
has no amount). A head's default is the food holding at least 70 % of its lines over at least 20
lines; a unit class with 20 lines of its own has its own default ("1 tsp soda" baking soda, "1 cup
soda" another food), a class with fewer falls back to the head's default unless its own lines (5 or
more) give that food less than 70 %. Lines of one wording whose classes name different foods give no
default. Nobody types a default in. The defaults over all classes go into the gate's catalog
(`GateCatalog.HeadDefaults`), which spares the head's default food the gate's variety rules; a class
default that differs from the overall one still meets them.

The same scan gives the gate its grams bound: the 99th percentile of grams per line of each USDA
category with at least 100 weighed lines (`GateCatalog.GramBounds`).

## The gate

Each answer is read by `LinkGate.Check` under the key, with the wording's lines (up to 200: raw text,
unit, grams through the target food's conversions, 0 for a line with no amount, and the recipe's
alcohol context), so the units, grams and alcohol rules read the real lines. The rules that read
words of the wording itself (fresh herb, part, form, polarity, percent, marked) are also read on the
wording as written, so a word the key dropped still holds the pair ("chopped tarragon" against dried
tarragon). An answer of the verified wording table rests on a row two blind readers agreed on, so the
gate reads it as readers' answer (head noun in place of containment and lead noun). The family check
(`LinkGate.Family`) runs over the pass's answers and the table's live food rows: an answer whose family
settles on another food, or on none, is held; the lane never re-points a wording itself.

What the gate holds stays held: the tiers never write a pair the gate refuses. Synonyms the USDA
entry does not carry ("sherry" for dessert wine, "saleratus") wait for the lexicon the readers build.

## The lane on the frame

Lane `wording-tiers`, one frame tier per wording tier; a wording no tier answers is recorded under
tier `abstain` (never audited). Off unless `WordingTiers:Enabled=true`. Each pass:

1. Recovers rows a tier loaded in the last two days that no decision records as written (the
   process stopped between the load and its decisions or relink): each is recorded as written live
   (decision `recovered`, evidence key on the wording's present line count) and its batch is
   relinked, so the undo reaches it; a row of a tier that is now paused is retracted instead, since
   that tier's undo has already run.
2. Takes in-use uncurated wordings (no FDC id, not curated, live lines) with no live row in the
   table, most lines first, whose name is in key form (the relink finds a wording's lines by it), and
   that the lane has not decided under the tier's current signature at the wording's present
   line-count band (a power of two), so a wording is asked again when its line count passes the next
   power of two or the rules change. A would-be write recorded while its tier was in shadow (or
   paused) does not count once the tier is live: the backlog a tier qualified on in shadow is asked
   again and written. A wording set aside by its tier's cap (decision `capped`) waits 24 hours.
3. Decides each by the tiers and the gate, holds a pair the rejection memory still holds
   (`retracted`: a retract with no evidence key, or one on this lane's evidence while the wording has
   fewer than twice the lines it was loaded on), and runs the family check.
4. Per tier: loads the rows through `VerifiedWordingService.LoadAsync` with source
   `deterministic:<tier>`, batch `wt-<tier>-<stamp>` and evidence key `wording-tiers:lines-<line count>`;
   a dry run unless the tier is live. A row the loader refuses is recorded as not writing. Live, it
   then relinks that batch only (`relink/verified?batch=`).
5. Records every decision (`curation.LaneDecision`: subject `wording:<key>`, the food or the hold,
   stratum the line band, signals with the key, food, evidence, batch, every tier's answer and five
   sample lines).
6. For each tier that decided, audits a sample (the larger of 30 or 25 %) and evaluates the tier.

Caps: `WordingTiers:MaxPerDay` (300) writes per tier in 24 hours, `WordingTiers:HeadDefaultMaxPerDay`
(1) for the head-default tier, since one default moves every line of its wording; a wording over its
tier's cap is recorded as `capped` (not a write, never audited) and is not selected again until the
24-hour window has passed, so a spent cap does not hold the top of the list and the pass reads the
wordings below it. `WordingTiers:BatchSize` (100),
`WordingTiers:PauseSeconds` (5), `WordingTiers:IdleMinutes` (60). Every pass passes the AI lane gate
(switch `ai-pause-wording-tiers`, `ai-pause-all`, quiet hours).

**Bars.** T1 (`verified-wording`, `alias`, `staple`): qualify on a Wilson lower bound of 99 %, pause
when a full window's observed corrected error is over 1.5 %. T2 (`name-key`, `alcohol-context`,
`head-default`): 98 % and 3 % (the alcohol design's 2 % pause bar is not above the 2 % its 98 %
qualify bar allows, which the frame refuses). A paused tier resumes only under a new signature. A
fault pauses a live tier and keeps a shadow tier in shadow: the loader refusing more than 5 % of at
least 20 rows a tier would write; the audit unreadable on more than a fifth of its sample; no model
host.

**Audit.** The auditor is the roster's auditor (`phi4`) reading the pair (wording, the food's USDA
description) with the link lane's same-food judge (`OllamaIngredientLinkVerifier`, prompt version 2):
a variety, a different food or several foods is a flag, `unsure` no answer. A flag counts as an error
only when reader B (`mistral-nemo`) re-reading the pair blind also calls it wrong. The combined
check's score comes from the committed `scores/link-audit.json` (built into the API); without that
record, or with one scored on another auditor model or another reader B (the record's `model` and
`checkModel`), no tier can leave shadow. The scorer lane that
writes it belongs to the wording readers.

**Undo.** `WordingTierUndo` (the frame's self-undo, and `POST /api/Pipeline/frame/wording-tiers/<tier>/undo`)
takes every row the tier wrote live since a time, found by the tier's decisions and the row's source,
and retracts it with `VerifiedWordingService.RetractAsync` and the evidence key it was loaded on: its
relinked lines, retired ingredients, withdrawn proposals and picks and import landings go back, and
the rejection memory keeps any tier from writing the pair again until the wording's lines double or
an admin's `overrideRejections` load lifts it. A retract never demotes a food the row's load curated
(lead decision): catalog admission decides curation.

## Measured on the fixtures

The no-model scorer lane `wording-tiers` (record `scores/wording-tiers.json`, required) runs the tiers
and the gate on the wording of every pair of `link-pairs.tsv` judged right, wrong, near or compound
(7,326 pairs, 4,869 distinct wordings; a wording that is itself a kept w01 row is left out), over
`gate-catalog.tsv` and `usda-catalog.tsv`, with the 232 kept w01 rows as the verified wording table
and no lines. Recorded for `wording-tiers/1`, and recorded again at the wave-5 merge under the
`link-gate/6` signature, when the name normalizer began to take a hyphenated mixed number off a
wording and the tiers write one more pair (`butter, 1-1/2 oz`, judged right; the by-tier counts below
are those of the first record):

| | |
|---|---|
| pairs whose food the tiers write | 1,858 |
| judged right among them | 99.52 % |
| judged-wrong pairs written (false accepts) | 9 of 1,362 (0.66 %): five "mustard" to prepared mustard (the staple), three fig wordings to raw figs and "cold corned beef", all among the gate's own known 12 |
| agreement (a right pair's food written, or a bad pair's food not) | 43.7 % |

By tier, over the 4,869 distinct wordings: `staple` answers 664 (13.6 %) and writes 615 (1,330
judged pairs right, 5 not); `name-key` answers 673 (13.8 %) and writes 351 (459 right, 4 not);
`verified-wording` answers 58 and writes 49 (59 right, 0 not); `alcohol-context` answers 12 and writes
1; `alias` and `head-default` have no input in the fixtures (no trusted aliases, no curated lines) and
are scored by the rule-given rows of `wording-tiers.tsv`. 752 wordings are compound and 2,710 get no
tier. Of the wordings a tier answers but the gate holds, the commonest rules are `generic` (166),
`incomplete` (80), `lead-noun` (66), `containment` (48) and `state` (32). The pairs are judged by a
model reviewer (agent-review-2026-11-13): these figures are agreement with that reviewer, in sample
for the gate.

## Not here

- The wording readers, the lexicon, the size gate (500 lines) and the sentinel retraction:
  `cleanup2/wording-readers`.
- The `link-audit` scorer that records the audit check's score: `cleanup2/wording-readers`.
- A Pipeline page block of its own: the frame block already lists every tier with its mode, figures,
  reason and undo; the wording lane's block is the readers' item.
