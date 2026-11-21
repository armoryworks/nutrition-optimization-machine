# Wording readers: two blind models on the lane frame

The second half of the one decision pipeline (plan C1 step 3; wordings design 2.1 to 2.6 and 3.1).
The deterministic tiers (`wording-tiers.md`) answer what a rule can; the readers take the in-use
uncurated wordings the tiers left and have two models of different families read each one blind.
A model only classifies: it chooses one of the catalog's foods by number, says the catalog has none,
says the wording names no single food, proposes a synonym, or gives one of a few fixed readings of an
unclear alcohol word. It never supplies a number. Every food answer passes the one link gate
(`link-gate.md`), and a row is written only while its tier is live on the lane frame
(`lane-frame.md`), through the verified wording loader with source `ai:readers:<hash>` and an evidence
key, so one per-wording retract takes it back (`food-catalog-ingestion.md`, verified wording table).

Code: `Nom.Orch/Services/Support/WordingReaders.cs` (pack, outcome table, nutrition-equivalent rule,
unclear rules, lexicon form; pure), `AlcoholFixedReading.cs` (the fixed readings; pure),
`WordingReaderRecords.cs` (the golden records at run time), `Nom.Orch/Services/OllamaWordingReader.cs`
(one reader), `OllamaWordingReaders.cs` (the roster's readers, sentinel and audit),
`WordingReaderLaneService.cs` (the lane and its undo), `Nom.Api/Services/WordingReaderHostedService.cs`,
`Nom.Data/Recipe/WordingLexiconEntity.cs` (`recipe."WordingLexicon"`).

## Which wordings

A wording is read when all hold:

1. It is an in-use uncurated wording in key form with no live row in the verified wording table (the
   tiers' own selection, `WordingTierLaneService.WaitingQuery`).
2. The tier lane decided it under its current signatures at its present line band and no tier answer
   writes it: no tier answered, or every answer was held by the gate, the family check or the rejection
   memory. A capped tier decision is not a decision yet. So the two lanes never write the same wording.
3. This lane has not decided it under its current signature at that band. An unreadable or capped
   decision is asked again after 24 hours; a new signature (rules, prompts or any of the four models)
   asks everything again.
4. It is not a compound (`LinkAutoApplyGuard.IsCompound`): those are recorded with no model call and
   left to the compound line rules, as the tiers leave them.

Order: the recipes the wording alone keeps from being fully linked (no other ingredient line of the
recipe on an uncurated food; counted once and kept for six hours), then lines.

## The evidence pack

Built once per wording, deterministically:

- up to 40 lines from different recipes (3 for a wording with fewer than 3 lines), each with its
  recipe's title and whether the recipe came from a web site or an old book;
- the share of lines in each unit class (spoon, volume, mass, count, container, none);
- up to 12 candidates (8 for a small pack): the food a staple or name rule gives the wording, the owner
  of a trusted alias of it, the name matcher's shortlist (kept to what the wording's alcohol sense
  allows), the shortlists of its hyphen and space spellings, and the shortlist of what a lexicon word
  of it means; never a prepared or branded food. Each with its name, USDA description, USDA category,
  kcal per 100 g and nutrient rows.

The pending model pick is not shown. Each reader sees the candidates in its own order (a shuffle
seeded by the wording and the reader's role, so a rerun repeats it). Full packs go one to a call;
small packs go six to a call, so the long single-line tail costs about a sixth of a call per wording
per reader. All of reader A's calls run before any of reader B's, as the host keeps one model loaded.

## The readers

Reader A is the roster's `ReaderA` (`qwen2.5:14b-instruct`), reader B its `ReaderB` (`mistral-nemo`);
the roster refuses two readers of one family at start-up. `OllamaWordingReader`: temperature 0, seed
20261120, a context of 8,192 tokens, a JSON schema. Each answer is `food` with the number of a
candidate, `none`, or `unclear`, with a short reason and, when the wording's word is an old, regional
or brand name of a common food, what it means in a USDA description's words. An entry whose number,
verdict or pick cannot be read is left out, never guessed. Prompt version 1; `OllamaWordingReader.Signature`
hashes the instructions, verdicts, fixed readings and request options.

## The outcome table

| A | B | Result |
|---|---|---|
| food F | food F | to the gate |
| food F | food G, same lead word, same USDA category, energy within 10 %, protein, fat and carbohydrate each within 15 % of the larger (amounts under 1 g per 100 g count as equal) | nutrition-equivalent: the one with fewer description words the wording lacks (then more nutrient rows) goes to the gate |
| food F | anything else | no row (`readers disagree`) |
| none | none | a `none` row, unless a candidate passes the gate on its own (as a lone answer) |
| unclear | unclear | an `unclear` row only when a rule agrees: no unit class above 70 %, more than 20 % of the lines do not name the wording, or the wording is a bare class word (meat, fish, fruit, herbs, spices, stock, gravy, wine, fat and the like) |
| either missing | | no row (`unreadable`, asked again after 24 hours) |

A `none` or `unclear` row moves no line: it stops the link lane asking a model and feeds the catalog
worklist or the per-line lanes. Such a decision is recorded as not writing, so the auditor (which reads
a food) never samples it; the lane's undo still finds the row by its batch.

**The gate.** A food answer is checked by `LinkGate.Check` as two readers' answer (`ReadersAgreed`)
with up to 200 of the wording's lines (unit, grams through the food's conversions, the recipe's
context for the alcohol rules), then by the family check over the pass's answers and the table, and by
the rejection memory (a retracted pair stays out until the wording's lines double, the readers'
evidence key `wording-readers:lines-<n>`). A wording with more than 500 lines is written only when the
sentinel reads 20 of its lines that name it and finds none of them another food, variety or form
(wordings design check 9).

## The lexicon

`recipe."WordingLexicon"` (term, means, source, batch, note) holds synonyms a pair with no shared word
may rest on ("saleratus" means "baking soda"). An entry is a classification, never a number. It is
proposed for a wording of one word when both readers gave the same meaning blind (normalised, one to
four words, not the term itself); reader A's model then answers as the refuter ("give a reason this is
not the same food, or say none"). With no reason, the entry is recorded under frame tier `lexicon` and
written while that tier is live (journaled `WordingLexiconAdd`). The gate reads an entry in
`GateCatalog.Lexicon`: a term is met by a food that holds every word of what it means. A wording whose
meaning the lexicon took in the pass, and that the readers did not both give a food, is read again by
both with the foods its meaning names added to its candidates. A live read tier never rests on an entry
the lexicon tier has not written: while the lexicon tier is not live, a live read pass does not use the
pass's entries. A synonym the lexicon's undo took out is never proposed again.

The lexicon answers only the gate's identity rules (head noun, containment, lead noun). The state and
marked rules still read the wording's own words: "macaroni" means "pasta" passes identity but is held
by `state: dry` against "Pasta, dry, enriched", and "glucose" by `marked: light` against light corn
syrup (wording-lexicon.tsv, `GoldenWordingReaderTests`).

## The fixed readings of an unclear alcohol word

A wording whose lines share one of the senses the alcohol rules leave unclear (`wine-unclear`, bare
wine in an old text; `cider-unclear`, bare cider; `alcohol-unclear`, alcohol with no food context) is
read in the fixed-reading mode (alcohol design 2.3, plan A6), tier `alcohol`. The relink moves every
line that names the wording, so each reader reads every line of it (twelve to a call), each with its own
recipe's title, other lines and the start of its method, and gives one reading per line. A wording with
more than 200 lines, or whose lines are shared with another uncurated ingredient of the same name (the
relink would move those too, unread), is not read and gets no row (`not every line read`).

| Word | Readings | Sense | USDA food |
|---|---|---|---|
| wine | table, fortified, unsure | `wine-table`, `fortified` | 173185 table wine, 175112 dry dessert wine |
| cider | sweet, hard, unsure | `apple-juice`, `hard-cider` | 173933 apple juice, 174146 hard cider |
| alcohol | drunk, not-food, unsure | `spirit`, none | 174815 distilled spirit 80 proof, no row |

A row is written only when both readers give the same reading on every line, neither is unsure,
and no data rule contradicts it on any line (a fortified reading of bare wine in a recipe from a web
source; a sweet reading of a cider the recipe ferments, barrels, casks or works with yeast). The
reading maps to its fixed food and the gate reads the reading's sense in place of the line's own
(`GateLine.Sense`), so the alcohol rule allows that food and no other; for apple juice and the spirit,
whose USDA entries lack the word, the reading's words stand in for the gate's identity rules for that
pair alone.

## Sentinel and post-checks

After a live batch, the auditor's same-food judge (the sentinel, roster `Auditor`, `phi4`) reads up to
100 of the moved lines, at least one per wording, each against the food. A wording with two or more
lines read as another food, variety or form is retracted at once (`RetractAsync` with its evidence
key). Six or more wrong per 100 read (at least two), or more than three moved lines that weigh nothing
on their new food, are faults that pause the tier. With no sentinel model set, a live batch is a fault.
When the sentinel has retracted more than 4 % of the wordings a tier wrote live in the last 7 days (with
at least 50 written), the tier pauses (wordings design 3.2).

**Recovery.** The loader commits a batch's rows before the lane records them, relinks and runs the
sentinel. Each pass first looks for rows of `ai:readers:*` loaded in the last two days that no live
decision records: each is recorded now as written live (`recovered`) on its wording's present line
count, with that evidence key and the food's USDA description, so the auditor and the undo find it,
and its batch is relinked; a row of a paused tier is retracted with that evidence instead.

## On the lane frame

Lane `wording-readers` (`AiLanes.WordingReaders`, switch `ai-pause-wording-readers`), frame tiers:

| Tier | Writes | Qualify | Pause | Golden record |
|---|---|---|---|---|
| `read` | food rows (none and unclear rows ride along, not audited) | Wilson lower bound 98 % | above 3 % over a full window of 500 | `wording-read` |
| `lexicon` | lexicon entries | 97 % | above 4 % | `wording-lexicon` |
| `alcohol` | fixed-reading rows | 97 % | above 4 % over 200 | `alcohol-reading` |

**Audit.** The auditor (`phi4`) reads a sampled `read` or `lexicon` decision's pair (the wording, or
a lexicon term, against the food's USDA description, or what the term means) with the same-food judge.
A sampled `alcohol` decision keeps up to three of its lines with their recipes (`FixedSample`), and the
auditor reads them in the fixed-reading mode; it is wrong when the auditor reads any of them another
way. Reader A and reader B made every decision audited, so neither may confirm a flag (plan C0.2): the
auditor's flag stands alone (`AuditorFlagStands`), and the frame corrects the audited error with the
auditor's own golden score: for `read` and `lexicon` the auditor-alone counts of
`scores/link-audit.json` (`WordingTierAuditRecord.EvaluateAlone`), for `alcohol` the fixed-reading
auditor counts of `scores/alcohol-reading.json` (`WordingReaderRecords.AuditScore`). The wording tiers
and the link re-check keep the combined check (auditor confirmed by reader B), since reader B made none
of their decisions. A tier stays in shadow while its own record or the record its audit is corrected
with is missing, scored another prompt or rule set (the link-audit record carries the same-food
judge's signature), another reader A, reader B or auditor, lacks the counts it is corrected with, or
is below its floor in `model-lanes.json` (`WordingReaderRecords.Evaluate`, `WordingTierAuditRecord`).

Caps: `WordingReaders:MaxPerDay` (300 food rows per tier in 24 hours), `WordingReaders:MaxLinesPerDay`
(10,000 lines of the wordings written over all tiers in 24 hours; a wording whose lines exceed what is
left is recorded `capped`), `WordingReaders:LexiconMaxPerDay` (30). Off unless
`WordingReaders:Enabled=true`; `WordingReaders:BatchSize` (60).

**Undo.** `WordingReaderUndo` (the frame's self-undo, and `POST /api/Pipeline/frame/wording-readers/<tier>/undo`):
for `read` and `alcohol`, every row the lane loaded in a batch of that tier since a time (food, none and
unclear alike) is retracted with the evidence key its decision recorded, so its lines, retired
ingredients, withdrawn proposals and picks and import landings go back and the rejection memory holds
the pair; for `lexicon`, every entry written since then is soft-deleted and journaled
(`WordingLexiconUndo`), that synonym is not proposed again, and every `read` or `alcohol` food row
loaded after the entry for a wording that holds its term is retracted the same way, since the gate let
it through on that entry.

## Golden sets and scorer lanes

| Lane | Cases | Passes when | Floor |
|---|---|---|---|
| `wording-read` | every `=` and `!` row of `wording-pairs.tsv` and every judged pair of `link-pairs.tsv` whose wording the tiers leave | an `=` row's food is written, a `!` row's food is not | agreement 40 %, precision 97 %, false accepts 1 % |
| `wording-lexicon` | `wording-lexicon.tsv` (40 rows from the w01 pairs and old and British names) | an `=` row's entry means the judged food; a `!` row gets no such entry | 80 %, precision 97 %, false accepts 1 % |
| `alcohol-reading` | `alcohol-readings.tsv` (30 lines) | the reading written is the judged one; nothing for `unsure`; never the reading a rule row refuses (`!sweet`) | 97 %, false accepts 2 %; the auditor (`--audit-model`, the roster's) reads each line too, and its flags on the judged reading (known good) and the other reading (known bad) of each judged line are the alcohol tier's audit score |
| `link-audit` | 400 known-bad and 400 known-good pairs of `link-pairs.tsv`, by a stable hash of the id | a bad pair is flagged by both, a good one is not | 75 %; its combined flag counts are the audit check's score for the tiers and the re-check, its auditor-alone counts (`auditorKnownBad` and so on) the readers' |

All four are scored by an operator against the model host
(`dotnet run --project nom-api/Nom.Import -- --golden-score --lane <lane> --host <host> --model <reader A or auditor> --check-model <reader B> --record`);
the record names both models (`model`, `checkModel`) and, for `link-audit` and `alcohol-reading`, the
auditor scored alone (`auditModel`). None is required in `model-lanes.json` until it
is recorded; until then the tiers that need it stay in shadow. The fixtures carry no lines and no
nutrient amounts, so the `wording-read` scorer reads each wording on a pack without lines and never
finds two different foods nutrition-equivalent. The judged rows are model-reviewer judgements (status
`agent`): their figures are agreement with that reviewer.

## Not here

- A Pipeline page block of its own (wordings item 12): the frame block lists the lane's tiers with
  mode, figures, reason and undo.
- The reader-agreement drift check (wordings design 3.2): each batch journals both readers'
  agreement (`Agreed`, `BothRead`), but no golden agreement figure is recorded to compare with yet.
- The kcal-per-serving post-check (wordings design 2.6): moved lines that weigh nothing are checked;
  a recipe's energy change is not.
- The grams bound of the gate: the readers' gate catalog has no grams-per-line bounds (the tiers
  compute them from curated lines); the units rule still runs.
- Re-asking a wording when a new catalog food shares its lead word: re-asks follow a new signature or
  a doubled line count only, as for the tiers.
