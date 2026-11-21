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
food in another make, not another food, so it is never proof. With a refuting hold:

1. **Move** to the first food a deterministic tier names that the gate lets through, in the
   pipeline's order: the verified wording row, a trusted alias, a staple or the name matcher, a
   curated food of the wording's name, then the drink the lines' alcohol sense allows (the sense
   table's foods in order). The container set's only tier is the one curated canned food that shares
   the food's lead word and holds every word of the wording (drained when the line is drained). The
   alcohol and canned tiers vouch for the food's identity themselves, so the gate's word rules
   (containment, lead noun, head noun, generic, state, word order, percent, rule-other, other-alias)
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

A move or take-back also removes the wording's aliases on the old food that a rule or a model pick
put there (null, `link:ai:`, `deterministic:recheck:`, `deterministic:recheck2:`,
`link:deterministic:`; never a USDA description, an `admin:` or a `retired:` alias), and a move gives
the wording a trusted alias `deterministic:recheck2:<tier>` on its new food, so a later import
follows it rather than the applied link of the retired holder. That alias and the lines it moved
are read again by the `alcohol`, `part-herb`, `compound` and `aliases` sets, so a move made under an
older gate is checked again when the gate's signature changes.

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
counted `skippedRebuilt`, so undo the rebuild first), then the aliases, then the created ingredients
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

Operator notes (staging checkpoint 2): read a set's dry run as a lower bound. It counts lines, but
not the ingredients a run creates as homes for unlinked lines, the aliases it untrusts or removes,
the names it frees or the wording rows it acts against; the apply reports them. The withdrawal's
undo answers 0 for every count, dry run and apply, even when it restores the picks (compare the
proposals before and after instead). A set's undo leaves a retired food it renamed to free a name
under its new name, and the ingredients it created as soft-deleted rows. After the withdrawal every
withdrawn wording is a link-lane source again: expect `linkQueue` to rise to about 64,000 while the
link lane stays paused.

`POST names?apply=` lists curated foods whose name claims a colour, "whole" or "seed" their USDA
entry lacks while the entry names a form the name lacks, and renames those in the fixed table by
FDC id (170929 "Spices, mustard seed, ground": "Black Mustard Seed" to "Ground Mustard") through
the journaled rename, under one stamp; the same undo gives the name back.

## The lane

Lane `link-recheck` on the lane frame, one tier per set (`LinkRecheckHostedService`, off unless
`LinkRecheck:Enabled=true`; `LinkRecheck:BatchSize` 2000, `LinkRecheck:MaxPerDay` 2000 a set,
`LinkRecheck:IntervalHours` 24; switch `ai-pause-link-recheck`). Each round runs the sets in order
over the wordings not decided under the set's signature (`LinkRecheckRules.SetSignature`: the
re-check's version and tables, `LinkGate.Signature` and `AlcoholLineContext.Signature`) in the last
30 days, records every decision on the frame, and writes only while the tier is live. After a batch
the roster's auditor (`phi4`) reads a sample of the moves and take-backs blind with the same-food
judge (a move is wrong unless the line is its new food; a take-back is wrong when the line was its
old food), reader B (`mistral-nemo`) confirms a flag, and the frame evaluates the tier: qualify on a
98 % Wilson lower bound, pause when a full window of 300 shows more than 3 % confirmed wrong, or when
more than 5 % of a live batch's lines were no longer where it found them. A pause undoes the lane's
own runs since the last clean window (`LinkRecheckLaneUndo`; an operator's runs are left to their
stamp).

An operator's apply records its moves and take-backs on the frame too (mode live), so
`POST audit/{set}` can audit them. `AuditAndEvaluateAsync` evaluates a set with the committed
`scores/link-audit.json` (lane `link-audit`, scored by the wording readers' scorer, see
`wording-readers.md`): its known-bad and known-good flags are the audit check's score, and a record
that is missing, scored another same-food judge prompt (its `signature`), another auditor or reader B
than the set is audited with, or below its `model-lanes.json` floor (75 %), keeps the set in shadow. Until that record is committed the sets are applied by an operator, one dry run at a time,
and the lane decides in shadow.

## Status

`GET status` (and the Pipeline page block "Link re-check") shows whether the lane is on, the rules'
version and signature, how many model picks wait, the last withdrawal and the newest run of each set.
