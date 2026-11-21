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
| `prose-judge.tsv` | `ProseCheck.Check` on a faithful rewrite and planted faults of each recipe; the masking and rule rendering of the clean-room lane; compiled into Nom.Orch as the lane's judge cases and canaries (see `prose-lane.md`) | `check2`: `passed` or the first reason check v2 gives; `expect` (`faithful`/`fault`) and `fault` (the planted fault) are what the judge must find | `status` |
| `piece-counts.tsv` | `RecipeRepairGrounding.PieceRefusal` (repair lane fills and `quantities/revert`) | `expect`: `keep` or `revert` | `status` |
| `startup-fills.tsv` | `ScrapedRecipeRepairService.RepairBatchAsync` (the start-up repair's amount for a line that states none, in a recipe with its method, its other lines and, when `journal` names one, an open line journal) | `expect` (`<quantity> <unit>`, or `-` for no amount) | `status` |
| `link-gate.tsv` + `gate-catalog.tsv` | `LinkGate.Check` (the one link gate, see `link-gate.md`) | `expect`: `pass`, or the code of a rule that must hold the pair; the target's facts in the row | `status` |
| `wording-pairs.tsv` + `gate-catalog.tsv` | `LinkGate.Check` for two readers' answer; `LinkGate.Family` | `expect`: `=` must pass, `!` must be held, `~` must win its family | `status` |
| `wording-tiers.tsv` + `gate-catalog.tsv` + `usda-catalog.tsv` | `WordingTiers.Decide` (the deterministic wording tiers and the gate, see `wording-tiers.md`); `lines` (`raw\|unit class`, ` ¦ `-joined), `title`, `site`, `table` (`verified:<wording>=<food id or verdict>`, `alias:<name>=<owner>`), `curated` (head lines, `head\|class\|food\|count`) | `key` (the harder key, `-` for none), `tier` (the first tier that answers, `-`), `answer` (its food), `expect` (the food written once the gate has read the pair, `-` when held or none) | `status` |
| `alcohol-context.tsv` + `gate-catalog.tsv` | `AlcoholLineContext.Read` and `.Refuse` (the alcohol sense rules, see `alcohol-context.md`) | `expect` (the sense code), `allows` (an FDC id the sense must allow, or `-`), `refuses` (an FDC id it must refuse, or `-`); `title`, `context` (the recipe's other lines, `;`-joined) and `site` give the recipe | `status` |
| `alcohol-retention.tsv` | `AlcoholRetention.Read` (the alcohol retention reader, see `alcohol-retention.md`): `steps` (the recipe's steps, ` \| `-joined, `-` for none) and `line` (the drink line) | `expect` (the basis: `no-steps`, `no-heat`, `overnight`, `hot-liquid`, `flamed`, `timed:<method>:<minutes>`, `up-to:<method>[:<minutes>]`), `factor` (the share that stays, from the USDA Release 6 row) | `status` |
| `catalog-admission.tsv` | `CatalogAdmissionRule.Decide` (USDA catalog admission: status, group from category, edibility, energy check, reasons, prepared food) | `expectStatus` (`Curated`, `PendingCuration`; the rule never rejects, and a food of the four prepared-food categories is judged like any other), `expectGroup`, `expectEdible`, `expectEnergy`, `expectReason`, `expectPrepared` (`true` for a Foundation or SR Legacy food of Baby Foods, Fast Foods, Meals, Entrees and Side Dishes, or Restaurant Foods) | `status` |
| `recipes.json` + `measurements.tsv` | `RecipeNutritionService.RecalculateAsync`, `.StatusFor` | `expected.calories/protein/fat` (4 dp), `countedLines`, `handLabel` | `status` |
| `recipes-rebuild.tsv` | `RecipeReprocessService.RebuildAsync` with apply (a recipe rebuilt in place from its stored record, see `recipe-reprocess.md`): `lines` (the source's lines, ` ¦ `-joined), `stored` (the rows before, `<raw>@<position>=<food>:<quantity unit>` or `:-`, `#kind`), `catalog` (curated foods, `name|grams per item`), `steps`, `version` (2 or more: a newer extraction) | `expect` (the rows after, in order, `<food>:<quantity unit>`, `:-`, `#kind`) | `status` |
| `containers.tsv` + `container-facts.tsv` | `ContainerSizeImportService.Propose` and `ContainerSizeResolver.Resolve` (the container size lane: a line, its era, its food's frozen USDA portions and branded class sizes, a density; `hide` = `yes` asks what the lane would assume with the stated size hidden) | `expectGrams` (whole grams, ±1 g; a hidden row passes within 10 % as the hold-out does; `-` stays uncounted) | `status` |
| `package-weights.tsv` | `FdcPackageWeight.Parse` (FDC branded `package_weight`) | `expectGrams`, `expectMl` (`-` none) | `status` |
| `line-labels.tsv` | model lane `line-classify` | `kind` | none (scored by the operator run) |
| `lines.tsv` + `startup-fills.tsv` + `line-kinds.tsv` | model lane `line-judge` (the reprocess lane's line judge) | the right row of each judged row and a row with its class's defect | none (scored by the operator run) |
| `link-pairs.tsv` + `gate-catalog.tsv` | scorer lane `link-gate` (no model) | `verdict` | none (scored by the record) |
| `alcohol-context.tsv` + `gate-catalog.tsv` | scorer lane `alcohol-context` (no model) | `expect`, `allows`, `refuses` | `status` (and the record) |
| `alcohol-retention.tsv` | scorer lane `alcohol-retention` (no model) | `expect`, `factor` | `status` (and the record) |
| `link-pairs.tsv` + `wording-pairs.tsv` + `gate-catalog.tsv` + `usda-catalog.tsv` | scorer lane `wording-tiers` (no model) | `verdict` | none (scored by the record) |
| `prose-judge.tsv` + `prose-stock.tsv` | scorer lanes `prose-rewrite` (writer end to end, planted faults through the gate), `prose-judge` (the judge), `prose-audit` (the auditor confirmed by its own blind re-read) | `expect`, `fault` | none (scored by the record) |
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
| `prose-judge.tsv` (2026-11-20) | 8 recipes: four constructed in our own words and four public-domain recipes already in `prose.tsv`; for each, one faithful rewrite and three or four planted faults (heat level, doneness cue, a dropped preparation, an added action or ingredient, swapped steps, a unit, a time or a range) | planted faults: one deterministic edit of the faithful rewrite (`rule:construction`, `ok`); faithful rewrites: the prose-lane-v2 builder (`agent-review-construction-2026-11-20`, `agent`) |
| staging rehearsal rows (2026-11-20) in `piece-counts.tsv`, `compound.tsv`, `unfold.tsv`, `lines.tsv`, `veto.tsv`, `prose.tsv` | the wrong and doubtful cases of the C1–C9 operator logs, with the right outcome or "left alone" | the model operator of the rehearsal (`agent-review-staging-2026-11-20`, status `agent`) |
| `startup-fills.tsv` (2026-11-20) | the split parts a v0.3.118 start filled on staging (pass-2 log, S1) and two stand-alone lines the same method rule misread (`known` until the R7 method-fill checks of `cleanup2/parser-rules`, now `agent`) | the model operator of the rehearsal (`agent-review-staging-2026-11-20`, status `agent`) |
| parser-rules rows (2026-11-20) in `lines.tsv` (`r1`-`r6`, `s13`, `can`), `line-kinds.tsv` (`r8`), `startup-fills.tsv` (`r7`), `compound.tsv` (`r9`) | the lines the reprocess design names for rules R1-R9 (its B6-B9 lists; the method-fill rows put the phrase the design quotes from each recipe in a short step), the containers design's "1 lb. 13 ounce can", and further lines of the same classes: the scraper sample of the sources design (item 13), numbered cans, "a couple of", "tbp", single-food headings | design rows: the design authors (`design:reprocess`, `design:containers`, `ok`); the rest: the parser-rules builder (`agent-review-parser-rules-2026-11-20`, `agent`) |
| `recipes.json` | 9 recipes cited in the golden-gate design and 26 sampled curated recipes; expected values are staging's stored labels, which the in-memory service reproduces to 4 dp | catalog facts and arithmetic |
| `catalog-admission.tsv` (2026-11-20) | 85 USDA foods (SR Legacy 2018-04, Foundation 2025-12-18) with their own category, nutrients and calorie factors: every food the catalog-admission design names, the first two SR foods of each category, foods that fail the energy check, and Foundation foods judged by their specific energy; 9 constructed rows (duplicate, no category, Quality Control category, branded oil, dressing, soda, sauce, cheese). Expected values computed from the rule as the design writes it, apart from the code. Hand-edited the same day for prepared foods (owner answer, second round): the 10 rows of the four prepared-food categories now expect `Curated` with no reason, every row gained `expectPrepared`, and 3 constructed prepared rows were added (an energy failure, a Foundation entree, a duplicate); 97 rows | rule (`rule:usda-*`, `rule:construction`) |
| `link-gate.tsv` (2026-11-20; 24 alcohol rows added the same day by `cleanup2/alcohol-context`, one or more per alcohol rule) | 90 pairs: real USDA foods from `gate-catalog.tsv` and constructed foods (branded, retired, prepared, rejected, short of nutrients), one or more per rule of the gate and 18 the gate must let through; expected values from the rules as the links and wordings designs write them | rule (`rule:link-gate`, `rule:construction`) |
| `wording-pairs.tsv` (2026-11-20) | the 232 kept rows of batch w01 (`staging/w01/verified-wordings-w01.tsv`), the 11 judge picks its second reader refuted (`dropped.tsv`), the wordings design's cases, and claret | w01 rows: the model reviewer and second reader (`agent-review-w01-*`, `agent`); refuted rows: the second reader; `gravy beef` and `sack`: the link-gate builder (`agent-review-link-gate-2026-11-20`); claret, potatoes, tarragon and the compound: rule (`ok`) |
| `wording-tiers.tsv` (2026-11-20) | 34 wordings over the frozen catalog: the harder key (a heading, measure filler, notes, compounds), every tier answering and written, held by the gate, stopped by a verified unclear row, two tiers disagreeing, an alias two foods share, the alcohol senses (rum, red and white wine written; sherry and bare wine held), and constructed head-line tables for the head defaults (spoon and volume soda, under 70 %, under 20 lines, a small class that disagrees, a class with no lines); expected values from the tier rules and the gate as the links, wordings and alcohol designs write them | rule (`rule:wording-tiers`, `rule:alcohol-context`, `rule:construction`) |
| `compound.tsv` row `wt-b11-01` (2026-11-20) | "the remains of cold ham or tongue": a first alternative the harder name key resolves (links B11) | rule (`rule:wording-tiers`) |
| `alcohol-context.tsv` (2026-11-20) | 161 lines: the classes of the alcohol design (its sections 1 and 2.1: the wrong-link classes measured on staging and every place where the bare word misleads) and the false matches of the old diet patterns; expected senses and foods from the design's rules. Ten rows the builder judged where the design is silent (country wines, malt liquor, brandy snaps, beer batter, white zinfandel, "poached in", "rum and raisin ice cream", "brandy and water", dry cider, rice wine) | design rules (`rule:alcohol-context`, `ok`); the ten: the alcohol-context builder (`agent-review-alcohol-context-2026-11-20`, `agent`) |
| staging checkpoint-1 rows (2026-11-21, `cleanup2/ck1-gate-alcohol`) in `link-gate.tsv` (lg-92 to lg-108) and `alcohol-context.tsv` (ac-162 to ac-175) | the five pairs the link gate let through on staging's waiting picks and should not (K4: pickle cucumbers, ground pork sausage on chorizo, cooked French beans on mature seeds, dried pinto beans on sprouted, cold roast pork on a raw cut), a constructed row per new rule and four pairs the new rules must still let through; the alcohol lines the sense rules misread (K10: amounts joined by "and" or "or" read as lists, two wines on Non-Alcoholic Wine, a measure of curaçoa) and further lines of the same classes | K4 pairs: the checkpoint operator (`agent-review-ck1-2026-11-21`, `agent`); constructed and rule rows: rule (`rule:construction`, `rule:link-gate`, `rule:alcohol-context`, `ok`); Catawba, vin blanc, coq au vin: the fix builder (`agent-review-ck1-gate-alcohol-2026-11-21`, `agent`) |
| `alcohol-retention.tsv` (2026-11-21, `cleanup2/alcohol-retention`) | 70 step texts with a drink line: every row of the USDA Release 6 alcohol table reached by a heating word and a time, the "up to" readings (heat with no time, under 15 minutes, a drink the steps never name), flaming, a hot liquid off the heat, stored overnight, no heat, no steps, and the time forms (mixed numbers, ranges, "half an hour", "1 hour 15 minutes", number words); ten realistic recipe methods judged by the builder (coq au vin, veal Marsala, tiramisu, Christmas pudding, beer bread, fondue twice, sangria, bourbon balls, risotto) | the reader's rules and the USDA table (`rule:alcohol-retention`, `ok`); the ten: the alcohol-retention builder (`agent-review-alcohol-retention-2026-11-21`, `agent`) |
| review rows of the checkpoint-1 fix (2026-11-21, `cleanup2/ck1-gate-alcohol`) in `link-gate.tsv` (lg-109 to lg-111) and `alcohol-context.tsv` (ac-176, ac-177) | a raw cut named with a last "roast" (pork loin roast, chuck roast) on a cooked food, held on its state; red wine named before the "au vin" dish it is for, read as red wine and refused on Chicken Stock | rule (`rule:construction`, `rule:link-gate`, `rule:alcohol-context`, `ok`) |
| `gate-catalog.tsv` (2026-11-20) | the curated foods and pending SR Legacy foods of staging (`staging/w01/foods.tsv`) with their USDA description, admission reason (none yet) and nutrient rows, and the USDA category from the SR Legacy 2018-04 and Foundation 2025-12-18 `food.csv` | catalog facts |
| `line-labels.tsv` | 120 lines labelled not an ingredient that are exactly a curated food's name (ingredient by the veto rule) and 60 lines the deterministic pre-pass labelled | rule |
| `recipes-rebuild.tsv` (2026-11-20, `cleanup2/reprocess-lane`) | 123 constructed recipes, two to four lines each, for the error classes of the reprocess design (section 1c): method fills (R7) and water fills, old measures and water on Piece (R5), `tablespn.`/`teaspn.` (R1), `tbp`, pinch and dash with no "of" (R2), "a couple of"/"a pair of" (R3), the amount after the name (R4), colon headings (R8), whole-food lists sharing a product (the rebuild keeps the current food; R9 belongs to the compound rules, D6), Artusi's Italian units (R6) and a newer English extraction replacing the mixed translation, junk foods named with an amount, lost order, and " + " folds; the design's own examples where it gives them. Three were `known` until staging checkpoint 1 (baseline 3, now 0; `cleanup2/ck1-parser-catalog` fixed them): a two-word food between the measure and its last word in the method ("three tablespoonfuls of olive oil"), "teacupful" missing from the method finder's measures, and "medium size" kept in a normalized name | rows that follow from a named rule: rule (`rule:R1` ... `rule:R8`, `rule:order`, `rule:fold`, `rule:residue`, `rule:current-link`, `ok`); the rest: the reprocess-lane builder (`agent-review-reprocess-lane-2026-11-20`, `agent`) |
| staging checkpoint 1 rows (2026-11-20, `cleanup2/ck1-parser-catalog`) in `lines.tsv` (`ck1-*`, 16) and `startup-fills.tsv` (`ck1-*`, 10) | the wrong and doubtful start-up fills and line reads of the checkpoint-1 log (K1, K2: hyphenated mixed numbers after the name, a measure "and a half", per-item ratios, a contradicted temperature, a handful as a piece, an alternative, a folded line), the right fills the log quotes as guards, and the reprocess-lane rows m-10 and w-10 as start-up fills; `r4-05` hand-edited the same day (the name drops "medium size") | mixed numbers and "and a half": rule (`rule:mixed-number`, `rule:and-a-half`, `ok`); m-10: `rule:R7`, `ok`; the rest: the model operator of the checkpoint and this builder (`agent-review-checkpoint1-2026-11-20`, `agent`) |
| `containers.tsv` (2026-11-20, `cleanup2/container-sources`) | 101 lines: one or more per class of `container-classes.tsv` (the mode, and its small and large sides), the SR Legacy container portions of 33 foods (drained and net cans, gelatin envelope and package, yeast packet, cream cheese small package), the cited can table across the three eras, standards of fill, stated sizes, and the lines containers.md says stay uncounted (mushrooms, tuna, tomato sauce, Greek yogurt container, marmalade). Expected values follow from the line's text, the cited table row, the SR portion, or the mode `ops/golden/container_facts.py` computed apart from the C# code. Two are `known` (baseline 2): "1 can tomato paste" of unknown era (6 oz then and now per containers.md, but no cited row names a historic tomato paste can yet) and the hold-out of "1 (14.5 oz) can diced tomatoes" against SR's 190 g "can" on canned tomatoes, which the hold-out keeps in shadow. One is `agent`: "1 can cloudberry marmalade" left uncounted on SR "Jams and preserves", where both the food and the verdict are the reviewer's. `container-facts.tsv` is frozen from FDC Branded 2025-12-18 and SR Legacy 2018-04 by `container_facts.py`; recapture it whenever the class map changes | rule (`rule:*`, `fdc:*`, `deterministic:*`, `design:containers`); agent (`agent-review-*`) |
| `package-weights.tsv` (2026-11-20) | 52 package_weight strings, the first of each distinct shape in the 2025-12-18 branded file, plus 12 constructed (multi-packs, counts, empty); expected values from `container_facts.py` | rule (`rule:fdc-branded-2025-12-18`) |

The judged pairs and wording moves were judged by agents, not by a person; their rows carry that in
`judgedBy`/`source` and have status `agent` (6,702 in `link-pairs.tsv`, 791 rows in `links.tsv`), so
a person can confirm or demote any of them. Their `known` rows (35 and 1) were set by the update
switch from the code's result, not by a person. The `link-pick` and `link-verify` floors in
`model-lanes.json` therefore measure agreement with that reviewer until a person confirms the rows,
and 60 of the `line-classify` cases are the deterministic pre-pass's own earlier answers. Of the right-judged pairs, 318 that the rule
answers with a different food are kept with `rule` `*` and status `-`: a pair judged right does not
make every other food wrong. Five of them (2026-11-20, parser-rules) were `~` rows the rule left
unanswered until the name dropped "tablespn.", "tablespns." and "tbp"; it now gives "Lemon juice,
raw" for lemon juice and "Onions, raw" for chopped onion.

Sets that no existing judgement fills yet: the repair lane's quoted amounts, dish grouping and tool
tagging. `model-lanes.json` lists them with `scorer: false`.

The clean-room prose lane (`prose-lane.md`) has three scorer lanes over `prose-judge.tsv`, scored by
an operator with `--golden-score --lane <lane> --host <model host> --model <model> [--check-model
<judge>] --record`: `prose-rewrite` (the writer through the whole pipeline with the judge; a planted
fault the gate lets through is a false accept, ceiling 0), `prose-judge` (floor 95 %, at most 2 %
false accepts) and `prose-audit` (the auditor's flags confirmed by its own blind re-read, never by
the judge that passed the rewrites; its known-bad and known-good counts are the audit check score the
lane frame corrects with; `--check-model` is for `prose-rewrite` only). None is `required` until
the staging proof records it, but the records are also compiled into Nom.Orch and the running lane
cannot qualify without a current record for its own models (`ProseLaneGolden.GoldenProblem`), so a
missing record holds the lane in shadow rather than the release. Of the 29 planted faults, 21 pass
check v2 by construction: they are the judge's work (heat levels, cues, dropped preparations,
added actions, swapped steps), and the lane's canaries draw from them first.

To add rows: run the matching query (`./ops/golden/capture.sh <query>.sql`, read-only against staging,
seed `GOLDEN_SEED`, default `golden-2026-11`), put judged answers in, mark the rows `new`, then use the
update switch. `ops/golden/build-fixtures.py --evidence <dir> --captures <dir> --out <dir>` rebuilds
every file from the captures and the evidence, all rows `new`.

## Model lanes

```
dotnet run --project nom-api/Nom.Import -- --golden-score --lane <link-pick|link-verify|line-classify|link-gate|alcohol-context|alcohol-retention|wording-tiers|line-judge> \
    --host http://<model host>:11434 --model <model tag> [--limit N] [--out failures.tsv] [--record]
```

- `line-judge`: the reprocess lane's line judge (`OllamaLineJudge`, reader B of the roster). Every
  judged row of `lines.tsv` (`parse`), `startup-fills.tsv` and `line-kinds.tsv` gives a right row and
  a row with its class's defect (a stated measure counted in pieces, a stated amount lost, the fill
  the row's note names, a heading as an ingredient, an ingredient as a note); each becomes a "better"
  case (the wrong row rebuilt into the right one) and a "worse" case (the reverse), eight to a call. A
  case passes when the judge answers as expected; a false accept is "better" on a worse case. Floors
  (reprocess.md section 3): at least 95 %, at most 2 % false accepts. The record also counts the
  combined audit check, the judge's "worse" confirmed by the rule audit `ReprocessRuleAudit` on the
  case's new row (`auditKnownBad`, `auditFlaggedBad`, `auditKnownGood`, `auditFlaggedGood`); the
  reprocess lane reads those as its audit check's score (the record is built into the API) and its
  rebuild tier cannot leave shadow without a record of the current prompt (`OllamaLineJudge.Signature`)
  and the model the lane asks. `required` is false until the first record is committed.

- `link-gate`, `alcohol-context`, `alcohol-retention` and `wording-tiers`: see below; they need no model.
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
- `alcohol-context`: no model and no host. `AlcoholLineContext.Read` and `.Refuse` on every row of
  `alcohol-context.tsv`: a case passes when the line reads its sense, allows its `allows` food and
  refuses its `refuses` food; a false accept is a `refuses` food the sense allows. Floors: score at
  least 98 %, no false accept; `GoldenAlcoholContextTests` also requires every rule-given row to pass.
  Its signature is `AlcoholLineContext.Signature`; `required` is true, so a changed rule needs a new
  record (`--golden-score --lane alcohol-context --record`).
- `alcohol-retention`: no model and no host. `AlcoholRetention.Read` on every row of
  `alcohol-retention.tsv`: a case passes when the reader gives the row's basis and factor; a false
  accept is a factor below the row's, which would understate the alcohol that stays. Floors: score
  100 %, no false accept. Its signature is `AlcoholRetention.Signature` (the patterns, the USDA rows
  and the alcohol trigger); `required` is true, and `GoldenAlcoholRetentionTests` checks the reader
  still scores exactly the record (`--golden-score --lane alcohol-retention --record`).
- `wording-tiers`: no model and no host. `WordingTiers.Decide` (the tiers and the gate, no lines) on the
  wording of every pair of `link-pairs.tsv` judged right, wrong, near or compound, with the kept w01 rows
  of `wording-pairs.tsv` as the verified wording table; a pair whose wording is itself a w01 row is left
  out, since the lane never asks the tiers about a wording the table holds. A case agrees when a right
  pair's food is the one the tiers write or a bad pair's food is not; a false accept is a bad pair's
  food written; precision is the share judged right among the pairs whose food is written. Floors: at
  least 99 % precision, at most 1 % false accepts, at least 40 % agreement. The fixtures hold no
  trusted aliases and no curated lines, so the alias and head-default tiers are scored by
  `wording-tiers.tsv` only. Its signature is `WordingTiers.Signature` (which holds the gate's and the
  alcohol rules' signatures); `required` is true, and `GoldenWordingTierTests` checks the tiers still
  score exactly the record and prints each tier's coverage and precision.

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
