# Golden sets and the release gate

A golden set is a fixed list of cases whose answer is a person's judgement or an authoritative
deterministic fact, never a model's answer and never the code's own output promoted unseen. Before a
release, or a prompt, model or rule change, goes to production, every set is scored; a release is
held when a lane scores below its floor.

There are two kinds:

- **Deterministic sets** score the line rules in CI with no database and no model host. They run in
  `dotnet test` (trait `Category=Golden`) on every push, and in the `gate` job of
  `.github/workflows/build-package.yml`, which both image jobs need.
- **Model sets** score a model lane on a model host an operator names, with
  `dotnet run --project nom-api/Nom.Import -- --golden-score ...`. The run is refused under CI, opens
  no database and writes nothing but its own score file. CI checks the committed score record.

All files live in `nom-api/Nom.Api.Tests/Fixtures/Golden/`. The read-only capture queries and the
fixture builder live in `ops/golden/`.

## File format

TSV, UTF-8, one header row; lines starting with `#` are skipped. In a cell, `-` means "no answer"
and `*` means "not judged" (the cell is not compared). Every judged file has an `id`, one or more
status columns, a `source` (where the judgement comes from) and a `note`.

| File | Unit under test | Expected columns | Status columns |
|---|---|---|---|
| `lines.tsv` | `IngredientLineParser.Parse`, `.StatedSize`; `IngredientNameNormalizer.Normalize`, `.Bare` | `quantity` (3 dp), `measurement`, `size` (`14 Ounce`), `wording`, `bare` | `parse`, `name` |
| `wordings.tsv` | `Normalize`, `Bare`, `IsResidue` | `normalized`, `bare`, `residue` (`yes`/`no`) | `status` |
| `line-kinds.tsv` | `RecipeLineClassifier.Classify` (the deterministic pre-pass) | `kind` | `status` |
| `veto.tsv` | `RecipeLineKindGuard.Refusal` (the line-label veto) | `expect`: a reason, `refused` (any reason) or `-` | `status` |
| `links.tsv` + `catalog.tsv` | `LineRelinkService.Decide` (the relink rule) | `expect`: `=Food` the rule moves it there; `~Food` there or nowhere; `stay`; `!Food` anywhere but there | `status`; `stays` (a line already on its `=`/`~` food is not moved) |
| `link-pairs.tsv` + `usda-catalog.tsv` | `IngredientLinkService.DeterministicTarget` (staple names, then the name matcher) | `rule`: `~id` the rule gives this food or none; `!id` never this food | `status` |
| `compound.tsv` + `compound-vocabulary.tsv` | `CompoundLineSplitter.Plan` | `kind` (`Split`, `PickAlternative`, `Leave`, `-`), `rule`, `parts` (food names, `\|`-joined) | `status` |
| `unfold.tsv` | `FoldedLine.Segments` | `segments` (count, or `-` when the line is not read) | `status` |
| `prose.tsv` | `RewriteFidelity.Check`; `ProseCheck.Check` (check v2, with `prose-stock.tsv`) | `expect`: a reason the check must give, or `passed`; `check2`: `passed`, a reason, `!reason` (must not give it) or `!quantity` (no amount reason) | `status`; `status2` |
| `prose-stock.tsv` | the stock phrases check v2 runs with | `sites` (stock at 3 or more) | none |
| `prose-verbs.tsv` | the verb groups of check v2, compiled into Nom.Orch | `words` (`\|`-joined) | none |
| `piece-counts.tsv` | `RecipeRepairGrounding.PieceRefusal` (repair lane fills and `quantities/revert`) | `expect`: `keep` or `revert` | `status` |
| `startup-fills.tsv` | `ScrapedRecipeRepairService.RepairBatchAsync` (the start-up repair's amount for a line that states none, in a recipe with its method, its other lines and, when `journal` names one, an open line journal) | `expect` (`<quantity> <unit>`, or `-` for no amount) | `status` |
| `link-gate.tsv` + `gate-catalog.tsv` | `LinkGate.Check` (the one link gate, see `link-gate.md`) | `expect`: `pass`, or the code of a rule that must hold the pair; the target's facts in the row | `status` |
| `wording-pairs.tsv` + `gate-catalog.tsv` | `LinkGate.Check` for two readers' answer; `LinkGate.Family` | `expect`: `=` must pass, `!` must be held, `~` must win its family | `status` |
| `catalog-admission.tsv` | `CatalogAdmissionRule.Decide` (USDA catalog admission: status, group from category, edibility, energy check, reasons, prepared food) | `expectStatus` (`Curated`, `PendingCuration`; the rule never rejects, and a food of the four prepared-food categories is judged like any other), `expectGroup`, `expectEdible`, `expectEnergy`, `expectReason`, `expectPrepared` (`true` for a Foundation or SR Legacy food of Baby Foods, Fast Foods, Meals, Entrees and Side Dishes, or Restaurant Foods) | `status` |
| `recipes.json` + `measurements.tsv` | `RecipeNutritionService.RecalculateAsync`, `.StatusFor` | `expected.calories/protein/fat` (4 dp), `countedLines`, `handLabel` | `status` |
| `line-labels.tsv` | model lane `line-classify` | `kind` | none (scored by the operator run) |
| `link-pairs.tsv` + `gate-catalog.tsv` | scorer lane `link-gate` (no model) | `verdict` | none (scored by the record) |
| `model-lanes.json` | every model lane | floors | — |
| `baseline.json` | — | allowed `known` rows per `file:column` | — |

`sources` in `unfold.tsv` holds only the recipe's source lines that equal a run of the line's own
parts (` ¦ `-joined), `{}` for an empty list and `-` for a recipe with no stored source. In
`prose.tsv` the rewrite is built by the test from the public-domain source: `identical` (the source
itself, must fail `unchanged`), `drop-step-N` (without a step that alone states a number, must fail
`lost-number`) and `add-number` (the last step gains "Rest it 437 minutes.", must fail `added-number`). `given` rows hold
the stored rewrite itself (`rewriteDescription`, `rewriteSteps`, and the recipe `title`), as short
excerpts of the staging rows a model operator judged on 2026-11-20 (source
`agent-review-staging-2026-11-20`, status `agent`): false alarms that must pass and real faults
that must fail. Check v2 (`check2`, `status2`, see `prose-check.md`) runs on every row, with the
ingredient `lines` when a row has them and an empty stock index when `stock` is `none`; its own rows
are constructed recipes and sentences, and no fault row may pass it and at most 5 % of the faithful
rows may fail it.

`recipes.json` keeps a recipe that is not public domain as numbers only: no title and no raw line.

## Status

- `ok`: the code must give the expected answer; a person set this status.
- `agent`: the expected answer was judged by a model reviewer (`agent-review-*` in `judgedBy` or
  `source`) and no person has confirmed it. The code must give it, as for `ok`, so it cannot drift
  from the reviewer's verdict, but the run reports these rows apart, and only a person turns one
  into `ok` or `known`.
- `known`: the expected cell holds the correct answer and the code still gets it wrong. When a fix
  makes it pass, the run fails with "NOW PASSES, promote it". The count per file and column may not
  exceed `baseline.json`, which only goes down. For `recipes.json`, `known` means the frozen data is
  wrong (an item weight, a yield read as servings); the arithmetic is still asserted.
- `new`, `review`: fail the run, so an unreviewed row cannot ship.
- `-`: the row is kept for reference and not asserted on that column.

Expected values change only by a hand edit that shows in the diff. A person reviews `review` rows and
sets them to `ok` or `known`; a model may pre-sort rows but never sets a status or types a value.
The test `No_row_judged_by_a_model_reviewer_is_marked_ok` keeps `link-pairs.tsv` and `links.tsv` from
holding an `ok` row whose judge is a model.

## The update switch

`NOM_GOLDEN_UPDATE=1 dotnet test nom-api/Nom.Api.Tests --filter Category=Golden` writes to the source
tree and does only this:

- fills the blank expected cells of a `new` row with the code's current output and marks it `review`;
- marks a `new` row whose expected cells are all judged `ok` or `known` by whether it passes
  (`agent` instead of `ok` when the row's judge is a model reviewer);
- flips a `known` row that now passes to `ok` (or `agent`, as above) and lowers `baseline.json`.

It never rewrites a judged cell and never raises a baseline (a new `known` row needs a hand edit to
`baseline.json` in the same commit). It is refused when `CI` or `GITHUB_ACTIONS` is set, and a run with
it always ends red. Run it twice when one file has two status columns, then build and run again
without it.

## Where the rows come from (first capture, 2026-11-20)

| Set | Source | Judged by |
|---|---|---|
| `link-pairs.tsv` | 7,718 wording → USDA pairs in `nom-audit-stack/evidence/automation-2026-11/judge/verdicts.pkl`; 262 of them were applied name matches on staging | reviewer agents, 2026-11-13 (`judgedBy` column) |
| `links.tsv` | 989 wording moves of `rollout-2026-11-16/verdicts.json` (agreed → `~`, refuted → `!`, none → `stay`) and examples 25–33 of the golden-gate design | reviewer agents, 2026-11-16; design |
| `lines.tsv`, `wordings.tsv`, `line-kinds.tsv` | examples cited in the golden-gate and count-fallback designs, the wave-1 report, the integration notes and commit d41eedf | design and report authors |
| `veto.tsv` | design-1 change 1 samples, and the veto's own definition (an amount, a curated name) | design; rule |
| `compound.tsv` | the top-80 compound wordings and sample part lines of the compound design | design |
| `unfold.tsv` | examples 1–16 of the unfold design, with source lists read from staging | design |
| `prose.tsv` | 12 public-domain recipes from staging, rewrites built by construction | rule (by construction) |
| `prose.tsv` check v2 rows, `prose-stock.tsv` (2026-11-20) | constructed recipes of our own text, two public-domain recipes and constructed sentences for the C9 faults and equal forms; fault rows are one deterministic edit of a faithful rewrite; the stock phrases are constructed | fault and equal-form rows: rule (`rule:construction`, `ok`); faithful rewrites: the model operator who wrote them (`agent-review-construction-2026-11-20`, `agent`) |
| staging rehearsal rows (2026-11-20) in `piece-counts.tsv`, `compound.tsv`, `unfold.tsv`, `lines.tsv`, `veto.tsv`, `prose.tsv` | the wrong and doubtful cases of the C1–C9 operator logs, with the right outcome or "left alone" | the model operator of the rehearsal (`agent-review-staging-2026-11-20`, status `agent`) |
| `startup-fills.tsv` (2026-11-20) | the split parts a v0.3.118 start filled on staging (pass-2 log, S1) and two stand-alone lines the same method rule still misreads (`known`) | the model operator of the rehearsal (`agent-review-staging-2026-11-20`, status `agent`/`known`) |
| `recipes.json` | 9 recipes cited in the golden-gate design and 26 sampled curated recipes; expected values are staging's stored labels, which the in-memory service reproduces to 4 dp | catalog facts and arithmetic |
| `catalog-admission.tsv` (2026-11-20) | 85 USDA foods (SR Legacy 2018-04, Foundation 2025-12-18) with their own category, nutrients and calorie factors: every food the catalog-admission design names, the first two SR foods of each category, foods that fail the energy check, and Foundation foods judged by their specific energy; 9 constructed rows (duplicate, no category, Quality Control category, branded oil, dressing, soda, sauce, cheese). Expected values computed from the rule as the design writes it, apart from the code. Hand-edited the same day for prepared foods (owner answer, second round): the 10 rows of the four prepared-food categories now expect `Curated` with no reason, every row gained `expectPrepared`, and 3 constructed prepared rows were added (an energy failure, a Foundation entree, a duplicate); 97 rows | rule (`rule:usda-*`, `rule:construction`) |
| `link-gate.tsv` (2026-11-20) | 66 pairs: real USDA foods from `gate-catalog.tsv` and constructed foods (branded, retired, prepared, rejected, short of nutrients), one or more per rule of the gate and 18 the gate must let through; expected values from the rules as the links and wordings designs write them | rule (`rule:link-gate`, `rule:construction`) |
| `wording-pairs.tsv` (2026-11-20) | the 232 kept rows of batch w01 (`staging/w01/verified-wordings-w01.tsv`), the 11 judge picks its second reader refuted (`dropped.tsv`), the wordings design's cases, and claret | w01 rows: the model reviewer and second reader (`agent-review-w01-*`, `agent`); refuted rows: the second reader; `gravy beef` and `sack`: the link-gate builder (`agent-review-link-gate-2026-11-20`); claret, potatoes, tarragon and the compound: rule (`ok`) |
| `gate-catalog.tsv` (2026-11-20) | the curated foods and pending SR Legacy foods of staging (`staging/w01/foods.tsv`) with their USDA description, admission reason (none yet) and nutrient rows, and the USDA category from the SR Legacy 2018-04 and Foundation 2025-12-18 `food.csv` | catalog facts |
| `line-labels.tsv` | 120 lines labelled not an ingredient that are exactly a curated food's name (ingredient by the veto rule) and 60 lines the deterministic pre-pass labelled | rule |

The judged pairs and wording moves were judged by agents, not by a person; their rows carry that in
`judgedBy`/`source` and have status `agent` (6,702 in `link-pairs.tsv`, 791 rows in `links.tsv`), so
a person can confirm or demote any of them. Their `known` rows (35 and 1) were set by the update
switch from the code's result, not by a person. The `link-pick` and `link-verify` floors in
`model-lanes.json` therefore measure agreement with that reviewer until a person confirms the rows,
and 60 of the `line-classify` cases are the deterministic pre-pass's own earlier answers. Of the right-judged pairs, 313 that the rule
answers with a different food are kept with `rule` `*` and status `-`: a pair judged right does not
make every other food wrong.

Sets that no existing judgement fills yet: the repair lane's quoted amounts, the prose rewrite
lane's own output (its prompt is private to `ProseRewriteHostedService`), dish grouping and tool
tagging. `model-lanes.json` lists them with `scorer: false`.

To add rows: run the matching query (`./ops/golden/capture.sh <query>.sql`, read-only against staging,
seed `GOLDEN_SEED`, default `golden-2026-11`), put judged answers in, mark the rows `new`, then use the
update switch. `ops/golden/build-fixtures.py --evidence <dir> --captures <dir> --out <dir>` rebuilds
every file from the captures and the evidence, all rows `new`.

## Model lanes

```
dotnet run --project nom-api/Nom.Import -- --golden-score --lane <link-pick|link-verify|line-classify|link-gate> \
    --host http://<model host>:11434 --model <model tag> [--limit N] [--out failures.tsv] [--record]
```

- `link-gate`: see below; it needs no model.
- `link-pick`: the ambiguous judged wordings (a staple or the name matcher would answer the rest),
  asked six at a time with the name matcher's shortlist of ten from `usda-catalog.tsv`. A case passes
  when the pick is a food judged right, or, with no right food on the shortlist, is not one judged
  wrong, near or compound. A false accept is a pick judged wrong.
- `link-verify`: every judged pair, twelve at a time; right pairs must be `same`, others must not.
- `line-classify`: `line-labels.tsv`, twenty lines at a time; a false accept is a non-ingredient answer
  for a line that is an ingredient.
- `link-gate`: no model and no host (`--host` and `--model` may be left out). `LinkGate.Check` on every
  pair of `link-pairs.tsv` judged right, wrong, near or compound, over `gate-catalog.tsv`. A case
  agrees when a right pair passes or a bad one is held; a false accept is a bad pair let through; the
  record also holds how many pairs the gate let through and the share of them judged right
  (`minPrecision` in `model-lanes.json`). Its signature is `LinkGate.Signature`, so a changed rule
  table needs a new record, and `required` is true: the record is always committed, and
  `GoldenLinkGateTests` checks that the gate still scores exactly what it records.

The run prints the score, the false-accept rate, the floor and every case that passed in the recorded
run and fails now, and exits 1 below the floor. `--record` writes `scores/<lane>.json`: model, host
name, the lane's prompt signature (a hash of the request it sends for one fixed question, model name
left out), the case-set hash, the score and the failed case ids. Commit it with the change it scores.

`GoldenModelLaneTests` then holds the release when a recorded lane's signature differs from the code
(a prompt, schema or option changed), its case set differs from the golden files, or its score is
below the floor; and when a lane marked `required` has no record. A lane that has a scorer, no
record and `required: false` is not checked: the test run reports
`Every_model_lane_with_a_scorer_has_been_scored` as skipped and names the lanes (and the floor test
itself as skipped while no lane is checked at all), so a green run never claims a gate that is not
armed. Score the lane with `--record` and set `required: true` in the same commit. The production
compose file must name the model the record names.

## Release gate

The `gate` job of `build-package.yml` runs on every `v*` tag and manual dispatch, before either image
is built or retagged: it checks that a tagged commit is on `main`, then runs the whole backend suite in
Release, golden sets included. E2E stays outside the gate. The gate catches code regressions against
frozen catalogs; drift of the live catalog needs a replay against staging, which this does not do.
