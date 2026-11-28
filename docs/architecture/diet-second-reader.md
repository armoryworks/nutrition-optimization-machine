# The second diet reader (measured; a shadow lane behind a setting since ck21)

Plan item ck20 (wave 30). The rule reading of the severe diet types (`DietFoodWords`, `DietRecipeText`, the
consensus and the gate) shows about one recipe in 80 of the allergen-weighted proof picks that it should not
(draws 18 to 21: 19 misses in 1,320 shown picks), and what it misses is everyday food knowledge no list
finishes: a frittata is eggs, a gravy is thickened with flour, prunes are dried fruit, a pie made "as you would
a beef-steak pie" has a crust. At the other end it holds 30,000 to 67,000 recipes per type as unknown, most of
them plainly safe. This item measures whether a local model, as a second and independent reader, fixes both
ends. It builds the reader and its golden scorer; ck21 adds the shadow lane and the gate setting (last section).

## The reader

`Nom.Orch/Services/OllamaDietSecondReader.cs`. One call per recipe to an Ollama host, at temperature 0 with
seed 1, answered as JSON by a schema:

1. `unlisted`: every part of the dish the ingredient lines do not give (a dish the title names, a sauce, dough,
   crust or coating the method uses, another recipe it is made as, a method that breaks off), each with what it
   is usually made of;
2. `complete`: whether the recipe itself gives the whole dish;
3. for each of the eleven severe types (`DietVocabulary.Classes`): the carrier food and one of `contains`
   (contains or very likely contains), `may` (may contain or cannot tell) and `free` (clearly free).

The user message holds the title, the ingredient lines and the method steps (cut at 4,000 characters), never
the rule verdict. The prompt defines what each type forbids and gives five worked examples that are not rows of
the set (a leek tart, fried smelts, a plum sauce made as another, a turnip pie baked as a meat pie, stewed
rhubarb). One deterministic rule follows the reply: a recipe the reader finds is not whole (`complete` false or
missing) is never `free`; a `free` answer becomes `may`. A reply missing a type or answering another word is
not read at all.

The reader only reads and flags. It takes an `HttpClient`, a host and a model; no other service, lane, gate or hosted
service takes it (`GoldenDietSecondReaderTests.The_reader_is_wired_into_nothing_and_holds_nothing_that_could_write`
reads every type of Nom.Orch, Nom.Api and Nom.Data, its fields, properties, signatures and method bodies, so a
reader built inside a method or registered for injection fails it),
it writes nothing, and it authors no nutrition number. Since ck21 the shadow lane `DietSecondReaderLaneService` builds it
(the test allows that lane and `DietSecondGate` only). Its signature (`OllamaDietSecondReader.Signature`) hashes
the instructions, the type keys, the answer words, the options, the schema, the method limit and the prompt
version.

## The golden lane `diet-second-reader`

`Fixtures/Golden/diet-second-reader.tsv`, one row per recipe and severe type:

- `truth`: `contains` (contains or may contain the type's food), `safe` (plainly safe) or `doubtful`;
- `rules`: the gate's verdict when the row was judged (`shown`, `flagged`, `unknown`);
- `kind`: for a shown row judged `contains`, the miss kind. Hidden-food kinds (`DietReaderReport.HiddenKinds`):
  `title`, `base-lost`, `paste-lost`, `method-food`, `named-dish`, `word`, `composite`, `catalog`, `link`,
  `made-product`, `analogue`. Kinds that hide no named food: `referral` (made as another recipe), `choice` (a
  line left to the cook). Other rows carry the hold or pick kind;
- `weight` and `frame`: a sampled safe or doubtful shown pick of the whole proof draws (frame `draws`) stands for
  `weight` picks of its draw, so the weighted shown rows of the frame are the bar's denominator.

The rows come from judged samples only, every one judged by an agent reviewer (status `agent`, source
`agent-*`); no model's answer set a truth:

| Source | What it gives |
|---|---|
| ck17 draws 6 to 17, ck18 draws 18 to 21 (`build/ck17-diet-proof.drawN.ids`, `ck18-composites.drawN.ids`, the two `draws.tsv` logs) | every shown miss (with its kind) and every doubtful shown pick; 6 safe shown picks a draw, weighted; the wrong flags; a sample of right flags; unknown holds of the ck17 cells a reviewer judged 10 of 10 plainly safe |
| checkpoint 8, operator 1's diet reading draw (`staging/ck8-samples/ck8-op1-diet-reading-*`) | per recipe: shown misses and doubtful, wrong and by-design flags, every unknown and title or method hold judged plainly safe, over-held or right |
| ck16's review reads and draw 2 (`build/ck16-diet-known.review-reads.tsv`) | released recipes judged right or a miss; method holds judged plainly safe or right |
| checkpoints 6 and 7, operator 1's coverage samples | shown misses and title gaps, doubtful shown picks, over-flags, title and method holds |

Only cookbook recipes (public domain, read from staging's stored text) are committed; the web recipes of the
same samples were measured from files outside the repo, with the same scorer (`--golden <dir>`). The split is
by a hash of the recipe id (`ModelLanes.InDietSplit`), so all types of one recipe fall in one half and one call
serves them.

Scoring: a `contains` row passes when it is not read free (reading it free is a false accept); a `safe` row
passes when it is read free; `doubtful` rows are reported, not scored. `model-lanes.json` gives the lane no floor
(`minScore` 0, `required` false) and no record is committed: until the owner sets a bar, the release gate
reports it as unscored.

```
dotnet run --project nom-api/Nom.Import -- --golden-score --lane diet-second-reader \
    --host http://<model host>:11434 --model <model tag> [--split working|held-out] [--limit N] \
    [--answers answers.jsonl] [--agree other-model-answers.jsonl] [--replay answers.jsonl] [--golden <dir>]
```

`--replay` reports a kept run again without a host. Kept answers carry no prompt signature, model or row set, so a
replay takes neither `--record` nor `--limit`; a record is only ever written by a run that asked the host.

Besides the score the run prints, per severe type and in all: (a) the shown misses the reader does not call
free, and by miss kind; (b) the weighted share of shown safe picks it does not call free (the coverage it would
cost); (c) of the unknown holds judged plainly safe, how many it calls free, and of those judged not safe, how
many; the flagged side as a check; for the `draws` frame, the hidden-food misses the rules and the reader would
both show, over the weighted picks the two would still show (not the picks the rules alone show), with the Wilson
95 % interval, and the share of shown picks the reader keeps. The picks still shown are estimated from about six
weighted safe samples a draw, so the verdict against the bar of 1 in 330 is taken from the upper bound with the kept
share of the sampled rows at its own Wilson 95 % lower bound (`DietReaderReport.BarOf`); with `--agree`, the same unknown and shown figures when both readers call a
recipe clear; and the seconds per recipe with the hours for 116,000 recipes.

## Measured

See the build report `build/ck20-diet-second-reader.md` in the audit evidence for the full tables. In short:
prompt version 1 (the `unlisted` and `complete` steps), measured on 2026-11-26 and 27 over 575 judged rows (cookbook
and web recipes; 305 recipes in the working half, 264 held out), with phi4, qwen2.5:14b-instruct and mistral-small:24b.
Held-out half, scored once:

| | Rules alone | + phi4 | + qwen2.5 | + mistral-small | + mistral-small and phi4 |
|---|---|---|---|---|---|
| Hidden-food misses per weighted pick still shown (draws frame) | 14 in 2,932 (0.48 %, CI 0.28 % to 0.80 %) | 5 in 2,678 (0.19 %, 0.08 % to 0.44 %) | 6 in 2,093 (0.29 %, 0.13 % to 0.62 %) | 4 in 2,523 (0.16 %, 0.06 % to 0.41 %) | 1 in 2,458 (0.04 %, 0.01 % to 0.23 %) |
| Upper bound with the kept picks at their lower bound | | 0.51 % | 0.77 % | 0.48 % | 0.28 % |
| Shown picks kept | 100 % | 91.3 % | 71.4 % | 86.0 % | 83.8 % |
| Shown misses held (all kinds) | 0/45 | 39/45 | 37/45 | 39/45 | |
| Unknown holds judged plainly safe called free | | 59/71 | 46/71 | 53/71 | 49/71 (both) |
| Unknown holds judged not safe called free | | 3/22 | 4/22 | 2/22 | 2/22 (both) |
| Seconds a recipe (116,000 recipes) | | 13.1 (423 h) | 9.4 (304 h) | 20.9 (672 h) | about 1,100 h |

- One second reader meets the bar of 1 hidden-food miss in 330 weighted shown picks only at the point figure.
- The rules with mistral-small and phi4 both clear meet it at the upper bound on the held-out half (0.28 %), but on
  the working half only at the point figure (1 in 1,999; upper bound 0.35 %). It is not proven on both halves.
- Releasing unknown holds when two readers agree is not safe: weighted to the real share of not-safe holds, about 1 in
  45 released recipes would hold the food.

## The shadow lane and the gate setting (plan item ck21)

Built in `cleanup2/ck21-approved-pool` (see `diet-gate.md`, "The second reader in the stored gate"): the pair
mistral-small:24b and phi4 reads the approved pool and the recipes waiting for approval that only the reading keeps
waiting as the shadow lane
`diet-second-reader` and stores each reading per recipe and model with its prompt, text and store signatures. The stored
gate reads the readings only while the platform setting `diet-second-reader-gate` is on (off by default): a recipe the
rules find clear is then shown to a severe type of the reader only when both stored readings call it clearly free, and a
recipe not yet read is held as not yet read. The auto-approval lane waits for both readings and for the rules to
understand a recipe before it approves it. The reader never releases an unknown hold. The prompt is version 1 as measured
here; a `num_predict` cap or any other change is a new signature, which must be measured again before the setting is
turned on, and makes every reading stale.

## Batches, its own pause and readings that travel (plan item ck23)

Built in `cleanup2/ck23-second-reader` (see `diet-gate.md`, "The second reader in the stored gate" and "Judged holds").
Checkpoint 9 could not run the lane: `ai-pause-all`, which the operator steps keep on, held it, and by its code it read each
recipe with both models in turn, a model swap per recipe on a host that holds one model at a time (U4). The lane only reads
and stores readings; it never writes to a recipe, so it is no writing lane and `ai-pause-all` no longer holds it (its own
switch `ai-pause-diet-second-reader`, quiet hours and the host breaker do). The Pipeline page says so beside "Pause all AI
lanes" (the lanes it does not stop, from `heldByPauseAll`) and on the lane while it runs with that switch on, so an operator
who frees the host for a scoring run pauses the reader as well. Its endpoint's readings of the last 24 hours and latest
reading come from the lane's decision journal, so an imported reading is current but not counted as read. A pass reads a batch of 100 recipes with
mistral-small and then the same batch with phi4, and resumes with phi4 when it was stopped between them.

**Readings that travel.** A reading costs about 35 s of model time a recipe (both models), so a reading made once is
taken again where it matches exactly instead of spending the hours twice:

- `GET /api/DietAdmin/second-reader/readings?after=&recipes=` exports the stored readings page by page (recipe id, model,
  prompt and text signatures, the answer word and carrier food per type); `currentOnly` (default) leaves out unreadable
  readings and readings of another prompt.
- `POST /api/DietAdmin/second-reader/readings/import?apply=` takes a reading only where the model is of the pair, the
  prompt signature is the reader's current one, the recipe is live and in the lane's scope, its text signature now equals
  the reading's (the text it was made of, signed as the lane signs it), every type has a known answer word, and no current
  readable reading of that recipe and model exists here; every other reading is left and counted by its reason. A dry run
  unless `apply=true`; every reading taken is a `DietReadingImport` entry in `audit."AuditLogEntry"` with the row before and
  after, under the import's stamp, and `POST .../import/undo?stamp=` puts the row before back (or removes the row) where the
  reading is unchanged since.
- The offline scorer (`--golden-score --lane diet-second-reader --answers`) now writes on each line the model, the prompt
  signature and the text signature of the recipe it was shown, so its answers import the same way. An older answers file
  (the ck22 and checkpoint 9 runs) carries none of these: `ops/diet-readings.py import --model --prompt --set` sends the
  set's title, lines and method as the text, and nom-api signs them; a recipe whose text on the target copy is not the
  text the scorer read is left (`text signature differs`).

A reading carries verdict words and food words only, never a nutrition number, and an imported reading is decided by the
gate as the lane's own would be: only while `diet-second-reader-gate` is on.
