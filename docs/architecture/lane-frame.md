# Lane frame: shadow, qualify, pause

Every automated lane that writes runs in the same frame. The frame decides whether a lane may
write, measures it from an audited sample, pauses it when the figure drops, and undoes the lane's
writes since its last clean window. It is built once, in Nom.Orch, and the lanes are rebuilt on it
(wording tiers and readers, applied-link re-check, prose rewrite v2, reprocess, containers,
source admission).

The frame is in place; no lane runs on it yet. Lanes that exist today (link, prose rewrite, repair,
line classify, dish grouping, tool tagging) keep their own switches until their branch moves them.

## Modes

`curation."LaneState"` holds one row per lane and tier (`Tier = '*'` is the whole lane).

| Mode | The lane | Leaves it when |
|---|---|---|
| `shadow` (also: no row) | decides and records every decision, writes nothing | its audited sample qualifies (`LaneQualify`) |
| `live` | writes, records every decision that writes with `Written = true` | a pause trigger fires (`LanePause`), or its signature, golden record or audit-check score goes (`LaneShadow`) |
| `paused` | decides in shadow (probation), writes nothing | a sample decided after the pause qualifies again (`LaneResume`); with `ResumeNeedsNewSignature`, only under a new signature |

A lane asks `AiLaneGate` before each write batch:

```csharp
var mode = await gate.WriteModeAsync(lane, tier, ct);
```

That is paused while `ai-pause-all` or `ai-pause-{lane}` is on, or while the lane's or the tier's
row is paused; otherwise the tier's mode; shadow when there is no row or the state cannot be read.
The state is in the database, so a pause holds across a restart. Nothing goes live by a deploy:
a new lane has no row and starts in shadow.

## Decisions and the audit

`curation."LaneDecision"` holds every decision: lane, tier, subject (`wording:123`), the answer,
the mode, whether it was written, the signature, a stratum, the signals and verdicts as `jsonb`,
and the audit outcome. `ILaneFrameService.RecordAsync` stores one. `Writes` says whether the
decision is one the lane writes (a pick or a rewrite, not an abstention or a no-op) in any mode;
`Written` says it was written, which only a live lane does. Only decisions that write are audited
and counted, so the sample a shadow lane qualifies on is the same kind of decision it writes live.

`ILaneFrameService.AuditAsync` reads a stratified random sample of a tier's unaudited decisions
that write:
the larger of `LaneSamplePlan.Minimum` (default 30) and `Rate` (default 5 %), each stratum in
proportion. The lane supplies two checks:

- `ILaneAuditor`: the auditor model, of a family neither reader uses.
- `ILaneAuditCheck`: a second, independent check of a flagged decision (reader B re-reading it
  blind, or a rule audit).

| Auditor | Second check | Outcome | Counts as |
|---|---|---|---|
| right | not asked | `pass` | audited, right |
| wrong | agrees | `error` | audited, wrong |
| wrong | disagrees | `flagged` | audited, right |
| no answer, or wrong and the check cannot tell | | `unreadable` | not audited |

## Qualify and pause (`LaneQualifier`)

A lane hands its bars as a `LanePolicy`:

| Field | Meaning |
|---|---|
| `QualifyBar` | Wilson 95 % lower bound the corrected precision must reach to go live |
| `PauseBar` | error rate a live lane's observed error over a full rolling window may not exceed |
| `PauseWindow` | the rolling window, in audited writes (default 200) |
| `MinimumAudited` | a floor on the qualifying sample, above what the bar needs |
| `ResumeNeedsNewSignature` | a paused lane resumes only under another signature (links) |

A policy whose pause bar is not above `1 − QualifyBar` is refused: a lane that just qualified would
pause at once.

**Minimum sample.** A bar needs a sample even with no error. Without correction:

| Qualify bar | Audited needed with 0 errors | Errors allowed at 300 |
|---|---|---|
| 99.0 % | 381 | none (300/300 gives 98.7 %) |
| 98.0 % | 189 | 1 |
| 97.0 % | 125 | 3 |

A tier stays in shadow until it has the sample its bar needs (`NeededAudited` on the Pipeline page).
The pause window grows the same way: a window too small for a clean run to bound the error under the
pause bar is widened to the smallest that can (T1 at 1.5 % reads at least 253, not 200), so a full
window holds enough audits to tell a lane at its bar from one above it.

**Corrected error (Rogan-Gladen).** The combined check (auditor flag plus second check) is scored
on golden known-bad and known-good rows: its sensitivity `Se` and false-flag rate `f`. The lane
passes the score as `AuditorScore(KnownBad, FlaggedBad, KnownGood, FlaggedGood, Source)`. The
corrected error is `(p − f) / (Se − f)`. Its bounds use the Wilson bounds of `p`, `Se` and `f` that
make the lane look worse, and the upper bound never credits more false flags than errors were
confirmed (`f` there is at most `p`), so a weak, noisy or barely scored check needs a larger sample
than an exact one: with no confirmed error the upper bound is `upper(p) / lower(Se)`. A rule audit with
no sampling error passes `AuditorScore.ExactRule` and is not corrected. Without a usable score the
lane cannot qualify, and a live lane goes back to shadow.

**Qualify.** Shadow to live when the audited sample (current signature, since the window start)
is at least the sample needed and `1 − upper(corrected error) ≥ QualifyBar`, and the lane reports
no golden problem and no fault (`PauseReasons`); a shadow lane with a fault stays in shadow.

**Pause.** Live to paused when the lane reports a fault (`PauseReasons`: auditor unreachable, a
canary false accept, rule-audit failures), or when, over the last `PauseWindow` audited writes:

- the window is full and its observed corrected error is above `PauseBar`; or
- the window is still filling and the lower bound of the error is above `PauseBar` (a clear fault,
  early).

The observed corrected error (`ErrorEstimate.Pausing`, `WindowError` on the page) is the confirmed
share for an exact check. For a sampled check it is the confirmed share, less no more false flags
than the check's lower false-flag bound and no more than were confirmed, divided by the check's lower
sensitivity bound: never kinder to the lane than the check can vouch for.

A full window that passes marks the newest decision in it as the last clean point (`LastCleanAt`).

This is the rule the lead set on 2026-11-20 (LQ-4) from the owner's direction that automation must
work: a live tier pauses on the observed error of a full window, not on its upper bound. The
upper-bound rule asked for proof that the error was under the bar, so a lane running at its target
paused in most windows (one error in T1's 253 paused it, about 72 % of windows at 99.5 % precision,
and with `ResumeNeedsNewSignature` such a tier could not come back without a new signature). Under
the observed rule, with an exact check, T1 (1.5 % over 253) pauses at 4 or more errors: about 4 % of
windows at its 99.5 % target and about 95 % of windows at 3 % error; T2 (3 % over 200) pauses at 7
or more. Qualifying still needs the Wilson lower bound, so a lane goes live only on proof and stops
when it is seen to fail.

**Self-undo.** On a pause the frame calls the lane's `ILaneUndo.UndoSinceAsync(tier, LastCleanAt)`,
first as a dry run (logged and journaled), then applied (journaled). The undo writes its own journal
in the lane's domain, so it can itself be undone. A lane with no undo registered is paused and
journaled with "no undo registered"; a failed undo leaves the lane paused and journals the error.

**Resume.** A pause resets the window start. Probation is the lane deciding in shadow after the
pause; it resumes when that sample qualifies as above. It never resumes by waiting.

**Signature.** A lane passes a hash of what decides (models, prompt versions, rules version). On a
changed signature the frame first reads the live lane's window under the old signature: if it fails
the pause test the lane pauses and undoes as above (paused under the old signature); otherwise it
goes back to shadow without an undo. After that, decisions under the old signature no longer count.

## Journal

`audit."AuditLogEntry"`, `EntityType = 'LaneState'`, `EntityId` = the state row,
`PropertyName = '{lane}/{tier}'`:

| ChangeType | When | OldValue / NewValue |
|---|---|---|
| `LaneQualify` | shadow to live | old mode and reason / new mode, reason, figures |
| `LanePause` | live to paused | as above, with the trigger |
| `LaneResume` | paused to live | as above |
| `LaneShadow` | live to shadow without a fault | as above |
| `LaneUndo` | each self-undo step, and an operator undo applied | – / applied, since, undone, skipped, remaining, note |

## Model roster

Settings `Ai:Roster` (environment `Ai__Roster__ReaderA` and so on):

| Role | Setting | Default | Licence |
|---|---|---|---|
| Reader A, prose writer, picker | `ReaderA` | `qwen2.5:14b-instruct` | Apache-2.0 |
| Reader B, judge of every prose rewrite, line judge | `ReaderB` | `mistral-nemo` | Apache-2.0 |
| Auditor (samples only) | `Auditor` | `phi4` | MIT |

The API refuses to start when reader B is of reader A's family, when the auditor is of either
reader's family, or when a role's family is not one known to publish OSI-licensed models (qwen,
mistral, phi, granite, olmo). The family is the tag's leading letters (`qwen2.5:7b-instruct` is
qwen, `mistral-nemo` mistral, `phi4` phi), with known offshoots mapped (`mixtral` → mistral; `qwq` →
qwen); a tag that names Llama or Gemma anywhere (`Meta-Llama-3.1-8B`, `dolphin-llama3`) is of that
family and refused. Fine-tunes under other names (`hermes3`, `nemotron`) fall outside the list and
are refused too. The check is by family: a family on the list may still publish a model under
another licence (Mistral's `codestral`), so the final model inside each family is chosen by golden
score and its own licence checked.

## Pipeline page

`GET /api/Pipeline/status` returns `frame`: every row with its mode, reason, since, figures
(audited and needed, confirmed errors, unconfirmed flags, corrected error, precision bounds against
the bar, the rolling window's observed error against the pause bar with its bounds, the audit check's
own scores), the
decisions of the last 24 hours, and the roster. Admin → Pipeline shows it under "Lane
qualification" (`data-testid="pipeline-frame"`, one item `pipeline-frame-{lane}-{tier|all}`).
Scores from agent-judged golden rows are labelled as agreement with the reviewer model.

`POST /api/Pipeline/frame/{lane}/{tier}/undo?apply=false` is the undo of a tier's writes since its
last clean window: a dry run for a curation admin (the page's button), applied only by a user-roles
admin and journaled as `LaneUndo`.

## Building a lane on the frame

1. Register an `ILaneUndo` for the lane (`services.AddScoped<ILaneUndo, MyLaneUndo>()`).
2. Before each batch: `WaitAsync(lane)` as today, then `WriteModeAsync(lane, tier)`.
3. Decide every item; write only when live; `RecordAsync` each decision with the mode and signature.
4. Daily or per batch: `AuditAsync` with the auditor and second check; then `EvaluateAsync` with the
   policy, the check's golden score, the golden problem (null when the record for this signature is
   current and meets its floor) and any fault.
