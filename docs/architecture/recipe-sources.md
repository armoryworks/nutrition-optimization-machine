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
   - `skipped`: another discovery job was running (429), full checks are held, or there is no seed.

   Discovery reads recipe pages: pass 1 reads each seed's homepage, sitemap and 2 recipe pages, and pass 2
   probes the homepage of every site linked from them, and one recipe-looking page when that homepage shows
   no recipe signal. So it is held exactly like a full check. It runs only while `FullChecks` is on (off, the
   run is skipped with "Full checks are held ..."), and a seed is an approved web source that may be fetched
   (`SourceRightsService.MayFetch`: clear, a recorded permission, or facts-only with facts-only use approved by
   an admin). A facts-only class from a check alone, forbidden, permission-needed and unclassed sources never
   seed it. **What seeds it on a first enable**: nothing until the terms-only pass has been reviewed. The
   terms-only checks come first in the lane; the admin reads each class and its deciding quote, approves
   facts-only use of the sources that pass the review, and turns `FullChecks` on; from then on those sources
   seed it. Until then each run is skipped and says why ("N approved web source(s) have no class yet ...", "M
   are facts-only and wait for an admin to approve facts-only use"), so a waiting lane never reads as an
   empty one.

   **The exclusion list.** Every discovery job carries `excludeDomains` (`SourceDiscoveryExclusion`), so neither
   pass fetches a host NOM holds as:
   - **forbidden** or **permission-needed**: a source by its effective rights class (a recorded permission lifts
     it), a candidate by the class its last check's report gives it (the check lane's own bar);
   - **rejected**: a source or candidate an admin rejected;
   - **unclassed**: a candidate whose last check gave no class (a new candidate before its terms-only check, a
     lead, a check that did not finish), and a source with no class (an approved web source before its terms-only
     check, a user's pending request, a hand source).
   - a class **recorded by hand** (`source-rights.md`): its forbidden or permission-needed host, as a source, as a
     candidate (whose class it is, before its report's) and as a host NOM has no row for; a source it bars is held
     with the rights gate on or off and is never a seed;
   - a site that **refused** an earlier run (below), as permission-needed, until an admin has looked at it.

   A source that may be fetched is never held, and a host whose terms allow automated access (facts-only or clear,
   an approval for facts-only use or not) is not held: its homepage may be probed, as a full check may read it.
   Which sources may be fetched follows `SourceRights:Enforce` as seeding does: on, those `MayFetch` allows; off,
   every approved web source (each one seeds and is harvested then). Every other source is held by its class
   whether the gate is on or off, so a pending or rejected source that is forbidden, permission-needed or unclassed
   is always on the list; a candidate's reasons always hold. An unclassed or rejected candidate of the host, or of
   a parent host, of an approved source that may be fetched gives way to that source (its own check and the
   admin's later approval decide): it is not sent and the run names it, so a lead or an old rejection never holds
   an approved seed back. A candidate whose report makes it forbidden or permission-needed never gives way. The rows of one host
   (`budgetbytes.com`, `www.budgetbytes.com`) are one entry under the strongest reason, an entry excludes every
   subdomain, and a host under a barred parent is not sent twice. Entries are sent as the scraper reads them (lower
   case, no `www.`, international names in punycode); a held domain that is not a host name is named on the run.

   A site a run finds is a new candidate with no class, so the next runs hold it until its terms-only check
   classes it; a lead stays a lead (it is never checked, see 2) and is no longer probed again. The scraper takes
   at most **5,000** entries. Past that, every forbidden and permission-needed host is still sent, first, and the
   rejected and unclassed hosts fill the room left. They stand in the order NOM first held their rows (a source's
   creation, a candidate's first sighting), then by name, and each run goes on from the host after the last one the
   previous such run sent (`ExcludeCursorSince`, `ExcludeCursorHost`), round the list. A newly held host joins at
   the end, so every held host is sent within a bounded number of runs however many new hosts come in. The run
   stores how many were sent and left out and says so, and a warning is logged. When the forbidden and
   permission-needed hosts fill all 5,000 entries exactly, there is no room and the others are left out (shown on
   every run) until there is. When the forbidden and permission-needed hosts alone pass 5,000, no job starts:
   the run is **failed** with the count, and an error is logged; none is ever dropped to make the list fit.

   What comes back: `candidatesExcluded` (the excluded hosts the seeds link to, with the seed and the entry that
   excluded each; no request was made) is stored on the run (`CandidatesExcluded`, `CandidatesExcludedJson`) for
   the admin and never makes or counts a candidate. A seed NOM also holds is a fault in NOM's rows: it is held
   back before the job (another row of the host, or of a parent host, holds it), and a seed the scraper still
   reports in `seedsExcluded` is a NOM fault too; both are logged as errors, shown on the run (`SeedsExcluded`,
   `ExcludeNote`) and not rotated. A host on the list that the scraper returns as found anyway (a scraper from
   before `excludeDomains`) is never kept as a candidate, and the run says the scraper did not apply the list.

   **Refusing hosts.** The scraper stops asking a site that refuses a run (a bot challenge or a 429 on any page, a
   401, 402 or 403 on its robots.txt or homepage) and reports it in `refusingHosts` (`domain`, `url`, `httpStatus`,
   `botChallenge`). NOM stores the list on the run (`RefusingHosts`, `RefusingHostsJson`) and each site in
   `recipe.SourceRefusingHost` (first and last refusal, how often, the last page and status). A site there is held
   from every later run, on the exclusion list as permission-needed and never sent as a seed (an approved seed
   that refused is held back with a note on the run), until an admin has looked at it: "Looked at: release" on
   Admin → Scraping Sources (`POST api/ScrapingSource/refusing/{id}/release`, who and when kept, journaled as
   `SourceRefusingRelease`, shown in the sources journal and undone there while it is the site's latest row and no
   later refusal has held it again: the undo holds the site again). A refusal reported after that holds it again. A refusal makes no candidate, class or
   check.

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
3. **Checks** (`RunNextCheckAsync`). One `POST /api/source-check/jobs` at a time, budget
   `CheckBudgetSeconds`, at most `MaxChecksPerDay` in any 24 hours (failed ones count, and so do the lane's
   own terms-only checks). Each check is one of two kinds, stored on the row (`SourceCheck.Mode`):
   - **terms-only** (`sampleSize` 0): robots.txt, the homepage and the terms pages, no recipe page;
   - **full** (`sampleSize` 20): the same and a sitemap and 20 sample recipe pages.

   **The first check of any source or candidate is terms-only**, whatever it holds: a site whose own words
   forbid automated collection, or that refuses NomBot, must never have a recipe page requested because NOM
   had not read it yet. A full check is asked only when a written permission is recorded or the latest
   terms-only report of that source (or candidate) allows one (`SourceRightsRule.AllowsFullCheck`: a
   terms-only report under a rule version NOM reads that classes the site facts-only or clear, so nothing in
   its words forbids automated access or reuse, nothing refused NomBot and no terms page was left unread),
   and only while `FullChecks` is on (off by default). For a **source** the setting alone never starts one:
   it is checked in full only when a written permission is recorded, when an admin has approved facts-only
   use of it (one journaled click on Admin → Scraping Sources, undone from the journal), or, for a clear
   source an admin admitted from a candidate's clear full check, its weekly re-check
   (`SourceAdmissionService.FullCheckApproved`). A clear class from a terms-only report approves nothing, nor
   does a full check that once ran without review; a source whose terms-only report allows a full check but that
   no admin approved waits, and nothing checks it again by itself until then. A **candidate** is checked in
   full on its terms-only report alone while `FullChecks` is on (a clear full check is what puts it in the
   approval queue). A terms-only report that allows no full check holds the target
   until `RecheckDays` have passed (a bar is held by the class it derived: forbidden and permission-needed
   targets are never due; an inconclusive one, such as a host with no address, is tried again by time).
   Without the scraper's terms-only mode (`TermsOnlyChecks` off) no source or candidate without a passing
   terms-only report is checked at all. A terms-only check stores the rights class and AI flag of an
   approved source (never replacing a clear class it cannot see the licence of), and marks a candidate not
   clear only when it found a bar; it gives no admission verdict and does not stamp the weekly re-check
   date. If the scraper answers a terms-only request with recipe pages read, the check is stored as failed
   without its report, so nothing (the stale re-derive of a source with no class included) can class the
   site from it. Whether a check was full is what NOM asked for (`SourceCheck.Mode`), not the mode its report
   names: a full check answered under a mode NOM does not know still counts as the target's full check, so
   the target is not due again on the next tick.

   Order: a terms-only check an admin asked for (below), then approved web sources due for their
   re-check (those due a terms-only check before those due a full one), then user requests, then new
   candidates by how often they were seen, then **leads**. A lead is a site discovery found and never probed;
   discovery holds it as unclassed and never probes it again, so a lead discovery holds (the same test as
   the exclusion list: by the class of its last check's report, and not giving way to an approved source
   that may be fetched) gets a terms-only check (robots.txt, the homepage and the terms pages, no recipe page) within
   `MaxChecksPerDay`, after everything else. It is never checked in full. A lead whose report bars it becomes
   not clear and stays held; a lead whose terms allow automated access is no longer held, and a later
   discovery run may probe it and make it a new candidate, and it is no longer counted as held. A lead of a
   host an approved source that may be fetched covers is not checked (it gives way to that source, as in the
   exclusion list); one covered only by a source that may not be fetched is held, so it is checked. A source is due when it was never checked in full or its last
   full check is older than `RecheckDays`. A candidate is due when it has no full check, when the policy
   version changed since its last full check (once: the next check stores the current one), or when that
   check did not finish or was clear and is older than `RecheckDays`. The rule version a report came back
   under never makes a target due again: a scraper newer or older than NOM expects cannot make the lane
   check the same site on every tick. A source whose effective rights class is forbidden or permission-needed,
   and a candidate whose last report classes it so (`source-rights.md`), is never due: no automatic check
   fetches it, and the admin page says so beside the source. A source or candidate whose
   check failed or was lost is not taken again for `FailedRetryHours` (24 h), so one site that
   always fails cannot use the whole daily cap. Each check is a row in
   `recipe.SourceCheck` with the scraper's report stored whole.
4. **The single verdict** (`SourceAdmissionPolicy`, version `source-admission/4`, a pure function,
   no model). Clear only when all of these hold:
   - the report is complete and under rule version `source-check/6` (stored `source-check/5`, `source-check/4`
     and `source-check/2` reports are still read; an approval needs a check under `source-check/6`;
     `source-admission/4` made `source-check/6` current, so a `/6` report that was inconclusive as newer is read,
     and every candidate's last full check is due once again under the new policy version). A report under
     a newer `source-check/N` is read on the reasons NOM knows, flagged "newer check version" on the admin page,
     never clear and inconclusive until NOM reads that version; any other version is not read. Reasons are
     read by their code, never by their place; a reason NOM does not know, and an outcome it does not know
     (also `info` on a reason other than `crawl-delay` and `sample-yield`, and a terms-only `not-checked`),
     is unknown, never a pass;
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
   harvested. The golden set `sources.tsv` holds the rule's cases, and `source-check-6.tsv` replays what
   `source-check/6` changes on stored reports (see `golden-sets.md`).
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
   every `RecheckDays`, and the verdict is shown beside it with its reasons, except a source whose rights
   class is forbidden or permission-needed: the weekly re-check skips it and says so. For such a source, and
   for one with no class yet, an admin may start a **terms-only check** (`POST /api/ScrapingSource/{id}/terms-check`): the scraper's
   `sampleSize` 0 mode reads robots.txt, the homepage and the terms pages and no recipe page; it is queued,
   journaled at both ends (`SourceTermsCheck`, a check lost to a restart included), run by the next tick
   outside the daily cap (not counted in it), and re-derives
   only the rights class and AI flag (`source-rights.md`). It is refused while `TermsOnlyChecks` is off. Harvesting is paused
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

## First enable with the exclusion list (`source-check/6`)

Staging first, then production in the same order.

1. **Deploy the scraper first.** The reference scraper that takes `excludeDomains` reports `source-check/6`. A NOM
   that reads `/6` reads its stored `/5` reports as before, and an older NOM reads `/6` as a newer version (never
   clear, nothing changes), so the scraper may go first; NOM must not start discovery against a scraper without
   `excludeDomains`, because such a scraper probes the hosts NOM excludes (the run then says "did not apply
   excludeDomains" and keeps none of them).
2. **Schema.** `./db/apply.sh --dry-run` shows eight new columns on `recipe.SourceDiscoveryRun` (`ExcludeSent`,
   `ExcludeLeftOut`, `CandidatesExcluded` with default 0, `ExcludeCursorSince`, `ExcludeCursorHost`,
   `SeedsExcluded`, `CandidatesExcludedJson`, `ExcludeNote`) and nothing else from this change; then apply and deploy nom-api and nom-ui.
3. **Version changes on the first tick.** `source-rights/4`: the tick re-derives every stored class from its stored
   report (no class of the 23 stored `/5` reports changes; `source-check-5.tsv` and `source-check-6.tsv` hold
   them). `source-admission/4`: every candidate's last full check is due once again, inside `MaxChecksPerDay`.
4. **Terms-only pass and review** as before (`FullChecks` off, the default): discovery runs are skipped and send
   nothing. Each terms-only check that classes a source facts-only withholds that source's prose in the same
   tick (see "Facts-only withhold" below); Admin → Scraping Sources shows before the check how many recipes
   (and approved ones) a facts-only class would withhold, and after it how many its class withholds.
5. **Open full checks** (`FullChecks` on, and facts-only use approved per source that passed the review: a
   source with no approval gets no full check and does not seed discovery). On the first discovery run after it, Admin → Scraping Sources shows the
   run with "excluded N held hosts". Check that N is the number of forbidden, permission-needed, rejected and
   unclassed hosts (sources and candidates, one per host, less the candidates the note names as giving way to an
   approved source that may be fetched), that the run carries no other exclude note, and that the scraper's log
   shows no request to any of the hosts sent. A note that names a NOM fault (a seed also held) means two rows
   of one host disagree: resolve the rows before the next run.

## Facts-only withhold

The withhold of a facts-only source's prose follows its class by rule, in the tick that sets the class
(`SourceRightsService.SyncRecipesAsync`), from a terms-only check or a full one. It is journaled per recipe
(`ProseWithholdFactsOnly`, which the prose lane reads) and **once per source**: a `SourceFactsOnlyWithhold`
row on the `FactsOnlyProse` field, `withheld | N recipes (M approved) withheld by the facts-only class of
terms-only check X`. Its undo (one click in the journal, or the undo of the class row that made the source
facts-only) takes back the class with the withhold: the class goes back to what it was before (kept on the
check it came from, so the stale re-derive does not bring it back from the same report) and every recipe of
the source whose latest prose change is a facts-only withhold is shown again; both are journaled as
`SourceUndo` rows that can themselves be undone, either one bringing back the class and with it the withhold
(the `FactsOnlyProse` undo row takes back its paired `Rights` undo row). Nothing then checks the source again by itself (its report
allows a full check, which waits for an admin); an admin may start a terms-only check of it.

Figures, per approved web source (`SourceProseFiguresModel`): its recipes and approved recipes, those whose
prose is withheld now, and those a facts-only class would withhold when it comes (not withheld and not in
NOM's own words), and whether its class came from a terms-only report. Admin → Scraping Sources shows them
on each source (`scraping-source-prose-figures`, before a class `scraping-source-prose-would-withhold`,
after it `scraping-source-prose-withheld-by-class`); the Pipeline page lists every source with recipes
(`pipeline-rights-prose-item`); the re-derive dry run lists a terms-only report that would withhold prose as
such, with its counts (`scraping-sources-rights-run-withhold`).

**What an operator sees at first enable** (`TermsOnlyChecks` on, `FullChecks` off): before the first tick each
of the approved web sources shows "no rights class yet" and "A facts-only class ... would withhold the prose of N
recipes (M approved) in the same tick", and the Pipeline page's source rights block lists the same per source
under "a facts-only class would withhold". After each tick the checked source shows its class; for a facts-only
one, "prose withheld N (M approved)" and "The facts-only class of terms-only check X withholds this source's
prose", the Pipeline row turns to "withheld N (M)", and the journal holds one `SourceFactsOnlyWithhold` row for
it with an Undo button. Staging checkpoint 4 (2026-11-23) withheld 13,066 recipes (2,308 approved) of nine
sources this way.

## Admin actions, journal and undo

Every change of a source's state is a row in `audit.AuditLogEntry` (`EntityType`
`ScrapingSource`, see `Support/SourceJournal.cs`), with the field, its old and new state and a
detail: `SourceApprove`, `SourceReject`, `SourceHandRecord`, `SourceHarvestPause`,
`SourceHarvestResume`, `SourceWithhold`, `SourceRelease`, `SourceRecheck` (a verdict change),
`SourceProbationPass`, `SourceProbationFail`, `SourceFactsOnlyWithhold` (above) and `SourceUndo`.

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
The four hand sources the owner approved on 2026-11-21 (VA Nutrition, NHLBI, the Library of
Congress's digitized cookbooks and NCI's "Down Home Healthy Cooking") are listed with their licence
facts in `ops/hand-import/sources.csv`, and their documents come in through `ops/hand-import.py`
(fetch, text, extract, staged import): see `hand-import.md`.

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
| `TermsOnlyChecks` | false | the scraper has its terms-only mode (`sampleSize` 0). The first check of every source and candidate is terms-only, so off, nothing without a passing terms-only report is checked; an admin may start a terms-only check of a forbidden, permission-needed or unclassed source |
| `FullChecks` | false | full checks (20 sample recipe pages) may run, and discovery runs; a source only with a recorded permission, facts-only use approved by an admin, or a clear class of a source admitted from a candidate's clear full check, a candidate after a terms-only check allowed it. Off, a first enable runs the terms-only pass alone; set it true after the review |

`SourceDiscovery:AutoApprove` is no longer read (a warning is logged if it is set).

## Where it shows

- Admin → Scraping Sources, discovery runs: each run with its seeds, how many hosts it excluded (and left out
  over the limit) and the excluded hosts its seeds link to (`scraping-sources-run-excluded`), a seed NOM also
  holds (`scraping-sources-run-seeds-excluded`), and the note of a NOM fault, a list over the limit or a scraper
  that ignored the list (`scraping-sources-run-exclude-note`).
- Admin → Scraping Sources: the queue, the approved sources with their re-check and the kind of
  their last check (`scraping-source-last-check-kind`: terms-only or full, its status and date; a
  report under a newer rule version is flagged, `scraping-source-newer-check-version`), the candidates
  that are not clear with their reasons, user requests, hand sources, discovery runs and failed
  checks, and the journal with its undo buttons.
- Admin → Pipeline, "Recipe sources": the lane's state, failed runs in a row and the last good
  run, the queue and the approved sources by verdict, paused, withheld, in probation and
  harvestable counts, the checks today against the cap and the failed checks of the week.

## Rights class

Each web source's rights class (clear, facts-only, permission-needed, forbidden) and its AI reservation are
derived by rule from the stored report of its last check, journaled, and re-derived by every re-check; they
gate harvesting, URL and staged imports and discovery seeds, and decide what readers see of its recipes. See
`source-rights.md`.

## Not built here

- Copy-site detection (design Q3) and the "lines on a curated food" probation figure: both depend
  on other lanes' state and would not make a verdict plainer.
- The harvester itself lives outside the repository; it must read the harvestable list and honour
  each source's recipe limit and crawl-delay.
