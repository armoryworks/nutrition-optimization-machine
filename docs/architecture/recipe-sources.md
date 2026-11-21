# Recipe sources: discovery, checks and the approval queue

NOM imports recipes only from domains an admin has approved in `recipe.ScrapingSource`
(whitelist-only, see `docs/scraper-integration.md`). This page describes how NOM finds and
judges candidate sites, how an admin approves one, and how approved sources are watched. It is
the NOM side; the fetching is done by the operator's scraper service through its job routes.

The owner's rule (2026-11-20): the owner keeps the approval and takes responsibility only for
sites that are clearly usable without ambiguity, so plain that the approval is a rubber stamp.
So there is **no rule admission and no standing policy**: nothing is approved by code. The lane
finds candidates, records the facts that make a site clear, and offers only clear sites for a
one-click approval. A site that is not clear is never offered; it is listed apart with its
reasons.

## The flow

1. **Discovery** (`SourceAdmissionService.RunDiscoveryAsync`). Every `IntervalHours` the lane
   starts `POST /api/discover/jobs` with the `SeedsPerRun` approved web sources discovered
   longest ago (harvest not paused), and polls `GET /api/discover/jobs/{id}` every
   `PollSeconds` until the job ends. Each run is a row in `recipe.SourceDiscoveryRun`:
   - `completed` or `partial` (the budget ran out; what was found is kept);
   - `failed`: the job could not start, the scraper answered an error or no answer, the job
     read 404 (lost in a scraper restart), it reported `failed`, or it did not end by its
     deadline plus 60 s. A failed run keeps the candidates of the last poll and is shown as
     failed: a broken lane never reads as a lane with nothing to find;
   - `skipped`: another discovery job was running (429), or there is no seed.

   A run or check still marked `running` past its budget plus 60 s and a grace of
   `StaleGraceSeconds` (300 s) had its poll cut off (nom-api restarted, or the tick failed): the
   next tick stores the run as `failed` and the check as `lost`, so it is counted and shown with
   the failed ones.

   The seeds the scraper says it visited, and the seeds it refused (search engines, platforms),
   have `LastDiscoveredUtc` set, so a seed the budget left unread comes first next time and a
   refused seed does not hold a seed slot in every run. Refused seeds are listed on the run for an
   admin.
2. **Candidates** (`recipe.SourceCandidate`). Each domain with a recipe signal is kept once
   (without `www.`), with first and last seen and how often. A domain found but not probed is a
   `lead` and is never checked. A domain that is already a source (any status) is not a
   candidate; a domain an admin rejected is never offered again. A user's request for an unknown
   domain (the Pending row the scrape gate creates) becomes a candidate too.
3. **Checks** (`RunNextCheckAsync`). One `POST /api/source-check/jobs` at a time, sample size 20,
   budget `CheckBudgetSeconds`, at most `MaxChecksPerDay` in any 24 hours (failed ones count).
   Order: approved web sources due for their re-check, then user requests, then new candidates
   by how often they were seen. A candidate is checked again when its last check was under other
   rules, did not finish, or was clear and older than `RecheckDays`. A source or candidate whose
   check failed or was lost is not taken again for `FailedRetryHours` (24 h), so one site that
   always fails cannot use the whole daily cap. Each check is a row in
   `recipe.SourceCheck` with the scraper's report stored whole.
4. **The single verdict** (`SourceAdmissionPolicy`, version `source-admission/1`, a pure function,
   no model). Clear only when all of these hold:
   - the report is complete and under rule version `source-check/2`;
   - the scraper's own verdict is clear and every one of its reasons (`check-complete`, `https`,
     `bot-challenge`, `robots-nombot`, `robots-general`, `robots-ai`, `crawl-delay`,
     `reservation`, `terms`, `licence`, `credits`, `enumerable`, `structured-data`,
     `free-to-read`, `language`, `sample-yield`) is present and `pass` or `info`;
   - the licence kind is one that allows commercial reuse and adaptation with at most
     attribution: `cc0`, `public-domain-mark`, `public-domain`, `cc-by`;
   - the sample was the full 20 pages;
   - NOM's own vetting (`RecipeVettingService`) passes at least 18 of the sample recipes, and at
     least 85 % of their ingredient lines carry a quantity or need none ("salt to taste", a
     heading);
   - no admin rejected the domain before.

   Anything missing or unreadable counts against. A check that did not finish, or came under
   unknown rules, is **inconclusive**: shown as not clear, and it changes nothing that is
   harvested. The golden set `sources.tsv` holds the rule's cases (see `golden-sets.md`).
5. **The approval queue** (Admin → Scraping Sources, `GET /api/ScrapingSource/review`). Only
   candidates whose last check is clear, conclusive, under the current rules and at most
   `ClearValidDays` old. Each shows the facts that make it plain: the licence kind and basis,
   each licence statement quoted with the page it was found on, the terms pages read and any
   prohibition, robots.txt for all crawlers, NomBot and the AI crawlers, the crawl-delay, and the
   sample yield. One approve button: `POST /api/ScrapingSource/candidates/{id}/approve` refuses
   (409) anything not clear, and a domain an admin rejected. It is the only way the API approves
   a web source: there is no approve route for a source row, so a user's request is approved
   only after its check comes back clear. The source starts in probation.
6. **Probation** (`WatchAsync`). For `ProbationDays` days or until `ProbationRecipes` recipes,
   the harvestable list gives the harvester a remaining recipe limit. Then the live figures are
   read from the database: at most 10 % of the recipes in Requires Revision and at most 20 % of
   the lines without a quantity they need. Passing ends the probation; failing (or no recipe at
   all) ends it and pauses harvesting, journaled and shown.
7. **Re-judging and the weekly re-check.** Every approved web source is checked by the same rule
   every `RecheckDays`, and the verdict is shown beside it with its reasons. Harvesting is paused
   by the lane only when a conclusive verdict **turns** from clear to not clear. A first verdict
   that is not clear (today's sources) only shows: what happens to a source that is not clear and
   to its recipes is the owner's decision, and the lane assumes neither answer.
   The staged import enforces both pauses and the probation limit itself: it refuses a recipe
   from a source whose harvesting is paused, and from a source in probation that already holds
   its limit, whatever list the harvester read.
8. **The harvestable list** (`GET /api/ScrapingSource/harvestable`, `CanManageCuration`, the
   harvester's API key). Approved web sources whose harvesting is not paused and whose recipes
   are not withheld, each with its probation limit and the crawl-delay of its last check. The
   external harvester reads this list instead of keeping its own.

## Admin actions, journal and undo

Every change of a source's state is a row in `audit.AuditLogEntry` (`EntityType`
`ScrapingSource`, see `Support/SourceJournal.cs`), with the field, its old and new state and a
detail: `SourceApprove`, `SourceReject`, `SourceHandRecord`, `SourceHarvestPause`,
`SourceHarvestResume`, `SourceWithhold`, `SourceRelease`, `SourceRecheck` (a verdict change),
`SourceProbationPass`, `SourceProbationFail` and `SourceUndo`.

- **Pause harvesting for this source** (`POST /api/ScrapingSource/{id}/pause-harvest`) and
  resume (`/resume-harvest`): the harvestable list follows at once; recipes already imported
  stay.
- **Withhold this source's recipes** (`POST /api/ScrapingSource/{id}/withhold-recipes`, a dry run
  with the count unless `apply=true`): every recipe whose URL is on the source's domain (with or
  without `www.`) gets `Recipe.SourceWithheldById`, and the single visibility rule
  (`RecipeVisibilityExtensions.VisibleTo`) keeps it from everyone: search, suggestions, meal
  plans, its author included. Household cookbooks leave it out, and a meal plan that already
  holds it keeps the meal by name with no description. A recipe imported later from a withheld
  source is withheld too.
  Release (`/release-recipes`) clears exactly the recipes that withholding holds.
- **Undo** (`POST /api/ScrapingSource/journal/{entryId}/undo`): an approval, a pause or resume, a
  withholding or release, or a hand source's licence facts is undone while the source still holds the entry's new state; the undo
  is journaled and can itself be undone. Probation results and verdict changes are records, not
  actions, and are not undone.

These two actions make either answer to the owner's open question one click per source. The
lane never removes a source or a recipe on its own.

## Hand sources

PDFs and scanned books (the VA, NHLBI, NCI and Library of Congress candidates of the 2026-11-20
research) cannot pass the check: they carry no schema.org Recipe and often no machine-readable
licence. An admin records them as **hand sources** (`POST /api/ScrapingSource/hand`, Kind
`hand`): the domain where the statement was found, a title, the licence kind (one of the clear
kinds above), the statement as the source words it, and its URL. A hand source is approved by
being recorded, is journaled, is never fetched and is never on the harvestable list. Its licence
facts are journaled as their own row (field `Licence`, the old kind, statement, URL and who
entered them), so recording a hand source again over a mistake is undone with the facts it had.

## Staged import gate

With `SourceDiscovery:StagedImportGate` on (the shipped default), `POST /api/StagedImport`
takes a recipe only from an approved source: its URL's domain, or, for a recipe with no URL, the
source the batch names with `sourceId` (a hand source). Refused recipes are counted (`refused`)
and listed. The licence comes from the source's record (`PublicDomain` only for `public-domain`,
`cc0` or `public-domain-mark`); the caller's `publicDomain` flag is not read
(`publicDomainFlagIgnored`). A named source never covers a URL on another domain. A source whose
harvesting is paused takes nothing, and a source in probation takes no more recipes than its
probation limit.

## Settings (`SourceDiscovery`)

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | false | discovery, checks and the watch; off, the lane fetches nothing |
| `IntervalHours` | 24 | between discovery runs |
| `SeedsPerRun` | 3 | seeds per run, least recently discovered first |
| `MaxCandidatesPerRun` | 10 | the job's `maxCandidates` |
| `BudgetSeconds` | 600 | a discovery job's budget |
| `CheckBudgetSeconds` | 1200 | a source check's budget (covers crawl-delays up to about 35 s) |
| `MaxChecksPerDay` | 10 | checks started in any 24 hours |
| `RecheckDays` | 7 | re-check of approved sources and of clear candidates |
| `ClearValidDays` | 30 | the oldest clear check an approval may rest on |
| `PollSeconds` | 15 | between job polls |
| `TickMinutes` | 30 | between lane ticks (one check per tick) |
| `ProbationDays`, `ProbationRecipes` | 21, 50 | probation length and recipe limit |
| `StagedImportGate` | true | the staged import gate above |

`SourceDiscovery:AutoApprove` is no longer read (a warning is logged if it is set).

## Where it shows

- Admin → Scraping Sources: the queue, the approved sources with their re-check, the candidates
  that are not clear with their reasons, user requests, hand sources, discovery runs and failed
  checks, and the journal with its undo buttons.
- Admin → Pipeline, "Recipe sources": the lane's state, failed runs in a row and the last good
  run, the queue and the approved sources by verdict, paused, withheld, in probation and
  harvestable counts, the checks today against the cap and the failed checks of the week.

## Not built here

- Copy-site detection (design Q3) and the "lines on a curated food" probation figure: both depend
  on other lanes' state and would not make a verdict plainer.
- The harvester itself lives outside the repository; it must read the harvestable list and honour
  each source's recipe limit and crawl-delay.
