# Prose lane v2: clean-room rewrites

A scraped recipe's method and description are shown only in our own words. Recipes whose stored
prose is the source's (quarantined, `ContainsSourceProse`) or failed the prose check (withheld,
`ProseWithheld`, see `prose-check.md`) are rewritten by this lane. Withheld is a holding state, never
an end: the lane works every withheld recipe off, approved ones first, and a recipe it cannot rewrite
is reported by the cause that held it, so the engine is fixed rather than the recipe left behind.

Code: `ProseMasking`, `ProseFactSheet`, `ProseCleanRoom`, `ProseLanePrompts`, `ProseDescription`,
`ProseLaneJournal`, `ProseLaneGolden` (Nom.Orch `Services/Support`); `ProseLaneService`,
`ProseRewriteUndoService`, `OllamaProseWriter`, `OllamaProseJudge` (Nom.Orch `Services`);
`ProseRewriteHostedService` (Nom.Api) runs it.

## Why the old lane failed, and what this lane changes

The old lane gave the writer the source sentences at temperature 0 with "preserve every fact, keep
the same number of steps", and cleared quarantine on `RewriteFidelity` (bare numbers and 8-word
runs). The re-check with check v2 found 8,852 shown recipes failing (prose.md 1.1). Each failure
class has a cause in that engine; this lane removes each cause rather than catching the symptom.

| Failure class (check v2 reason) | Cause in the old engine | How this lane removes it |
|---|---|---|
| `copied-text`, `unchanged` (close paraphrase, wholesale copy) | The writer saw the source wording and was told to keep its sentences and order, so it trimmed words; the retry saw the same text. | The writer never sees the source: it gets fact sheets (verbs, line tokens, equipment, amount tokens, cues of at most 6 words) and the ingredient names. Attempt 2 runs at temperature 0.6 with a new seed and attempt 3 in another style (one short sentence per action), each told why the last failed in the lane's own words, never by quoting the source. |
| copied description | Descriptions are marketing prose and the old lane passed them on almost unchanged. | The description is built by rule from the dish name and the main ingredients (`ProseDescription`); the source description is never read. |
| `wrong-amount`, `changed-unit` (¾ → ½, ½ Tbsp → ½ tsp) | The model re-typed Unicode fractions and units itself. | Every amount of the source method is a token (⟦Q1⟧) holding the exact source text, put back by code. A written amount outside the tokens, a digit, or a token used twice or not at all is refused by the form rule before check v2 runs. |
| `changed-range` (7–10 min → 8) | The model "simplified" ranges. | Ranges are inside the tokens; the model cannot change them. |
| `lost-quantity`, `added-quantity` | The model dropped or invented timing hints under a paraphrase instruction. | Each step's amount tokens must appear exactly once in that step (a bracketed conversion may be dropped); no amount may be added. |
| `lost-step`, `too-short` ("heat" → "mix", lost sections) | The old check compared numbers only, so a dropped step or a changed verb passed. | The fact sheet must keep every lexicon verb and every ingredient token of its step (rule-checked, one retry with the problems), the written step must keep them again, and check v2 checks verbs, ingredients and length. |
| a changed meaning check v2 cannot see (heat level, doneness cue, a dropped "do not stir", an added action) | Nothing read the meaning. | A judge of another model family (`mistral-nemo`) reads every rewrite against the source, step by step, before it is written; a rewrite it does not find faithful is never written. |
| `step-marker` | Scraped steps carried "2." markers the importer kept and the model copied. | The form rule refuses a step marker in the written text; the marker cleanup (`prose-check.md`) strips stored ones. |
| stock-phrase false alarms (v1) | v1 counted any 8-word run, including phrases every site uses. | Check v3 weighs ingredient names and amounts at half and counts a run from 14.5 weighted words; stock phrases count like other words, since the limits are set and measured that way (`prose-check.md`). |

## The pipeline (per recipe)

1. **Mask (rules).** `ProseMasking.Mask` reads the source method (the newest `jsonld` document;
   a quarantined recipe without one uses its own stored text) with the prose check's quantity
   reader: every amount becomes ⟦Qn⟧ (a bracketed conversion is its own token and may be dropped),
   every word naming an ingredient line's head word becomes that line's ⟦Ln⟧, and step markers and
   cross-references are gone.
2. **Extract (writer model).** The masked method becomes fact sheets (`StepFacts`: actions, objects,
   equipment, amounts, cues; JSON schema). `ProseFactSheet.Problems` checks them: one per step, every
   amount token of the step exactly once and none of another step, every line token kept, every
   lexicon verb kept, no entry over 6 words, no digit or amount outside the tokens. A failure is asked
   once more with the problems; sheets that still fail are built by rule from the masked step (its
   verbs, line tokens and amount tokens, no cues).
3. **Write (writer model).** From the sheets, the ingredient names (`⟦L1⟧ = olive oil`) and the dish
   name only. Attempts: style "steps" at temperature 0; the same at 0.6 with seed 7 and the reasons;
   style "actions" at 0.3 with seed 11 and the reasons; then, when the sheets are the extractor's,
   the rule rendering of the sheets (`ProseFactSheet.Render`), which needs no model.
4. **Gate.** The form rules on the masked text (`ProseCleanRoom.FormProblems`); the tokens put back;
   prose check v2 against the source with the current stock index; the judge (reader B of the
   roster). The first attempt all three pass is the rewrite.
5. **Give up.** A recipe no attempt passes keeps its withheld or quarantined state and is stamped
   with the lane version (`Recipe.ProseLaneVersion`, `ProseAttempts` = 4). Its failure class (the
   first reason of its last writer attempt; the rule rendering after it is a fallback, so its own
   reason would hide what the writer failed on) is recorded and counted on the Pipeline page. It is taken again
   by itself as soon as the lane version changes: the version is a hash of every prompt, the attempt
   schedule, the masking, fact-sheet and description rules, the check version, the writer model and
   the judge model (`ProseLanePrompts.LaneVersion`).

Order: approved withheld recipes, then other withheld, then quarantined imports; within each, never
tried first, then the oldest attempt. While the lane is in shadow or paused, a recipe already decided
under the current lane version is not taken again (a pass stamps `ProseLaneVersion` with
`ProseAttempts` 0), so each decision the frame counts is of a different recipe; the live lane takes
them again to write them.

A withheld recipe whose method a person wrote (a step stamped with a person) stays withheld only for
its copied description (the recipe form keeps the stored description when it is left empty). The live
lane takes it, keeps the person's steps untouched, replaces the description by the rule-built one and
lifts the withhold; no model is asked. A quarantined recipe with a person's step is never taken. A
description a person wrote while the prose was withheld or quarantined is journaled by the recipe
form (`ProseDescriptionByPerson`, the text clamped) and the lane keeps it, as long as it is unchanged,
instead of the rule-built one. When the rule-built description is empty (a recipe with no name) the
description is left empty: the source description is never kept when a withhold or quarantine is
lifted. A model
host outage ends the sweep and charges no recipe; an HTTP error answer sends the recipe to the back of
the queue, uncharged.

## Models

Writer: `Ai:RewriteModel` when set, else the roster's reader A (`qwen2.5:14b-instruct`). Judge:
reader B (`mistral-nemo`). Auditor: the roster's auditor (`phi4`). The lane refuses to run (and says
why on the Pipeline page) when the judge is of the writer's family or the auditor of either.

A recipe whose source reserves its content against AI use (`Recipe.SourceAiReserved`, see
`source-rights.md`) never reaches the models: the lane takes the model-free path
(`ProseCleanRoom.RunRuleOnly`: rule sheets, rule rendering, form rules and check v2, no judge), records
the decision in the `rule-only` tier and writes the rendering only while that tier is live, or leaves the
recipe withheld with its key facts and link. The `rule-only` tier has its own mode on the frame: every
decision is audited by rule (`ProseRuleOnlyAudit`, the rendering read back against the stored source; no
model), `MeasureRuleOnlyAsync` evaluates it with the lane's bars and `AuditorScore.ExactRule`, and a pause
undoes only its writes (the undo reads the `rule-only` writer tag).

A withheld facts-only recipe (`Recipe.SourceRights`) with a step a person edited is rewritten whole: the
description-only path is for methods a person wrote on sources whose method may be shown.

## On the lane frame

Lane `prose-rewrite`, tier `*` (`lane-frame.md`). The lane asks the frame for its mode before each
sweep:

- **shadow** (no state row, as after a deploy) or **paused** (probation): it decides every recipe
  and records each decision in `curation.LaneDecision` (the rewrite it would write is in the signals),
  but writes nothing and lifts nothing.
- **live**: it writes each rewrite.

Every decision is recorded with the lane version as its signature: `rewrite` (writes) or
`exhausted:<class>`. Bars (`ProseLaneService.Policy`): qualify at a Wilson 95 % lower bound of 96 %
precision on at least 100 audited rewrites; pause when the observed corrected error of a full window
of 100 audited writes is above 5 %.

**Audit.** When enough unaudited rewrites are waiting, the frame audits a stratified sample (strata:
withheld approved, withheld, quarantined): a quarter while in shadow or on probation, a twentieth while
live, at least 30. The auditor reads each sampled rewrite against its source with the judge's rubric;
a flag counts as an error only when the auditor, asked again blind about the flagged step with the
confirmation prompt, agrees (`ProseLaneAudit`). The judge never confirms: every rewrite in the sample
already passed it, so a fault it misses it misses again, and a check it confirms would find almost no
error whatever the lane's real error. The check's sensitivity and false-flag rate come from the
`prose-audit` record (below), scored with the same two auditor reads, and the frame corrects the error
with them. What remains is a shift of population: the sample holds faults the judge missed, which may
be harder for the auditor too than the planted faults it is scored on. The auditor is of a third
family, so its misses are not the judge's by construction; the staging proof's reading of 40 rewrites
by the operator is the check on that.

**Faults that pause it** (or hold it in shadow): a planted fault let through in the last canary run;
a gate yield (rewrites among recipes decided) under 50 % over the last 200 recipes. **Golden
problems** (back to shadow): no current `prose-rewrite` record for the writer model or no current
`prose-judge` record for the judge model (signature, model, case set and floor are all checked at run
time against the records compiled into Nom.Orch).

**Canaries.** After the first sweep under a version and after every 100 rewrites decided since, 4 planted
faults of `prose-judge.tsv` (those check v2 cannot see first, rotating) and 1 faithful rewrite go
through the gate (check v2, then the judge) and are recorded in the `canary` tier. A planted fault let
through pauses the lane until a later canary run is clean; a faithful rewrite refused is recorded only.

**Self-undo.** On a pause the frame calls the lane's undo since its last clean window (dry run, then
apply), so the rewrites since then go back to withheld or quarantined.

## Journal and undo

Each rewrite writes, with one timestamp, `ProseRewrite` rows on the recipe: the old description, each
old step (`RecipeStep:{n}`) and the old prose state (`ProseState`: withheld, quarantined, check
version, lane version, attempts, attempted-at, the shown step numbers), each old value full and
unclamped (split over rows when longer than one row holds), each new value tagged
`[writer laneVersion]`. Step rows are keyed by recipe and number and never deleted outright: a rewrite
revives a removed row of a number it needs and removes shown rows past its last step.

`POST /api/ProseRewrite/undo?sinceUtc=&laneVersion=&apply=false` (and the frame's undo) restores the
description, the steps and the state exactly, so a recipe goes back to withheld or quarantined and its
old text is never shown again; a description-only write (above) restores only the description and the
state. It skips, and counts, a recipe a person edited since and one whose prose a later journal entry
changed (`ProseLaneJournal.TouchesProse`: a re-check withhold or requarantine, a person's description,
a marker strip, a step split, another rewrite, or any row on the description, a step or the prose
state), and a rewrite of the steps when a rebuild replaced the recipe's steps since (a
`recipe.RecipeReprocessJournal` row with new steps that is not undone; undo the rebuild first). Rows
of other lanes that leave the prose alone (line classification and non-food screening
attempts, curation status) do not stop it. Each restore writes a `ProseRewriteUndo` row; a repeat dry
run shows nothing left. The re-check undo (`recheck/undo`) skips a recipe rewritten after its withhold,
unless the lane's undo has since taken that rewrite back.

## Golden set and records

`prose-judge.tsv` (golden folder, compiled into Nom.Orch): 8 recipes (4 constructed in our own
words, 4 public domain), each with one faithful rewrite and three or four planted faults. Scored by
an operator on a model host, never in CI:

```
dotnet run --project nom-api/Nom.Import -- --golden-score --lane prose-judge   --host <model host> --model mistral-nemo --record
dotnet run --project nom-api/Nom.Import -- --golden-score --lane prose-audit   --host <model host> --model phi4 --record
dotnet run --project nom-api/Nom.Import -- --golden-score --lane prose-rewrite --host <model host> --model qwen2.5:14b-instruct --check-model mistral-nemo --record
```

Floors (`model-lanes.json`): `prose-rewrite` 95 % end to end and 0 planted faults let through;
`prose-judge` 95 % with at most 2 % false accepts; `prose-audit` 75 %. Commit the three records under
`scores/`; the build compiles them in, and the running lane reads them. Any change to a prompt, the
attempt schedule, the masking, fact-sheet or description rules, or the check version changes the
signature and needs new records.

## Settings

`Ai:BatchOllamaUrl` (on/off), `Ai:RewriteModel`, `Ai:Roster:*`, `ProseRewrite:IntervalMinutes` (10),
`ProseRewrite:BatchSize` (25), `ProseRewrite:DrainWhileFull`, `ProseRewrite:PauseSeconds` (5),
`Ai:Lanes:prose-rewrite:MaxItemsPerDay` (`AI_PROSE_REWRITE_MAX_PER_DAY`). The pause switches
`ai-pause-all` and `ai-pause-prose-rewrite` stop it before a sweep. `ProseRewrite:CheckFidelity` and
`ProseRewrite:RetryDays` are gone.

## Pipeline page

The prose rewrite lane row shows the writer, judge and auditor and its queue (withheld or quarantined
recipes not given up under the current version). Recipes: "Prose the rewrite lane gave up on"
(`pipeline-metric-recipes-prose-gave-up`) and, when any, the give-ups by failure class
(`pipeline-prose-gave-up-classes`). The frame block shows the lane's mode, figures and undo.

## Limits

- The extractor sees the masked source (its words other than amounts and ingredient names). Only the
  writer is clean-room; a cue of at most 6 words may carry a source phrase into the sheet, and check
  v2's copy rules are what stop it reaching the text.
- The rule rendering is terse ("Heat and cook oil and garlic in the pan: ½ tablespoon, a minute or
  two; until fragrant."). It is shown only when check v2 and the judge pass it.
- Shadow and probation decide each recipe once per lane version; when every eligible recipe has been
  decided, the lane in shadow waits (it does not decide the same recipes again) until it qualifies or
  its version changes.
- Not built here: probation at a fixed 50 items an hour, an operator alert after three failed
  probations, and regenerating the shown recipes that passed v2 but came from the old lane (plan
  question 5, phase C).
