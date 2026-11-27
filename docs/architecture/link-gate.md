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
| `compound` | the wording names more than one food (and, or, with, &, +, ;, a slash, a second food after a comma). A joiner between two preparation words ("garlic, minced or grated", "chickpeas, drained and rinsed", "stewed and strained tomatoes"), a comma after leading notes ("boneless, skinless chicken breasts") and a one-food name with a joiner ("half and half", "lea & perrins'") are one food (`link-gate/9`) | links B3, wordings 2.1, staging checkpoint 2 M22 |
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
| `head-noun` | for two readers' answer, in place of the two rules above: the wording's head noun ("X of Y": Y) is not in the food; for a lone answer too (`link-gate/9`): the head noun is in the food only after "with", so the food is another food made or topped with it ("cake-crumbs" on "Cake, coffeecake, cinnamon with crumb topping", detail `with crumb`); since `link-gate/14`, for any answer: an oil, flour, butter or milk of a food is not the food: a wording whose head is one of them, after another food word and not after "in" or "with", needs a food that is that product, named on its own or closing a compound word, not only joined to a process by a hyphen ("roasted peanut oil" on "Peanuts, spanish, oil-roasted" is held; "peanut oil" on "Oil, peanut" and "butter milk" on Buttermilk pass) | wordings check 3, staging checkpoint 2 M10 |
| `head-lead` | for a lone answer: the food is a product (pickle, sauce, soup, pie, jam, jelly, relish, pudding, salad, dressing, gravy, stew, and since `link-gate/13` cake: "one-half cake chocolate" is a cake of baking chocolate, not Chocolate Cake) made from the wording's head noun, the wording names the product only before its head, and the head leads foods of its own ("pickle cucumbers", cucumbers for pickling, on "Pickles, cucumber, sour") | staging checkpoint 1, K4 |
| `variety` | for a lone answer: the food's own catalog name ends in a word of its description that is not its lead, not a part, state or form word, and that the wording lacks ("ground pork sausage" on Raw Pork Chorizo, "Sausage, pork, chorizo, ..."); and, since `link-gate/11`, for a lone answer on a food that is no drink: its name carries a word its description carries too that sets it apart from the plain food of its kind and the wording lacks: a colour no kind takes by default (yellow, orange, purple, golden; the red or green of a bell pepper, kidney bean or snap bean is the plain food and stays accepted), an animal species (turkey, chicken, pork, beef, duck, guinea ...) or a regional variety (Italian, Polish, Swiss ...): "sausage links" is not Sweet Italian Sausage, "raw, lean ham" not Turkey Ham, "bell peppers, thinly sliced" not Yellow Bell Pepper; since `link-gate/12` only while a plain food of its kind exists (another live food led by the same word of its description that is not a prepared food and carries no such word): "scrapple" is Scrapple, pork and "headcheese" Headcheese, pork, the only kinds USDA lists (golden `lg-159`, `lg-160`). **Since `link-gate/13` (the distinguishing rule, detail `food is a variety, species, colour, flavour, form or part the wording does not name`), for a lone answer on a food that is no drink, every distinguishing word of the food must be accounted for**: a word of its own catalog name that its USDA entry carries too, or, for a food whose name is its entry, a word of the entry's qualifying segments (after the lead, read up to "with", "without" or "no"). A word is accounted for when the wording names it (folded, or as the end of a compound word such as "spearmint" for mint), when it is an alternative of a word the wording names ("red and white"), when it is joined by a hyphen to a word that is accounted for ("freeze" in "freeze-dried" for "dried chives"), when another rule of the gate reads it (a state, form, part, marked, neutral, filler or class word, the lead the lead-noun rule requires, or an entry word such as grade, trim, pack, "full cut", boneless, undiluted), when every other live food of the kind the wording fits carries it too, or when it is the default of its kind: another curated food of the same lead that the wording fits as well carries it in its USDA entry while its catalog name, a name a person gave it, leaves it out, and, for a colour, no food so named carries another colour instead (P9: "Cream-Style Corn" is the yellow cream-style corn, so "No. 2 can Cream Style Corn" passes to the yellow one; no plainly named sweet pepper is red, so "sweet pepper" on Red Bell Pepper is held). Whatever is left holds the pair: "brown flour" is not brown rice flour, "wild salmon" not coho, "non-fat plain yogurt" not the vanilla one, "salt butter" not Whipped Butter (golden `lg-161` to `lg-170`). Holding is cheap and a wrong accept is not, so a variety a cook does not name (red bell pepper for "bell pepper", black turtle beans for "black beans", a cut of beef for "beef short ribs") is held too; see the figures under the scorer | staging checkpoint 1, K4; staging checkpoint 3, N7; review CK6R-4; staging checkpoint 4, P6, P9 |
| `form` | the wording says a form (dried, canned, cooked, roasted, frozen, smoked, cured, pickled, ground, powder, salted, fresh; "roast" before another word, as in "cold roast pork", is roasted) the food does not have outside its brackets or, since `link-gate/13`, in its own catalog name ("smoked herrings" on Smoked Herring); forms given as alternatives of one another ("fresh or frozen cranberries", "fresh or canned tomatoes") are no form the food must have, and the raw or uncooked entry of a nut, seed, legume or grain (USDA categories 12, 16, 20) is the dry product ("dry lentils" on "Lentils, raw", "raw sesame seeds" on the dried seeds) (the cut name "top loin (roasts)" is no roasting); green pod beans (French, string, snap, green, runner, wax beans, haricots verts) on mature seeds with no dried or dry word, and "shelled" or "fresh" seeds on mature seeds ("a quart of shelled Lima beans" on dried limas, `link-gate/9`); neutral table: dry, ground and pounded for a dried spice, browned or dried for flour, dry for a drink | links B3, wordings check 4, staging checkpoint 1 K4 |
| `fresh-herb` | the target is a dried spice and the wording, or more than 5 % of its lines, has a fresh cue (fresh, sprig, handful, bunch, stalk, leaves, chopped, minced), not "freshly ground" or, since `link-gate/13`, a grated or grating spice ("grates of fresh nutmeg"), and "handful" not on a spice that is no leaf herb ("handful of broken cinnamon"), and "leaves" not when the spice is itself a leaf. Since `link-gate/9`, on a spice that is no leaf herb (basil, oregano, tarragon, thyme, rosemary, sage, marjoram, dill, parsley, chervil, coriander leaf, mint, savory, bay ...) "fresh" and "freshly" are no cue before a word of grinding (cracked, crushed, milled, pounded, pulverized) or on black or white pepper, which is only ever the dried corn: "freshly cracked black pepper", "fresh black pepper" and "anise seed, freshly crushed" are the spice, while "fresh ginger", "fresh turmeric" and "fresh red pepper" are still held, and a produce cue on any spice still holds ("chopped peppers" on black pepper) | wordings 1.5 class C, 2.7, staging checkpoint 2 M22 |
| `part` | the wording names a part (skin, peel/rind/zest, juice, seeds, leaves, kernels, greens, bran, germ, roe, yolk, egg white, bones) the food is not, or the food is a part (a later description segment that is a part word, or says "only") the wording does not name ("potatoes" for "Potatoes, raw, skin"). Since `link-gate/9`: the leaves or greens of a leaf food (spinach, parsley, lettuce, arugula, kale, the leaf herbs ...) are the food, though not of a fruit ("grape leaves" on grapes and "lime leaves" on limes are held), and "bone-in" names a cut, not bones, on either side ("roast beef bones" on a bone-in rib roast is held; "bone-in chicken thighs" and "bone-in pork chops" are no part). Since `link-gate/13`: a part the entry has only after "with" is not the food ("fresh orange peel" on "Oranges, raw, with peel" is held) unless the wording too asks for the food with it ("1 can salmon with bones" on "... solids with bone and liquid"); leaves are a part of a seed spice ("dried celery leaves" on Celery Seed); the food's own catalog name counts as its entry ("clam juice" on Clam Juice, "cardamon seeds" on Cardamom Seeds); leaves are met by greens ("beet leaves" on Beet Greens); a part word that only qualifies another food word of the wording is no part ("chicken bone broth", "bran flakes"), nor is one after "per", "every" or "each" ("sugar per pint of juice") | links B3, wordings check 4, staging checkpoint 2 M10, M22; staging checkpoint 4, P6, P13 |
| `marked` | the description has a marked word (light, imitation, substitute, reduced, low, nonfat, skim, free, non, artificial, diet, decaffeinated, instant, mix, flavored, pressurized, "with salt added") the wording lacks | links B3 |
| `percent` | for a lone answer, the description states a percent the wording does not, under a lead word with other foods; for any answer, the wording states a percent the food does not. Since `link-gate/13` a stated 100 % names a pure food, not a variant ("100% whole wheat flour"), and a food that states no percent meets 0 % when it is nonfat, fat-free or skim and 1 % or 2 % when it is lowfat ("0% greek yogurt", "2% greek yogurt") | links B3; staging checkpoint 4, P13 |
| `polarity` | salted against unsalted or without salt, sweetened against unsweetened (since `link-gate/13` the entry's "not sweetened" is unsweetened: "Nuts, coconut meat, dried (desiccated), not sweetened") | links B3, wordings check 4 |
| `generic` | for a lone answer, the wording is one food word that leads three or more foods and the food qualifies it ("cheese" for "Cheese, gouda"); a number in the description the wording lacks qualifies it too ("in flour" and "extra flour" for "Flour, 00", `link-gate/9`). Since `link-gate/14`, for any answer, two readers' too: a bare made-product word (sauce, soup, gravy, dressing, pie, cake, jam ... with nothing else but neutral words such as "homemade") is no evidence for one particular product, so a food that carries a word setting it apart is held ("homemade sauce" on "Sauce, homemade, white, medium", detail `sauce: white`), while an alternative the food joins to the product is none ("jam" on "Jams and preserves") | links B3, staging checkpoint 2 M7, M10 |
| `group` | the wording's head noun is a group word (wine, cheese, flour, oil, juice, sauce, stock and 37 more) and the target's USDA category is not one it allows ("glasses of sherry" for red wine vinegar). Since `link-gate/13` a group word after "in" is a packing medium, not the head ("sardines in oil", "sun-dried tomatoes in oil", "butter rolled in flour"), and the group word that ends the food's own catalog name is no conflict ("clam juice" on Clam Juice) | wordings check 5; staging checkpoint 4, P13 |
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
("crisco", "saleratus") passes only through a lexicon entry (`GateCatalog.Lexicon`, filled
from `recipe."WordingLexicon"` by the wording readers, see `wording-readers.md`). A line two readers
gave a fixed reading of an unclear alcohol word carries that reading's sense (`GateLine.Sense`), which
the alcohol rules read in place of the line's own; no other caller sets it, so the rules and the
gate's signature are unchanged. A drink's old or trade name ("sack") passes without one when its
lines' own alcohol sense allows the food (below).

**Alcohol sense as identity evidence (`link-gate/7`, `link-gate/8` since the wave-6 merge).** A drink named by an old or trade name
("sherry", "sack", "Madeira", "port", "claret", "chablis", "ale") is a USDA entry that does not carry
the word, so the word rules held it whatever produced the answer. `LinkGate.AlcoholIdentity` reads
every line of the pair (the wording, with no lines) by its own words, without its recipe; when every
one is a drink, they share one sense, and the target's FDC id is in that sense's allowed table, the
answer is read as two blind readers' is and needs no word of the wording in the food: `lead-noun`,
`containment`, `head-noun`, `head-lead`, `variety`, `generic` and the description-side `percent` are
not applied, and "dry" in the description (a dry dessert wine) is no state the wording must name. A
sense read from the recipe alone (bare "wine" in "White Wine Mussels") is no identity evidence, a
list or a line of another sense breaks the shared sense, and every other rule, the alcohol rules
first, holds as before: "sherry" is never the sweet dessert wine or the rum. The full credit goes
only to the first food of the sense's table that the catalog holds ready (`WordingTiers.Ready`), the
food the alcohol tier answers: red table wine for the red sense, Dry White Wine for the white. A
later food of the table (the red and white tables list the USDA varietals after the table wine)
gets the lead noun and a drink's state words excused and nothing more, so every word of the wording
must still be in the food: "merlot" passes to Merlot, but "chianti", "claret" or "pinot noir" on
Merlot and "champagne" on Chardonnay are held by `containment` (lg-123 to lg-128).

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
colours and entry words of the distinguishing rule, the ratio words, measure cues and dry-goods categories
(since `link-gate/13`), the products of a food (oil, flour, butter, milk; since `link-gate/14`), the staple names (`StapleFoods`, read by the head-default spare and, through the name rule, by
`rule-other`), the prepared categories (`CatalogAdmissionRule.PreparedCategories`) and the matcher
and compound guard tables, and the alcohol sense rules (`AlcoholLineContext.SignatureSource`, its
own version, every pattern and the allowed-food table). Any change to a table changes it; a change to a rule's code rather than
a table raises `LinkGate.Version`. `LinkGateTests` checks that every staple and prepared category is
in the hashed text. The scorer lane `link-gate` records it, and a lane tier on the frame passes it
in its gate signature, so a changed gate drops that tier back to shadow.

## Golden files and the scorer

- `link-gate.tsv`: rule-given rows (`rule:link-gate`, `rule:construction`, status `ok`); lg-161 to lg-176
  come from staging checkpoint 4 (2026-11-23, `link-gate/13`): the eight wrong accepts of P6 and the right pick
  of P9, as the checkpoint operator judged them (`agent`, `agent-operator-checkpoint4-2026-11-23`), lg-100 now
  names the cut it roasts (a bare "cold roast pork" on the top loin is lg-161, held), and constructed rows for
  the default colour, the dry product, a pure 100 %, greens, a packing medium and a hyphened form
  (`rule:construction`, `ok`); lg-129 to
  lg-147 come from staging checkpoint 2 (2026-11-21, `link-gate/9`): the four wrong accepts of M10
  and the `extra flour` move of M7 (held by `form`, `head-noun`, `generic`, `part`), and the M22 pairs
  the generic rules held although right (preparation words and commas read as two foods, fresh cues
  on pepper and anise seed, the leaves of spinach and parsley, powdered cracker on cracker meal), all
  judged by the checkpoint operator (`agent`, `agent-review-ck2-2026-11-21`), with constructed rows
  that guard the narrowed rules (`rule:construction`, `ok`); lg-148 to lg-155 come from the review of
  that branch: bone-in cuts are no part (`agent`, `agent-review-ck4rg-2026-11-21`), and fresh ginger,
  fresh turmeric and fresh red pepper on the ground spice, the leaves of a fruit, and fresh white
  pepper (`rule:construction`, `ok`); lg-68 to
  lg-91 are the alcohol rows, one or more per alcohol rule of the design, and two pairs the alcohol
  rules let through. lg-92 to lg-108 come from staging checkpoint 1 (2026-11-20): the five pairs the
  gate let through on staging's waiting picks and should not (K4: `agent`, judged by the checkpoint
  operator), a constructed row for each new rule and four pairs those rules must still let through
  (`rule:construction`, `ok`), and the six alcohol lines of K10 (amounts joined by "and" or "or"
  read as lists, two wines on Non-Alcoholic Wine, a measure of curaçoa). lg-109 to lg-111 come from
  the review of that fix (`rule:construction` and `rule:link-gate`, `ok`): a raw cut named with a
  last "roast" on a cooked food, and a red wine named before the dish it is for on Chicken Stock.
  lg-112 to lg-122 (`rule:link-gate`, `ok`) are the alcohol identity rows of `link-gate/7`: sherry,
  sack, Madeira, port, claret and a sherry line let through to the food their sense allows, and
  sherry on the sweet dessert wine and the rum, port on the dry one, rum on a wine, and a wording
  whose lines mix a drink with a vinegar still held. lg-123 to lg-128 (`rule:link-gate`, `ok`, from
  the review of that branch) hold a regional or other varietal wine on a sibling varietal of the
  sense's table (Merlot, Chardonnay) and let through chianti on red table wine and merlot on Merlot. The test reads each row's FDC id from `gate-catalog.tsv`. Each is a
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
  classes; for `link-gate/6`, the wave-5 merge of both; and for `link-gate/7`
  (`cleanup2/ck2-undo-reprocess`), when the name normalizer began to take "circa" off an Italian
  amount, with the same figures, and separately (`cleanup2/ck2-diet-gate`) the alcohol sense as
  identity evidence, which let two more pairs through, both judged right, with the same false
  accepts; for `link-gate/8`, the wave-6 merge of both; for `link-gate/9`, raised on two branches from
  the same base: the name normalizer began to read a range or count of number words up to fifty
  ("sixteen eggs", "twenty heads of mushrooms", "twenty yolks of eggs"), a trailing range with no
  unit, an amount after the name followed by a description, and the Italian "d'" and "spicchio"
  (`cleanup2/ck4-parser-method`, six more pairs through, all judged right), and separately the
  checkpoint-2 rules and their review (`cleanup2/ck4-recheck-gate`, 62 more judged pairs through), each
  with the same false accepts and no pair regressed; and for `link-gate/10`, the wave-13 merge of
  both, with `alcohol-context/6` (the ratafia rule of `cleanup2/ck4-diet-containers` moves no pair);
  and for `link-gate/11` with `alcohol-context/7` (`cleanup2/ck6-recheck`, staging checkpoint 3 N7:
  a species, regional variety or non-default colour in the food's name that the wording lacks): it
  lets through 2,376 of 7,664 pairs, 99.5 % of them judged right, and 11 of 1,406 pairs judged wrong,
  near or compound (0.8 %; one fewer), with agreement 49.1 % (3,760) and no pair regressed. A first
  cut that also held red, green, white, black and brown regressed 15 pairs the reviewer had judged
  right (red bell pepper for "bell pepper", green snap beans, red kidney beans, white turkey meat),
  so those colours stay out of the rule; and for `link-gate/12` (review CK6R-4: the qualifier holds
  only where a plain food of the kind exists) with the same figures and no pair regressed; and for `link-gate/13`
  (`cleanup2/ck9-link`, staging checkpoint 4 P6, P9, P13): it lets through 2,356 pairs (20 fewer), 99.6 % of
  them judged right, and 10 of 1,406 pairs judged wrong, near or compound (0.7 %; one fewer), agreement 48.8 %
  (3,742). 26 pairs the reviewer judged right are now held, every one by the distinguishing rule: a colour a
  cook does not name (red bell pepper for "bell pepper" eight times, red kidney beans, green snap beans, green
  leaf lettuce, green sweet peppers, white deli turkey), a variety or species (black turtle beans for "black
  beans" three times, dill pickles twice, rich vanilla ice cream twice, mild Italian sausage, dark corn syrup,
  bell for the yellow pepper beside the sweet one twice) or a cut (chuck for short ribs, eye of round); 8 judged
  right are newly let through by the P13 fixes. On the 100 fresh accepts staging checkpoint 4 read (P6),
  replayed over the frozen catalog with no lines: `link-gate/12` lets 99 through, 8 of them wrong (79 right, 12
  doubtful: 90.8 % right of those judged); `link-gate/13` lets 79 through, none wrong (72 right, 7 doubtful:
  100 %), and holds 21: all 8 wrong, 8 right and 5 doubtful. `link-gate/14` (`cleanup2/ck19-small`, staging
  checkpoint 8 T5: "homemade sauce" on White Sauce and "roasted peanut oil" on oil-roasted peanuts, 2 of 45 fresh
  accepts) scores the same pairs exactly as `link-gate/13` (2,356 let through, 10 false accepts, 3,742 agreeing, no
  pair regressed); its two rules are proven by golden `ck19s-lg-*` and `LinkGateDistinguishingTests`. Floors: precision at
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
