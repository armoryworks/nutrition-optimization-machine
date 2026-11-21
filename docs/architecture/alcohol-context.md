# Alcohol in context: line sense rules, the link gate, the shortlist and the diet gate

The bare word misleads. "Sherry vinegar" is a vinegar, "a glass of sherry" is a fortified wine,
"½ tsp rum extract" is a flavouring, "oyster liquor" is the oysters' liquid, "a wine glass of milk"
is milk, "ginger beer" is a soft drink and "Scotch barley" is barley. On staging (2026-11-20) a rule
classifier found 1,609 lines on a food of another sense, almost all from model picks: brandy on
Coffee Cream Liqueur (1,066 lines), sherry on Red Wine Vinegar (319), beer on the non-alcoholic malt
beverage (87). The diet gate matched `%rum%` and `%gin%` and so flagged Bread Crumbs and Ginger Root
for No Alcohol. This page describes the rules that read the sense and the three places they act.
Design: alcohol stream, items A1, A2 and A9 (`cleanup2/alcohol-context`).

## The sense rules (`AlcoholLineContext`)

`AlcoholLineContext.Read(line, context)` reads one line, with its recipe when the caller has it
(`AlcoholRecipeContext`: title, other lines, steps, whether it came from a web source). No model. The
first rule that fires wins:

1. **Lists and alternatives** ("brandy or rum", "red wine (or chicken broth)", "brandy and water"):
   each part is read on its own (sense `list`). Amounts joined by "and" or "or" are one amount, not
   a list ("One and one-half gallons of cider", "one cup and a half of cider", "a spoonful or two
   of sack", "a tumbler and a half or six wine glasses of claret"; `alcohol-context/3`).
2. **Not a drink**: `vinegar`, `vinaigrette`; `non-alcoholic` (non-alcoholic, alcohol-free,
   alcohol-removed, dealcoholised, "NA"); `extract-food` (a meat, beef, malt, yeast, anchovy or
   mushroom extract or essence, either way round: "anchovy essence", "essence of anchovies"); `yeast` (ale or beer yeast, brewer's yeast, barm); `not-food`
   (turpentine, camphor, nitre, hartshorn, bay rum, white spirit, rubbing alcohol); `other` (Scotch
   barley, oats, groats or bonnet; a sack of; a ham hock; Port Salut; Muscatel or Malaga raisins and
   grapes; champagne mango; maraschino cherries; malt for ale; "for the rum"); `liquid` ("liquor"
   with no spirit word).
3. **A measure** (wineglass, wine-glassful, claret-glass, sherry glass): the food is what follows
   "of"; only a drink there is read on (`measure` otherwise), so "two port wine-glassfuls of
   curaçoa" is the liqueur (curaçoa, the old spelling, is read as curaçao).
4. **A dish named after a drink** ("white wine sauce", "brandy butter", "beer batter",
   "Kirsch-flavoured syrup", "soaked in rum", "poached in red wine"): `dish`.
5. **A flavouring** ("rum extract", "almond essence", "brandy flavouring", "1 tsp rum flavor"):
   `extract`. A flavour word followed by the name of a product is not a flavouring ("chicken flavor
   bouillon", "lemon flavor gelatin"): such a line has no alcohol word and reads `none`, so the gate
   and the shortlist leave its food alone.
6. **Rice and cooking wines**: Shaoxing, Chinese rice wine, mirin (`rice-wine`, no USDA food);
   cooking wine or cooking sherry (`cooking-wine`); sake (`sake`).
7. **Soft drinks**: ginger beer, root beer, spruce or birch beer (`soft-drink`).
8. **Cider**: boiled cider (`boiled-cider`, no USDA food); hard cider, perry (`hard-cider`); sweet,
   fresh, new, apple or mulled cider (`apple-juice`: unfiltered apple juice in American usage); bare
   cider (`cider-unclear`).
9. **Fortified wine**: cream or sweet sherry, port, Tokay, Malaga, Muscatel, malmsey, sweet wine
   (`fortified-sweet`); sherry, sack, amontillado, fino, oloroso, dry Madeira or Marsala
   (`fortified-dry`); bare Madeira or Marsala (`fortified`: either, the two USDA foods are within 5 %).
10. **Liqueurs** (checked before spirits so "coffee brandy" and "cherry brandy" stay liqueurs): coffee
    liqueur and Kahlua (`coffee-liqueur`), Irish cream (`cream-liqueur`), crème de menthe
    (`creme-de-menthe`); fruit brandy, Grand Marnier, curaçao, amaretto, bitters, bare "liqueur" and
    the rest (`liqueur`, no USDA food).
11. **Spirits**: rum (`rum`), vodka (`vodka`), whiskey, bourbon, Scotch (`whiskey`); brandy, cognac,
    Calvados, kirsch, grappa, gin, Hollands, tequila, arrack, proof or rectified spirit, spirits of
    wine (`spirit`); "alcohol" in a recipe whose other lines are food (`spirit`), else
    `alcohol-unclear`.
12. **Beer**: light beer (`light-beer`), malt liquor (`malt-liquor`), beer, ale, lager, stout,
    porter, small or table beer (`beer`).
13. **Wine**: vermouth (`vermouth`, no USDA food); Sauternes, late harvest, ice wine
    (`wine-late-harvest`); fruit and flower wines and mead (`country-wine`, no USDA food); rosé,
    blush, white zinfandel (`wine-rose`); white burgundy and white wine names (chablis, riesling,
    hock, Rhenish, champagne, prosecco …, `wine-white`); red wine names (claret, burgundy, merlot,
    cabernet …, `wine-red`); then red or white next to "wine".
14. **Bare wine**: a colour in the recipe (title, other lines, steps) picks red or white; both
    colours, or an old text with none, is `wine-unclear` (in old puddings and jellies "wine" often
    meant sherry or Madeira, with about 85 % more energy); a recipe from a web source with none is
    `wine-table`. Bare wine with no recipe at all is `wine-unclear`.

The rules are English and old-cookbook English; other languages fall to `none` and are left alone.
Two senses were added by the builder beyond the design: `malt-liquor` and `country-wine`.

## The allowed-food table

Each drink sense allows a fixed set of USDA SR Legacy foods by FDC id (`AlcoholLineContext.Allowed`):

| Sense | FDC ids |
|---|---|
| `wine-red` | 173190 red table wine and the red varietals (Merlot 171925 and the 6-row varietals, which the readiness rule holds) |
| `wine-white` | 174837 white table wine (Dry White Wine #164) and the white varietals (Chardonnay 174110 …) |
| `wine-rose` | 171908 |
| `wine-late-harvest` | 173202, 174844 (both 6 rows: no ready food today, so these lines stay unlinked) |
| `wine-table` | 173185 table wine, all; also 173190 and 174837 (all table wines within 5 %) |
| `fortified-dry`, `fortified-sweet`, `fortified` | 175112; 173176; both |
| `cooking-wine`, `sake`, `hard-cider` | 169070; 167723; 174146 |
| `rum`, `vodka`, `whiskey` | 174817, 174818, 174819, each with 174815 (distilled 80 proof: every 80-proof spirit carries the same values) |
| `spirit` | 174815 |
| `coffee-liqueur`, `cream-liqueur`, `creme-de-menthe` | 175097 and 173665; 175098; 173168 |
| `beer`, `light-beer`, `malt-liquor` | 168746; 168749; 171907 |

`AlcoholLineContext.Refuse(reading, fdcId, description)` says why a reading refuses a food:

- a drink refuses any food outside its set (`alcohol-drink`); a food with no FDC id is never in it;
- a drink with no USDA food or an unclear one refuses every food (`alcohol-no-food`);
- a sense that is not a drink refuses an alcoholic food (a food of a drink set, or a USDA description
  led by "Alcoholic beverage" or naming a liqueur, hard cider or malt liquor, unless it says
  non-alcoholic) and a food not of its kind: a vinegar must be a vinegar, an extract an extract or
  flavouring, yeast yeast, sweet cider apple and no vinegar (`alcohol-not-drink`); not food refuses
  everything;
- a measure (`measure`: the food after "of" has no alcohol word) also refuses a food named by an
  alcohol word, such as Non-Alcoholic Wine;
- a list refuses only when every part does; a part with no alcohol word ("chicken broth") refuses
  an alcoholic food and a food named by an alcohol word ("sugar or brandy" does not name
  Non-Alcoholic Wine by its first part). Two wines ("a bottle of Catawba or Sauterne wine", "a
  wine-glassful of Sauterne or vin de Grave") therefore never pass on Non-Alcoholic Wine. Sauterne
  without the s is the late harvest white; "vin" and Catawba are wine with no colour USDA gives
  (`wine-unclear`), "vin blanc" and "vin rouge" white and red, "au vin" a dish. A line that names
  a drink besides the dish it is for reads as that drink ("1 bottle red Burgundy, for the coq au
  vin" is red wine and refused on Chicken Stock; "2 cups chicken stock for coq au vin" stays a
  dish; `alcohol-context/4`).

The table is checked in the golden tests against the frozen catalog: every id is a USDA alcoholic
beverage (category 14), and every drink sense but `wine-late-harvest` has a food with at least 20
nutrient rows.

## Where the rules act

**The link gate.** Three rules of `LinkGate` (`alcohol-drink`, `alcohol-no-food`, `alcohol-not-drink`;
see `link-gate.md`) hold any answer, from any tier or model, that a line's sense refuses: every line
of the wording when the caller passes lines, the wording itself when it does not. The gate reads the
target's FDC id (`GateFood.FdcId`); `LinkGate.Version` is `link-gate/7` and its signature includes
the sense rules, so a changed rule drops a lane tier back to shadow. A drink sense read from a line's
own words that allows the target is also identity evidence in the gate (`LinkGate.AlcoholIdentity`),
so "sherry", "sack", "port" or "claret" answered with the food of its sense is not held for lacking
the word. Over the 7,664 judged pairs of
`link-pairs.tsv` the rules refuse 55, of which 54 were judged wrong, near or compound and one
("c. cider" on apple juice, judged right by the reviewer) is bare cider, which the rules leave unclear
on purpose.

**The shortlist.** `FoodNameMatcher.Shortlist(name, max, sense)` lists only the foods a sense
allows, so a model choosing for "brandy" cannot pick the cream liqueur, and for "sherry vinegar"
cannot pick a wine. The link lane passes the wording's own reading; `AlcoholLineContext.Shared`
gives the one reading a wording's lines agree on, for lanes that read lines.

**The diet gate.** No Alcohol, Halal and Whole30 now carry a nutrient criterion on alcohol (nutrient
5038) and word-bounded name patterns in place of `%rum%`, `%gin%` and the rest
(`DefaultRestrictionCriteria`, `DietCriteriaMatcher`):

- A food counts when it stores more than 0.5 g alcohol per 100 g, unless it is a flavouring (a name
  read as an extract or flavouring, or a USDA Spices and Herbs food). Vanilla extract (34 g per
  100 g, used by the teaspoon) is not excluded: owner answer 6 leaves extracts to a note on the
  recipe page, which the alcohol-retention item builds with the label note.
- A food that stores no alcohol figure (an unlinked wording food) counts when its own name reads
  as a drink, a drink with no USDA food, an unclear drink or a dish named after one, never by an
  alias. Whole recipe lines are stored as aliases ("little sugar and brandy or raisin wine" on
  Sugar, "glass of the best port wine or a glass of new unskimmed milk" on Milk, "Shaoxing rice
  wine, dry sherry or sake" on Red Wine Vinegar, "beer" on Beef Broth); on staging (checkpoint 1,
  K9) reading them flagged 2,341 foods and about 4 in 10 recipes, every recipe with sugar or milk.
  The candidates come from one Postgres word match on names (`~*` on
  `AlcoholLineContext.DrinkNamePattern`), then the rules read each name. A dish counts because it carries the drink ("brandy butter" is raw
  brandy); this is the safe side for a No Alcohol household, and goes beyond the design, which left
  dishes out.
- The word patterns (`\mrum\M`, `\mgin\M` …, Postgres word starts and ends, matched with `~*`) are
  the fallback for names: a name they match counts unless the rules read it as an alcohol word that
  is not a drink, and a food that stores alcohol is left to its figure. An alias they match counts
  only when it holds no alcohol word and a rule or a person gave it (`IngredientAliasTrust`), so an
  alcohol pattern never flags a food by its alias; a pattern an admin adds for something else reads
  only such aliases too, and the lines on a food it matches only through another alias decide for
  their own recipes (`docs/architecture/diet-gate.md`).
- Vinegars, non-alcoholic drinks, soft drinks, yeast, cooking liquid, measures, Bread Crumbs,
  Ginger Root and Crumbled Feta no longer match.
- A recipe line's own text is read too. A line that reads as a drink counts whatever food it is
  linked to, so a drink line still linked to a food that is not a drink ("1 glass of sherry" on Red
  Wine Vinegar, "cider" on Apple Cider Vinegar, "white wine" on Non-Alcoholic Wine: the wrong links
  the applied-link re-check (A4, `cleanup2/link-recheck`) moves) keeps its recipe flagged, while the
  vinegar's own lines stay cleared. The recipe page names such a match by the line's text. Meal
  planning and search exclude these recipes by id (`RestrictedIngredientSet.RecipeIds`), read from
  every live line whose text holds a drink word (one `~*` probe on `RawLine`, no list of foods), so
  a drink line on Sugar or Milk keeps its recipe out of plans as it does on a vinegar. Every word
  that can give a line a counted sense is in `DrinkNamePattern` (with the accented spellings:
  rosé, curaçao, crème, Bénédictine, Gewürztraminer, Sémillon); golden tests check it over every
  golden line, every judged wording and every word of the rules' trigger that the diet gate counts
  (the red grapes sangiovese, gamay, grenache, tempranillo, barolo and valpolicella were missing
  before `alcohol-context/4`).
- **The probes are kept, not repeated** (checkpoint 1, K8: each took 1.2 to 1.9 s on staging and ran
  for every household set). `DietGateCache` keeps the answer of each whole-catalog probe (the foods
  a restriction type's criteria match, with the recipes whose lines decide for themselves; the drink
  lines) per database. It is marked stale by
  `DietGateCacheInvalidator`, an EF Core interceptor on the API's context, when a command writes
  `recipe."Ingredient"`, `"IngredientAlias"`, `"RecipeIngredient"` or `nutrient."IngredientNutrient"`
  (at once outside a transaction, at its commit inside one; SaveChanges, bulk updates and raw SQL all
  pass there), and after 15 minutes in any case (writes from Nom.Import or psql). A stale answer is
  served while one refresh runs on its own context, so a write never makes a household wait; only the
  first read after a start computes in line. A stale answer is never served once its refresh failed
  (logged as a warning, "Diet gate: the refresh of probe ... failed") or once it is older than
  twice the 15 minutes: the next household set then computes it in line, so a probe that keeps
  failing shows as an error, not as an old set. The per-recipe matches read only the recipe's own foods
  and lines and are not cached.
- The same rules serve the per-recipe diet matches (`GetDietMatchesAsync`, the severe-conflict
  filter of search and the plan) and the household's forbidden-food set (meal planning, search).
- A criterion with a per-serving cap (`MaxAmountPerServing`) is not read: excluding a recipe above
  0.5 g retained alcohol per serving from drinks needs the retention factors and is built by
  `cleanup2/alcohol-retention`.

`DefaultRestrictionCriteria.EnsureAsync` brings a restriction type that holds only untouched default
rows to the new defaults at start-up: it adds the word patterns and the alcohol nutrient row and
removes the default `%…%` alcohol patterns. A type an admin has edited is left alone (its old
patterns stay until an admin changes them). Rolling the release back puts the old patterns back on
the old release's own start-up, since its defaults are then missing again.

## Golden set and scorer

- `alcohol-context.tsv`: 177 lines with the expected sense, a food the sense must allow and one it
  must refuse; 164 rule-given rows (`rule:alcohol-context`, `ok`) from the design's classes and the
  staging checkpoint-1 lines (K10: ac-162 to ac-172), ten judged by the alcohol-context builder and
  three by the checkpoint fix (Catawba, vin blanc, coq au vin; `agent`); ac-176 and ac-177
  (`rule:alcohol-context`, `ok`) are wine lines that name the "au vin" dish they are for. `GoldenAlcoholContextTests` requires every row to pass, every
  sense to have a row, and every sense of the design a rule-given one.
- Scorer lane `alcohol-context` in `model-lanes.json`: no model, `required`, floor 98 % and no false
  accept; the committed record must be what the rules score now
  (`dotnet run --project nom-api/Nom.Import -- --golden-score --lane alcohol-context --record`).
- 24 rows in `link-gate.tsv` (lg-68 to lg-91), one or more per alcohol rule of the gate, and the six
  K10 lines of checkpoint 1 on Apple Cider Vinegar and Non-Alcoholic Wine (lg-103 to lg-108).
- The diet replay over the golden lines: no line read as a drink loses its flag, no line that is
  not a drink is flagged, and 26 lines the old patterns flagged are cleared. A second replay links
  every rule-given golden line to a food that is not a drink (Red Wine Vinegar, Apple Cider Vinegar,
  Non-Alcoholic Wine, Sugar), which is the staging state of the wrong links; every line whose
  expected sense counts keeps its recipe flagged on the recipe page and in plans, Sugar included,
  and no other line is flagged.

## Not built here

- The deterministic tier `deterministic:alcohol-context` (A3) is a tier of `cleanup2/wording-tiers`.
- The repair of the lines on a food of another sense (A4) is the `alcohol` rule set of the link
  re-check (`link-recheck.md`); it reads each line with its recipe and applies
  `AlcoholLineContext.Refuse`, moving the line to its sense's food or back to its wording.
- The fixed-reading residue lane for bare wine, bare cider and "alcohol" (A6) is a mode of the
  wording readers (`cleanup2/wording-readers`); until then those lines stay unlinked.
- Retention, the page note and the per-serving exclusion (A7, A8) are `cleanup2/alcohol-retention`.
