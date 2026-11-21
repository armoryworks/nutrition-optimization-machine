# Link gate: one deterministic check for every wording-to-food answer

`LinkGate` (`nom-api/Nom.Orch/Services/Support/LinkGate.cs`) is the one gate every answer to "which
food is this wording" passes before anything writes it, whatever produced the answer: a name match,
a staple, a verified wording, a trusted alias, a lone model pick, or two blind readers that agreed.
It holds a pair and says why; it never chooses a food. It joins the stricter hold rules of the links
design (B3), the wording pair checks of the wordings design (checks 1 to 8) and the alcohol sense
rules (`AlcoholLineContext`, see `alcohol-context.md`; rules of this gate, not a second gate). It
uses no model.

Today it screens the name matches and model picks of the link lane
(`LinkAutoApplyService.ScreenAsync` and the hold report, which both used `LinkAutoApplyGuard.Hold`
before). The wording tiers (`wording-tiers.md`) call it with each wording's lines, the data-given
head defaults and the grams bounds by USDA category, which they compute from the approved recipes;
the readers (`cleanup2/wording-readers`) and the re-check of applied links call the same
`LinkGate.Check`; the re-check of links already made is described in `link-recheck.md`.

## Input

- `GatePair`: the wording, the ingredient it is on (`SourceId`), the target `GateFood`, how the
  answer was reached (`NameMatch`, `ModelPick`, `ReadersAgreed`) and, when the caller has them, the
  wording's lines (`GateLine`: raw text, unit, grams through the food's conversions and, when the
  caller has it, the rest of the line's recipe as `AlcoholRecipeContext`: title, other lines, steps,
  whether it came from a web source).
- `GateFood`: name, USDA description (the `fdc:description` alias), data type, curation status, USDA
  category (`FdcCategoryId`), admission reason, nutrient row count, whether calories, protein,
  carbohydrate and fat are all stored, deleted, FDC id. A fact left null is not checked.
- `GateCatalog`: the curated name map the relink rule reads, the foods under each lead word, how many
  foods carry each lead word, trusted aliases by wording key, the name matcher and the staples,
  data-given head defaults, the lexicon, and the 99th percentile of grams per line by category.
  `GateCatalog.Build` makes one from the foods a pass can link to.

## Rules

`Check` returns every rule that holds a pair, in this order; `Hold` returns the first as a reason.
The code is what golden files and reports name.

| Code | Holds when | From |
|---|---|---|
| `heading` | the line is a heading ("For the frosting:") | links B3 |
| `compound` | the wording names more than one food (and, or, with, &, +, ;, a slash, a second food after a comma) | links B3, wordings 2.1 |
| `retired` | the target is deleted | wordings check 1 |
| `branded` | the target is a branded food | links B3, wordings check 1 |
| `prepared` | the target is in a USDA prepared category (Baby Foods, Fast Foods, Meals, Entrees and Side Dishes, Restaurant Foods; `PreparedFoods`, from the stored `FdcCategoryId`) | owner, 2026-11-20: never a link target |
| `uncurated` | the target is neither curated nor a pending SR Legacy or Foundation food with no admission reason | wordings check 1, catalog admission |
| `incomplete` | the core nutrients are missing, or it has fewer than 20 nutrient rows while a food under the same lead word has more than 5 more (Claret, 6 rows, against red table wine, 33) | wordings check 1 |
| `alcohol-drink` | a line (or, with no lines, the wording) reads as a drink and the food is not one of its sense's allowed USDA foods ("brandy" on Coffee Cream Liqueur, "glass of sherry" on Red Wine Vinegar, "beer" on the non-alcoholic malt beverage); a food with no FDC id is never one | alcohol design A2 |
| `alcohol-no-food` | a line reads as a drink USDA has no food for (vermouth, mirin and Shaoxing, most liqueurs, boiled cider, fruit and flower wines) or one its recipe cannot tell (bare cider, bare wine in an old text or with no recipe, "alcohol" with no food around it) | alcohol design A2, 2.3 |
| `alcohol-not-drink` | a line reads as something that is not a drink (a vinegar, a flavouring extract, a meat or yeast extract, ale yeast, a cooking liquid, a measure, a dish named after a drink, a soft drink, sweet cider, a non-alcoholic drink, Scotch barley and the like) and the food is an alcoholic drink, or is not of the line's kind (a vinegar for a vinegar, an extract or flavouring for an extract, yeast for yeast, apple and no vinegar for sweet cider); or the line is not food at all | alcohol design A2 |
| `other-food` | the wording is the name of another curated food that is not a prepared food (`LineRelinkService.TargetFor`) | links B3, wordings check 2 |
| `other-alias` | a trusted alias gives the wording to another food | wordings check 2 |
| `state` | the description carries a state (dried, canned, cooked, frozen, ground, powder, sweetened, cured, roasted, smoked, pickled, dry, sprouted; "roast" before another word, as in "cold roast pork", counts as cooked, while a last "roast" names the raw cut: "pork loin roast" and "chuck roast" are held on a cooked food, `link-gate/5`) the wording lacks, and another food under the same lead word lacks it too | links B3, wordings check 4 |
| `word-order` | a name match reads in USDA's order ("gravy beef" for "Gravy, beef") | links B3 |
| `rule-other` | a staple or the name matcher gives the wording to another food | wordings check 7 |
| `shortlist` | a lone model pick is not on the name matcher's shortlist of 12 and no name rule gives it | wordings check 7 |
| `lead-noun` | the description's lead (after a class word such as "Spices," or "Alcoholic beverage, distilled,"; an identity class such as "Oil," or "Vinegar," stays) has a word the wording lacks ("molasses" for "Cookies, molasses", "nut meats" for "Nuts, coconut meat") | links B3 |
| `containment` | a food word of the wording is not in the food's name or description (folded: plural, old spelling, hyphen or space joins, one edit on words of 6 letters or more, a lexicon entry) | links B3 |
| `head-noun` | for two readers' answer, in place of the two rules above: the wording's head noun ("X of Y": Y) is not in the food | wordings check 3 |
| `head-lead` | for a lone answer: the food is a product (pickle, sauce, soup, pie, jam, jelly, relish, pudding, salad, dressing, gravy, stew) made from the wording's head noun, the wording names the product only before its head, and the head leads foods of its own ("pickle cucumbers", cucumbers for pickling, on "Pickles, cucumber, sour") | staging checkpoint 1, K4 |
| `variety` | for a lone answer: the food's own catalog name ends in a word of its description that is not its lead, not a part, state or form word, and that the wording lacks ("ground pork sausage" on Raw Pork Chorizo, "Sausage, pork, chorizo, ...") | staging checkpoint 1, K4 |
| `form` | the wording says a form (dried, canned, cooked, roasted, frozen, smoked, cured, pickled, ground, powder, salted, fresh; "roast" before another word, as in "cold roast pork", is roasted) the food does not have outside its brackets (the cut name "top loin (roasts)" is no roasting); green pod beans (French, string, snap, green, runner, wax beans, haricots verts) on mature seeds with no dried or dry word; neutral table: dry, ground and pounded for a dried spice, browned or dried for flour, dry for a drink | links B3, wordings check 4, staging checkpoint 1 K4 |
| `fresh-herb` | the target is a dried spice and the wording, or more than 5 % of its lines, has a fresh cue (fresh, sprig, handful, bunch, stalk, leaves, chopped, minced), not "freshly ground", and "leaves" not when the spice is itself a leaf | wordings 1.5 class C, 2.7 |
| `part` | the wording names a part (skin, peel/rind/zest, juice, seeds, leaves, kernels, greens, bran, germ, roe, yolk, egg white) the food is not, or the food is a part (a later description segment that is a part word, or says "only") the wording does not name ("potatoes" for "Potatoes, raw, skin") | links B3, wordings check 4 |
| `marked` | the description has a marked word (light, imitation, substitute, reduced, low, nonfat, skim, free, non, artificial, diet, decaffeinated, instant, mix, flavored, pressurized, "with salt added") the wording lacks | links B3 |
| `percent` | for a lone answer, the description states a percent the wording does not, under a lead word with other foods; for any answer, the wording states a percent the food does not | links B3 |
| `polarity` | salted against unsalted or without salt, sweetened against unsweetened | links B3, wordings check 4 |
| `generic` | for a lone answer, the wording is one food word that leads three or more foods and the food qualifies it ("cheese" for "Cheese, gouda") | links B3 |
| `group` | the wording's head noun is a group word (wine, cheese, flour, oil, juice, sauce, stock and 37 more) and the target's USDA category is not one it allows ("glasses of sherry" for red wine vinegar) | wordings check 5 |
| `units` | with lines: the food weighs under 90 % of them, or more than 5 % are liquid measures (gill, pint, gallon, glass, ml) on a dry food | wordings check 6 |
| `grams` | with lines and a bound: more than 2 % of the lines weigh more than the 99th percentile of their category | wordings check 6 |

A target that is the default food of the wording is spared the variety rules (`state`, `lead-noun`,
`percent`, `generic`): the staple entry of the wording, or, when the wording is only its food words
plus filler and forms the food meets ("browned flour"), the staple entry or data-given head default
of those words. A staple's sense is never stretched past that: "fresh butter" keeps its fresh.

Two blind readers of different families that agreed (`ReadersAgreed`) are read by the wording
checks: their answer is held by `head-noun` instead of `containment` and `lead-noun`, and the
`generic` and description-side `percent` rules, which stand in for a reader's choice of variety, are
not applied. Every other rule holds for them as for a lone answer. A synonym with no shared word
("crisco", "saleratus", "sack") passes only through a lexicon entry (`GateCatalog.Lexicon`, filled
from `recipe."WordingLexicon"` by the wording readers, see `wording-readers.md`). A line two readers
gave a fixed reading of an unclear alcohol word carries that reading's sense (`GateLine.Sense`), which
the alcohol rules read in place of the line's own; no other caller sets it, so the rules and the
gate's signature are unchanged.

**Family.** `LinkGate.Family` takes a batch of answers. Wordings that fold to one key (plural,
hyphen or space, "X wine" against "X", a unit word in front) must agree on one food. Within a key
whose answers name more than one food, the food with no readiness fault and the most nutrient rows
wins and the others are to be re-pointed to it; when the best two tie, no answer of that key is
written. "claret" and "claret wine" both go to red table wine.

**Matcher fix.** `FoodNameMatcher` now treats a later description segment that is one part word on
its own ("Potatoes, raw, skin") as part of the food's lead words, so no name rule gives "potatoes"
to the skin. The fix promoted five `known` rows of `link-pairs.tsv`.

## Signature

`LinkGate.Signature` is a hash of `LinkGate.Version` and every table the rules read: the gate's own
word lists, phrases (the salted and sweetened polarity phrases, the dried cues that turn a fresh cue
off, the egg-white and "only" words) and patterns (heading, slash, percent, "with salt added"), the
staple names (`StapleFoods`, read by the head-default spare and, through the name rule, by
`rule-other`), the prepared categories (`CatalogAdmissionRule.PreparedCategories`) and the matcher
and compound guard tables, and the alcohol sense rules (`AlcoholLineContext.SignatureSource`, its
own version, every pattern and the allowed-food table). Any change to a table changes it; a change to a rule's code rather than
a table raises `LinkGate.Version`. `LinkGateTests` checks that every staple and prepared category is
in the hashed text. The scorer lane `link-gate` records it, and a lane tier on the frame passes it
in its gate signature, so a changed gate drops that tier back to shadow.

## Golden files and the scorer

- `link-gate.tsv`: rule-given rows (`rule:link-gate`, `rule:construction`, status `ok`); lg-68 to
  lg-91 are the alcohol rows, one or more per alcohol rule of the design, and two pairs the alcohol
  rules let through. lg-92 to lg-108 come from staging checkpoint 1 (2026-11-20): the five pairs the
  gate let through on staging's waiting picks and should not (K4: `agent`, judged by the checkpoint
  operator), a constructed row for each new rule and four pairs those rules must still let through
  (`rule:construction`, `ok`), and the six alcohol lines of K10 (amounts joined by "and" or "or"
  read as lists, two wines on Non-Alcoholic Wine, a measure of curaçoa). lg-109 to lg-111 come from
  the review of that fix (`rule:construction` and `rule:link-gate`, `ok`): a raw cut named with a
  last "roast" on a cooked food, and a red wine named before the dish it is for on Chicken Stock. The test reads each row's FDC id from `gate-catalog.tsv`. Each is a
  pair the gate must let through (`pass`) or a pair a named rule must hold. Every rule has a row
  (`Every_rule_of_the_gate_has_a_rule_given_row` reads the rule codes from `LinkGate` itself, so a
  new rule without a row fails). `readers` = `yes` reads the pair as two blind readers' answer (the
  head-noun rule); `aliasOf` names the food the wording is a trusted alias of (the other-alias rule).
- `wording-pairs.tsv`: the 232 kept rows of batch w01 (judged by the model reviewer and second
  reader, `=`, status `agent`), the w01 judge picks the second reader refuted (`!`, `agent`), the
  wordings design's rule cases (`!`, `ok`) and its judged cases (`agent`), and claret: held for its
  six nutrient rows and re-pointed by the family check to red table wine (`~`, `ok`). The rows the
  gate holds that w01 kept are `known` (88; synonyms that need the lexicon, states the readers did
  not say, foods short of nutrients), with the count in `baseline.json`.
- `gate-catalog.tsv`: the frozen catalog the gate reads in the golden tests: the curated foods and
  the pending SR Legacy foods of staging on 2026-11-20 with their USDA description, USDA category
  (from the SR Legacy 2018-04 and Foundation 2025-12-18 releases), admission reason (`-` in the
  first capture, which predates `--admit`) and nutrient row count. `ops/golden/gate-catalog.sql`
  recaptures it once `--admit` has stored the categories and reasons; a pending USDA food that
  admission holds by rule keeps its reason and is held as `uncurated`, as in production.
- Scorer lane `link-gate` in `model-lanes.json`, with no model: the gate replayed over every pair of
  `link-pairs.tsv` judged right, wrong, near or compound. Run it with
  `dotnet run --project nom-api/Nom.Import -- --golden-score --lane link-gate [--record]`; it needs
  no host. Recorded on 2026-11-20 (`link-gate/2`, and again for `link-gate/3` with the alcohol
  rules, with the same figures: every judged pair the alcohol rules refuse is already held by
  another rule; on 2026-11-21 for `link-gate/4` (`cleanup2/ck1-parser-catalog`), when the name
  normalizer the gate reads began to take a hyphenated mixed number, a range and trailing size words
  off the end of a wording, which lets `butter, 1-1/2 oz` through to salted butter, one more pair
  accepted and the same false accepts; for `link-gate/4` and `link-gate/5` of
  `cleanup2/ck1-gate-alcohol`, the checkpoint-1 rules and the review fix that keeps a last "roast"
  from meeting the cooked state, with the same figures, since none of the judged pairs is of those
  classes; and for `link-gate/6`, the wave-5 merge of both): it lets through 2,307 of 7,664 pairs,
  99.5 % of them judged right;
  it lets through 12 of 1,406 pairs judged wrong, near or compound (0.9 %). Floors: precision at
  least 99 %, false accepts at most 1 %, agreement at least 45 %. The record is required, and
  `The_committed_link_gate_record_is_what_the_gate_scores_now` checks in CI that the gate still
  scores exactly what was recorded. These are agreement with the reviewer of 2026-11-13, a model,
  and in-sample: the rules were tuned on these pairs. The held-out figure comes from the lanes'
  shadow runs on the frame.

The 12 pairs it lets through and should not: "teaspoonful of mustard" and four more to prepared
mustard (the staple; old cookbooks meant dry mustard by the teaspoon, which only the unit tells),
"chopped figs" and two more to raw figs (dried figs, which nothing in the wording says), "crushed
red pepper" to sweet red pepper, "sweet-breads" to sweet bread, "soup stock" to chicken stock and
"cold corned beef" to raw corned beef.
