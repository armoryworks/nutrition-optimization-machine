# Prose check v2

A scraped recipe's method and description are shown only in our own words. The prose check decides
whether a text is our own: it compares a rewrite with the stored source (`recipe.ScrapedDocument`) by
rule, with no model. Version 2 (`ProseCheck`, `ProseCheck.Version = 2`) replaces the bare-number
comparison of `RewriteFidelity`. The re-check and the rewrite lane are to call the same function, so
they always agree; until those are moved onto it (the prose withhold and prose lane v2 branches),
`RewriteFidelity` stays the gate they use.

The check is text only: `ProseCheck.Check(source, rewrite, ingredientLines, stockIndex)`. The finding
lists every rule broken with its detail, the copy measures it computed, the check version and the
stock-index version it ran with.

## Rules

A rewrite passes only when every rule holds.

1. **Quantities, not bare numbers** (`ProseQuantities`). Each amount is read as a value or range,
   a unit family (time, temperature, length, volume, mass, count) and a unit.
   - "a" or "an" before a unit is 1; "a minute or two" is 1 to 2 minutes; "a half hour" and "half an
     hour" are 30 minutes; "1 and 1/2" and "one and a half" are 1.5; "1 hour 30 minutes" is one
     amount; "9 x 13-inch" is two lengths.
   - Two amounts are equal when they are of one family and agree within 2 % at both ends once
     converted: ¼ lb is 4 oz, 2 pints are 4 cups, 175 °C is 350 °F. "350 degrees" with no scale
     equals 350 °F or 350 °C.
   - A unit changed inside a family is `changed-unit` (½ tablespoon to ½ teaspoon, 30 seconds to 30
     minutes). A range collapsed, widened or narrowed is `changed-range`. A source amount the rewrite
     lacks is `lost-quantity`; an amount the source, its title and its ingredient lines lack is
     `added-quantity`.
   - Step markers ("2.", "Step 3", "2In a bowl", a trailing "3.") and cross-references ("as in step
     3", "No. 156") are not amounts. A count of exactly one, and a spelled-out count before "sides",
     "edges", "ends" or "equal", is soft: never required, never added.
   - An amount in brackets right after another of its family ("350°F (175°C)", "9-inch (23 cm)") is
     a conversion and may be dropped; one stated must still match. Any other bracketed amount
     ("(350° F.)" alone, "1 can (15 oz)") is required.
2. **Amounts against the ingredient lines.** An amount up to 4 words before an ingredient's name
   (the last name word of a line, matched by the recipe's own line words) must equal a source method
   amount. If not, and the method states an amount of that family for that ingredient, it is
   `wrong-amount`: where the method and its own lines disagree, the method's amount is the one to
   keep. If the method states none, the line's amount or the sum of that ingredient's lines is
   allowed, and any other amount of the line's family is `wrong-amount`. The ingredient lines are
   the source's `recipeIngredient` (`ProseCheck.SourceLines`).
3. **Coverage.**
   - Every cooking verb of the source must be kept (`lost-step`). Verbs come from the synonym groups
     of `prose-verbs.tsv` (compiled into Nom.Orch): "heat", "warm" and "preheat" are one action,
     "heat" and "mix" are not. A word may be in several groups ("whisk" mixes and beats); a source
     verb is kept when the rewrite holds any word of any of its groups. A word right after an
     article, a heat level or "from", "off", "over" or "for" is a noun ("over medium heat"). A mixing
     verb followed by "in" ("stir in the flour") also adds.
   - Every ingredient a source step names must be in the rewrite (`lost-step`).
   - The source steps' amounts must appear in their order (`out-of-order`).
   - A method of 25 words or more must be rewritten at 0.4 to 1.3 times its length (`too-short`,
     `too-long`).
4. **Copy, ignoring stock phrases.** Words are lower-cased and every word holding a digit or a
   fraction is `#`; phrases are five words (`ProsePhrases`).
   - Distinctive containment, the share of the source's distinctive phrases (description and method)
     found in the rewrite, must be at most 0.12. It is counted when the source has 20 or more
     distinctive phrases; below that the run and sentence rules carry the check.
   - No run of copied distinctive phrases may be longer than 11 words.
   - No source sentence of 10 or more distinctive words may appear whole.
   - Steps word for word equal to the source method are `unchanged`.
   All three copy rules report `copied-text`. The limits C = 0.12 and R = 11 are the design's
   starting values, to be set from the staging proof at the 98th percentile of clean-room output and
   kept below every judged wholesale copy (0.28).
5. **Form.** No step marker (`step-marker`) and no empty step (`empty-step`).

Any change of a rule or a limit raises `ProseCheck.Version`.

## The stock-phrase index

`recipe.ProseStockPhrase` (`Hash`, `SiteCount`, `Version`) holds every five-word phrase that 3 or
more recipe sites use (`ProseStockIndex.StockSites`): a phrase 2 or more sites use besides the one a
source came from is stock, not copying ("bring a large pot of", "preheat the oven to #"). The hash is
the 64-bit FNV-1a of the phrase's words joined by single spaces.

`ProseStockService` builds it from every stored source document: the description and each method
step of the JSON-LD, phrases inside one step only, each site (the source host without "www.")
counted once per phrase. The same documents always give the same index. A write replaces the whole
index under the next version and journals one `ProseStockRebuild` entry in `audit.AuditLogEntry`
(`EntityType` `ProseStockPhrase`, old and new version and counts). An index that would not change is
not written and keeps its version.

```
POST /api/ProseRewrite/stock/rebuild?apply=false   # dry run: documents, sites, per-site counts, phrases, stock phrases, added, removed, versions
POST /api/ProseRewrite/stock/rebuild?apply=true
```

Curation-gated (`CanManageCuration`). There is no separate undo: a rebuild from the same documents
gives the same index. With no index built the check treats every phrase as distinctive, which is the
strict reading.

## Golden sets

- `prose.tsv`: column `check2` is the v2 outcome (`passed`, a reason the check must give, `!reason`
  for one it must not give, `!quantity` for no amount reason), `status2` its status, `lines` the
  ingredient lines, `stock` `none` to run with an empty index. Rows come from the public-domain
  recipes of the v1 rows, constructed recipes of our own text (`k1`-`k3`) and constructed sentences
  (`e-`, `f-`). Fault rows are made by one deterministic edit of a faithful rewrite and are rule-given
  (`rule:construction`, `ok`); the faithful rewrites themselves were written by a model operator
  (`agent-review-construction-2026-11-20`, `agent`).
- `prose-stock.tsv`: the frozen stock phrases the golden rows use, constructed (no staging data).
- `prose-verbs.tsv`: the verb lexicon itself.
- CI requires 0 false accepts on fault rows and at most 5 % false rejects on faithful rows
  (`Check_v2_lets_no_constructed_fault_through_and_rejects_at_most_5_percent_of_faithful_rewrites`),
  and a fault row for every rule.

Copyrighted source text is never committed. Cases from scraped recipes belong in a staging-only set
that refers to recipe ids.

## Proving it on staging

1. Rebuild the stock index as a dry run: it must read about 21,954 documents on 17 sites (the
   2026-11-20 analysis), then apply.
2. Once the re-check runs v2 (the prose withhold branch), compare with the analysis: of the 2,422
   copied-only failures in the listed 5,000, about 939 had at most 3 words outside phrases of 3 or
   more sites; v2 should no longer fail those for copying.
