# Prose lane v4: the whole method reworded, read in context

A scraped recipe's method and description are shown only in our own words. Recipes whose stored
prose is the source's (quarantined, `ContainsSourceProse`) or an earlier paraphrase of it (withheld by
origin, `ProseWithheld`, see `prose-check.md`, check v8) are reworded by this lane, every one as a whole. Withheld is a holding state, never an end: the
lane works every withheld recipe off, approved ones first, and a recipe it cannot reword is reported by
the cause that held it, so the engine is fixed rather than the recipe left behind.

Code: `ProseReword`, `ProseSentences`, `ProseLanePrompts`, `ProseDescription`, `ProseLaneJournal`,
`ProseLaneGolden`, and for the rule rendering of AI-reserved recipes `ProseMasking`, `ProseFactSheet`,
`ProseCleanRoom` (Nom.Orch `Services/Support`); `ProseLaneService`, `ProseRewriteUndoService`,
`OllamaProseWriter` (`IProseRewriter`), `OllamaProseStepJudge`, `OllamaProseJudge` (Nom.Orch
`Services`), `ProseJudgeRules`; `ProseRewriteHostedService` (Nom.Api) runs it.

## The queue, and the sources it never takes (2026-11-26, `cleanup2/ck19-small`, staging checkpoint 8)

- **Kept out by class.** The lane never takes a recipe whose source's class is permission-needed or forbidden
  (`ProseLaneService.KeptOutClasses`, read from the recipe's `SourceRights` stamp): such a recipe stays withheld,
  with its key facts and source link, until the owner decides that source. At checkpoint 8 the 2,449 recipes of the
  two permission-needed sources were in the queue; once the lane wrote they would have been shown with NOM's own
  method.
- **The queue from an operator endpoint.** `GET /api/ProseRewrite/queue?sample=50` (curation policy, reads only)
  gives the queue under the lane's current version in the lane's own order (`ProseLaneService.Ordered`: approved
  withheld, then other withheld, each by the queue figure, most copied first, then quarantined; never tried first,
  then the oldest try): `total`, `byTier`, `bySourceClass` (`none` for a recipe of no classed web source),
  `byApproved`, `aiReserved` (the rule-only tier's share), `keptOutByClass` (the withheld or quarantined recipes of
  permission-needed and forbidden sources) and the first recipes with their position, host, class, approval, state
  and queue figure. It reads the queue as a live lane with a live rule-only tier would take it.

## Version 4 (2026-11-24, `cleanup2/ck15-prose`): the whole method, and a strict copy check

The lead's note of 2026-11-24, after the proof of check v7: no word-order rule can sort the earlier paraphrases
at the bars, so they are withheld by origin and the lane rewords their whole method; the copy check is used on
the lane's own new text, where it can be strict because a false copy verdict only costs a retry.

- **Whole method only.** The repair-by-sentence mode is gone: `ProseReword.RunAsync(input, writer, judge)` rewords
  every sentence of the source method, after the writer has read the whole method, the step and the sentence; it
  sees the source. The description is built by rule. `ProseCopiedSentences` is only the queue figure: the lane takes
  approved withheld recipes first, then other withheld ones, each most copied first, then quarantined imports.
- **Strict copy check** (`ProseRebuilt`, `prose-check.md` "Check v8"): each reworded sentence passes only when it is
  clearly rebuilt; a synonym swap, drop or move in the source's frame is refused with reason `copy` and reworded
  again. The gate refuses a text with any sentence it reads as a copy, after the prose check and before the judge.
  The writer's prompt says so ("swapping words for synonyms while the sentence keeps its shape is copying too").
- **The retry cost.** On 21 staging methods rewritten by hand, 47 % of the first-round rebuilt sentences were refused,
  14 % in a second round and 7 % in a third; 14 of 21 recipes passed whole within three rounds. The lane's give-ups
  will be mostly `copy` until its writer rebuilds as strictly; the next prompt round measures it.
- **The judge's model is a setting:** `Ai:ProseJudgeModel` (else the roster's reader B), so the next prompt round can
  try `mistral-small:24b`; the lane refuses a judge outside the OSI-licensed families or of the writer's family.
  The scorer's judge is `--check-model`, else `Ai:ProseJudgeModel`, else reader B.
- **Version** `v4-…` (`ProseLanePrompts.LaneVersionPrefix`): every recipe given up under v3 is eligible again, and the
  re-check reads a method whose latest lane rewrite carries a `v4-` tag as the lane's own (exempt).
- **Ships paused.** The lane has no golden record under its new signature, so the frame keeps it in shadow (it writes
  nothing) and it cannot qualify; it is journaled and undone by `ProseRewriteUndoService` as before, and stops on
  `ai-pause-prose-rewrite`.

## Prompt round on version 4 (`reword/4`, `cleanup2/prompt-prose-rewrite`)

The first scores of the source-reading writer (qwen2.5:14b-instruct, judge mistral-nemo) showed it giving up on
nearly every recipe: on the working half 48 of 49 recipes, 28 on a lost fact and 18 on the copy check. The round
changed how the writer is asked and told, never the copy line, a fact rule or a golden row:

- **The facts to keep, by rule, beside each sentence** (`ProseReword.Keep`): its amounts as written, the foods of
  the recipe's lines it names, each cooking verb with the words that keep it (`ProseVerbLexicon.WordsOf`), its heats
  in the fixed phrases of `ProseFactSheet.HeatsOf`, how a food is cut and the oven. These are exactly what
  `SentenceProblems` checks, so the writer is told up front what it is checked on.
- **What failed, said so it can be put right.** A lost fact names the amount to write as listed, the food to name
  or the action with its words; a copy says which measure fired and names the writer's own words that keep the
  published order (`ProseReword.Followed`; for a formula its facts too). The writer is also shown its last refused
  rewrite. The texts go only to the writer, which reads the source anyway; an attempt keeps only the class.
- **Five rewrites of each sentence in one call** (`rewrite`, `other`, `third`, `fourth`, `fifth`), each asked in a
  named frame (the time or the state first, the food as the subject, the vessel or the heat with the first action as a
  condition, two clauses joined by "which" or "so that", the most unlike the source); the first that keeps every fact
  and is clearly rebuilt is kept. The prompt tells the writer that small words kept in the source's order make a copy,
  to name things by what they are in the dish and to give each rewrite four or more words of its own, but no fact the
  sentence does not state. **Five rounds** (`RewordRounds`: temperature 0, then 0.5
  seed 7, 0.8 seed 11, 0.7 seed 13, 0.9 seed 17).
- **A step the judge refuses is reworded again** in the next round, while rounds remain, with the kinds of fact the
  judge found lost, changed or added; the whole goes through the gate again. Before, one refusal gave the recipe up.
- **The prompt** keeps the three readings and adds how to rebuild a fact-dense sentence (another subject: the food,
  the pan or the time), no semicolons, one sentence per rewrite, and examples of our own (no golden text).
- **Fractions** are written the way the source sentence writes them (`SameFractions`: "1/4" where it has "¼"), so the
  same amount is not read as a lost one; any other amount stays as the writer wrote it.
- **A measured food's amount is put back** (`PutBackAmounts`): where the source sentence gives an amount right before a
  food of the recipe's lines ("¼ teaspoon salt") and the rewrite names that food once without it ("the salt"), the
  amount goes back before the food. A time, a food named twice or already measured, and a food the rewrite names after
  another word of its own are left as written, for the fact check to read.
- **Irregular participles** ("beaten", "shaken", "frozen", `ProseVerbLexicon.Participles`) keep their verb in a
  rewrite ("the eggs should be beaten") and are never read as a source verb to keep.
- **Rule fixes.** A verb after a named food ("let the butter melt") is read as a verb (`RuleOnlyAudit` reads a masked
  food as a word, not a gap after "the"); the cut rule (`ProseCleanRoom.MannerProblems`: "thinly", "finely",
  "coarsely", "roughly", "thickly", or their plain forms) refuses a step that leaves out how a food is cut, in the
  writer's checks and in the gate before the judge, since the judge let such a step through.

Scored on qwen2.5:14b-instruct with the judge on mistral-nemo: 89.1 % of the 258 cases (working half 93.1 %,
held-out 85.0 %), 4 of 164 planted faults accepted by the judge, 70 of 94 recipes through within the five rounds,
about 136 s a recipe on a host that keeps both models loaded; below the bar of 95 % and no false accept, so no
record is committed and the lane stays in shadow. The judge's refusals of faithful steps are the largest cause of
the recipes given up. `build/prompt-prose-rewrite.md` (outside the repo) holds the rounds, the yield, the cost and
what is left.

## Prompt round n on version 4 (`reword/5`, the judge at round D)

With the judge on `mistral-small:24b` and its own rules (`judge-pair/3`), most refusals of m1's text were filler
the writer added: a clause that states nothing ("by which time the mixture is well combined"), a readiness, a
purpose or a sign of doneness the source does not give. The round fits the writer to that judge; the copy line and
every fact class are as before.

- **Filler refused by rule** (`ProseReword.Filler`, reason `filler`): a rewrite that adds "by which time", "at which
  point", "by the time", "ready", "the next step", "the process", "complete", "in order to", "to ensure", "which
  allows", "so that" (unless the sentence gives a sign of doneness), "doneness", or an "until", "till" or "as soon as"
  where the sentence gives no sign of doneness, fails that rewrite; the next of its five rewrites is tried, and a
  rewrite that only stays a copy is preferred to one with a lost fact or filler. A frame whose words the source
  sentence has is its own.
- **The prompt** no longer offers "By the time ..." or "what the cook sees", names the filler it refuses, asks for
  the four own words without a result or purpose, and names the five frames without "which", "so that" or "by
  which time" (the time, the vessel or the heat first; the food as the subject; the first action as a condition; the
  passive with the actions in order; the most unlike). The examples carry no filler.

Scored on all 258 cases (qwen2.5:14b-instruct, judge mistral-small:24b): 76.4 % (working half 77.1 %, held-out
75.6 %), 4 of 164 planted faults accepted by the judge, 37 of 94 recipes through within the five rounds (57 given
up: 28 on the copy check, 6 on a fact, 23 on the judge), about 148 s a recipe on a host that holds one model at a
time. Below the bar of 95 % and no false accept, so no record is committed. Three further rounds (the judge's
settled filler allowed, the sign of doneness kept by rule, the writer told where its own words may come from, m1's
prompt with only the guard) each lost recipes to the copy check and were not kept. `build/prompt-prose-rewrite.md`
(outside the repo) holds the rounds, the cost and what is left.

## Version 3 (2026-11-24, `cleanup2/ck11-prose`): the writer reads the source

The owner's answer of 2026-11-23: "The entire text needs to be read for context, then the paragraph,
then the sentence. Then rewording can happen". The clean-room writer, which never saw the source, is no
longer the design; the fact sheets stay as the checker's input.

1. **Work list.** A withheld or quarantined recipe is reworded from its source method, every sentence;
   its description is built by rule (`ProseDescription`). A recipe withheld for its copied sentences
   (`ProseCopiedSentences` above 0, not facts-only and not AI-reserved) keeps its text, and only the
   sentences the prose check reads as copies are reworded; a copied sentence of its description is
   replaced by the rule description. A facts-only or AI-reserved recipe is reworded as a whole whatever
   its count. Order: approved withheld, other withheld, quarantined.
2. **Three readings, then the sentence** (`ProseLanePrompts.Reword`, `IProseRewriter.RewordAsync`). One
   call a step: the writer is given the dish, the recipe's ingredient lines, the whole source method,
   the step as it now reads and the sentences of it to reword, and answers in that order with what the
   dish and the work are (`method`), what the step is for (`step`), and for each sentence what the cook
   must do or know (`reading`) and only then the `rewrite`.
3. **Facts first, then copy, sentence by sentence** (`ProseReword.SentenceProblems`). Each reworded
   sentence must hold the same ingredients, the same amounts as written, every cooking verb and every
   heat or oven of the sentence it replaces, add no amount (`ProseCleanRoom.RuleOnlyAudit`,
   `ProseCleanRoom.HeatProblems` from the fact-sheet rounds), and be neither a copy nor a sentence kept
   nearly word for word of the source (`ProseSentences`). A sentence that fails is reworded again in
   the next round with what failed, in the lane's words; three rounds (`RewordRounds`: temperature 0,
   then 0.5 seed 7, then 0.8 seed 11), then the recipe is given up under its lane version and keeps its
   state (withheld stays withheld).
4. **Gate.** The whole text must not fail the prose check, `contradicts-lines` included (an amount the
   recipe's own line contradicts, the checkpoint 6 madeleines' class), and may hold no copied sentence;
   then the judge (mistral-nemo) reads each changed step against the step it replaces (the source step
   for a whole rewording). A recipe is shown when all of it passes, and not before: a recipe with
   copied sentences is withheld until then; a rewrite clears its `ProseCopiedSentences`.
5. **Frame.** The lane version is `v3-` with the reword, rule-rendering and judge signatures and the
   two models, so every recipe given up under v2 is eligible again. The lane ships in shadow on the new
   version and cannot qualify: `prose-rewrite` has no committed golden record under the new signature
   (`ProseLaneGolden.GoldenProblem`), and the pause switch `ai-pause-prose-rewrite` stays on until an
   operator lifts it. Every write is journaled and undone by `ProseRewriteUndoService` as before.
6. **Golden lane rows and scorer.** `--golden-score --lane prose-rewrite` rewords each recipe of
   prose-judge.tsv from its method as a whole (`prose-rewrite:recipe:<key>`), rewords the copied
   sentences of each faithful rewrite the sentence rule finds some in (`prose-rewrite:repair:<id>`,
   `ProseLaneGolden.IsRepairCase`) and puts each planted fault through the gate. No record is
   committed: the next prompt round scores it on the model host.

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
| stock-phrase false alarms (v1) | v1 counted any 8-word run, including phrases every site uses. | Check v4 counts every word in full and a run from 16 words, with the close stretch, share, description and sentence tests beside it; stock phrases count like other words, since the limits are set and measured that way (`prose-check.md`). A rewrite the check is only unsure of goes to the judge like one it passes. |

## The pipeline (per recipe)

1. **Mask (rules).** `ProseMasking.Mask` reads the source method (the newest `jsonld` document;
   a quarantined recipe without one uses its own stored text) with the prose check's quantity
   reader: every amount becomes ⟦Qn⟧ (a bracketed conversion is its own token and may be dropped),
   every word naming an ingredient line's head word becomes that line's ⟦Ln⟧, and step markers and
   cross-references are gone.
2. **Extract (writer model).** The masked method becomes fact sheets (`StepFacts`: actions, objects,
   equipment, amounts, cues; JSON schema). `ProseFactSheet.Problems` checks them: one per step, every
   amount token of the step exactly once and none of another step, every line token kept, every
   lexicon verb kept, no entry over 6 words, no digit or amount outside the tokens. Sheets that fail
   are first mended by rule (`ProseCleanRoom.MendSheets`: the step's own amount tokens, a missing
   ingredient token or lexicon verb added, an entry with an amount, a number or too many words
   dropped), so one slip does not cost the extractor's equipment and cues; sheets that cannot be
   mended are asked once more with the problems; sheets that still fail are built by rule from the masked step (its
   verbs, line tokens and amount tokens, the oven when it names one, no cues). Rules then add to each
   sheet (`facts/2`) the stove heat, oven heat and serving temperature the step names, in the lane's
   own fixed phrases (`ProseFactSheet.HeatsOf`: "over medium heat", "in a hot oven", "serve cold"),
   which amount token measures which ingredient token (`MeasuresOf`: "⟦Q2⟧ of ⟦L3⟧") and which time
   token belongs to which verb (`TimingsOf`: "fry for ⟦Q4⟧"), and put the actions in the step's order
   (`InStepOrder`), since the writer, which never sees the step, cannot know any of these and left
   heats out more than anything else. Unmasking writes a line token with the words of its name the
   method puts before it ("olive oil", not "oil"; `ProseMasking` `mask/2`). A written step that leaves out a
   measured amount, or puts a time on a food, is put right by rule before the form rules
   (`ProseFactSheet.RepairMeasures`).
3. **Write (writer model).** From the sheets, the ingredient names (`⟦L1⟧ = olive oil`) and the dish
   name only, one call per step (all sheets shown, the one step written, JSON schema `{"step":"..."}`)
   with that step's checklist line (`ProseFactSheet.Checklist`: its actions, ingredient tokens,
   amount tokens, heats and measures) and the problems that name it. Attempts: style "steps" at
   temperature 0; the same at 0.6 with seed 7 and the reasons; style "actions" at 0.3 with seed 11
   and the reasons; then the rule rendering of the sheets (`ProseFactSheet.Render`, a measured
   ingredient written "⟦Q2⟧ of ⟦L3⟧"), which needs no model, also of rule-built sheets. An attempt
   after the first writes again only the steps the last one failed on (the steps a form problem, a
   check reason or the judge names; every step when none is named) and keeps the others
   (`ProseLanePrompts.Retry`, `retry/2`), so one hard step no longer costs the whole recipe.
4. **Gate.** The form rules on the masked text (`ProseCleanRoom.FormProblems`, which also hold the
   judge's heat rules, `HeatProblems`, so a step that drops or changes its heat or its oven is
   written again before the judge is asked); the tokens put back;
   prose check v2 against the source with the current stock index; the judge (reader B of the
   roster). The first attempt all three pass is the rewrite. A rewrite the check is only unsure of
   (a source verb not found, amounts out of order, an added amount, a length outside its bounds)
   goes to the judge, which reads the check's weak signal for each step as a note.

   **The judge** (`OllamaProseStepJudge`, the unpaired-parts judge, `judge-rules/11`). Rules decide first and ask no
   model (`ProseJudgeRules.Before`): a rewrite with another number of steps, one the prose check
   fails on a fact (the copy rule is no question of meaning), and a step pair whose stove heat,
   oven heat or serving temperature differ (`ProseJudgeRules.Heats`: "over medium heat" and "on
   medium" agree, "moderate oven" and "hot oven" do not) are faults, and so is a step pair whose
   original names a stove heat, an oven heat or a serving temperature the rewrite step leaves out
   (`ProseJudgeRules.HeatsLost`; a serving temperature is kept by its word anywhere in the step) or
   names the oven the rewrite step neither names nor bakes or roasts in (`ProseJudgeRules.OvenLost`).
   Then a rule pairs each clause of the original step with the rewrite step (`ProseJudgeRules.Pair`):
   a clause is paired when every content word of it is in the rewrite step as written, with an ending
   dropped, by its root ("creamy", "cream"), as a word of the same meaning in the rule's own table of
   actions, utensils and cues ("pour" and "add", "basin" and "bowl", "tender" and "soft"), or as a verb
   of the same group of the prose vocabulary, its irregular participles included ("should be beaten");
   the generic "cook" never keeps another method, and a cooking verb counts only where it stands as a
   verb ("into cubes" is no cutting). Voice ("should be", "gets", "needs"), articles, the order of the
   clauses, a repeated unit word, a heat level (the heat rules above decide it), taking or switching
   off the heat said either way, and the amounts of a step the prose check finds the same ask for
   nothing; words of degree in a cue do ("slightly thickened" is not "very thick"). A rewrite clause
   with a content word the original step does not hold, that no other step of the method names and
   that is more than a verb of placing, or with a stove or oven heat the original does not name, is a
   thing that may be added. A rewrite step shorter than 0.8 of its original's words is read whole even
   when every clause pairs, since a terse step can hold every word and lose how they go together.
   The rules only the judge runs have their own version (`judge-pair/1`, `ProseJudgeRules.PairVersion`,
   beside `judge-rules/11`, which the auditor shares). Under it a word of manner of the original
   ("lightly", "gently", "quickly", "on each side"; "a side" keeps "each side") and the side a food
   faces ("skin side down") must be in the rewrite step; the writer's filler frames add nothing when
   the method has their action at that place (`ProseJudgeRules.Filler`: "by which time they are
   ready for frying" and "sliced for frying" name an action of this step or a later one, "before
   frying" one of a later step, "once the fritters are fried", "with the noodles cooked" and "after
   toasting" one of this step or an earlier one, "which is needed for the next step" and "to prepare
   the cream" the next step or a food of the method); the head word of the recipe title ("the dip" of
   a lentil dip) adds nothing when no step names it (`ProseJudgeRules.Dish`); and a step that binds an
   amount to something it does not measure ("3 minutes of mushrooms", "cook for ¼ teaspoon") is a
   fault by rule (`ProseJudgeRules.AmountMisbound`), since its amounts agree and every word pairs.
   Only what the rule cannot settle reaches the model, each in a call of its own (temperature 0,
   seed 17, context 8192, a JSON schema), with the recipe title and the previous step of both texts:
   an unpaired clause with the pairs the rule made and the
   words it could not match (`JudgePart`: kept, changed or lost, with the rewrite's words), and a
   possible addition with the whole original method and the words the original does not hold
   (`JudgeNew`: said or new, with the original's words). An answer stands only when its quote is in
   the step by rule (`ProseJudgeRules.PartKept`, `.PartSaid`) and names no other heat. A step is the
   same only when every clause is paired or kept and nothing is added; an answer that cannot be read
   is no pass, and a step where the model keeps a part with words it then reads as new is refused
   with the note `disagreement`, which the scorer's failures show, so these are counted. Its examples are written for the prompt; none is a golden row. The judge model is the
   roster's reader B unless `Ai:ProseJudgeModel` names another (the owner approved `mistral-small:24b`
   as a candidate judge).
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
`Ai:ProseJudgeModel` when set (a prompt round may try `mistral-small:24b`), else reader B (`mistral-nemo`). Auditor: the roster's auditor (`phi4`). The lane refuses to run (and says
why on the Pipeline page) when the judge is of the writer's family or the auditor of either.

A recipe whose source reserves its content against AI use (`Recipe.SourceAiReserved`, see
`source-rights.md`) never reaches the models: the lane takes the model-free path
(`ProseCleanRoom.RunRuleOnly`: rule sheets, rule rendering, form rules, the prose check and the strict
copy check of new text, `ProseRebuilt`, in every sentence; no judge), records
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
live, at least 30. The auditor reads each sampled rewrite against its source and the recipe's own
ingredient lines (`OllamaProseAuditor`, `ProseAudit`). Rules decide first: the prose check of the pair
run with the lines, so a text that disagrees with its lines (`contradicts-lines`) or fails another fact
rule is flagged, and a step count, heat or oven changed or left out (`ProseJudgeRules.Before`), then
the audit's own rule: an "until" sign of doneness of a source step whose state word the rewrite step
does not hold, in a rewrite step that says no "until", "when" or "once" (`ProseJudgeRules.CueLost`); no
model is asked then and the flag needs no re-read. Otherwise the auditor reads each changed step pair
(a step that differs only in articles, punctuation or a word said twice is not changed) in a call of
its own (`ProseLanePrompts.AuditStep`) with the dish, the lines, the whole source method
for context and the rules' notes on that step (amounts one step states and the other does not,
`ProseAuditEvidence`, and the check's weak signals; hints, never verdicts), lists each difference
(left out, changed, added) with both texts' words before its verdict; temperature 0 and a fixed seed.
A difference stands only when a rule cannot show it is wording only (`ProseAudit.Standing`): a thing
left out or changed whose source words, words of size and manner included, are all in the rewrite
step (as written, with an ending dropped, or as a word of the same group) is dropped, unless, for a
thing changed, the rewrite's words hold a word the source step does not ("cover the mixture with a
jar" for "cover the jar") or the two hold the same words in another order; a thing added whose words are all in the source step is
dropped; a difference whose source words are not in its step, or a "differs" that lists nothing,
stands. A step with no standing difference is not flagged. Its rubric is its own:
it lists differences, where the judge lists every fact of the step and then asks again about each it
does not find. A flag of the model counts as an error only when the auditor, asked again blind with
the confirmation prompt about one of the steps it flagged (in order, at most three), agrees
(`ProseLaneAudit`). The judge never confirms: every rewrite in the sample
already passed it, so a fault it misses it misses again, and a check it confirms would find almost no
error whatever the lane's real error. The check's sensitivity and false-flag rate come from the
`prose-audit` record (below), scored with the same two auditor reads, and the frame corrects the error
with them. What remains is a shift of population: the sample holds faults the judge missed, which may
be harder for the auditor too than the planted faults it is scored on. Its rules are the gate's own
(the gate runs the check with the lines and the judge runs the same rules first), so on the sample,
which passed the gate, they find nothing, while on the golden rows they decide many planted faults:
the record's sensitivity is higher than the model part's alone, which is what finds errors in the
sample. The auditor is of a third
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
words, 4 public domain), each with one faithful rewrite, three or four planted faults written by hand
and up to 13 more planted by `ops/golden/plant-prose-faults.py` (130 rows), and 86 recipes written for
the set to measure false refusals: 45 faithful rewrites written for the set and 55 rewrites of the
lane's own writer, each judged step by step (13 faithful, 42 not), 230 rows with 66 faithful. Scored by
an operator on a model host, never in CI:

```
dotnet run --project nom-api/Nom.Import -- --golden-score --lane prose-judge   --host <model host> --model mistral-nemo --record
dotnet run --project nom-api/Nom.Import -- --golden-score --lane prose-audit   --host <model host> --model phi4 --record
dotnet run --project nom-api/Nom.Import -- --golden-score --lane prose-rewrite --host <model host> --model qwen2.5:14b-instruct --check-model mistral-nemo --record
```

`prose-judge` takes `--split working|held-out|all` for prompt rounds; a record is always scored on
every row, and the scorer prints the faithful rewrites it refuses as a figure of its own (false
refusals, which the floor does not count). The unpaired-parts judge (`judge-rules/11`, `judge-pair/3`,
round D) scores 91.7 % (211/230) on `mistral-small:24b` with 4 of 164 faults let through (2.4 %) and
15 of the 66 faithful rewrites refused (22.7 %, 3 of them as a disagreement), at about 6.5 s a row
(working half 97.1 %, held-out half 87.2 %); round C (`judge-rules/11` alone) scored the same 91.7 %
(working 95.2 %, held-out 88.8 %), and on mistral-nemo 78.3 % (180/230), 4 of 164 let through and 46
of 66 refused, at about 1.5 s a row. All are below the floor, so no record is committed (kept with the
evidence); the judge before them (prompt version 3, `judge-rules/7`,
check v5) scored 87.8 % on mistral-nemo with 5 let through and 23 refused, and had to be
scored again. The judge's signature is part of the `prose-rewrite` signature and
of the lane version, so `prose-rewrite` must be scored again with it, and the `prose-audit`
signature carries the judge rules' version, so it moves too. `prose-rewrite` (writer
`facts/3`, `mask/2`, `retry/2`) with qwen2.5:14b-instruct and that judge scores 86.3 % (113/131) on
its working half, 2 of 82 faults let through, 15 of 49 recipes through at the first attempt, 8 on the
rule rendering, 16 given up, about 98 s a recipe; below its floor, so no record is committed (kept
with the evidence). The no-source writer is no longer the design (the owner's answer of 2026-11-23:
the writer reads the method, the step and the sentence before it rewords); the fact-sheet heats,
measures and timings, the heat form rules and the measure repair stand for the writer that replaces
it. `prose-audit` (rules with the lines, then phi4 step by step with its wording-only differences
dropped by rule, `audit-rules/2`, check v8, `judge-rules/11`) scores 89.6 % (206/230) on phi4, over its
floor: 4 of the 164 faults let through and 20 of the 66 faithful rewrites flagged (30 %; the score counts
them; 28 of 66 under `audit-rules/1`; 90.4 %, 3 and 19 under `judge-rules/7`). Its record is committed
(`scores/prose-audit.json`). Its signature holds the prose check version and the judge rules' version,
so its record is made again after each new check version or judge rules version.

From v4 the `prose-rewrite` cases are each recipe of the file reworded as a whole (`prose-rewrite:recipe:<key>`)
and each planted fault put through the facts gate (the check, then the judge; the strict copy check is not part of
it, so a fault is refused for its fault and not for its wording); the repair cases of v3 are gone.

Floors (`model-lanes.json`): `prose-rewrite` 95 % end to end and 0 planted faults let through;
`prose-judge` 95 % with at most 2 % false accepts; `prose-audit` 75 %. Commit the three records under
`scores/`; the build compiles them in, and the running lane reads them. Any change to a prompt, the
attempt schedule, the masking, fact-sheet or description rules, or the check version changes the
signature and needs new records.

## Settings

`Ai:BatchOllamaUrl` (on/off), `Ai:RewriteModel`, `Ai:ProseJudgeModel`, `Ai:Roster:*`, `ProseRewrite:IntervalMinutes` (10),
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
