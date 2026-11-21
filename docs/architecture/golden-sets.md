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
| `wording-lexicon.tsv` + `gate-catalog.tsv` | `WordingReaders.LexiconTerm` and `.Meaning` (the lexicon's form); `LinkGate.Check` for two readers' answer with the row's entry (see `wording-readers.md`) | `expect`: `=` the term means `means` (its entry must clear the gate's identity rules for `foodId` when one is given), `!` it does not | `status` |
| `alcohol-readings.tsv` | `AlcoholLineContext.Read` (each line is unclear in its `mode`) and `AlcoholFixedReading.Combine` (the fixed readings of the readers, see `wording-readers.md`); `title`, `context` (`;`-joined), `steps`, `site` give the recipe | `expect`: the reading (`table`, `fortified`, `sweet`, `hard`, `drunk`, `not-food`), `unsure` (nothing written), or `!reading` (a data rule refuses it) | `status` |
| `alcohol-context.tsv` + `gate-catalog.tsv` | `AlcoholLineContext.Read` and `.Refuse` (the alcohol sense rules, see `alcohol-context.md`) | `expect` (the sense code), `allows` (an FDC id the sense must allow, or `-`), `refuses` (an FDC id it must refuse, or `-`); `title`, `context` (the recipe's other lines, `;`-joined) and `site` give the recipe | `status` |
| `alcohol-retention.tsv` | `AlcoholRetention.Read` (the alcohol retention reader, see `alcohol-retention.md`): `steps` (the recipe's steps, ` \| `-joined, `-` for none) and `line` (the drink line) | `expect` (the basis: `no-steps`, `no-heat`, `overnight`, `hot-liquid`, `flamed`, `timed:<method>:<minutes>`, `up-to:<method>[:<minutes>]`), `factor` (the share that stays, from the USDA Release 6 row) | `status` |
| `diet-gate.tsv` | `HouseholdRestrictionResolver.ResolveAsync` and `.ResolveSevereForPersonAsync`, `RecipeOrchestrationService.GetDietMatchesAsync` and `.GetSevereDietConflictsAsync` (the diet gate's name patterns, see `diet-gate.md`): one food (`food`, `category`, `aliases` as `text\|source`, ` ¦ `-joined, `-` for no source) and one recipe line on it under one default `restriction`; a row with `alcohol` (the food's figure per 100 g) also gives the line's `grams`, the recipe's `servings` and its `steps` (` \| `-joined), and the alcohol retention run reads the steps before the gate | `expect`: `out` (kept out of plans, flagged on the page, and for severity 4 or 5 hidden from the person's search and a severe conflict) or `in` (offered and clear everywhere) | `status` |
| `sources.tsv` | `SourceAdmissionPolicy.Judge` (the single verdict of a recipe source check, see `recipe-sources.md`): the scraper's verdict, rule version, whether the check completed, the reasons a row names (`code:outcome`, `missing:code`; every other scraper reason passes), the licence kind, the sample size, NOM's vetting (`vetChecked`, `vetPassed`) and quantity counts (`lines`, `linesFine`), an earlier admin rejection | `expectVerdict` (`clear` or `not-clear`), `expectConclusive` (`no` for an unfinished check or unknown rules), `expectAgainst` (the reasons that keep it from clear, in the policy's order) | `status` |
| `source-rights.tsv` | `SourceRightsRule.Derive` (the rights class of a web source from its stored check report, see `source-rights.md`): the facts a report holds (`homepage` outcome, `robots` status, `nombot`, `aiBlocked` agents or `signal:` Content-Signals, `noAi` pages, `tdmRep`, `challenged` and `refused:` pages, `termsPages` as `url=outcome`, `sentences` as `url :: text` joined by ` \|\| `, the licence kind and whether its reason passed, `statements` as `kind=text`, `complete`, `ruleVersion`, and the `source-check/4` facts in `access`: `links=N`, `sitemap-refused:url`, `samples-refused:N@url`) | `expectClass` (`clear`, `facts-only`, `permission-needed`, `forbidden`, `-` when inconclusive), `expectAi` (`yes`/`no`), `expectQuote` (text the deciding quote holds) | `status` |
| `rights-sentences.tsv` | `RightsSentences.Read` (NOM's reading of a sentence a source check quoted) | `expectAutomated`, `expectReuse`, `expectAi` (`yes`/`no`) | `status` |
| `recipe-titles.tsv` | `RecipeTitleRule.Reduce` with `RecipeTitleRule.BrandNames` of the row's `credit` (the title rule for facts-only recipes) | `expect` (the title kept) | `status` |
| `prepared-contents.tsv` | `IngredientStatementReader.Read` (a prepared meal's ingredient statement and allergen lines, see `prepared-contents.md`): one `statement` as FDC publishes it or a household confirms it | `expectRead` (`yes` or `no`), `expectReason` (why it is unread, up to the first colon), `expectIngredients` (`parent>name`, `+` for a compound with listed parts, `\|`-joined), `expectDeclared` and `expectAdvisory` (`word/class`, `\|`-joined) | `status` |
| `branded-lift.tsv` | `BrandedSourceAgreement.Judge` and the branded branch of `CatalogAdmissionRule.Decide` (the branded lift: agreeing and disagreeing sources, status, reason) | `expectAgreeing` (the FDC record counts as one), `expectDisagreeing` (Open Food Facts included), `expectStatus` (`Curated` at two or more agreeing once each disagreement takes one away), `expectReason` (`awaiting_second_source`, `sources_disagree`, or a hold the agreement never overrides) | `status` |
| `catalog-admission.tsv` | `CatalogAdmissionRule.Decide` (USDA catalog admission: status, group from category, edibility, energy check, reasons, prepared food) | `expectStatus` (`Curated`, `PendingCuration`; the rule never rejects, and a food of the four prepared-food categories is judged like any other), `expectGroup`, `expectEdible`, `expectEnergy`, `expectReason`, `expectPrepared` (`true` for a Foundation or SR Legacy food of Baby Foods, Fast Foods, Meals, Entrees and Side Dishes, or Restaurant Foods) | `status` |
| `recipes.json` + `measurements.tsv` | `RecipeNutritionService.RecalculateAsync`, `.StatusFor` (with `ContainerWeighing`: a recipe may give `containerLane`, `containerOldBookLane`, `sourceSite`, `sourceAttribution` and `publishedYear`, and a food `description`, `categoryId` and `containerSizes`) | `expected.calories/protein/fat` (4 dp), `countedLines` (counted and counted-assumed), `assumedLines` (0 when absent), `handLabel` | `status` |
| `recipes-rebuild.tsv` | `RecipeReprocessService.RebuildAsync` with apply (a recipe rebuilt in place from its stored record, see `recipe-reprocess.md`): `lines` (the source's lines, ` ¦ `-joined), `stored` (the rows before, `<raw>@<position>=<food>:<quantity unit>` or `:-`, `#kind`), `catalog` (curated foods, `name|grams per item`), `steps`, `version` (2 or more: a newer extraction) | `expect` (the rows after, in order, `<food>:<quantity unit>`, `:-`, `#kind`) | `status` |
| `containers.tsv` + `container-facts.tsv` | `ContainerSizeImportService.Propose` and `ContainerSizeResolver.Resolve` (the container size lane: a line, its era, its food's frozen USDA portions and branded class sizes, a density; `hide` = `yes` asks what the lane would assume with the stated size hidden) | `expectGrams` (whole grams, ±1 g; a hidden row passes within 10 % as the hold-out does; `-` stays uncounted) | `status` |
| `container-eras.tsv` + `container-era-lines.tsv` + `container-era-facts.tsv` | `ContainerCorpusSizes` and `ContainerSizeResolver.Resolve` with a book (the corpus tiers: sizes from the sized lines of the same book, then the same era, before a cited period source and today's size); the facts file is what `ops/container-era-facts.py` mines from the lines file, and the C# tiers must give it row for row | `expectGrams` (whole grams, ±1 g; `-` stays uncounted), `expectBasis` (the tier) | `status` |
| `package-weights.tsv` | `FdcPackageWeight.Parse` (FDC branded `package_weight`) | `expectGrams`, `expectMl` (`-` none) | `status` |
| `line-labels.tsv` | model lane `line-classify` | `kind` | none (scored by the operator run) |
| `lines.tsv` + `startup-fills.tsv` + `line-kinds.tsv` | model lane `line-judge` (the reprocess lane's line judge) | the right row of each judged row and a row with its class's defect | none (scored by the operator run) |
| `link-pairs.tsv` + `gate-catalog.tsv` | scorer lane `link-gate` (no model) | `verdict` | none (scored by the record) |
| `alcohol-context.tsv` + `gate-catalog.tsv` | scorer lane `alcohol-context` (no model) | `expect`, `allows`, `refuses` | `status` (and the record) |
| `alcohol-retention.tsv` | scorer lane `alcohol-retention` (no model) | `expect`, `factor` | `status` (and the record) |
| `link-pairs.tsv` + `wording-pairs.tsv` + `gate-catalog.tsv` + `usda-catalog.tsv` | scorer lane `wording-tiers` (no model) | `verdict` | none (scored by the record) |
| `wording-pairs.tsv` + `link-pairs.tsv` + `gate-catalog.tsv` + `usda-catalog.tsv` | model lane `wording-read` (readers A and B, the refuter, the outcome table and the gate) | `expect`, `verdict` | none (scored by the record) |
| `wording-lexicon.tsv` | model lane `wording-lexicon` (both readers and the refuter) | `means`, `expect` | `status` |
| `alcohol-readings.tsv` | model lane `alcohol-reading` (both readers' fixed readings) | `expect` | `status` (rule rows checked without a model) |
| `link-pairs.tsv` | model lane `link-audit` (the auditor confirmed by reader B, and the auditor alone; 400 bad and 400 good pairs) | `verdict` | none (scored by the record) |
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
| `recipes.json` | 9 recipes cited in the golden-gate design and 26 sampled curated recipes; expected values are staging's stored labels, which the in-memory service reproduces to 4 dp. Three constructed recipes (990001-990003, 2026-11-21, `cleanup2/container-weighing`) weigh unsized cans: two cans of black beans at the active modern mode with the lane live, the same recipe with the lane in shadow (the cans stay uncounted), and a 1931 book's can of drained corn at the 1933 period can nearer the modern size (No. 303 scaled by the modern drained share), with the old-book tier live; their expected totals are the arithmetic of the stated sizes and the cited table row | catalog facts and arithmetic; rule (`rule:containers-weighing`, `deterministic:*`) |
| `catalog-admission.tsv` (2026-11-20) | 85 USDA foods (SR Legacy 2018-04, Foundation 2025-12-18) with their own category, nutrients and calorie factors: every food the catalog-admission design names, the first two SR foods of each category, foods that fail the energy check, and Foundation foods judged by their specific energy; 9 constructed rows (duplicate, no category, Quality Control category, branded oil, dressing, soda, sauce, cheese). Expected values computed from the rule as the design writes it, apart from the code. Hand-edited the same day for prepared foods (owner answer, second round): the 10 rows of the four prepared-food categories now expect `Curated` with no reason, every row gained `expectPrepared`, and 3 constructed prepared rows were added (an energy failure, a Foundation entree, a duplicate); 97 rows | rule (`rule:usda-*`, `rule:construction`) |
| `branded-lift.tsv` (2026-11-21; 6 rows added in review) | 33 constructed branded foods and their sources: a household label with and without a gram weight, a manufacturer page, Open Food Facts agreeing and disagreeing, a later FDC release, an unknown source, one household or Open Food Facts read twice, two to five agreeing sources against one disagreement, a "<1 g" value, a can of soda at 355 g, holds the agreement never overrides (zero energy, energy check), and the tolerance's edges (`branded-agreement/2`): values 18 and 24 % off on a 40 g serving and a 5 g serving's kcal off by half, which are inconclusive and lift nothing, the same serving within 10 %, beyond both bounds, and a near-zero fat inside the capped rounding. Expected values worked by hand from the rule as the owner stated it ("two or more, more is better"), with the arithmetic in each note | rule (`rule:construction`) |
| `link-gate.tsv` (2026-11-20; 24 alcohol rows added the same day by `cleanup2/alcohol-context`, one or more per alcohol rule) | 90 pairs: real USDA foods from `gate-catalog.tsv` and constructed foods (branded, retired, prepared, rejected, short of nutrients), one or more per rule of the gate and 18 the gate must let through; expected values from the rules as the links and wordings designs write them | rule (`rule:link-gate`, `rule:construction`) |
| `wording-pairs.tsv` (2026-11-20) | the 232 kept rows of batch w01 (`staging/w01/verified-wordings-w01.tsv`), the 11 judge picks its second reader refuted (`dropped.tsv`), the wordings design's cases, and claret | w01 rows: the model reviewer and second reader (`agent-review-w01-*`, `agent`); refuted rows: the second reader; `gravy beef` and `sack`: the link-gate builder (`agent-review-link-gate-2026-11-20`); claret, potatoes, tarragon and the compound: rule (`ok`) |
| `wording-tiers.tsv` (2026-11-20) | 34 wordings over the frozen catalog: the harder key (a heading, measure filler, notes, compounds), every tier answering and written, held by the gate, stopped by a verified unclear row, two tiers disagreeing, an alias two foods share, the alcohol senses (rum, red and white wine written; sherry and bare wine held), and constructed head-line tables for the head defaults (spoon and volume soda, under 70 %, under 20 lines, a small class that disagrees, a class with no lines); expected values from the tier rules and the gate as the links, wordings and alcohol designs write them | rule (`rule:wording-tiers`, `rule:alcohol-context`, `rule:construction`) |
| `compound.tsv` row `wt-b11-01` (2026-11-20) | "the remains of cold ham or tongue": a first alternative the harder name key resolves (links B11) | rule (`rule:wording-tiers`) |
| `alcohol-context.tsv` (2026-11-20) | 161 lines: the classes of the alcohol design (its sections 1 and 2.1: the wrong-link classes measured on staging and every place where the bare word misleads) and the false matches of the old diet patterns; expected senses and foods from the design's rules. Ten rows the builder judged where the design is silent (country wines, malt liquor, brandy snaps, beer batter, white zinfandel, "poached in", "rum and raisin ice cream", "brandy and water", dry cider, rice wine) | design rules (`rule:alcohol-context`, `ok`); the ten: the alcohol-context builder (`agent-review-alcohol-context-2026-11-20`, `agent`) |
| staging checkpoint-1 rows (2026-11-21, `cleanup2/ck1-gate-alcohol`) in `link-gate.tsv` (lg-92 to lg-108) and `alcohol-context.tsv` (ac-162 to ac-175) | the five pairs the link gate let through on staging's waiting picks and should not (K4: pickle cucumbers, ground pork sausage on chorizo, cooked French beans on mature seeds, dried pinto beans on sprouted, cold roast pork on a raw cut), a constructed row per new rule and four pairs the new rules must still let through; the alcohol lines the sense rules misread (K10: amounts joined by "and" or "or" read as lists, two wines on Non-Alcoholic Wine, a measure of curaçoa) and further lines of the same classes | K4 pairs: the checkpoint operator (`agent-review-ck1-2026-11-21`, `agent`); constructed and rule rows: rule (`rule:construction`, `rule:link-gate`, `rule:alcohol-context`, `ok`); Catawba, vin blanc, coq au vin: the fix builder (`agent-review-ck1-gate-alcohol-2026-11-21`, `agent`) |
| `alcohol-retention.tsv` (2026-11-21, `cleanup2/alcohol-retention`) | 74 step texts with a drink line: every row of the USDA Release 6 alcohol table reached by a heating word and a time, the "up to" readings (heat with no time, under 15 minutes, a drink the steps never name), flaming, a hot liquid off the heat, stored overnight, no heat, no steps, heat after a chilled, set-aside or separate step left out, and the time forms (mixed numbers, ranges, "half an hour", "1 hour 15 minutes", number words); ten realistic recipe methods judged by the builder (coq au vin, veal Marsala, tiramisu, Christmas pudding, beer bread, fondue twice, sangria, bourbon balls, risotto) | the reader's rules and the USDA table (`rule:alcohol-retention`, `ok`); the ten: the alcohol-retention builder (`agent-review-alcohol-retention-2026-11-21`, `agent`) |
| review rows of the checkpoint-1 fix (2026-11-21, `cleanup2/ck1-gate-alcohol`) in `link-gate.tsv` (lg-109 to lg-111) and `alcohol-context.tsv` (ac-176, ac-177) | a raw cut named with a last "roast" (pork loin roast, chuck roast) on a cooked food, held on its state; red wine named before the "au vin" dish it is for, read as red wine and refused on Chicken Stock | rule (`rule:construction`, `rule:link-gate`, `rule:alcohol-context`, `ok`) |
| alcohol identity rows (2026-11-21, `cleanup2/ck2-diet-gate`) in `link-gate.tsv` (lg-112 to lg-122) and `wording-tiers.tsv` (wt-alc-01 changed, wt-alc-09 to wt-alc-11) | sherry, sack, Madeira, port, claret and a sherry line let through to the food their alcohol sense allows (`link-gate/7`); sherry on the sweet dessert wine and the rum, port on the dry one, rum on a wine and a wording whose lines mix a drink and a vinegar still held; the alcohol tier's sherry, port, claret and sack written. Six `known` rows of `wording-pairs.tsv` that now pass (sack twice, ale, sherry wine, port, chablis) became `agent` and `baseline.json` fell from 94 to 88 | rule (`rule:link-gate`, `rule:alcohol-context`, `ok`); the six promoted rows keep their w01 judge (`agent`) |
| `diet-gate.tsv` (2026-11-21, `cleanup2/ck2-diet-gate`) | 32 rows over Halal, Whole30, Kosher, Celiac Disease, Lactose Intolerance, Milk Allergy and Peanut Allergy: the K9 class of staging checkpoint 1 carried to the `%` criteria (a recipe line stored as an alias with no source, or a model pick's, on Butter, Sugar, oils, water, lemon juice, fish stock and an oat drink), each with the line the alias came from, which must keep its recipe out, and a line that does not name the restricted thing, which must be offered; foods counted by name, by USDA category (Pork Products), by their USDA description, an admin's alias and a retired duplicate's name | rule (`rule:diet-gate`, `ok`) |
| `sources.tsv` (2026-11-21, `cleanup2/source-admission`; 3 rows added by `cleanup2/source-rights-followup`: a clear and a not-clear report under `source-check/4`, an unread `source-check/3`) | 46 constructed source checks (49 with the follow-up): every scraper reason failing or unknown on its own, the clear licence kinds and the conditional ones (CC BY-SA, OGL, CC BY-NC), a missing reason and an odd outcome, an unfinished check and old rules, the edges of NOM's own checks (18 and 17 of 20 recipes vetted, 85 and 84 of 100 lines), an earlier rejection, and current and candidate sources as `auto/source-candidates.md` describes them (an AI reservation in robots.txt, terms forbidding automated collection, all rights reserved, a public-domain site without schema.org data, recipe PDFs). Expected values worked by hand from the owner's rule that only a site usable without ambiguity is offered | rule (`rule:construction`, `ok`) |
| `source-rights.tsv` (2026-11-21, `cleanup2/source-rights`; 14 rows added by `cleanup2/source-rights-followup`: skinnytaste, budgetbytes, Sally's, acozykitchen, twopeasandtheirpod, eatingwell, myplate, culiplan and frije under `source-check/4` with the facts of the scraper's replay of the saved results and the research's quoted sentences, and constructed off-domain terms, a refused sitemap, refused samples, a recipe-page footer and an unread `source-check/3`) | 45 rows (59 with the follow-up): the 22 hosts judged in source-rejudge.md (2026-11-20), each with the facts and quoted sentences the research recorded, as a check with defects D1 to D7 fixed reports them (eatingwell's and seriouseats' robots.txt notice, pinchofyum's, skinnytaste's, budgetbytes', Sally's and David Lebovitz's terms, minimalistbaker's and cookieandkate's challenges, the AI-crawler blocks and loveandlemons' `noai`, culiplan, frije and myplate); 23 constructed cases for every branch of the rule (licences, a forbidding sentence before a licence, unread terms, an unfinished check, old rules, robots, refused pages, TDM, Content-Signal, `noai` header, an AI-only sentence, a reserve-the-right sentence, a conduct rule, a denylisted domain). Expected values written by hand from the research's resolutions and the rule | rule (`rule:source-rejudge-2026-11-20`, `rule:construction`, `ok`) |
| `rights-sentences.tsv` (2026-11-21, `cleanup2/source-rights`) | 35 sentences: every sentence source-rejudge.md quotes, the decoys the check misread (reserving the right to modify, unsolicited messages, malware, a grant for personal use without "only", People Inc's non-commercial grant), and constructed ones (three added in review: a robot ban joined to a conduct rule by ", or to", a copy ban beside a spam rule, and a sentence whose every action is conduct) | rule (`rule:source-rejudge-2026-11-20`, `rule:construction`, `ok`) |
| `recipe-titles.tsv` (2026-11-21, `cleanup2/source-rights`) | 30 titles written for the rule (no site's title is copied): dish names kept, boasts (first-person boasts with their subject, added in review), tails, asides, praise, family and source possessives removed, possessives that are part of a dish and words like "one pot" and "quick" kept | rule (`rule:construction`, `ok`) |
| `prepared-contents.tsv` (2026-11-21, `cleanup2/prepared-contents`) | 42 constructed statements: a plain list, parentheses, nested brackets and braces, colon and dash headings and a title heading, "contains 2% or less of" and "less than 2%" markers, "and/or" and "or" alternatives with a shared head, purpose parentheses, "cured with:", a regulated Contains line (also glued to the last period, doubled, without a colon, as "Allergens:", with a class and its species), "may contain", "may contain traces of", shared facility and equipment lines (also followed by a "with ..." clause, and two facility clauses in one warning), "may contain one or more of" as a list of alternatives, a period and a heading inside a parenthesis, footnote markers, claims and "added to" phrases; and the unread cases: empty, a placeholder, not text, an unclosed parenthesis, a run-on item, a Contains line or warning with no allergen, an allergen line with no list. Expected values worked by hand from the rule as the item states it ("may contain" and facility lines count as containing for allergies; unread means unknown, never safe) | rule (`rule:construction`, `ok`) |
| `diet-gate.tsv` rows dg-33 to dg-40 (2026-11-21, `cleanup2/ck3-small`) | the lead's decision after wave 6: a beef stew with a cup of red wine braised 2.5 hours (about 0.16 g per serving of 8 after USDA row 5009) kept out for Halal and Whole30 and offered for No Alcohol; the same wine simmered 30 minutes (about 1.1 g) kept out for No Alcohol; a spoon of white wine baked 2.5 hours kept out for Halal, offered for No Alcohol; vanilla extract offered for both | rule (`rule:diet-gate`, `ok`) |
| `gate-catalog.tsv` (2026-11-20) | the curated foods and pending SR Legacy foods of staging (`staging/w01/foods.tsv`) with their USDA description, admission reason (none yet) and nutrient rows, and the USDA category from the SR Legacy 2018-04 and Foundation 2025-12-18 `food.csv` | catalog facts |
| `line-labels.tsv` | 120 lines labelled not an ingredient that are exactly a curated food's name (ingredient by the veto rule) and 60 lines the deterministic pre-pass labelled | rule |
| `recipes-rebuild.tsv` (2026-11-20, `cleanup2/reprocess-lane`) | 123 constructed recipes, two to four lines each, for the error classes of the reprocess design (section 1c): method fills (R7) and water fills, old measures and water on Piece (R5), `tablespn.`/`teaspn.` (R1), `tbp`, pinch and dash with no "of" (R2), "a couple of"/"a pair of" (R3), the amount after the name (R4), colon headings (R8), whole-food lists sharing a product (the rebuild keeps the current food; R9 belongs to the compound rules, D6), Artusi's Italian units (R6) and a newer English extraction replacing the mixed translation, junk foods named with an amount, lost order, and " + " folds; the design's own examples where it gives them. Three were `known` until staging checkpoint 1 (baseline 3, now 0; `cleanup2/ck1-parser-catalog` fixed them): a two-word food between the measure and its last word in the method ("three tablespoonfuls of olive oil"), "teacupful" missing from the method finder's measures, and "medium size" kept in a normalized name | rows that follow from a named rule: rule (`rule:R1` ... `rule:R8`, `rule:order`, `rule:fold`, `rule:residue`, `rule:current-link`, `ok`); the rest: the reprocess-lane builder (`agent-review-reprocess-lane-2026-11-20`, `agent`) |
| staging checkpoint 1 rows (2026-11-20, `cleanup2/ck1-parser-catalog`) in `lines.tsv` (`ck1-*`, 16) and `startup-fills.tsv` (`ck1-*`, 10) | the wrong and doubtful start-up fills and line reads of the checkpoint-1 log (K1, K2: hyphenated mixed numbers after the name, a measure "and a half", per-item ratios, a contradicted temperature, a handful as a piece, an alternative, a folded line), the right fills the log quotes as guards, and the reprocess-lane rows m-10 and w-10 as start-up fills; `r4-05` hand-edited the same day (the name drops "medium size") | mixed numbers and "and a half": rule (`rule:mixed-number`, `rule:and-a-half`, `ok`); m-10: `rule:R7`, `ok`; the rest: the model operator of the checkpoint and this builder (`agent-review-checkpoint1-2026-11-20`, `agent`) |
| second checkpoint-1 round (2026-11-21, `cleanup2/ck2-undo-reprocess`) in `lines.tsv` (`ck2-*`, 2) and `recipes-rebuild.tsv` (`k14-*`, `d-*`, 7) | K14: Artusi lines that are only an amount ("Grammi 100", "Grammi circa 100", recipes 18849 and 18338) read in grams and kept on their food, never on a food named by a number; the six doubtful items of the checkpoint-1 continuation, each decided by rule: 68702 `pig` (no count of a food with no weight per item, R7), 39621 (the amount of the alternative that names the food), 82042 (one tumbler per jelly on the currant jelly row), 102887 (cloves the spice counted in pieces); 22541 (a cut-short recipe with real steps is not rejected by rule) and 66331 (an unreadable jsonld-only source is held) are unit tests in `RecipeReprocessTests`, since the rebuild golden set has no column for a recipe status or a scraper parse | K14 rows and 68702: rule (`rule:R6`, `rule:R7`, `ok`); 39621, 82042, 102887: the fix builder (`agent-review-ck2-undo-reprocess-2026-11-21`, `agent`) |
| `containers.tsv` (2026-11-20, `cleanup2/container-sources`) | 101 lines: one or more per class of `container-classes.tsv` (the mode, and its small and large sides), the SR Legacy container portions of 33 foods (drained and net cans, gelatin envelope and package, yeast packet, cream cheese small package), the cited can table across the three eras, standards of fill, stated sizes, and the lines containers.md says stay uncounted (mushrooms, tuna, tomato sauce, Greek yogurt container, marmalade). Expected values follow from the line's text, the cited table row, the SR portion, or the mode `ops/golden/container_facts.py` computed apart from the C# code. One is `known` (baseline 1): the hold-out of "1 (14.5 oz) can diced tomatoes" against SR's 190 g "can" on canned tomatoes, which the hold-out keeps in shadow. Since `cleanup2/container-weighing` (lead decision on unsized cans in old or undated books) b-03, b-04 and b-19 expect the modern size instead of "uncounted" (b-19, tomato paste of unknown era, was `known` and is `ok`); b-60, a gelatin package in a pre-1960 booklet, stayed uncounted, since only a can or a jar fell back to the modern size (since `cleanup2/container-era-sizes`, owner's third-round answer 7, every kind falls back as the last tier and b-60 expects SR's 85 g package, by hand edit), and nine rows e-01 to e-09 cover the period row `h33-veg-medium` (canned corn in an old or undated book at No. 303, drained; tomato paste, whose 6 oz can the period cans do not cover; mushrooms with no modern size, uncounted; a large can, a modern recipe and a food of another category). One is `agent`: "1 can cloudberry marmalade" left uncounted on SR "Jams and preserves", where both the food and the verdict are the reviewer's. `container-facts.tsv` is frozen from FDC Branded 2025-12-18 and SR Legacy 2018-04 by `container_facts.py`; recapture it whenever the class map changes | rule (`rule:*`, `fdc:*`, `deterministic:*`, `design:containers`); agent (`agent-review-*`) |
| `container-eras.tsv` (2026-11-21, `cleanup2/container-era-sizes`) | 18 unsized lines of old, undated and modern recipes against the constructed corpus of `container-era-lines.tsv` (25 invented lines of invented books, no corpus text) and `container-facts.tsv`: a book's own package size, a book with two sizes once each taking its era's, a book with no line taking its era's, an undated book and a gelatin package taking today's size last, a size word as its own key, two packages, a drained-solids food left uncounted with its reason, a numbered can not mined (the period can), a stated size; since the review, a book whose size holds exactly half of its lines giving none (r-16) and one book stating two sizes in one band giving no era size (r-17, r-18). `container-era-facts.tsv` is `ops/container-era-facts.py`'s output for the lines file, compared row for row with the C# tiers; rewrite it with the script whenever either changes | rule (`rule:container-era-sizes`, `deterministic:*`) |
| `package-weights.tsv` (2026-11-20) | 52 package_weight strings, the first of each distinct shape in the 2025-12-18 branded file, plus 12 constructed (multi-packs, counts, empty); expected values from `container_facts.py` | rule (`rule:fdc-branded-2025-12-18`) |
| size-first rows (2026-11-21, `cleanup2/parser-size-first`) in `lines.tsv` (`sf-01`-`sf-14`), `container-era-lines.tsv` (`l-26`-`l-29`) and `container-eras.tsv` (`r-19`-`r-21`) | the size-first container lines the container-era-sizes report found only the miner read ("1 lb. can salmon", "1 quart can tomatoes", "½ pint can"), further lines of the class (a box, bag, jar and bottle, a hyphenated size, pounds and ounces with no count), the lines the item names as unchanged ("1 No. 2 can corn", "a 3-oz. package", "one 8-ounce bottle", "1 lb. canned tomatoes", "1 can (1 lb.) tomatoes"), and constructed old books whose only sized lines are size-first, giving a same-book and a same-era size and a stated one; `sf-14` ("2 lb. cans", no count: no size per can) is the builder's judgement | rule (`rule:size-first`, `ok`); `sf-14`: the builder (`agent-review-parser-size-first-2026-11-21`, `agent`) |
| size-first review rows (2026-11-21, `cleanup2/parser-size-first` review) in `container-era-lines.tsv` (`l-30`-`l-33`) | constructed old-book lines with a size in pounds and ounces ("1 lb. 13 oz. can peaches") and sizes led by a number word ("one pound can salmon", "a pound can of salmon", "Two-pound can of salmon"), which the C# corpus reader mined and the miner did not; `container-era-facts.tsv` rewritten by the script | constructed lines; facts from `ops/container-era-facts.py` |
| staging checkpoint 2 rows (2026-11-21, `cleanup2/ck4-parser-method`) in `lines.tsv` (`ck4-*`, 11), `startup-fills.tsv` (`ck4-*`, 24) and `recipes-rebuild.tsv` (`ck4-*`, 26) | the logged examples of M4 (ratios in another comma clause or of an unlisted form: 101742, 57526, 84548, 100431, 77642, 121518, 84733, 52082, 133876, 89125, 80462, 68810, 91149, 30742), M5 (method ranges: 140662, 78381, 43777, 77521, 75713, 112130), M6 (69123, 24918; 98289 and the start-up re-read are a unit test in `ScrapedRecipeRepairTests`), M17 (18849's count lines, 18902 `d'acqua`, sun dried tomatoes, zwieback, the coriander line), M18 (44821, 53533, 48764, 28093, 88278, 58512, 98645, 77127, 49994, 74937, 103143), M19 (26804, 103566; 59107 and 110476 decided by R7), M20 (97751) and the doubtful items 18502 and 130814, with steps built around the phrase the log quotes; guards for "each side", "every few minutes", "and"-joined lines and a description holding a second amount | ranges, measures and the after-name amount: rule (`rule:range`, `rule:measure-and-measure`, `rule:and-a-half`, `rule:R4`, `rule:R7`, `ok`); ratios, "or" lines, food choices and Italian names: the fix builder (`agent-review-checkpoint2-ck4-2026-11-21`, `agent`) |
| review of `cleanup2/ck4-parser-method` (2026-11-21) in `recipes-rebuild.tsv` (`ck4-pm1-*`, 5; `ck4-zwieback` re-judged) | an "or" line's first alternative read with the words the alternatives share: "lemon or lime juice" is lemon juice, never Lemon; "chicken or vegetable broth", "milk or dark chocolate" never Chicken or Milk; "lemon or lime" is Lemon (guard) | the review fixer (`agent-review-ck4-parser-method-review-2026-11-21`, `agent`) |
| staging checkpoint-2 rows (2026-11-21, `cleanup2/ck4-recheck-gate`) in `link-gate.tsv` (lg-129 to lg-147) and `alcohol-context.tsv` (ac-178 to ac-202); the w01 row wp-w01-103 promoted from `known` | M10: the four wrong accepts (shelled limas on dried limas, cake-crumbs on a crumb-topped coffeecake, "in flour" on 00 Flour, roast beef bones on a bone-in cut) and M7's "extra flour" on 00 Flour; M22: the pairs the gate's generic rules held although right (preparation words and commas read as two foods, fresh cues on pepper and anise seed, the leaves of spinach and parsley, powdered cracker on cracker meal); constructed rows that guard the narrowed rules; M21: drink words naming another food, flavour words in cook's notes, and the re-check's doubtful alcohol items decided by rule; "lea & perrins' sauce" now passes the narrowed compound rule (promoted by the update switch, baseline 88 to 87) | M10, M7 and M22 pairs: the checkpoint operator (`agent-review-ck2-2026-11-21`, `agent`); constructed and M21 rows: rule (`rule:construction`, `rule:alcohol-context`, `ok`); three doubtful items (a ratio line of milk and brandy, "bourbon chair caramel", "spirits of nutmeg"): the fix builder (`agent-review-ck4-recheck-gate-2026-11-21`, `agent`) |
| review rows of the checkpoint-2 fix (2026-11-21, `cleanup2/ck4-recheck-gate`) in `link-gate.tsv` (lg-148 to lg-155) and `alcohol-context.tsv` (ac-203 to ac-205) | bone-in chicken thighs and pork chops, which the bone part word held although right; fresh ginger, fresh turmeric and fresh red pepper on the ground spice, the leaves of grapes and limes on the fruit, fresh white pepper on white pepper; sherry raisins, port figs and Marsala pears, which still count the wine | the two bone-in pairs and the three wine lines: the review fixer (`agent-review-ck4rg-2026-11-21`, `agent`); the rest: rule (`rule:construction`, `ok`) |

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
