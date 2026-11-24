using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The alcohol sense rules (alcohol design 2.1) and where they act: each misleading class of
    /// the design, the allowed-food table, the link gate rules that hold a pair a line's sense
    /// refuses, and the shortlist that keeps only a sense's foods. Descriptions and FDC ids are the
    /// SR Legacy rows of the staging catalog; the wrong links are the classes measured on staging
    /// (brandy on Coffee Cream Liqueur, sherry on Red Wine Vinegar, beer on the malt beverage).
    /// </summary>
    public class AlcoholLineContextTests
    {
        private const long Curated = (long)CurationStatusEnum.Curated;
        private const long Pending = (long)CurationStatusEnum.PendingCuration;

        private static readonly GateFood CoffeeCreamLiqueur = new(282237, "Coffee Cream Liqueur", "Alcoholic beverage, liqueur, coffee with cream, 34 proof", "sr_legacy_food", Curated, 14, NutrientRows: 32, FdcId: "175098");
        private static readonly GateFood RedWineVinegar = new(279459, "Red Wine Vinegar", "Vinegar, red wine", "sr_legacy_food", Curated, 2, NutrientRows: 19, FdcId: "172240");
        private static readonly GateFood MaltBeverage = new(282003, "Non-Alcoholic Malt Beverage", "Malt beverage, includes non-alcoholic beer", "sr_legacy_food", Curated, 14, NutrientRows: 33, FdcId: "174863");
        private static readonly GateFood DrySherry = new(287461, "Alcoholic beverage, wine, dessert, dry", "Alcoholic beverage, wine, dessert, dry", "sr_legacy_food", Pending, 14, NutrientRows: 31, FdcId: "175112");
        private static readonly GateFood Spirit = new(287443, "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof", "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof", "sr_legacy_food", Pending, 14, NutrientRows: 33, FdcId: "174815");
        private static readonly GateFood Beer = new(287397, "Alcoholic beverage, beer, regular, all", "Alcoholic beverage, beer, regular, all", "sr_legacy_food", Pending, 14, NutrientRows: 33, FdcId: "168746");
        private static readonly GateFood VanillaExtract = new(106, "Vanilla Extract", "Vanilla extract", "sr_legacy_food", Curated, 2, NutrientRows: 32, FdcId: "173471");
        private static readonly GateFood CiderVinegar = new(286950, "Apple Cider Vinegar", "Vinegar, cider", "sr_legacy_food", Curated, 2, NutrientRows: 33, FdcId: "173469");

        private static GateCatalog Catalog() => GateCatalog.Build(new[] { CoffeeCreamLiqueur, RedWineVinegar, MaltBeverage, DrySherry, Spirit, Beer, VanillaExtract, CiderVinegar });

        private static IReadOnlyList<string> Codes(GatePair pair) => LinkGate.Check(pair, Catalog()).Select(h => h.Code).ToList();

        [Theory]
        [InlineData("1 glass of sherry", "fortified-dry")]
        [InlineData("4 tablespoonfuls of brandy", "spirit")]
        [InlineData("1 cup beer", "beer")]
        [InlineData("1 tablespoon sherry vinegar", "vinegar")]
        [InlineData("½ tsp rum extract", "extract")]
        [InlineData("1 tsp beef extract", "extract-food")]
        [InlineData("1/2 cup oyster liquor", "liquid")]
        [InlineData("a wine glass of milk", "measure")]
        [InlineData("a wine-glassful of Hollands", "spirit")]
        [InlineData("2 oz ginger beer", "soft-drink")]
        [InlineData("a pint of Ale-yeast", "yeast")]
        [InlineData("2/3 cup cooking sherry", "cooking-wine")]
        [InlineData("3 tbsp Shaoxing wine", "rice-wine")]
        [InlineData("1/4 cup sake", "sake")]
        [InlineData("1 cup white wine sauce", "dish")]
        [InlineData("2 cups sweet cider", "apple-juice")]
        [InlineData("1 cup hard cider", "hard-cider")]
        [InlineData("1 pint cider", "cider-unclear")]
        [InlineData("spirits of turpentine", "not-food")]
        [InlineData("2 oz Scotch barley", "other")]
        [InlineData("a hock of a cold ham", "other")]
        [InlineData("a bottle of hock", "wine-white")]
        [InlineData("4 oz Port Salut, sliced", "other")]
        [InlineData("1/2 cup port", "fortified-sweet")]
        [InlineData("1 oz peach brandy", "liqueur")]
        [InlineData("1/4 cup Baileys Irish cream", "cream-liqueur")]
        [InlineData("1 cup cabernet sauvignon", "wine-red")]
        [InlineData("1 cup rosé", "wine-rose")]
        [InlineData("1 cup dealcoholised red wine", "non-alcoholic")]
        [InlineData("2 cups bread crumbs", "none")]
        [InlineData("1 tbsp grated ginger root", "none")]
        public void Each_rule_reads_its_class(string line, string sense)
        {
            AlcoholLineContext.Read(line).Sense.Should().Be(sense);
        }

        [Fact]
        public void Bare_wine_takes_its_colour_or_its_age_from_the_recipe()
        {
            AlcoholLineContext.Read("1 cup wine").Sense.Should().Be("wine-unclear", "no recipe to read");
            AlcoholLineContext.Read("1 cup wine", new AlcoholRecipeContext("Braised beef", new[] { "2 cups red wine" })).Sense.Should().Be("wine-red");
            AlcoholLineContext.Read("1 cup wine", new AlcoholRecipeContext("Chicken with white wine")).Sense.Should().Be("wine-white");
            AlcoholLineContext.Read("1 cup wine", new AlcoholRecipeContext("Chicken stew", HasSourceSite: true)).Sense.Should().Be("wine-table");
            AlcoholLineContext.Read("1 pint of wine", new AlcoholRecipeContext("Wine jelly")).Sense.Should().Be("wine-unclear", "in an old text bare wine may be sherry or Madeira");
        }

        [Fact]
        public void Alcohol_in_a_recipe_of_food_is_a_spirit_and_otherwise_unclear()
        {
            AlcoholLineContext.Read("1/2 cup alcohol", new AlcoholRecipeContext("Lemon cordial", new[] { "1 cup sugar", "peel of 2 lemons" })).Sense.Should().Be("spirit");
            AlcoholLineContext.Read("1/2 cup alcohol", new AlcoholRecipeContext("Liniment", new[] { "1 oz camphor" })).Sense.Should().Be("alcohol-unclear");
            AlcoholLineContext.Read("1/2 cup alcohol").Sense.Should().Be("alcohol-unclear");
        }

        [Fact]
        public void A_list_reads_each_part_and_a_part_with_no_alcohol_word_allows_only_foods_that_are_not_alcoholic()
        {
            var reading = AlcoholLineContext.Read("1 cup red wine or chicken broth");

            reading.Kind.Should().Be(AlcoholSenseKind.List);
            reading.Parts!.Select(p => p.Sense).Should().Equal("wine-red", "none");
            AlcoholLineContext.Refuse(reading, "174536", "Soup, chicken broth, ready-to-serve").Should().BeNull();
            AlcoholLineContext.Refuse(reading, "173190", "Alcoholic beverage, wine, table, red").Should().BeNull();
            AlcoholLineContext.Refuse(reading, CoffeeCreamLiqueur.FdcId, CoffeeCreamLiqueur.Text).Should().NotBeNull();
        }

        [Fact]
        public void A_drink_allows_only_its_table_and_a_sense_that_is_not_a_drink_refuses_an_alcoholic_food()
        {
            var sherry = AlcoholLineContext.Read("1 glass of sherry");
            AlcoholLineContext.Refuse(sherry, DrySherry.FdcId, DrySherry.Text).Should().BeNull();
            AlcoholLineContext.Refuse(sherry, RedWineVinegar.FdcId, RedWineVinegar.Text)!.Code.Should().Be(LinkGate.AlcoholDrink);
            AlcoholLineContext.Refuse(sherry, null, "Alcoholic beverage, wine, dessert, dry").Should().NotBeNull("a food with no FDC id is not in the table");

            var vinegar = AlcoholLineContext.Read("sherry vinegar");
            AlcoholLineContext.Refuse(vinegar, RedWineVinegar.FdcId, RedWineVinegar.Text).Should().BeNull();
            AlcoholLineContext.Refuse(vinegar, DrySherry.FdcId, DrySherry.Text)!.Code.Should().Be(LinkGate.AlcoholNotDrink);

            var sweetCider = AlcoholLineContext.Read("sweet cider");
            AlcoholLineContext.Refuse(sweetCider, CiderVinegar.FdcId, CiderVinegar.Text)!.Code.Should().Be(LinkGate.AlcoholNotDrink, "sweet cider is apple juice, never a vinegar");
            AlcoholLineContext.Refuse(sweetCider, "173933", "Apple juice, canned or bottled, unsweetened, without added ascorbic acid").Should().BeNull();

            AlcoholLineContext.Refuse(AlcoholLineContext.Read("1 tsp peppermint extract"), "173474", "Peppermint, fresh")!.Code.Should().Be(LinkGate.AlcoholNotDrink, "a flavouring is an extract");
            AlcoholLineContext.Refuse(AlcoholLineContext.Read("1 tsp peppermint extract"), VanillaExtract.FdcId, VanillaExtract.Text).Should().BeNull();
            AlcoholLineContext.Refuse(AlcoholLineContext.Read("1/4 cup dry vermouth"), "174837", "Alcoholic beverage, wine, table, white")!.Code.Should().Be(LinkGate.AlcoholNoFood);
        }

        [Theory]
        [InlineData("1 cup Burgundy Pinot Noir")]
        [InlineData("1/2 cup Sauternes dessert wine")]
        [InlineData("1 cup Burgundy table wine")]
        [InlineData("2 cups Rioja Tempranillo")]
        [InlineData("1 cup vodka infused with lemon peel")]
        public void A_place_word_before_a_grape_or_wine_noun_and_a_drink_infused_with_something_stay_the_drink(string line)
        {
            var reading = AlcoholLineContext.Read(line);
            reading.Sense.Should().NotBe("dish", "review CK10S-3: these read as dishes and lost their drink");
            reading.Kind.Should().Be(AlcoholSenseKind.Drink);
        }

        [Theory]
        [InlineData("2 cubes chicken flavor bouillon", "none")]
        [InlineData("1 (3 oz) pkg lemon flavor gelatin", "none")]
        [InlineData("1 envelope onion flavour soup mix", "none")]
        [InlineData("1 tsp rum flavor", "extract")]
        [InlineData("1 tsp vanilla flavoring to taste", "extract")]
        [InlineData("1/2 tsp butter flavouring, optional", "extract")]
        [InlineData("2 tsp essence of anchovies", "extract-food")]
        [InlineData("1 tsp essence of lemon", "extract")]
        public void A_product_named_with_a_flavour_word_is_not_a_flavouring_and_a_fish_essence_is_a_food_extract(string line, string sense)
        {
            AlcoholLineContext.Read(line).Sense.Should().Be(sense);
        }

        [Fact]
        public void A_flavour_word_in_a_product_name_does_not_hold_the_product_or_empty_its_shortlist()
        {
            var bouillon = new GateFood(278801, "Chicken Bouillon", "Soup, chicken broth or bouillon, dry", "sr_legacy_food", Curated, 6, NutrientRows: 32, FdcId: "171562");
            var gelatin = new GateFood(276108, "Gelatin Dessert Mix", "Gelatin desserts, dry mix", "sr_legacy_food", Curated, 19, NutrientRows: 32, FdcId: "168775");
            var catalog = GateCatalog.Build(new[] { bouillon, gelatin, VanillaExtract });

            LinkGate.Check(new GatePair("2 cubes chicken flavor bouillon", 1, bouillon, ReadersAgreed: true), catalog).Select(h => h.Code).Should().NotContain(LinkGate.AlcoholNotDrink);
            LinkGate.Check(new GatePair("lemon flavor gelatin", 1, gelatin, ReadersAgreed: true), catalog).Select(h => h.Code).Should().NotContain(LinkGate.AlcoholNotDrink);
            AlcoholLineContext.Refuse(AlcoholLineContext.Read("2 tsp essence of anchovies"), "174183", "Fish, anchovy, european, canned in oil, drained solids").Should().BeNull("a fish essence is not held for lacking an extract word");

            var matcher = new FoodNameMatcher(new[] { new FoodCandidate(278801, "171562", "Soup, chicken broth or bouillon, dry", "sr_legacy_food") });
            matcher.Shortlist("chicken bouillon", 10, AlcoholLineContext.Read("chicken flavor bouillon")).Select(c => c.FdcId).Should().Equal("171562");
        }

        [Fact]
        public void Every_drink_sense_has_foods_and_no_other_sense_has_any()
        {
            var drinks = AlcoholLineContext.SenseCodes.Where(s => AlcoholLineContext.KindOf(s) == AlcoholSenseKind.Drink).ToList();

            drinks.Should().NotBeEmpty();
            drinks.Should().OnlyContain(s => AlcoholLineContext.Allowed(s).Count > 0);
            AlcoholLineContext.SenseCodes.Where(s => AlcoholLineContext.KindOf(s) is AlcoholSenseKind.Held or AlcoholSenseKind.Unclear or AlcoholSenseKind.NotDrink)
                .Should().OnlyContain(s => AlcoholLineContext.Allowed(s).Count == 0);
            AlcoholLineContext.Allowed("fortified-dry").Should().NotIntersectWith(AlcoholLineContext.Allowed("spirit"));
            AlcoholLineContext.Allowed("beer").Should().NotContain("174863", "the malt beverage includes non-alcoholic beer");
        }

        [Fact]
        public void A_glass_of_sherry_never_reaches_red_wine_vinegar_brandy_never_the_cream_liqueur_and_beer_never_the_malt_beverage()
        {
            Codes(new GatePair("glass of sherry", 1, RedWineVinegar, ReadersAgreed: true)).Should().Contain(LinkGate.AlcoholDrink);
            Codes(new GatePair("brandy", 1, CoffeeCreamLiqueur, ModelPick: true)).Should().Contain(LinkGate.AlcoholDrink);
            Codes(new GatePair("beer", 1, MaltBeverage, ReadersAgreed: true)).Should().Contain(LinkGate.AlcoholDrink);
            Codes(new GatePair("sweet cider", 1, CiderVinegar, ReadersAgreed: true)).Should().Contain(LinkGate.AlcoholNotDrink);
            Codes(new GatePair("brandy", 1, Spirit, ReadersAgreed: true)).Should().NotContain(new[] { LinkGate.AlcoholDrink, LinkGate.AlcoholNoFood, LinkGate.AlcoholNotDrink });
        }

        [Fact]
        public void With_lines_any_line_whose_sense_refuses_the_food_holds_the_pair()
        {
            var lines = new[]
            {
                new GateLine("1 glass of sherry", "glass", 120m),
                new GateLine("2 tablespoons sherry", "tablespoon", 30m),
            };
            Codes(new GatePair("sherry", 1, DrySherry, ReadersAgreed: true, Lines: lines)).Should().NotContain(LinkGate.AlcoholDrink);
            Codes(new GatePair("sherry", 1, DrySherry, ReadersAgreed: true, Lines: lines.Append(new GateLine("1 tbsp sherry vinegar", "tablespoon", 15m)).ToList()))
                .Should().Contain(LinkGate.AlcoholNotDrink, "one line is a vinegar");

            var bare = new[] { new GateLine("1 cup wine", "cup", 240m, new AlcoholRecipeContext("Chicken stew", HasSourceSite: true)) };
            var table = new GateFood(287419, "Alcoholic beverage, wine, table, all", "Alcoholic beverage, wine, table, all", "sr_legacy_food", Pending, 14, NutrientRows: 33, FdcId: "173185");
            Codes(new GatePair("wine", 1, table, ReadersAgreed: true, Lines: bare)).Should().NotContain(LinkGate.AlcoholNoFood, "a modern recipe reads bare wine as table wine");
            Codes(new GatePair("wine", 1, table, ReadersAgreed: true)).Should().Contain(LinkGate.AlcoholNoFood, "with no lines the wording alone is unclear");
        }

        [Fact]
        public void The_shortlist_of_a_wording_with_a_sense_holds_only_the_foods_of_that_sense()
        {
            var matcher = new FoodNameMatcher(new[]
            {
                new FoodCandidate(282237, "175098", "Alcoholic beverage, liqueur, coffee with cream, 34 proof", "sr_legacy_food"),
                new FoodCandidate(287443, "174815", "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof", "sr_legacy_food"),
                new FoodCandidate(279459, "172240", "Vinegar, red wine", "sr_legacy_food"),
                new FoodCandidate(287461, "175112", "Alcoholic beverage, wine, dessert, dry", "sr_legacy_food"),
                new FoodCandidate(287420, "173190", "Alcoholic beverage, wine, table, red", "sr_legacy_food"),
            });

            matcher.Shortlist("alcoholic beverage", 10).Should().HaveCount(4, "with no sense every alcoholic beverage is listed");
            matcher.Shortlist("alcoholic beverage", 10, AlcoholLineContext.Read("brandy")).Select(c => c.FdcId).Should().Equal("174815");
            matcher.Shortlist("red wine", 10, AlcoholLineContext.Read("red wine")).Select(c => c.FdcId).Should().Equal("173190");
            matcher.Shortlist("red wine vinegar", 10, AlcoholLineContext.Read("red wine vinegar")).Select(c => c.FdcId).Should().Equal("172240");
            matcher.Shortlist("wine", 10, AlcoholLineContext.Read("wine")).Should().BeEmpty("bare wine has no food until its recipe tells");
        }

        [Fact]
        public void The_sense_rules_are_part_of_the_gate_signature()
        {
            LinkGate.SignatureSource().Should().Contain(AlcoholLineContext.SignatureSource());
            AlcoholLineContext.SignatureSource().Should().Contain("sense:fortified-dry=Drink:175112").And.Contain(AlcoholLineContext.Version);
        }

        [Theory]
        [InlineData("1 cup dry white wine", true)]
        [InlineData("1 glass of sherry", true)]
        [InlineData("1 pint cider", true)]
        [InlineData("brandy butter", true)]
        [InlineData("1/4 cup dry vermouth", true)]
        [InlineData("1 tablespoon sherry vinegar", false)]
        [InlineData("1 tsp rum extract", false)]
        [InlineData("2 oz ginger beer", false)]
        [InlineData("1/2 cup oyster liquor", false)]
        [InlineData("a wine glass of milk", false)]
        [InlineData("1 cup non-alcoholic beer", false)]
        public void The_diet_gate_counts_drinks_and_dishes_named_after_them_not_vinegars_flavourings_or_soft_drinks(string line, bool counts)
        {
            AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(line)).Should().Be(counts);
        }
    }
}
