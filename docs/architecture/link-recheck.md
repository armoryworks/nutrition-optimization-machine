# Link re-check: links already made, put through the gate again

The link gate (`link-gate.md`) judges every new answer to "which food is this wording". The links
made before it existed are re-checked here (plan C3): about 66,000 waiting model picks, about
111,000 applied model links (October and the unguarded 2026-11-03 auto-apply), the trusted aliases
the alias classification gave (the potato skins and dried herbs), the alcohol lines on a food of
another sense (alcohol A4), can lines on a raw food (containers 2.6) and the lines on prepared foods.

Code: `LinkRecheckService` and `LinkRecheckRules` (Nom.Orch), `LinkRecheckController`
(`api/FoodCatalog/link-recheck`), `LinkRecheckHostedService` (the lane). No model decides anything;
a model only audits a sample.

## Withdraw the waiting picks

`POST withdraw-picks?apply=` withdraws every pending `ai:` link proposal no person reviewed (soft
delete), one `LinkRecheckWithdraw` journal entry each, all under one stamp. None is applied as it
stands (37 % were right). The link lane then asks each wording again, and every answer it makes
passes the gate before it applies. The journal entries have their own change type on purpose: the
time-window `relink/undo?sinceUtc=` and a wording retract take back `ProposalWithdraw` entries, and
neither may reopen 66,000 picks. The catalog admission pass journals its own withdrawals as
`CatalogPickWithdraw` for the same reason. `POST undo?stamp=` puts them back, except a pick that changed since
or whose ingredient got a newer link proposal (the lane asked again).

## Rule sets

`POST run/{set}?apply=&max=&sampleSize=` runs one set over at most `max` wordings, most lines first.
The unit is one wording on one food: the live scraped ingredient lines on the food whose own text
names the wording (`LineRelinkService.NamesWording`), or, for the alcohol and container sets, the
lines of one wording and one reading. Run them in this order (plan 3.3 step 8):

| Set | Wordings | Acts on |
|---|---|---|
| `alcohol` | every line on a curated food with an alcohol word whose sense (`AlcoholLineContext.Read` with the recipe's title, lines, steps and source) refuses the food, and every model, context-free or re-check alias on a curated food whose sense refuses it (alcohol A4) | `alcohol-drink`, `alcohol-no-food`, `alcohol-not-drink` |
| `part-herb` | applied model links and rule-given trusted aliases (including the re-check's own) | `part`, `fresh-herb` |
| `compound` | the same | `compound`, `heading` |
| `container-form` | every can, tin or jar line on a food whose USDA entry is not canned, and every drained line on the whole can | the container line rule |
| `prepared` | every line on a prepared food (USDA categories 3, 21, 22, 25) | `prepared` |
| `applied-1103` | model links applied on 2026-11-03 | every gate rule |
| `applied` | every other applied model link | every gate rule |
| `aliases` | trusted aliases with a `deterministic:recheck:`, `deterministic:recheck2:` or `link:deterministic:` context | every gate rule |
| `wordings` | verified wording rows (report only: writes nothing) | every gate rule |

For each wording the gate is asked about the food it is on, with its lines. A set other than
`alcohol` reads the three alcohol rules only when the wording itself is refused
(`LinkRecheckRules.ForSet`): the alcohol set reads every line by its own sense, and one line of a
wording that reads as a drink or a flavouring ("garlic, minced (for a milder garlic flavor)") never
decides for its 662 other lines. When the gate holds the pair with a rule the set acts on, the
re-check asks whether the hold is **positive evidence that the food is wrong** (`link-recheck/2`,
staging checkpoint 2 M22; `LinkRecheckRules.Refutes`):

| Refutes the food | Only fails to prove it right (not proven) |
|---|---|
| `heading`, `retired`, `branded`, `prepared`, the three alcohol rules, `form`, `fresh-herb`, `head-lead`, `polarity`, `group` | `containment`, `lead-noun`, `generic`, `state`, `marked`, `variety`, `word-order`, `rule-other`, `other-alias`, `shortlist`, `uncurated`, `incomplete` |
| `compound`, unless the wording gives alternatives ("or", a slash) and one of them is the food (`LinkGate.NamesAlternative`): a line of alternatives counts its first by the compound lane's own rule | `compound` on alternatives one of which is the food ("butter or olive oil" on Butter, "light or dark brown sugar" on Brown Sugar) |
| `part` the wording names that the food lacks ("lime leaves" on Limes); `part` on the food's own segment unless it is the ordinary eaten part of a food named whole | `part` on the food's own segment flesh, meat, "meat only", leaf ("baked potatoes" on "Potatoes, baked, flesh", "goose" on "Goose, meat only") |
| `percent` when the wording states the percent; `head-noun` when the food has the head noun only as an addition ("with crumb") | `percent` from the description alone; `head-noun` of two readers |
| `other-food` when a word of the wording is also missing from the food ("olive oil" on Canola Oil) | `other-food` on a food that holds every word ("15 oz black beans (canned)" on Canned Black Beans while "Black Beans" exists) |

A marked variant the wording does not name ("ketchup" on "Ketchup, restaurant", "tomato sauce" on
"... with salt added", "thin cream" on light cream, "non-dairy milk" on imitation milk) is the same
food in another make, not another food, so it is never proof.

Since `link-recheck/6` (staging checkpoint 4, P13), three more holds are no proof:

- a `part`, `group`, `form`, `percent` or `polarity` hold on a wording that names the food as its
  first alternative (`LinkGate.NamesFirstAlternative`): "butter or margarine" on Butter, "nutmeg or
  lemon peel" on Nutmeg, "lemon juice or vinegar" on lemon juice. A line of alternatives counts its
  first, so the hold comes from another alternative. A single first word that leads foods of its own
  is an alternative, not an adjective ("lemon juice or white vinegar" on vinegar stays refuted);
- a `form` hold whose form a cook gives the food at home or keeps it in (`PreparationForms`: cooked,
  boiled, stewed, baked, fried, steamed, braised, grilled, broiled, poached, roasted, toasted, browned,
  frozen): "toasted pine nuts", "roasted garlic", "frozen mango chunks" are the food prepared or kept;
- a `compound` hold on a wording that names no other food (`LinkGate.NamesOtherFood`: no word of it
  leads foods of its own in the catalog and is missing from the food, a form, a state or an "-ed"
  preparation word apart): "heavy cream, whipped", "eggs, preferably at room temperature", "dates
  stoned and sliced" are one food.

Since `link-recheck/7` (staging checkpoint 5, Q14 and Q15), two more:

- a `fresh-herb` hold when every fresh cue of the wording and its lines qualifies a later item, not the
  food named first (`LinkRecheckRules.FreshOfLaterItem`): the cue stands in an item (split at a comma,
  semicolon, colon, bracket or "or") after the first item that names a word of the food, and a word
  follows it there that the first item does not hold. "1 cinnamon stick, 1-inch piece of fresh turmeric"
  on Cinnamon (38519) and "cayenne pepper, 1/2 teaspoon, or chopped sweet pepper, 1 small" on Cayenne
  Pepper (49500) keep their spice; "rosemary, chopped" and "rosemary, 2 sprigs fresh rosemary" on the dried
  rosemary are still taken off it (golden lr-ck10s-01 to lr-ck10s-03);
- a `group` or `compound` hold that only the part after a ratio phrase raises
  (`LinkRecheckRules.BeforeRatio`: "per", "for each", "for every", "to each", "to every", "to a
  <measure> of"; a bare "each" or "every" is none, since "1 cup each sugar and flour" spreads one amount
  over two foods, review CK10S-7): the gate checked again on the wording cut before the phrase no longer
  raises it. "sugar per quart of juice", "sugar, 10 oz. for each pound of juice", "sugar to a gallon of
  vinegar" on Sugar (lr-ck10s-04 to lr-ck10s-06). The gate already read the ratio for its part rule
  (P13); these classes now read it too. Both classes stay below the bar and write nothing; the rule
  only takes wrong refutations out of their candidates.

The `separable lean only` and `lean only` segments of a meat entry are its ordinary eaten part, as
"meat only" is ("shoulder of veal" on Veal Shoulder). Fixes in the gate itself (`link-gate/13`, see
`link-gate.md`) remove other wrong refutations the checkpoint's rows showed: "not sweetened" is
unsweetened, a stated 100 %, 0 % or 2 % is met by a pure, nonfat or lowfat food, "fresh or frozen" gives
alternatives, the raw entry of a legume, grain or seed is the dry product, the food's own name counts
for its part, form and group, leaves are greens, a part word that qualifies another food word or follows
"per" is no part, a group word after "in" is a packing medium, and a grated spice or a handful of one that
is no leaf herb is the dried spice.

### Re-check classes and the bar (link-recheck/6, staging checkpoint 3 N12, review CK6R-1, CK6R-2, CK6R-5, ck7 judged rows, staging checkpoint 4 P13)

A refuting hold belongs to a **re-check class**: the three alcohol rules are one class (`alcohol`),
the container line rule is `container-form`, and every other refuting rule is its own class
(`prepared`, `part`, `fresh-herb`, `compound`, `form`, `other-food`, `group`, ...;
`LinkRecheckRules.ClassOf`). A class may move or take back lines **by rule** only when it holds at
least 95 % of its checkpoint-judged golden lines right over at least 40 of them
(`LinkRecheckRules.ClassPrecisionBar`, `ClassMinimumJudged`). The judged lines are
`Fixtures/Golden/link-recheck.tsv`, status `agent`: every sampled group (a wording on a food, with
its example lines) the operators of staging checkpoints 2 and 3 judged, with both verdicts (`judged`
wrong: the food was wrong, and for a move `rightFood` is the food the operator judged right; right:
the re-check was wrong to act, or a kept line was right to keep; doubtful: kept as doubtful and left
out of both sides of the figure). The checkpoint operators were agents, not people, so these are
operator readings by an agent (`source` `agent-operator-checkpoint3-...`, `agent-review-ck2-...`,
`agent-review-ck3-...`), never a person's judgement. Checkpoint 3 read every alcohol, container-form,
prepared and move sample of its dry runs (`lr-dry1-<set>.json`) and named its other verdicts; a row
exists only for a group the log judged. Rule-given rows (status `ok`: the alcohol sense table, and
the owner's rule that a prepared food is never a line's food) must be right too, but they never
count toward the 40: they test take-backs by construction and say nothing about the food a move
picks.

Lines from scraped web sources are not committed as their authors wrote them. The checkpoint
samples do not say which source a line came from, so every `agent` row is treated as scraped and its
`lines` and `wording` are masked (review CK7R-1). A word keeps its form only when it is one of:

- a word of the name or description of the row's own food or `rightFood` in `gate-catalog.tsv`;
- a word of `link-recheck-words.tsv`, which lists each kept word with its kind: units, numbers and
  function words (`unit-or-function`), the drink words of the alcohol sense table (`drink`), words
  of the string tables of the gate, the re-check rules and the compound splitter (`gate-rule`), and
  the few other words a row's decision was found to read (`decision`, such as "depending" in a
  cook's note about wine).

Every other word (a cook's aside, a brand, another food, a serving note) becomes a stable
pseudo-word (`qkuvur`), the same word always the same pseudo-word. The masker started from that
vocabulary and, for the 23 rows whose decision changed, kept back only the words the decision
needed. Each row was then decided in both forms by the scorer (`--rows`). Outcome, class, every
class the row counts for, what it would write, the new food and whether that is right are identical
for all rows, so the masked rows test what the readable ones test.

What stays as written is a plain ingredient statement: an amount, a unit, the food's catalog words,
and a drink, form or part word, such as "1 teaspoon smoked paprika" or "1/2 pint of cream, whipped".
On this build that is 568 of the 928 `agent` rows (145 of the 242 rows of staging checkpoint 4). Every row with an aside, a brand or another
food's name is masked in part. The golden test `A_checkpoint_row_keeps_only_its_foods_words...`
refuses an `agent` row that keeps any other word, so a word is kept only by an edit to
`link-recheck-words.tsv` that shows in the diff. The readable rows stay with the staging evidence,
outside the repository. The history of the integration branch still holds the readable lines of the
first ck6 rows; this file masks them from here on.

The classes allowed to write are `LinkRecheckRules.ProvenClasses`, read from the committed per-class
record `Fixtures/Golden/scores/link-recheck-classes.json` (built into the API,
`LinkRecheckClassRecord`), never written by hand. The scorer writes it with `--record` (refused under
CI): each class's figures and the classes at the bar, under the rules' version and **decision
signature** (`LinkRecheckRules.DecisionSignature`, the signature without the proven classes). A
record scored under other rules proves nothing (`ProvenProblem`, shown on `GET link-recheck/status`
as `provenProblem`). The golden test `The_proven_classes_are_the_committed_record...` re-scores the
rows and refuses a record that differs from them, so a class is added or taken out only by the
rows; `Every_class_that_writes_by_rule_holds_the_bar_on_its_judged_rows` refuses a proven class
below the bar; `Rule_given_rows_never_lift_a_class_over_the_minimum...` holds out any class whose
checkpoint-judged rows are fewer than 40, whatever its rule rows; and
`A_class_below_the_bar_writes_nothing...` checks that no other class writes. After a change to the
rules or the rows: run the scorer with `--record`, commit the record with the change, and build
again.

The per-class scorer prints the table (no database, no model):

    dotnet run --project nom-api/Nom.Import -- --recheck-classes [--rows] [--record] [--golden <dir>]

It decides every row with `LinkRecheckRules.Judge` (the same call a run makes) over the frozen
catalog of `gate-catalog.tsv`. It scores each class as if it were allowed to write: a take-back is
right when the food was wrong, and a move when the food was wrong and the new food is the row's
`rightFood`. A row counts for **every class whose hold refutes its food**
(`RecheckVerdict.Classes`), not only for the class that decides it today. Once proven, any of
those classes would make the same move or take-back, so a class is scored on exactly the rows it
would write, and no figure depends on which classes are proven when the rows are scored (review
CK7R-2; `A_class_is_scored_on_every_row_it_refutes_whichever_classes_are_proven`). It prints judged rows, precision, the agent and rule split, coverage (the share of
all rows judged wrong the class puts right), whether it meets the bar and whether it writes by rule;
`--rows` adds every row's decision. On this build (`link-recheck/7`, 998 rows: 663 judged wrong, 275
right, 60 doubtful; the 242 rows of staging checkpoint 4's reading, section 10 of its log, are lr-751 to
lr-992, source `agent-operator-checkpoint4-2026-11-23`, read from the unread groups the ck7 build listed; lr-ck10s-01 to
lr-ck10s-06 are rule rows of staging checkpoint 5, Q14 and Q15):

| Class | Checkpoint-judged right / judged | Rule rows | Precision | Writes |
|---|---|---|---|---|
| alcohol | 302 / 302 | 20 / 20 | 100 % | **by rule** |
| prepared | 86 / 86 | 40 / 40 | 100 % | **by rule** |
| fresh-herb | 55 / 55 | 1 / 1 | 100 % | **by rule** (since `link-recheck/6`) |
| part | 64 / 69 | | 92.8 % | candidates only: **not proven** |
| compound | 23 / 35 | | 65.7 % | candidates only: **not proven** |
| form | 15 / 26 | | 57.7 % | candidates only: **not proven** |
| container-form | 18 / 18 | | 100 % | candidates only (22 lines short; staging holds no more groups) |
| other-food | 11 / 14 | | 78.6 % | candidates only: **not proven** |
| group | 9 / 12 | | 75.0 % | candidates only: **not proven** |
| percent, polarity | 5 / 5, 3 / 3 | | 100 % | candidates only (too few rows) |
| head-noun, head-lead, heading | 0 / 2, 1 / 1, 1 / 1 | | | candidates only |

Written by rule: 522 rows, 504 of them rows judged wrong and put right (lr-ck10s-03 the new one), the rest doubtful. None is
written against its judgement. Part, compound, form, group and other-food still fail the bar on the
checkpoints' rows after the fixes above, and percent and polarity have too few: their refutations
stay candidates, which the model audit decides on their own tiers (`candidate:<class>`); none of them
is proven by rule. The rows they still refute wrongly are cooks' alternatives the rules cannot read
("spelt, white, or oat flour", "butter _or_ margarine"), measures ("butter, the size of an egg"), a
food's own variety ("smoked paprika", "pounded white sugar"), and the seeds of pomegranate, which are
its eaten part.

These figures are the rows, not the checkpoint's own reading, and for five sets they lean against
the class (review CK7R-3). The log read every alcohol, container-form and prepared sample and every
move, so those rows are the whole reading. For part-herb, compound, applied-1103, applied and
aliases it read unlinks at random. It named every wrong one but only some of the right ones, and it
did not record which samples it read, so the unnamed right unlinks have no row.

| Set | Unlinks read (log) | Wrong (log) | Right unlink rows | Unread or unnamed unlink groups |
|---|---|---|---|---|
| part-herb | 45 | 15 | 11 | 128 |
| compound | 40 | 21 | 5 | 173 |
| applied-1103 | 30 | 10 | 10 | 138 |
| applied | 30 | 17 | 7 | 129 |
| aliases | 30 | 22 | 3 | 141 |

So compound, part, form and group are scored on fewer right take-backs than the operator saw. For
compound, the log's own count is 19 of 40 right (47.5 %), against 16.9 % on the rows. No class
below the bar would reach it on the log's counts either, so no verdict changes. A later reading
records every sample it reads, by index, right or wrong, so the rows can be the whole reading.

A refutation by a class that is not proven is a **candidate**: the lines stay where they are, the
run counts them (`candidates`, `candidateLines`, `byCandidate` by class; the sample outcome
`candidate` with the move or take-back it would make). Each class decides its candidates on a
**tier of its own** on the lane frame, `candidate:<class>` (`LinkRecheckRules.CandidateTier`, with
its own signature, `TierSignature`), never on the tier of the set they came from: an applied run or
a lane batch records each candidate there in shadow, with the set as its stratum, as a decision that
would write, and after each round the lane has the frame's auditor (the link-audit lane, `phi4`
confirmed by `mistral-nemo`) sample each class tier and evaluates it against the committed
`scores/link-audit.json`. So a set whose would-write decisions are mostly a class that is right
(alcohol holds) can never carry a compound or part candidate into writing. A candidate is written
only while its class tier is live, in a lane batch whatever the set's mode (not while the set or
the lane is paused) and in an operator's applied run alike (counted in a dry run the same way);
`byQualified` counts them by class. A lane batch writes each class's candidates as a run of that
tier under a stamp of its own, so the tier's pause undoes only its own writes, and the status lists
those runs beside the sets. A wording already decided under the set's signature in the last 30
days is not decided again, except a candidate recorded in shadow (by the lane or by an operator's
run) whose class tier has since gone live: the lane decides it again and writes it. To let a class
write by rule instead, add the lines a staging checkpoint judged to link-recheck.tsv (masked) until
it holds the bar, then run the scorer with `--record` and commit the record: the class is then in
`ProvenClasses` and the lanes' signature changes with it.

With a refuting hold of a class that writes:

1. **Move** to the first food a deterministic tier names that the gate lets through, in the
   pipeline's order: the verified wording row, a trusted alias, a staple or the name matcher, a
   curated food of the wording's name, then the drink the lines' alcohol sense allows (the sense
   table's foods in order). The container set's only tier is the one curated canned food that shares
   the food's lead word and holds every word of the wording (drained when the line is drained). The
   alcohol and canned tiers vouch for the food's identity themselves, so the gate's word rules
   (containment, lead noun, head noun, generic, state, word order, percent, rule-other, other-alias, and
   since `link-recheck/6` variety, which the distinguishing rule of `link-gate/13` raises on a canned sister)
   are not read for them; every other rule is.
2. Else **take back** (refuted, no tier names a food): the lines go to the uncurated food of the
   wording's name (created when none is live, after a retired row holding the name is renamed out of
   the way). Like every wording food it may sit beside a curated or USDA food whose name differs only
   in case ("cheese tortellini" beside "Cheese Tortellini"). When the food the lines leave holds the
   very same name (the prepared baby food named "zwieback"), that food is first renamed to its USDA
   description, journaled under the stamp, so the lines can leave it and later imports stop landing
   on it (M24). Only another curated or USDA food holding the very same name stops it (the unique
   index on the name), and the lines then stay, counted `noHome`. No food is better than a wrong one.
   The recipe stays out of planning until the wording lanes find the right food, and the link lane
   asks about the new ingredient through the gate.

Holds that refute nothing **keep** the lines and are counted as not proven (`unproven`,
`unprovenLines`, `byUnproven` by rule), apart from the refuted ones (`moveGroups`, `unlinkGroups`,
`refutedLines`, `byReason`), so a run measures both. No move happens without a refuting hold, even
when a tier names another food. The container rule moves a can line to its one canned sister and
never takes a line back (counted as held); a sister must not carry a state the line does not name
("coconut cream, canned, sweetened" for "1 can coconut cream") or add a food after "with" ("Corn
with red and green peppers" for "a can of green corn"), and a drained line whose wording is the
liquid itself ("pineapple juice (drained juice from the pineapple chunks)") is no wrong form.

The gate's `units` and `grams` rules are not read: the re-check moves a line without its amount and
computes no grams. A line the compound lane put on an alternative on purpose (a `LineAlternative`
entry for its place), a wording the verified wording table gives to its food (taken back by
`wordings/{id}/retract`, which keeps the rejection) and a wording a person approved for the food (an
`admin:` alias) are left and counted. The `prepared` set is the exception: a prepared food is never a
recipe line's food (owner answer 2, second round), so it takes those lines off too and lists the
verified rows it acted against in `wordingRows`; retract each, or imports keep landing on the
prepared food. The `wordings` set lists the verified rows the gate now holds, with the row id, for
that retract.

A move or take-back also removes every alias of the wording on the old food that a rule or a model
pick put there (null, `link:ai:`, `deterministic:recheck:`, `deterministic:recheck2:`,
`link:deterministic:`; never a USDA description, an `admin:` or a `retired:` alias), in name order,
each once in a run by its key (food and name; an alias row has no id of its own, which is why
staging checkpoint 3 saw one of #276291's two aliases go on each apply, N18). A move gives the
wording a trusted alias `deterministic:recheck2:<tier>` on its new food, so a later import follows
it rather than the applied link of the retired holder, **only when the gate accepts the wording for
that food as for any wording** (not as the tier that vouched for the lines) **and the wording names
no restricted food the new food's own name and USDA entry do not** (`LinkRecheckRules.MayAlias`,
the default restriction patterns read by `DietPatternRule`). Otherwise the lines move without the
alias, counted `aliasesRefused`. Staging checkpoint 3 (N21): the alcohol set gave distilled
spirits the alias "corn brandy", and Corn Allergy then kept out 1,040 brandy recipes and Grain-Free
655. That alias and the lines it moved
are read again by the `alcohol`, `part-herb`, `compound` and `aliases` sets, so a move made under an
older gate is checked again when the gate's signature changes. Since `diet-patterns/5` a companion pattern
(`%sherry%` beside `%wine%`) adds no restricted word when every default type that holds it already reads the food
by another pattern, so "dry sherry" may still become an alias of a dessert wine (staging checkpoint 5, Q13).

## Journal and undo

Every write of a run carries the run's stamp (UTC, whole milliseconds): `LinkRecheckLine` entries for
lines (old and new `food@position`), `LinkRecheckAlias` for aliases (old and new name, context,
deleted), `LinkRecheckFood` for an ingredient created and a name freed, and a `LinkRecheckRun`
header. The header is written before the run's first write and completed with the counts at its end
(`complete`), so a withdrawal, set run or rename cut off partway (a timeout, a restart) still shows
its stamp on the status and is undone by it; the Pipeline page marks such a run "cut off before its
end" (`pipeline-link-recheck-run-cut-off-<set>`, `pipeline-link-recheck-withdraw-cut-off`). A header
not yet complete whose run is still going in the API process carries `running` on the status instead,
and the page shows "still running" (`pipeline-link-recheck-run-running-<set>`,
`pipeline-link-recheck-withdraw-running`), so a withdrawal partway through its chunks is not taken for
one cut off; the process forgets its running stamps when it stops, so after a restart such a header
reads as cut off again. Only a run that writes has a header: a lane batch in
shadow records its decisions on the frame and nothing in the journal. `POST undo?stamp=&apply=` takes the run back: lines through
`LineRelinkService.UndoEntriesAsync` (newest first; a line on a recipe rebuilt after the run stays,
counted `skippedRebuilt`, so undo the rebuild first), then the aliases (an alias the run created is deleted as a row, `ApplicationDbContext.HardDeleteAsync`, so no deleted copy is left behind under a request either, N18), then the created ingredients
that hold nothing again (deleted as rows, not soft-deleted, under a savepoint so one still
referenced is kept and counted `foodsKept`), then the freed and renamed names, newest first (a
created food the undo removes no longer holds the name, so every freed name comes back: M24; run
again on a stamp whose undo under `link-recheck/1` left its wording foods soft-deleted, it deletes
those that hold nothing, an uncurated food with no FDC id created within a day after the stamp that
holds the freed name, and gives the name back). A
withdrawal's undo reports `pickEntries`, `picksRestored` and `skippedAskedAgain` (M23). A set's dry
run walks the apply's own steps without writing (homes, created and renamed foods, freed names,
aliases, verified rows acted against, approved recipes), so its counts are the apply's except for
lines that moved since (an alias two groups of one wording on one food share, as the alcohol set's
sense groups can, is counted once, as the apply writes it once); an undo's dry run likewise counts the lines, aliases, foods and names it
would give back. Line moves have their own change type, so the time-window `relink/undo` and a wording
retract never take back a re-check's lines without its aliases; only its stamp does.

Operator note: after the withdrawal every withdrawn wording is a link-lane source again: expect
`linkQueue` to rise to about 64,000 while the link lane stays paused. A food holding the wording's
very name is renamed to its USDA description, or, when that differs from the wording only in case
("zwieback" described "Zwieback"), to the description with its USDA category ("Zwieback (Baby
Foods)"), or its id when it has none (`LinkRecheckService.RenameFor`, N18).

`POST names?apply=` lists curated foods whose name claims a colour, "whole" or "seed" their USDA
entry lacks while the entry names a form the name lacks, and renames those in the fixed table by
FDC id (170929 "Spices, mustard seed, ground": "Black Mustard Seed" to "Ground Mustard") through
the journaled rename, under one stamp; the same undo gives the name back.

## The lane

Lane `link-recheck` on the lane frame, one tier per set and one per class not proven
(`candidate:<class>`) (`LinkRecheckHostedService`, off unless
`LinkRecheck:Enabled=true`; `LinkRecheck:BatchSize` 2000, `LinkRecheck:MaxPerDay` 2000 a set,
`LinkRecheck:IntervalHours` 24; switch `ai-pause-link-recheck`). Each round runs the sets in order
over the wordings not decided under the set's signature (`LinkRecheckRules.SetSignature`: the
re-check's version and tables, `LinkGate.Signature` and `AlcoholLineContext.Signature`) in the last
30 days, records every decision on the frame, and writes a set's proven refutations only while the
set's tier is live and a candidate only while its class tier is live. After a batch
the roster's auditor (`phi4`) reads a sample of the moves and take-backs blind with the same-food
judge (a move is wrong unless the line is its new food; a take-back is wrong when the line was its
old food), reader B (`mistral-nemo`) confirms a flag, and the frame evaluates the tier: qualify on a
98 % Wilson lower bound, pause when a full window of 300 shows more than 3 % confirmed wrong, or when
more than 5 % of a live batch's lines were no longer where it found them. After the sets, each class
tier is audited and evaluated the same way (a batch's skipped-lines fault goes to the classes it
wrote). A pause undoes the lane's own runs of that tier since the last clean window
(`LinkRecheckLaneUndo`; an operator's runs are left to their stamp).

An operator's apply records its moves and take-backs on the frame too (mode live), and its
candidates in shadow on their class tiers, so `POST audit/{set}` can audit them (`{set}` is a set or
a class tier, `candidate:<class>`). `AuditAndEvaluateAsync` evaluates a set with the committed
`scores/link-audit.json` (lane `link-audit`, scored by the wording readers' scorer, see
`wording-readers.md`): its known-bad and known-good flags are the audit check's score, and a record
that is missing, scored another same-food judge prompt (its `signature`), another auditor or reader B
than the set is audited with, or below its `model-lanes.json` floor (75 %), keeps the set in shadow. Until that record is committed the sets are applied by an operator, one dry run at a time,
and the lane decides in shadow.

## Status

`GET status` (and the Pipeline page block "Link re-check") shows whether the lane is on, the rules'
version and signature, the classes that write by rule (`provenClasses`, and `provenProblem` when the per-class record
is not current, so none does), how many model picks wait,
the last withdrawal and the newest run of each set and of each class tier (with `candidates` and `candidateLines`).

## The diet guard on take-backs and moves (link-recheck/6, staging checkpoint 4, P19)

The prepared set took "cheese tortellini", "zwieback", "frozen potstickers" and canned spaghetti off USDA foods
whose names or descriptions named pasta, wonton and pork, onto new wording foods whose names carry no such word,
so Celiac Disease and Wheat Allergy stopped flagging 111 and 128 recipes. The links were right; the diet side
was not. Two parts close it:

- the composite rows of `label-terms.tsv` for every food the prepared set creates at the checkpoints
  (`diet-gate.md`, checkpoint 4 section), so the new foods' names and the lines keep their flags;
- `RecheckDietGuard`, read by every run of every set after the decisions are made: a take-back (`unlink`) or a
  move (`move`, a promoted candidate included) of a group with lines may not lower the flag of any restriction type
  (every default type, companions and composites included, and every stored type's own pattern criteria) on its
  recipes. Before is read from the food's name, its USDA description and the lines' own text; after from the
  wording (a take-back) or the target's name and USDA description (a move), and the lines. A drop is allowed only
  when the new name reads as label language of the cited table (`StatementLabelTerms.Match`) and nothing the table
  says it is, contains or may contain is a word the type restricts ("chicken thighs" off breaded chicken may drop
  the wheat flag). The diet gate reads a recipe line by its food's name and composite companions only, so a known
  term whose restricted contents its name does not carry keeps the flag (review CK9T-1: "tvp" is soy, "lactose"
  milk, "einkorn" wheat, "lysozyme" egg, "animal rennet" veal; "frozen potstickers" may be shrimp). Otherwise the
  group is `held` with the reason `diet flags kept (contents of the wording not known; it would lower ...)` or
  `diet flags kept (a move to <food> would lower ...)`, and its lines stay on their food, on the safe side
  ("patty" off a fast-food hamburger, whether as a take-back or a move to a curated food named "Patty" (review
  CK9T-2), "the food" off a biscuit, a seasoning off a rice mix, an orange liqueur off orange juice for Citrus
  Allergy, a hard cider off cider vinegar for Sulfite Sensitivity). A group with no lines (an alias only) is not
  judged: no recipe's flag depends on it.

### Every other path that moves a line (staging checkpoint 5, Q21)

The w01 round trip (retract, unload with lines, reload, `relink/verified`) moved the 25 "Franco-American Spaghetti"
lines off USDA `Pasta with tomato sauce, no meat, canned`, where the prepared set's guard held them, back onto the
uncurated `Franco-American Spaghetti` of their name, and the reload refused the row (a prepared food), so they stayed:
Nightshade Sensitivity and Interstitial Cystitis stopped flagging the recipes. The unload did what the re-check had
refused to do. Since this build every path that moves a recipe line to another food passes the same guard,
`LineMoveDietGuard` (the re-check's `RecheckDietGuard` read line by line, with each food's name, USDA description and
FDC id read from the database the guard was loaded from, so a dry run on a scratch copy decides as the apply does):

| Path | Where | What a held move does |
|---|---|---|
| relink by name (`relink/preview`, `relink/apply`) | `LineRelinkService.PlanAsync` | left out of the plan, counted `heldByDiet` |
| a food's lines (`relink/food`, before `relink/retire`) | `MoveFoodAsync` | left on the food, counted; the retire then finds lines left and refuses |
| a wording's lines (`relink/wordings`) | `MoveWordingsAsync` | left out, counted by the dry run too |
| a verified wording's relink (`relink/verified`, the reload) | `LinkVerifiedWordingsAsync` | left on its uncurated row, which is then kept |
| the return of retracted import landings (the reload) | `ReturnLandingsAsync` | left where the retract put it, its entry kept; counted once (the verified relink that follows leaves the line out, review CK10S-5) |
| the put-back of import landings (retract, unload) | `PutBackLandingsAsync` | left on the wording's food, its entry kept; no food is made for it |
| the undo sweep (`relink/undo`, a retract's and an unload's line moves, LinkRecheck `undo`, a picked alternative, an unsplit) | `UndoEntriesAsync`, `CompoundLineService.UnpickAsync`, `UnsplitAsync` (read with the joined line it puts back) | left where it is, its entry kept for a later undo, counted `heldByDiet` (not `skipped`) |
| a picked alternative, a split | `CompoundLineService.ApplyAsync` (read with the line's whole text) | left, counted `heldByDiet`, by its preview too (left out of `lines` and the samples) |
| an applied link's undo (`LinkApply/undo-applied`) | `LinkApplyUndoService` | the link is left as it is, with the reason, unless `leaveMovedLines`; then the line stays on the linked food |
| a food merge (a link approval, a person's or a lane's) | `CatalogCleanupService.MergeIntoAsync`; `FoodCatalogReviewService.ApplyFdcLinkAsync` asks `LowersDietAsync` first | the link is held: it stays pending, and neither the merge nor the nutrient copy is made (the USDA food lacks what the ingredient's lines name, so it is another food; review CK10S-2) |

The re-check's own writes pass the guard group by group before its dry run counts them (above), so its writer does not
read them again line by line (`LineMoveDietGuard.Decided`). A move onto a USDA food with its description, a move onto
a food whose name or description still names what a type restricts, and a line whose own text names it are never held;
a move that raises a flag never is. Results carry `heldByDiet` (`relink`, `relink/undo`, `relink/verified`, the
retract's and unload's `lines`, LinkRecheck `undo`, `LinkApply/undo-applied` as `linesHeldByDiet`, the compound lines) and, where the
service moves lines itself, the first 200 holds with the types each keeps (`dietHolds`). Tests:
`LineMoveDietGuardTests` drives each path with a move that would lower a flag (the Q21 unload and reload round trip
leaves every line where it was), with the held link approval, the reload's single count and the unsplit; the compound
apply and preview by `CompoundLineServiceTests`.

The guard reads what the default patterns and stored criteria read in text; a food's trusted aliases beside its
USDA description, its category probe (`%pork%`) and a branded food's statement are not read, so a flag the old
food had only by those may still drop. The golden scorer of the classes (`--recheck-classes`) judges links, not
diet flags, and does not run the guard. `recheck-diet.tsv` runs the prepared set end to end on one constructed
line of each prepared food the set took lines off at checkpoints 3 and 4, with every default type on its own
household, and fails when a take-back to a wording of unknown contents lowers a recipe's flag.
