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
| `prose.tsv` | `RewriteFidelity.Check` | `expect`: a reason the check must give | `status` |
| `recipes.json` + `measurements.tsv` | `RecipeNutritionService.RecalculateAsync`, `.StatusFor` | `expected.calories/protein/fat` (4 dp), `countedLines`, `handLabel` | `status` |
| `line-labels.tsv` | model lane `line-classify` | `kind` | none (scored by the operator run) |
| `model-lanes.json` | every model lane | floors | — |
| `baseline.json` | — | allowed `known` rows per `file:column` | — |

`sources` in `unfold.tsv` holds only the recipe's source lines that equal a run of the line's own
parts (` ¦ `-joined), `{}` for an empty list and `-` for a recipe with no stored source. In
`prose.tsv` the rewrite is built by the test from the public-domain source: `identical` (the source
itself, must fail `unchanged`), `drop-step-N` (without a step that alone states a number, must fail
`lost-number`) and `add-number` (the last step gains "Rest it 437 minutes.", must fail `added-number`).

`recipes.json` keeps a recipe that is not public domain as numbers only: no title and no raw line.

## Status

- `ok`: the code must give the expected answer.
- `known`: the expected cell holds the correct answer and the code still gets it wrong. When a fix
  makes it pass, the run fails with "NOW PASSES, promote it". The count per file and column may not
  exceed `baseline.json`, which only goes down. For `recipes.json`, `known` means the frozen data is
  wrong (an item weight, a yield read as servings); the arithmetic is still asserted.
- `new`, `review`: fail the run, so an unreviewed row cannot ship.
- `-`: the row is kept for reference and not asserted on that column.

Expected values change only by a hand edit that shows in the diff. A person reviews `review` rows and
sets them to `ok` or `known`; a model may pre-sort rows but never sets a status or types a value.

## The update switch

`NOM_GOLDEN_UPDATE=1 dotnet test nom-api/Nom.Api.Tests --filter Category=Golden` writes to the source
tree and does only this:

- fills the blank expected cells of a `new` row with the code's current output and marks it `review`;
- marks a `new` row whose expected cells are all judged `ok` or `known` by whether it passes;
- flips a `known` row that now passes to `ok` and lowers `baseline.json`.

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
| `recipes.json` | 9 recipes cited in the golden-gate design and 26 sampled curated recipes; expected values are staging's stored labels, which the in-memory service reproduces to 4 dp | catalog facts and arithmetic |
| `line-labels.tsv` | 120 lines labelled not an ingredient that are exactly a curated food's name (ingredient by the veto rule) and 60 lines the deterministic pre-pass labelled | rule |

The judged pairs and wording moves were judged by agents, not by a person; their rows carry that in
`judgedBy`/`source` so a person can demote any of them. Of the right-judged pairs, 313 that the rule
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
dotnet run --project nom-api/Nom.Import -- --golden-score --lane <link-pick|link-verify|line-classify> \
    --host http://<model host>:11434 --model <model tag> [--limit N] [--out failures.tsv] [--record]
```

- `link-pick`: the ambiguous judged wordings (a staple or the name matcher would answer the rest),
  asked six at a time with the name matcher's shortlist of ten from `usda-catalog.tsv`. A case passes
  when the pick is a food judged right, or, with no right food on the shortlist, is not one judged
  wrong, near or compound. A false accept is a pick judged wrong.
- `link-verify`: every judged pair, twelve at a time; right pairs must be `same`, others must not.
- `line-classify`: `line-labels.tsv`, twenty lines at a time; a false accept is a non-ingredient answer
  for a line that is an ingredient.

The run prints the score, the false-accept rate, the floor and every case that passed in the recorded
run and fails now, and exits 1 below the floor. `--record` writes `scores/<lane>.json`: model, host
name, the lane's prompt signature (a hash of the request it sends for one fixed question, model name
left out), the case-set hash, the score and the failed case ids. Commit it with the change it scores.

`GoldenModelLaneTests` then holds the release when a recorded lane's signature differs from the code
(a prompt, schema or option changed), its case set differs from the golden files, or its score is
below the floor; and when a lane marked `required` has no record. The production compose file must
name the model the record names.

## Release gate

The `gate` job of `build-package.yml` runs on every `v*` tag and manual dispatch, before either image
is built or retagged: it checks that a tagged commit is on `main`, then runs the whole backend suite in
Release, golden sets included. E2E stays outside the gate. The gate catches code regressions against
frozen catalogs; drift of the live catalog needs a replay against staging, which this does not do.
