# Source rights: a source's rights as an engine property

What NOM may do with a web source, and with the recipes it took from it, is decided by rule from
recorded facts, not by a judgement each time. The facts are the source check's stored report
(`recipe."SourceCheck"."ReportJson"`, scraper rule `source-check/6`; stored `source-check/5`, `source-check/4` and `source-check/2`
reports are still read; see `recipe-sources.md`); the rule is `SourceRightsRule` (`source-rights/4`,
`nom-api/Nom.Orch/Services/Support/SourceRightsRule.cs`);
the engine is `SourceRightsService`. No model takes part anywhere in it.

This is the closing section of the source re-judging research (2026-11-20) built as code. It is a
reading of what sites publish; it is not legal advice.

## The class

Every web source carries a class on `recipe."ScrapingSource"` (`RightsClass`, with the deciding words
in `RightsQuote` and where they were found in `RightsQuoteUrl`, the check it came from in
`RightsCheckId`, and `RightsRuleVersion`). The rule reads the report and nothing else, in this order:

1. **forbidden**: a sentence of the site's own words that NOM's sentence reader (`RightsSentences`)
   reads as forbidding automated access or reuse. The sentences are the terms and footer prohibitions
   the check quoted and the robots.txt comments; robots.txt comments are read first because they are
   served on the domain itself. The sentence is kept as the deciding quote with its URL. A forbidding
   sentence counts even when the check was cut off.
2. A report under a scraper rule version the rule does not read (it reads `source-check/6` and stored
   `source-check/5`, `source-check/4` and `source-check/2` reports; `source-check/3` was never deployed with NOM, and anything
   that is not `source-check/N` is not read), from a check that did not finish, or for a host with no address
   (`access.homepageOutcome` `NoAddress`: DNS gave no A or AAAA record, so nothing could be read and every
   reason is unknown) gives **no class** (inconclusive): the stored class stands until a later check. A report
   under a **newer** `source-check/N` than NOM reads is classed on the facts NOM knows and flagged
   (`SourceRightsVerdict.NewerRuleVersion`; "newer check version" on the admin page), so a scraper released
   ahead of NOM does not leave every source unclassed (checkpoint 3, S1). Such a report is never **clear**: a
   licence that would make it clear under the version NOM reads gives facts-only instead
   (`SourceRightsVerdict.NewerLicenceUnread`), because the newer version's licence semantics are what NOM has
   not read, and a source already held clear keeps its clear class (no class from that report). A refusal before any fetch (the
   scraper's denylist, an ignored platform) is permission-needed with its reason even when the report is
   marked incomplete. An inconclusive
   report can raise the AI flag (a reservation it did reach) but never clears it: a check that stopped
   before the sample pages has not found the `noai` meta absent.
3. **permission-needed**: the terms could not be read or the site refuses automated fetches: the
   scraper refused to fetch the domain, the homepage was not read (so its footer is unknown) or carries no
   links as served (`access.homepageLinks` 0: a page built by scripts, whose terms cannot be found),
   robots.txt was refused or unreachable or bars NomBot or every crawler, a page answered a bot
   challenge or refused NomBot (`access.botChallengePages`, `access.refusedPages`, a sitemap in
   `enumeration.refusedPages`, sampled recipe pages in `samples.refused`), a linked terms page was not read
   (any outcome but `read`: also one linked on another domain, `off-domain`, which the check never fetches),
   or the TDM reservation file could not be read. A value the rule does not know is never a pass: a robots.txt
   status or a TDM reservation status outside the contract's set is permission-needed ("not established"), a
   terms page outcome other than `read` is a page not read, an AI crawler rule outside the set raises the AI
   flag, and a `mode` other than `full` is read as terms-only (it can neither lift a bar nor allow a full check
   on its own).
4. **clear**: the report's licence is CC0, the Public Domain Mark, a public-domain statement or CC BY,
   and the check's licence reason passed (so no "all rights reserved" stood beside it).
5. **facts-only**: anything else: nothing grants reuse and nothing forbids automated access. Facts
   carry no copyright; the publisher's expression does.

The sentence reader needs a prohibition cue ("may not", "must not", "agree not to", "you agree that
you will not", "is prohibited", "not permitted", "never granted", "does not consent", "without ...
written permission/consent", "no ... may be") together with an object word: automated access (scrape,
crawl, spider, robot, bot, data mining, harvest, automated, aggregation, systematic), reuse (reproduce,
reprint, republish, redistribute, copy, duplicate, derivative works, for publication, used on/in an
application), or AI use (artificial intelligence, machine learning, AI, LLM, training, fine-tuning,
text and data mining). A grant limited to "personal (non-commercial) use only" or "solely for your
personal use" is a reuse prohibition without a cue. A sentence about the site reserving the right to
change things forbids nothing (defect D3 of the research), and neither does a rule of conduct (no
spyware, nothing unlawful, no unsolicited messages). The conduct test is made per prohibited action: a
sentence is split into its actions at ";" and at ", or to" / ", and to", and a conduct word cancels only the
action it stands in, so "you agree not to use any robot ... to access the Website, or to introduce viruses"
still forbids automated access. "Text and data mining" is an AI reservation, not a
ban on fetching. The scraper's own category for a sentence is not read.

### The AI flag

`AiReserved` (with `AiReservedQuote` and its URL) is set apart from the class when the site reserves its
content against AI use: a sentence the reader reads as AI, a `noai` robots meta or `X-Robots-Tag`, a
declared TDM reservation (`tdmrep.json` or a `tdm-reservation` header), a robots `Content-Signal` with
`ai-train=no` or `ai-input=no`, or a named AI crawler blocked (or partly blocked) in robots.txt. While
it is set, no model reads the source's method or description (below).

### Written permission

An admin records a written permission from the publisher: who gave it, when, and its text as written
(`PermissionGrantedBy`, `PermissionGrantedOn`, `PermissionText`; at most 2,000 characters, quote the
sentences that grant it). It is journaled (`SourcePermission` on field `Permission`) and lifts
permission-needed, forbidden or facts-only to **clear** for that source (`SourceRightsService.EffectiveClass`):
the stored class stays what the report says. Undo restores the old facts from the journal.

### A class recorded by hand

A class otherwise comes only from a scraper report, so a host whose own words already forbid automated access
could be classed only by a request to it (staging checkpoint 5, Q22). An admin records a class by hand instead:
`POST api/ScrapingSource/rights/by-hand?apply=` (dry run unless `apply=true`) with the host, the class, the site's
own deciding words (`quote`, at most 1,000 characters), the page they stand on (`quoteUrl`, http or https) and the
date they were read (`readOn`, not in the future), or the form "Classes recorded by hand" on Admin → Scraping
Sources. It is stored in `recipe.SourceRightsRecord` (host without `www.`, class, quote, page, date, who and when)
and journaled twice over: one row on the record (`EntityType` `SourceRightsRecord`, `SourceRightsByHand`) and one
per source it sets (`SourceRightsByHand` on `Rights`, the source's old class kept in the row).

- **What it holds.** Every web source of the host (the bare and the `www.` row) takes the class, the quote and the
  page, with `ScrapingSource.RightsRecordId` naming the record, and its recipes are re-stamped as for any class. A
  candidate of the host, and a host NOM has no row for yet, are held by the record too; a source added later for the
  host takes it at the source lane's next tick (`SourceRightsService.ApplyRecordsAsync`, run before each check and
  discovery run).
- **The lane never overwrites it.** A verdict of a later report (`ApplyVerdict`, the weekly re-check, the stale
  re-derive, a candidate approval) leaves a class recorded by hand as it is; only the AI flag may still rise.
- **A forbidden or permission-needed class by hand is never fetched**, with the rights gate on or off
  (`SourceRightsService.HeldByHand`): no weekly check, no terms-only check (an admin's is refused too), no seed, no
  harvest, no URL import or staged import, and the host goes on every discovery job's exclusion list like any barred
  host, as a source or as a host with no row. A candidate of the host gets no check and cannot be approved.
- **The rule.** A class recorded by hand may be as strict as the class a source's check gave it, or stricter
  (clear, facts-only, permission-needed, forbidden, in that order), never laxer, and never laxer than the class the
  candidate's last report gives; **clear** only when every source of the host holds a recorded written permission
  (the permission rule above). **Forbidden** needs a quote that `RightsSentences` reads as forbidding automated access
  or reuse; a fact such as "the terms page answers a bot challenge" is recorded as permission-needed. One record
  stands per host: the same class, quote and page again is "already recorded"; a different one is refused until the
  standing record is undone.
- **Undo.** Undoing the record's journal row (or a source row it wrote) withdraws the record as a whole: every source
  it set goes back to the class it had before (from that source's own row), a facts-only withhold it made is ended
  where it is still the recipe's latest prose change, each is journaled as `SourceUndo`, and the record is kept,
  marked withdrawn. A source set back to no class keeps no derived time (`RightsDerivedAtUtc` cleared with the class;
  staging checkpoint 6, R21). The lane then classes the host by its own checks again. Those undo rows cannot themselves be
  undone: a class they would bring back stood on a record that is withdrawn, so it is recorded again instead. The
  withdrawal has a dry run like every journal undo (`GET api/ScrapingSource/journal/{entryId}/undo`, see
  recipe-sources.md).
- **Shown** on Admin → Scraping Sources beside the class badge ("Recorded by hand by person N on DATE (read DATE)",
  `scraping-source-rights-by-hand`; a class from a check says "Derived by rule from its check",
  `scraping-source-rights-by-lane`), in the list "Classes recorded by hand" (`scraping-sources-by-hand-item`, with
  its undo) and in the journal. `GET api/ScrapingSource/rights/by-hand` lists the records that stand.
- **Loading a list.** `ops/source-rights-record.py` sends a TSV (host, class, quote, page, date; each quote under 25
  words) through the route, a dry run unless `--apply`. `ops/source-rights/forbidding-hosts.tsv` holds the seven
  hosts of the 2026-11-20/21 re-judging (`source-rejudge.md`, resolution (d)) whose own words forbid automated
  access or reuse: eatingwell, seriouseats, pinchofyum, skinnytaste, budgetbytes (both rows), sallysbakingaddiction,
  davidlebovitz. A test records every row in a dry run under the rule.

## What the class gates

With `SourceRights:Enforce` (true in `appsettings.json`, off when there is no configuration, as in the
unit tests), a web source is fetched only when `SourceRightsService.MayFetch` holds: it is approved and
its effective class is clear, or it is facts-only and an admin approved **facts-only use** of it
(`FactsOnlyApprovedAt`, journaled `SourceFactsOnlyUse`, undoable). Forbidden, permission-needed and
unclassed sources are never fetched. The gate is applied in four places:

- the harvestable list (`GET api/ScrapingSource/harvestable`), which also names each source's effective
  class and AI flag, so the harvester can tell a facts-only source; the Pipeline's `sources.harvestable`
  count is the length of that same list (staging checkpoint 3, N28: it counted 22 while the list was empty);
- the one-URL import and the test scrape (refused before the scraper is called, with the quote);
- the staged import (refused and counted in `refused`);
- discovery seeds (`MayFetch` as above: discovery reads each seed's sitemap and 2 recipe pages, so a facts-only
  class from a check alone does not make a seed; facts-only use must be approved by an admin). Discovery also
  runs only while `SourceDiscovery:FullChecks` is on (off by default); a source is checked in full only after an
  admin approved facts-only use of it (or with a recorded permission) (`recipe-sources.md`).

No automatic check fetches a source whose effective class is forbidden or permission-needed
(`SourceRightsService.RecheckBarred`; lead decision on wave 10's owner question 1): the weekly re-check skips
it and says so on the admin page (`recheckSkipped`) and in the Pipeline count, and a candidate whose last
report classes it so is not checked again either. Sources of the other classes (clear, facts-only, a recorded
permission, unclassed) keep the weekly check, which is how a class is learned and re-learned.

**Terms-only first** (checkpoint 3, S3). A source with no class, or any source or candidate with no terms-only
report that allows a full check, gets a terms-only check from the lane before anything else: the first check of
a restored database's 22 sources reads only robots.txt, the homepage and the terms pages of each. A full check
(20 sample recipe pages) follows only when that terms-only report classes the site facts-only or clear
(`SourceRightsRule.AllowsFullCheck`), or a written permission is recorded. So a site whose terms or robots.txt
forbid automated collection, or that refuses NomBot (a homepage challenge, a refused terms page, a scraper's
denylist), is classed forbidden or permission-needed on its terms-only report and never has a recipe page
requested. A scraper-side refusal is stored as permission-needed with its reason ("the scraper refused to fetch
it (denylist)") and, being barred, is not checked again by itself. A terms-only check of a clear source cannot
see the per-page licence it was found clear on, so its facts-only verdict leaves the clear class standing.

For a barred source, and for one with no class yet, an admin may start a **terms-only check** by hand (`POST api/ScrapingSource/{id}/terms-check`,
"Start terms-only check" on the admin page): the scraper's `sampleSize` 0 mode reads robots.txt, the homepage
and the terms pages, and no `tdmrep.json`, recipe page or sitemap; a terms link or redirect whose path names a
recipe is not fetched (terms outcome `recipe-like`), and NOM reads it as terms not read (permission-needed). The request is queued as a `SourceCheck` with
purpose `terms-only` and status `queued`, journaled (`SourceTermsCheck` on field `TermsCheck`, with the admin and
the reason), and run by the source lane's next tick, before and outside the daily cap (terms-only checks are not
counted against `MaxChecksPerDay` nor in the Pipeline's "Checks today"); its end (completed, failed, or lost,
also when nom-api stopped while it was polling and the next tick finds it past its budget, and the class that
results) is journaled the same way. The check is marked running before the scraper job is started, so a
restart between the two leaves it to be found lost, never queued again to fetch the site a second time. It is refused while
`SourceDiscovery:TermsOnlyChecks` is off (the default), because a scraper without the mode would read a recipe
page; if the scraper still answers with recipe pages read, the check records that it has no terms-only mode.
NOM reads a report whose `mode` is `terms-only`, or whose `samples.requested` is 0, as terms-only.
Its report re-derives the class and the AI flag and nothing else: no admission verdict, licence, harvest pause or
re-check date. Because it reads fewer pages than a full check, it can confirm or find a bar but cannot lift one it
did not read: when the source is forbidden or needs permission on the words or the refusal of a page the terms-only
check did not read (a recipe page's footer, a refused sample or sitemap), a verdict of facts-only or clear gives no
class and the held class stands (`SourceRightsRule.Derive(report, heldClass, heldQuoteUrl, lastFullReport)`), and its
AI flag can only be raised. The pages that hold a bar are the stored quote's and every page of the source's last full
check with a forbidding sentence or a refusal (not only the first sentence quoted); a terms-only check lifts the class
only when it read each of them again. The last full check is the latest check NOM asked as a full one, whatever
mode its report names (or, before the mode was recorded, the latest whose report is not terms-only); terms-only
checks, the lane's and an admin's, are passed over. A lane terms-only check the scraper answered with recipe pages
is stored failed without its report, so it classes nothing. A source a terms-only check lifts to facts-only is due for its weekly full check on its old date.

## What a reader sees of a facts-only recipe

Each recipe of a classed web source carries its source's effective class and AI flag as stamps
(`Recipe.SourceRights`, `Recipe.SourceAiReserved`), set at import and re-set whenever the source's class,
flag or permission changes. For a **facts-only** recipe a reader gets facts and a link only:

- ingredient facts under NOM's own catalog names (the source's line wording, `RawLine`, is not served:
  `ProseWithholding.LineWording` leaves `Notes` empty in the detail response, in search, popular, recent
  and random results, in the smart shopping list, and leaves the line out of the bulk export and print;
  where a response must name one line, a diet warning (`GET api/Recipe/{id}/diet`, the meal plan's severe-diet
  conflicts) or the recipe page's alcohol note, `ProseWithholding.LineName` names it by NOM's own wording:
  amount, unit and catalog food, such as "0.25 cup Vanilla Extract"), times, yield, NOM-computed nutrition,
  the source link. A food is named only when it is a catalog food (curated, or taken from USDA with an FDC id):
  a food the import created from the line's own text can carry the publisher's sentence as its name (a whole tip
  on swapping ingredients, or "optional: a few drops maple extract"; staging checkpoint 4, P31: 5,181
  facts-only lines in 3,440 recipes), so such a line is named by its own food words only
  (`ProseWithholding.FoodName` / `FoodWords`: the line without its amount, unit and notes, up to its first comma,
  colon, bracket or dash, when that is at most four plain words none of which reads as a sentence, such as
  "drops maple extract"), and otherwise "Ingredient". This holds in the detail, search, the smart shopping list,
  a list a recipe is added to, the pantry's needed list, the bulk export, diet warnings, the alcohol note, the
  author dashboard's most used foods and the test scrape. The smart shopping list never adds two items named
  "Ingredient" together, as they may be different foods;
- the method only when it is NOM's own words, that is, when the clean-room prose lane's rewrite stands
  (`ProseLaneVersion` set, `ProseAttempts` 0, not withheld, not quarantined: `SourceRightsService.OwnMethod`);
- never the publisher's description, method text or photo.

This is done by the existing withheld state, not by a second redaction: a facts-only recipe whose shown
method is not the lane's own has its prose withheld (`ProseWithheld`, journaled
`ProseWithholdFactsOnly` on `ProseWithheld`, so the prose lane's undo sees it), which every endpoint
already honours, and the rewrite lane already takes withheld recipes. A quarantined import
(`ContainsSourceProse`) is withheld too, because its author, household, audience and curators still see
it, and so is a recipe with a step a person edited: a typo fixed in one step does not make the publisher's
description and other steps NOM's own. For such a recipe the lane rewrites the whole method clean-room
(not only the description, as it does for other withheld recipes a person edited); the person's edit stays
in the journal. The withhold ends by itself (`ProseWithholdFactsOnlyEnd`) when the source becomes clear (a
licence or a permission), and only while the withhold is still the recipe's latest prose change; it stands
while the source is forbidden or needs permission, because what happens to those recipes is the owner's
decision. `SourceImageUrl` was never served; `Image` stays empty.

While a recipe is withheld, its key facts come from the stored JSON-LD, or, once that has been reduced
(below), from the stored steps, by the same rule.

### Titles

`RecipeTitleRule` (`recipe-titles/1`) keeps a title that is only the dish name and reduces a distinctive or
branded one to the dish name: a tail after ` | `, ` - `, ` – `, ` — ` and bracketed asides go, a trailing
"recipe", a boast ("you'll ever make", "of your life", "to die for"), a leading possessive that names
family (Grandma's, Aunt Bee's) or the source itself (a name in the recipe's author byline or a possessive
site name: "Sally's" for Sally's Baking Addiction), "copycat", then praise words (best, ultimate,
perfect, famous, easy ...) and the articles left in front. "Shepherd's Pie" and "General Tso's Chicken"
keep their possessive; "One Pot Pasta" and "Quick Pickles" stay. If nothing of a dish would be left, the
title is kept. It runs at import for a facts-only source, and as a step over facts-only recipes
(`POST api/ScrapingSource/rights/titles?apply=`), skipping recipes a person edited; each rename is
journaled (`RecipeTitleRule` on `Name`) and a run is undone by its stamp. The test scrape of a facts-only
source (`POST api/RecipeScraping/test-scrape-url`, `POST api/RecipeImport/test-scrape-url`) shows the reduced
title, the times and each line's parsed amount, unit and food, with no description, method, line notes or
photo. Copies of a title stored before it was reduced are not served: a meal-plan entry of a facts-only recipe
takes the recipe's current name (`ProseWithholding.EntryTitle`), and the "From recipe: ..." note of a shopping
item names the recipe as it is now (`ShoppingItemNote`, in the smart shopping list and the grocery export).

### Third-party credit lines

`CreditLines` finds "Adapted from ...", "Recipe from ...", "Recipe courtesy of ...", "Courtesy of ...",
"Reprinted (with permission) from ...", "Excerpted from ..." in a recipe's source text and keeps the cue
and the name, but not a line that credits the author's own work or the page ("adapted from my cupcakes",
"recipe from this website", "recipe from now on": defect D4). The name is the run of capitalised words
after the cue (with "of", "the", "and", "by" and the like between them), ending at a comma or the first
lower-case word, so no clause of the publisher's description is ever kept with it. A title that runs on to
"by" and a capitalised name keeps the whole title, commas, colons and short lower-case words ("to", "et",
"of") included, then the name ("Almonds, Anchovies and Pancetta: A Vegetarian Cookbook, Kind Of by Cal
Peternell", "Coming Home to Sicily by Fabrizia Lanza", "Healthier Together by Liz Moody"); a lower-case word
longer than five letters before "by" is a sentence, and the old reading stands. A title with a colon followed
by "from" and a name credits the person, not the dish ("Dakkangjung: Sweet & Crispy Chicken from Maangchi" is
"Adapted from Maangchi"). Curly apostrophes read as straight ones, so "I’ve" starts a sentence too (staging
checkpoint 4, P29: 7 of 48 wrong). "Inspired by" names an
influence, not the work a recipe was taken from, and is not a credit. The text is read with its HTML
entities decoded, and where the source's markup lost the break between two elements the name ends at the
break: before a word that starts a sentence ("This", "Other", "I've", "The" after a name word), before a
word the name already holds ("by Tara O'Brady Tara writes"), and inside a word glued to the next
element's first word when that word starts a sentence ("James BeardUsing"; "HarperCollins" and
"KitchenAid" stay whole). A word repeated after "by" or "and" starts a new name, so "Ottolenghi Simple
by Yotam Ottolenghi" stays whole. A possessive ending is dropped, a word the length limit (160
characters) cut is left out, and a name made only of places, nationalities or languages ("Recipe from
South America") is no credit (staging checkpoint 3, N26: 18 of 50 proposed credits were malformed;
`credit-lines.tsv`). A person's name after "by" ends where the next sentence's first word runs into it with
no stop between (staging checkpoint 5, Q24: 4 of 48 wrong): a capitalised word after two name words that the
text goes on from in lower case ("by Cal Peternell Red chile flakes", "by Fabrizia Lanza As mentioned"), and a
capital glued to the second name word before lower-case text ("by Rebecca LefflerLentils double", "by Danielle
OronI tend"). A name of two words is never cut, and a word such as "with", "and", "who" or "writes" after a name
ends nothing, nor does a credit word ("copyright", "courtesy", "photography", "via", "reprinted" and the like:
"by Nancy Singleton Hachisu courtesy of the publisher" keeps the surname); a three-word name followed by a verb is the one shape this reads short. A publisher in brackets
before "by" is left out, so the author is kept ("Burma: Rivers of Flavor (Artisan), by Naomi Duguid" is
"Adapted from Burma: Rivers of Flavor by Naomi Duguid"). The credit is stored on
`Recipe.SourceCredit` at import and by a backfill step from the stored JSON-LD or record
(`rights/credits`, journaled `RecipeSourceCredit`, undone by stamp, `ruleVersion` `credit-lines/4`), and shown in the recipe page next to
the source link (`recipe-source-credit`). The reduction below reads credits before the prose goes.

## Prose retention

The publisher's prose is kept no longer than the engine needs it. `rights/retention` reduces the stored
source of a facts-only recipe once its clean-room rewrite has stood for `ProseRetentionDays` (30), or the
lane gave it up that long ago:

- the newest record (`scraper-recipe`), or, when only the JSON-LD was kept, a record built by rule from the
  JSON-LD's ingredient lines, is written again as a **facts record**: NOM's title, the source URL, site and
  author, every ingredient line as the record gave it (raw line, quantity, unit, notes), the times, yield
  and servings, marked `"reduced": true`, as the next version of the `scraper-recipe` format;
- the description, the method, the image URL and the raw JSON-LD are not kept: every older document of the
  recipe is soft-deleted;
- the step is journaled per recipe (`RecipeProseRetention` on `SourceDocuments`: the ids, formats and
  SHA-256 of what was removed, and the id and SHA-256 of the facts record), with a dry run, and a run is
  undone by its stamp while the removed documents are held;
- every other copy of the publisher's prose the engine holds for the recipe is reduced in the same step:
  the description and step values of its journal rows (the prose lane's old values, a re-check's, a marker
  strip's, a step split's), the text of its removed step rows and the step texts of its rebuild journal.
  Each is replaced by `(reduced by prose retention, sha256:...)`; a value is kept only when it is what the
  recipe shows now (its description or a live step, the lane's own words) or a person wrote it. The
  originals are held in one soft-deleted `held-prose` document, which the undo puts back and the purge
  deletes. A prose-lane undo of a rewrite whose old text was reduced is skipped as changed since, and a
  rebuild undo whose steps were reduced no longer matches and is skipped;
- after `RetentionUndoDays` (30) `rights/retention/purge` deletes the held documents for good
  (`RecipeProseRetentionPurge`); an undo is then refused. Only the hashes remain.
- With `RetentionAuto` (off by default) the source lane's tick runs the reduction and the purge.

A recipe given up by the lane keeps its withheld step rows: the owner's rule is that no recipe rests
withheld, so the lane retries it when its version changes, and it falls back to the stored steps when no
JSON-LD is left. Those rows are replaced the day a rewrite stands.

### What a rebuild can still do afterwards

The rebuild (`RecipeReprocessService`, `reprocess-core.md`) reads the newest record before any JSON-LD, so
it reads the facts record:

- it can still re-parse every ingredient line with NOM's parser, restore line order, apply new parser
  rules and links, re-vet, and run the method-fill checks against the recipe's live steps (NOM's own
  rewrite);
- it cannot re-extract steps (it never did for web sources: the prose lane owns those), compare against the
  source's method (the prose re-check, check v2 against the source and the prose stock index skip a recipe
  with no JSON-LD), rebuild key facts from the source text, or give the lane the source method again (a
  Phase C rewrite of such a recipe would need the source fetched again, from a fetchable source);
- the rejection of a non-recipe still works, because the live steps are NOM's rewrite.

## No model reads an AI-reserved source

For a recipe with `SourceAiReserved`:

- the prose lane takes the model-free path only (`ProseCleanRoom.RunRuleOnly`): the method is masked, its
  fact sheets are built by rule, rendered by rule, and written only when the form rules and check v2 pass;
  no extractor, writer, judge or auditor reads it, its decisions are recorded in the `rule-only` tier and its
  journal names `rule-only` as the writer. The `rule-only` tier is a tier of its own on the lane frame: it
  writes only while that tier is live (whatever the model tier's mode), every decision of it is audited by
  rule (`ProseRuleOnlyAudit`: the written method read back from its text against the stored source steps
  and lines, one step per step, every ingredient, amount and cooking verb kept), and the frame qualifies it
  from shadow, pauses it on its own observed error and undoes only its writes. Otherwise the recipe keeps no
  method and its link (withheld), and is retried when the lane changes;
- the tool tagger is given no step text, the reprocess line judge no method sentences, and the AI repair
  lane does not take the recipe, while its method is not the lane's own.

## Re-check, journal and undo

The weekly re-check (`SourceAdmissionService`, `recipe-sources.md`) stores each new report and derives the
class and the flag again from it; a change is journaled (`SourceRights` on `Rights` or `AiReserved`, with the
old and new class, quote, URL and check) and the recipes' stamps follow. The source lane's tick also
re-derives every source whose class was derived under an older rule version or from an older check, and
`POST api/ScrapingSource/rights/derive?apply=` does it on demand with a dry run. Every journal row of the
rights fields can be undone from the source journal while the source still holds its new state, and the
undo can be undone. An undone class keeps the check and rule it was derived from, so the tick's re-derive
of stale sources leaves it; the next new check derives the class again, by rule (a class is an engine
property of the newest report, not an admin's choice: a disagreement is settled by a permission, a
facts-only approval, or a rule change). The one exception is a class recorded by hand (above), which only
makes a class stricter on the site's own quoted words and which no report overwrites.

An undo of a recipe step by its stamp (`rights/undo`) counts a row an earlier undo already put back as
**already undone** (`alreadyUndone`, skip reason `already undone`; staging checkpoint 5, Q26), not as a title
or credit "changed since", and an older stamp never undoes a later run of the same step.

## Recipes already taken from forbidden and permission-needed sources

Nothing is done to them by itself. `GET api/ScrapingSource/rights/taken` lists, per forbidden or
permission-needed source with no permission, the quote, the recipes still taken, how many are shown, and how
many were retired and purged (`retired`, `purged`), and whether the source's recipes may be removed
(`removable`). A site recorded under both its bare and its `www.` host is listed once, so its recipes are not
counted twice in the list or in the Pipeline total.

### Removal of recipes taken from forbidden sources (owner's decision of 2026-11-24)

The owner decided that recipes already taken from forbidden sources are removed. `RecipeRemovalService` does it
in two stages, each an admin action with a dry run that equals its apply, per source (`sourceId`) or for every
forbidden source at once. No model takes part.

**Which recipes.** A host qualifies only when every web row of it (the bare and the `www.` row) has the effective
class **forbidden** (`SourceRightsService.EffectiveClass`): a written permission or a class lifted by a later check
or record on any row takes the host out, and this is read again before every batch of an apply, so a permission
recorded during a run stops it. Permission-needed sources are not touched (the owner has not decided them), and
neither is a facts-only one. The recipes are those whose `SourceUrl` is on the host, with or without `www.`,
including ones their author had already deleted (their stored text is purged too).

**Stage 1, retire** (`POST api/ScrapingSource/rights/removal/retire?sourceId=&apply=`, body `{reason}`). Each recipe,
its live lines, its live steps and its live stored documents are soft-deleted with the run's stamp (whole
milliseconds) as `DeletedAt` and the admin as `DeletedByPersonId`, saved without touching `LastModifiedDate` or
`LastModifiedByPersonId` (`SaveKeepingModifiedAsync`, so no lane later reads the admin as the editor of a step); its stored diet-gate rows
(`plan.DietRecipeState`, `plan.DietRecipeFlag`) are dropped; one `recipe.RecipeRemoval` row (recipe id, run, source,
host, address, the address as compared `UrlKey`, title, class, whether it was already deleted, counts) and one
journal row (`RecipeRetire` on `IsDeleted`) are written per recipe. Because every read surface and lane reads live
rows, the recipe leaves search, popular, recent, random, suggestions, the detail and share pages (the anonymous pool
of `VisibleTo`), a plan's picker and shuffle, cookbooks, the curation queue, the image finder, the bulk export and
print, the reprocess, prose, wording and link lanes (they read live lines, steps and documents), the prose stock
index (live documents) and the stored diet gate (its update trigger on `IsDeleted` and the dropped rows). A
reprocess-journal undo and a rights-step undo (`rights/undo`) skip a removed recipe.
- The run works in batches (`BatchSize` 200), each committed with its journal rows in one transaction, and is
  recorded in `recipe.RecipeRemovalRun` (kind, stamp, scope, planned, done, finished). A run an API restart cut off
  has no finish time. It is resumed, under its own stamp, only by a dry run or apply of the same scope (the same
  host, or every forbidden source); a run of another scope is refused, naming the cut-off run, until it is resumed or
  undone, so a dry run always shows what its apply does (admin page: "Resume"). An undo by its stamp also closes it.
  A cut-off run whose source no longer qualifies (a permission or a lifted class recorded since) is closed as it
  stands by the next apply, with a warning that the dry run shows too; what it retired stays retired until undone.
  Only one removal run works at a time.
- **What a household keeps.** A meal plan entry (`GET api/MealPlan`, `/{id}`, the week view), a favourite (a rating
  of 4 or more: `GET api/User/self/favorites`, `self/ratings`), a cookbook entry and a shopping-list item of a
  retired recipe stay. They are served with the recipe's title, its source link and the plain note "This recipe is
  no longer available here." (`recipeRemoved`, `recipeSourceUrl`, `recipeRemovedNote`; in a cookbook `removed`,
  `sourceUrl`, `removedNote`; on a shopping item the note "From recipe: TITLE. This recipe is no longer available
  here. Source: URL", on the plain shopping list read `GET api/ShoppingList/{id}` (which now serves the list's items),
  in the smart list and in the grocery export). Nothing of the source's wording or images is served: no description,
  steps, lines or image, and a shopping item named from the source's line text is renamed at retire to its catalog
  food, or else to its own food words (`ProseWithholding.FoodWords`, then the looser `PlainFoodWords`: preparation
  words, a closing purpose and every alternative after the first taken off, so "Finely-grated zest of one lemon or
  orange" is "lemon zest" and "avocado and cilantro for topping" is "avocado and cilantro"; the bare word "Ingredient"
  only when neither reading finds food words), the old name journaled (`RecipeRetireItemName`) for the undo
  (`RecipeRemovalService.ItemName`; staging checkpoint 7, S18).
- **Foods left without a recipe.** A food named from the source's lines (no FDC id, not curated) that a retired line
  names, that no live line of another recipe names and that nothing a household or another food relies on holds
  (a pantry, shopping list or household shopping item, a meal plan, a household food, a goal, a restriction, a
  substitution, a variation, a component, a message thread or a live verified wording) is retired with the recipes,
  under the run's stamp, one `RecipeRetireFood` journal row each (`Foods` in the report; the dry run counts the same).
  A food shared by two batches of one run goes with the later batch, and a food shared with a recipe an earlier run
  retired goes with the run that retires its last live line. The undo puts a food back when it puts back a line naming
  it, whichever retire retired the food (so undoing an earlier run never leaves a restored line on a retired food;
  staging: about 4,826 such foods on the six forbidden hosts).
- **Undo** (`POST api/ScrapingSource/rights/removal/undo?stamp=&apply=`): every recipe of the run that is not purged
  is put back exactly (`LastModified*` included): the rows soft-deleted with the stamp are restored, a recipe that was already deleted stays
  deleted, the item names come back where they still read as the retire wrote them, and the removal row is marked
  undone (`RecipeRetireUndo`). A repeated undo counts them as already done. The stored diet gate decides the recipe
  again on its next cycle. The foods the retire retired come back with their lines (`Foods` in the report, and in its
  dry run).

**Stage 2, purge** (`POST api/ScrapingSource/rights/removal/purge?sourceId=&apply=&backupTaken=`): on retired,
not undone recipes only, irreversible, refused unless `backupTaken=true` (take a database backup first). A
`sourceId` selects the recipes retired from that source's host, whichever of its rows (bare or `www.`) is named. A cut-off
purge run is resumed only with its own scope, as for the retire. Its dry run lists every table and column and the
rows it touches. Per recipe it:

| table | columns | action |
|---|---|---|
| `recipe.Recipe` | `Description`, `DescriptionNormalized`, `SourceImageUrl`, `SourceAttribution`, `SourceCredit`, `VettingIssues`, `RecipeYield`, `Image` and its five credit columns, `OrgUrl` | cleared; `Name`, `SourceUrl` and `SourceSite` stay |
| `recipe.RecipeStep` | `Description`, `Summary` | rows deleted (live and soft-deleted) |
| `recipe.RecipeIngredient` | `RawLine` and the line | rows deleted |
| `recipe.ScrapedDocument` | `RawJsonLd`: the stored page data, scraper records, held and reduced prose, the documents the prose stock index reads | rows deleted |
| `recipe.RecipeReprocessJournal` | `OldSteps`, `NewSteps`, line texts | rows deleted |
| `recipe.LineGuardHold` | `RawLine`, `StoredFood`, `RebuiltFood` | rows deleted |
| `recipe.ReprocessCandidate`, `recipe.RecipeImageCandidate`, `recipe.RecipeAsset` (any fetched image or file), `recipe.RecipeAugmentation`, `recipe.RecipeSubstitution` with its step effects | all | rows deleted |
| `plan.DietRecipeFlag`, `plan.DietRecipeState` | all | rows deleted |
| `audit.AuditLogEntry` | `OldValue`, `NewValue` of the recipe's rows (`Recipe`, `RecipeStep`, `RecipeIngredient`), its documents' rows (`ScrapedDocument`) and its items' renames | replaced by `(purged with the recipe, sha256:...)`; the removal's own rows stay |
| `curation.LaneDecision` (subject `recipe:ID`) | `SignalsJson`, `VerdictsJson`, `AuditJson` | cleared |
| `shopping.ShoppingListItem` | `Name` | renamed to the catalog food or its own food words where it still differs; the household's row stays |
| `recipe.Ingredient` (the foods the purged lines name, and the retired foods merged away from them: named by a free text below, under that name or under the "X (deleted #id)" name a merge gives; in both cases only when no line of any other recipe names the food, live or retired; a live food merely named like a line is never taken) | `Name`, `NameNormalized`, `PluralName`, `PluralNameNormalized`, `Description` | a food a household or another food holds is renamed by its food words (with its id when another food has that name); any other gets the purged placeholder name and is retired |
| `recipe.IngredientAlias`, deleted `recipe.IngredientWording` rows, `recipe.IngredientWordingRejection` of those foods; the alias a relink gave the food that took a merged food's lines (`SourceContext` `retired:ID`); and, since staging checkpoint 8 (T17), any alias, live or deleted and on any food (a catalog food too), whose name is a free text below that is a whole line wording, more than the line's food words ("For the glaze" on Glaze, "yellow pepper, chopped or sliced" on Yellow Sweet Peppers, written by the link lanes with no `retired:` context); a line's food words ("semisweet chocolate chips" of "1 cup semisweet chocolate chips"), or a line that is only food words and no heading ("unsalted butter"), never delete another food's alias, since every other recipe linked to that food matches by it | all | rows deleted |
| `curation.FoodCatalogProposal` (on those foods, or whose `CurrentValue` is a purged line's text, its normalised text or such a food's name, when no line of another recipe holds that text and no other live food is named by it) | `CurrentValue`, `ProposedValue` (unless a number or an id), `Reason` (when it quotes the text) | replaced by `(purged with the recipe, sha256:...)` |
| `audit.AuditLogEntry` of those foods (`Ingredient`) and proposals (`FoodCatalogProposal`) | `OldValue`, `NewValue` | replaced by the same hash; the removal's own rows stay |
| `curation.LaneDecision` (subject `wording:` such a text's key, when the key is itself free: no line of another recipe holds it, no other live food is named by it and no live verified wording row has it) | `Subject`, `SignalsJson`, `VerdictsJson`, `AuditJson` | cleared, the subject replaced by the hash |

What stays is the `recipe.RecipeRemoval` row (recipe id, source, host, address, title, with `PurgedAtUtc` and a
`PurgeSummary` of what went, by table) and the recipe row's title and address, so the address is never taken again
and a household's entries keep their title and link. A purged recipe can no longer be put back: its run's undo skips
it ("purged"). `RecipeRemovalTests` proves the purge by reading every string column of every entity with a
`RecipeId`, the recipe row, its journal rows and its lane decisions, and then every string column of every row of
the model for the purged recipes' own line wordings (a wording food, a food merged away from a purged line, its
proposals, a proposal keyed by the wording alone, a wording lane's decision, a food merged away under its tombstoned
name with its relink journal row and the alias the merge left), while a wording a recipe not removed still uses stays
(its food, its proposal, and the lane's decision about a key that live recipes link to) (staging checkpoint 7, S20: 24 proposal values and 13 food names were left before). The purge's dry
run counts, per source, the same household holdings as the retire's dry run (S19).

**Never taken again.** URL import, the test scrape, a data import naming the address and the staged import the
harvester uses refuse an address with a removal that is not undone ("This address was removed: its source HOST
forbids taking its recipes ..."), compared by `UrlKey` (host without `www.`, path without a trailing slash, any case,
no query or fragment), even after the source's class is lifted. The source stays forbidden, so nothing is fetched
from it either.

**Where it shows.** Admin → Scraping Sources, beside the rights report: per removable source "Retire (dry run)",
"Retire", "Purge (dry run)" and "Purge" (`scraping-sources-taken-*-btn`), "Retire all forbidden" and "Purge all
retired" with their dry runs, the backup confirmation (`scraping-sources-removal-backup-check`, which the purge
buttons need), the result with its per-source figures (recipes, approved, approved by a person: a recipe `Approval`
feedback row by a person other than the system; approver not recorded: curated with no recipe approval row at all,
such as a curated import or an approval given without notes; edited by a person, in meal plans, favourites, cookbooks and shopping lists) and the purge's tables, the runs with an undo per
retire run, and an unfinished run (`GET api/ScrapingSource/rights/removal`). Admin → Pipeline, "Source rights":
retired and purged totals and, per barred source, taken, retired and purged.

## Every surface that could show a facts-only recipe's text

Walked by tests with a facts-only recipe beside one of another class (`SourceRightsServiceTests`,
`FactsOnlySurfaceTests`, `ProseWithheldEndpointTests`):

| surface | what could leak | how it is closed |
|---|---|---|
| `GET api/Recipe/{id}` (line notes, food names) | line wording; a food named from the line | `LineWording`; `FoodName` |
| `GET api/Recipe/{id}` (`alcoholNote`), `GET api/Recipe/{id}/diet`, meal plan severe-diet conflicts | line wording in a warning; a food named from the line | `LineName`; `FoodName` |
| search, popular, recent, random, suggestions | line wording, description; a food named from the line | `LineWording`; withheld prose; `FoodName` |
| smart shopping list (generate, optimize), grocery export, a list a recipe is added to, the pantry's needed list | line wording; a stored distinctive title in an item note; a food named from the line | `LineWording`; `ShoppingItemNote`; `FoodName` (items named "Ingredient" are never merged) |
| `GET api/Recipe/dashboard/analytics` (`mostUsedIngredients`) | a food named from the line | `FoodName` |
| bulk export (JSON, CSV) and print (PDF) | line wording, description, steps; a food named from the line | `LineWording`; withheld prose; `FoodName` |
| meal plans, plan week view, dashboard | a stored distinctive title | `EntryTitle` |
| plans, cookbooks, curation queue, image finder | description, steps | withheld prose |
| test scrape (both routes) | the publisher's page as scraped, the scraper's food name | facts-only preview; `FoodWords` |
| admin pages that show line text (curation queue, reprocess, compound and unfold lines, line labels, the source pages) | line text | `CanManageCuration` only |
| every surface above, for a recipe **retired** from a forbidden source (`RecipeRemovalTests`) | the whole recipe | soft-deleted recipe, lines, steps and documents; dropped diet-gate rows |
| meal plan entries, favourites, cookbook entries, shopping items of a retired recipe | the source's wording, a line-named food | title, source link and the removed note only; items renamed at retire |

Search still matches a query against a withheld description or steps (it shows the title only), as the
withhold design chose; a person's own shopping note and a title a household typed for an entry of a recipe that
is not facts-only are served as written.

## Golden sets

- `source-rights.tsv`: the class and flag of the 22 hosts judged in the research, built from the facts and
  quoted sentences of source-rejudge.md as a check with defects D1 to D7 fixed reports them (rule-given,
  `rule:source-rejudge-2026-11-20`); the same hosts again under `source-check/4` where its facts change the
  report (`*-4` rows: skinnytaste's footer on the homepage and recipe pages and /legal/ linked as Privacy,
  budgetbytes' 4.3.3 item under its lead-in and its 2.2 consent clause, Sally's disclosure policy, eatingwell's
  refused child sitemap and off-domain terms, myplate's missing address, culiplan's and frije's script-built
  homepages); and constructed cases for every branch (`rule:construction`).
  Under `source-check/5` (`cleanup2/ck7-source-lane`): `*-5t` rows, the terms-only check the lane now makes
  first, for the hosts the research found barred (forbidden on their own words for eatingwell, seriouseats,
  pinchofyum, skinnytaste, both budgetbytes rows, Sally's and David Lebovitz; permission-needed for
  minimalistbaker's refused terms page and cookieandkate's refused homepage) and acozykitchen (facts-only, so a
  full check may follow); and constructed rows for a newer rule version (classed), a version that is not
  `source-check/N` (not read), unknown terms, robots.txt and TDM values (never a pass), and a refusal before
  fetch marked incomplete (permission-needed).
- `source-check-5.tsv` with `source-check-5-reports.json`: the 23 `source-check/5` reports the staging scraper
  stored on 2026-11-21 (checkpoint 3: the 22 current sources and the myplate.gov request), trimmed to what NOM
  reads (no parsed recipe, credit line or reason detail), replayed whole: class, AI flag and deciding quote,
  whether the policy reads them, whether the same report as a terms-only check allows a full check, and, in the
  test, the same class read under `source-check/6` (now current, conclusive) and with the newer flag under
  `source-check/7`. Nine hosts were refused by the staging
  scraper's denylist, so their stored report is a refusal (permission-needed, no full check). The
  ambitiouskitchen row is `agent`: its class rests on the quoted sentence, not re-read by a person.
- Under `source-check/6` (`cleanup2/ck8-source-exclude`, `source-rights/4`): `source-rights.tsv` gains a CC0
  report under `/6` (clear: while `/6` was newer than NOM read it was facts-only, so a class can change, hence the
  version) and the newer-version rows under `/7`; `source-check-6.tsv` replays what `/6` changes on the stored
  reports of chocolatecoveredkatie (S4: a credit line naming only the site's own linked titles is no credit) and
  101cookbooks (S5: an index page is never sampled), each beside its stored `/5` reading. No `/6` report of a
  current source has been made yet; the rows apply the change the scraper's contract describes, and neither class
  changes.
- `rights-sentences.tsv`: NOM's reading of each quoted sentence, the decoys of D3 among them.
- `recipe-titles.tsv`: the title rule, on titles written for the rule.

On the 22 stored `source-check/2` reports of the research (not in the repository) the rule gives the
research's resolution for 19 hosts. The three others (skinnytaste, budgetbytes, sallysbakingaddiction) read
facts-only because those reports miss their forbidding sentences (defects D1 to D3 of the scraper's check).
With the facts a `source-check/4` check reports for them (the replay of the saved results in the scraper's
build report) they read forbidden, which the `*-4` golden rows hold. No facts-only source should be approved for
facts-only use before its `source-check/4` re-check.

## Settings (`SourceRights`)

| Setting | Default | Meaning |
|---|---|---|
| `Enforce` | false (true in `appsettings.json`) | the fetch gate above |
| `ProseRetentionDays` | 30 | how long a rewrite stands (or a give-up rests) before the source prose is reduced |
| `RetentionUndoDays` | 30 | how long reduced documents are held for undo before the purge |
| `RetentionAuto` | false | run reduction and purge with the source lane's tick |
| `RetentionBatch` | 500 | recipes one reduction run takes |

## Where it shows

- Admin → Scraping Sources: each approved source with its class, the deciding quote and link, the AI
  reservation and its quote, a recorded permission, facts-only approval and why it is not fetched, why the
  weekly re-check skips it (`scraping-source-recheck-skipped`), its last terms-only check
  (`scraping-source-terms-check`) and the "Start terms-only check" button (`scraping-source-terms-check-btn`),
  and what refused NomBot (`scraping-source-access`: refused pages, refused samples, a script-built homepage, a
  host with no address); the
  "Record permission" form and the facts-only approve and withdraw buttons; the "Source rights" section
  with the derive dry run and apply, the titles, credit and retention steps with their dry runs and an
  undo of the last run, and the recipes taken from barred sources with their retire and purge actions and runs.
- Admin → Pipeline, "Source rights": sources by class, unclassed and stale ones (a class derived under an older rule
  version; a class recorded by hand is never stale, since no rule derived it: staging checkpoint 6, R21 counted all
  ten hand records as stale), AI-reserved sources,
  permissions, facts-only approvals, fetchable sources, facts-only recipes withheld and in NOM's words,
  AI-reserved recipes, prose due for reduction and held, recipes with a credit line, recipes taken from
  barred sources, retired and purged (in total and per barred source), and the last class change. "Recipe sources" also counts the sources the weekly re-check skips
  and the terms-only checks waiting.
