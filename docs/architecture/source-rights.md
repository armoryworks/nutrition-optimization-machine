# Source rights: a source's rights as an engine property

What NOM may do with a web source, and with the recipes it took from it, is decided by rule from
recorded facts, not by a judgement each time. The facts are the source check's stored report
(`recipe."SourceCheck"."ReportJson"`, scraper rule `source-check/2`, see `recipe-sources.md`); the
rule is `SourceRightsRule` (`source-rights/1`, `nom-api/Nom.Orch/Services/Support/SourceRightsRule.cs`);
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
2. A report under a scraper rule version the rule does not read, or from a check that did not finish,
   gives **no class** (inconclusive): the stored class stands until a later check. An inconclusive
   report can raise the AI flag (a reservation it did reach) but never clears it: a check that stopped
   before the sample pages has not found the `noai` meta absent.
3. **permission-needed**: the terms could not be read or the site refuses automated fetches: the
   scraper refused to fetch the domain, the homepage was not read (so its footer is unknown),
   robots.txt was refused or unreachable or bars NomBot or every crawler, a page answered a bot
   challenge or refused NomBot, a linked terms page was not read, or the TDM reservation file could not
   be read.
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

## What the class gates

With `SourceRights:Enforce` (true in `appsettings.json`, off when there is no configuration, as in the
unit tests), a web source is fetched only when `SourceRightsService.MayFetch` holds: it is approved and
its effective class is clear, or it is facts-only and an admin approved **facts-only use** of it
(`FactsOnlyApprovedAt`, journaled `SourceFactsOnlyUse`, undoable). Forbidden, permission-needed and
unclassed sources are never fetched. The gate is applied in four places:

- the harvestable list (`GET api/ScrapingSource/harvestable`), which also names each source's effective
  class and AI flag, so the harvester can tell a facts-only source;
- the one-URL import and the test scrape (refused before the scraper is called, with the quote);
- the staged import (refused and counted in `refused`);
- discovery seeds (only fetchable sources are seeds).

The weekly re-check itself still runs the source check on every approved source, because that check is
how a class is learned and re-learned (see the owner question in the build report).

## What a reader sees of a facts-only recipe

Each recipe of a classed web source carries its source's effective class and AI flag as stamps
(`Recipe.SourceRights`, `Recipe.SourceAiReserved`), set at import and re-set whenever the source's class,
flag or permission changes. For a **facts-only** recipe a reader gets facts and a link only:

- ingredient facts under NOM's own catalog names (the source's line wording, `RawLine`, is not served:
  `ProseWithholding.LineWording` leaves `Notes` empty in the detail response, in search, popular, recent
  and random results, in the smart shopping list, and leaves the line out of the bulk export and print),
  times, yield, NOM-computed nutrition, the source link;
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
journaled (`RecipeTitleRule` on `Name`) and a run is undone by its stamp.

### Third-party credit lines

`CreditLines` finds "Adapted from ...", "Recipe from ...", "Recipe courtesy of ...", "Courtesy of ...",
"Reprinted (with permission) from ...", "Excerpted from ..." in a recipe's source text and keeps the cue
and the name, but not a line that credits the author's own work or the page ("adapted from my cupcakes",
"recipe from this website", "recipe from now on": defect D4). The name is the run of capitalised words
after the cue (with "of", "the", "and", "by" and the like between them), ending at a comma or the first
lower-case word, so no clause of the publisher's description is ever kept with it. "Inspired by" names an
influence, not the work a recipe was taken from, and is not a credit. The credit is stored on
`Recipe.SourceCredit` at import and by a backfill step from the stored JSON-LD or record
(`rights/credits`, journaled `RecipeSourceCredit`, undone by stamp), and shown in the recipe page next to
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
facts-only approval, or a rule change).

## Recipes already taken from forbidden and permission-needed sources

Nothing is done to them by itself. `GET api/ScrapingSource/rights/taken` is the dry run for the owner: per
forbidden or permission-needed source with no permission, the quote, the recipes taken and how many are
still shown. A site recorded under both its bare and its `www.` host is listed once, so its recipes are not
counted twice in the list or in the Pipeline total. The one-click, journaled, undoable "withhold recipes" of `recipe-sources.md` is the action.
Nothing in this engine deletes a recipe.

## Golden sets

- `source-rights.tsv`: the class and flag of the 22 hosts judged in the research, built from the facts and
  quoted sentences of source-rejudge.md as a check with defects D1 to D7 fixed reports them (rule-given,
  `rule:source-rejudge-2026-11-20`), and constructed cases for every branch (`rule:construction`).
- `rights-sentences.tsv`: NOM's reading of each quoted sentence, the decoys of D3 among them.
- `recipe-titles.tsv`: the title rule, on titles written for the rule.

On the 22 stored reports of the research (not in the repository) the rule gives the research's resolution
for 19 hosts. The three others (skinnytaste, budgetbytes, sallysbakingaddiction) read facts-only because
the `source-check/2` reports miss their forbidding sentences (defects D1 to D3 of the scraper's check);
they read forbidden once the scraper quotes them. Until then no facts-only source should be approved for
facts-only use without reading its terms.

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
  reservation and its quote, a recorded permission, facts-only approval and why it is not fetched; the
  "Record permission" form and the facts-only approve and withdraw buttons; the "Source rights" section
  with the derive dry run and apply, the titles, credit and retention steps with their dry runs and an
  undo of the last run, and the recipes taken from barred sources.
- Admin → Pipeline, "Source rights": sources by class, unclassed and stale ones, AI-reserved sources,
  permissions, facts-only approvals, fetchable sources, facts-only recipes withheld and in NOM's words,
  AI-reserved recipes, prose due for reduction and held, recipes with a credit line, recipes taken from
  barred sources, and the last class change.
