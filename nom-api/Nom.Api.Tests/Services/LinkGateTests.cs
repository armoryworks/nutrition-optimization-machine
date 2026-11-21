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
    /// The link gate's rules that need more than a wording and a description: trusted aliases of
    /// another food, the family check, the units and grams checks on a wording's lines, reader
    /// answers read by their head noun, and the catalog the gate builds. Descriptions are real SR
    /// Legacy rows; the cases come from the wordings design (gelatine water lines, glucose by the
    /// pound, claret against claret wine).
    /// </summary>
    public class LinkGateTests
    {
        private const long Curated = (long)CurationStatusEnum.Curated;
        private const long Pending = (long)CurationStatusEnum.PendingCuration;

        private static readonly GateFood RedWine = new(287420, "Alcoholic beverage, wine, table, red", "Alcoholic beverage, wine, table, red", "sr_legacy_food", Pending, 14, NutrientRows: 33);
        private static readonly GateFood Claret = new(287424, "Alcoholic Beverage, wine, table, red, Claret", "Alcoholic Beverage, wine, table, red, Claret", "sr_legacy_food", Pending, 14, NutrientRows: 6);
        private static readonly GateFood Gelatin = new(276108, "Gelatin Dessert Mix", "Gelatin desserts, dry mix", "sr_legacy_food", Curated, 19, NutrientRows: 32);
        private static readonly GateFood CornSyrup = new(276169, "Light Corn Syrup", "Syrups, corn, light", "sr_legacy_food", Curated, 19, NutrientRows: 32);
        private static readonly GateFood TableSalt = new(280659, "Salt", "Salt, table", "sr_legacy_food", Curated, 2, NutrientRows: 33);

        private static GateCatalog Catalog(IReadOnlyDictionary<string, long>? aliases = null, IReadOnlyDictionary<int, decimal>? bounds = null, IReadOnlyDictionary<string, string>? lexicon = null) =>
            GateCatalog.Build(new[] { RedWine, Claret, Gelatin, CornSyrup, TableSalt }, trustedAliases: aliases, gramBounds: bounds, lexicon: lexicon);

        private static IReadOnlyList<string> Codes(GatePair pair, GateCatalog catalog) => LinkGate.Check(pair, catalog).Select(h => h.Code).ToList();

        [Fact]
        public void A_wording_a_trusted_alias_gives_to_another_food_is_held()
        {
            var catalog = Catalog(new Dictionary<string, long> { ["sea salt"] = 9 });

            Codes(new GatePair("sea salt", 1, TableSalt), catalog).Should().Contain(LinkGate.OtherAlias);
            Codes(new GatePair("sea salt", 9, TableSalt), catalog).Should().NotContain(LinkGate.OtherAlias, "the alias is the source's own");
        }

        [Fact]
        public void Claret_is_held_for_its_six_nutrient_rows_and_its_family_settles_on_red_table_wine()
        {
            var catalog = Catalog();

            LinkGate.Check(new GatePair("claret", 1, Claret, ReadersAgreed: true), catalog)
                .Should().Contain(h => h.Code == LinkGate.Incomplete && h.Detail!.Contains("6 rows"));
            var family = LinkGate.Family(new[] { ("claret", Claret), ("claret wine", RedWine), ("Claret", Claret) }, catalog);

            family.Select(f => f.Key).Distinct().Should().ContainSingle().Which.Should().Be("claret");
            family.Should().OnlyContain(f => f.Winner == RedWine.Id);
            family.Select(f => f.Keeps).Should().Equal(false, true, false);
        }

        [Fact]
        public void A_family_whose_best_two_foods_tie_writes_neither()
        {
            var sweet = new GateFood(287418, "Alcoholic beverage, wine, dessert, sweet", "Alcoholic beverage, wine, dessert, sweet", "sr_legacy_food", Pending, 14, NutrientRows: 33);

            var family = LinkGate.Family(new[] { ("sack", RedWine), ("sacks", sweet) }, Catalog());

            family.Should().OnlyContain(f => f.Winner == null && !f.Keeps);
        }

        [Fact]
        public void Wordings_that_differ_only_by_plural_hyphen_or_a_trailing_wine_share_a_family_key()
        {
            LinkGate.FamilyKey("egg-plant").Should().Be(LinkGate.FamilyKey("eggplant"));
            LinkGate.FamilyKey("Eggs").Should().Be(LinkGate.FamilyKey("egg"));
            LinkGate.FamilyKey("madeira wine").Should().Be(LinkGate.FamilyKey("madeira"));
            LinkGate.FamilyKey("wine").Should().Be("wine");
        }

        [Fact]
        public void Gelatine_lines_measured_by_the_gallon_fail_the_units_check()
        {
            var lines = new[]
            {
                new GateLine("1 box gelatine", "box", 85m),
                new GateLine("2 tablespoons gelatine", "tablespoon", 18m),
                new GateLine("1 gallon water", "gallon", 3785m),
            };

            LinkGate.Check(new GatePair("gelatine", 1, Gelatin, ReadersAgreed: true, Lines: lines), Catalog())
                .Should().Contain(h => h.Code == LinkGate.Units && h.Rule == "liquid measures on a dry food");
        }

        [Fact]
        public void Glucose_by_the_pound_passes_the_units_and_grams_checks_under_its_bound()
        {
            var lines = new[] { new GateLine("3 lbs glucose", "pound", 1361m), new GateLine("1 cup glucose", "cup", 328m) };
            var catalog = Catalog(bounds: new Dictionary<int, decimal> { [19] = 2000m }, lexicon: new Dictionary<string, string> { ["glucose"] = "corn syrup" });

            Codes(new GatePair("glucose", 1, CornSyrup, ReadersAgreed: true, Lines: lines), catalog)
                .Should().NotContain(new[] { LinkGate.Units, LinkGate.Grams, LinkGate.HeadNoun });
            Codes(new GatePair("glucose", 1, CornSyrup, ReadersAgreed: true, Lines: lines), Catalog(bounds: new Dictionary<int, decimal> { [19] = 500m }))
                .Should().Contain(LinkGate.Grams);
        }

        [Fact]
        public void Lines_the_food_cannot_weigh_fail_the_units_check()
        {
            var lines = new[] { new GateLine("1 box gelatine", "box", null), new GateLine("1 package gelatine", "package", null), new GateLine("2 tbsp gelatine", "tablespoon", 18m) };

            Codes(new GatePair("gelatine", 1, Gelatin, ReadersAgreed: true, Lines: lines), Catalog()).Should().Contain(LinkGate.Units);
        }

        [Fact]
        public void Two_blind_readers_answer_is_read_by_its_head_noun_not_by_every_word()
        {
            var milk = new GateFood(274581, "Milk", "Milk, whole, 3.25% milkfat, with added vitamin D", "foundation_food", Curated, 1, NutrientRows: 33);
            var catalog = GateCatalog.Build(new[] { milk, new GateFood(274575, "Milk, reduced fat", "Milk, reduced fat, fluid, 2% milkfat", "foundation_food", Curated, 1, NutrientRows: 33) });

            Codes(new GatePair("sweet milk", 1, milk, ModelPick: true), catalog).Should().Contain(LinkGate.Containment);
            Codes(new GatePair("sweet milk", 1, milk, ReadersAgreed: true), catalog).Should().BeEmpty();
            Codes(new GatePair("saleratus", 1, TableSalt, ReadersAgreed: true), catalog).Should().Contain(LinkGate.HeadNoun);
        }

        [Fact]
        public void A_lexicon_entry_carries_a_word_the_food_does_not_name()
        {
            var catalog = Catalog(lexicon: new Dictionary<string, string> { ["glucose"] = "corn syrup" });

            Codes(new GatePair("glucose", 1, CornSyrup, ReadersAgreed: true), catalog).Should().NotContain(LinkGate.HeadNoun);
            Codes(new GatePair("glucose", 1, CornSyrup, ReadersAgreed: true), Catalog()).Should().Contain(LinkGate.HeadNoun);
        }

        [Fact]
        public void The_catalog_groups_foods_by_the_lead_word_after_a_class_word_and_maps_curated_names()
        {
            var catalog = Catalog();

            LinkGate.LeadWord("Alcoholic beverage, distilled, rum, 80 proof").Should().Be("rum");
            LinkGate.LeadWord("Spices, tarragon, dried").Should().Be("tarragon");
            catalog.Siblings["wine"].Select(f => f.Id).Should().BeEquivalentTo(new[] { RedWine.Id, Claret.Id });
            catalog.Curated.Should().ContainKey("Salt").And.NotContainKey(RedWine.Name, "a pending food is not a curated name");
        }

        [Fact]
        public void A_drink_sense_read_from_the_lines_own_words_is_identity_evidence_and_one_read_from_the_recipe_is_not()
        {
            var dryDessert = new GateFood(287461, "Alcoholic beverage, wine, dessert, dry", "Alcoholic beverage, wine, dessert, dry", "sr_legacy_food", Pending, 14, NutrientRows: 31, FdcId: "175112");
            var whiteTable = new GateFood(164, "Dry White Wine", "Alcoholic beverage, wine, table, white", "sr_legacy_food", Curated, 14, NutrientRows: 33, FdcId: "174837");
            var unknownFdc = dryDessert with { FdcId = null };
            var catalog = GateCatalog.Build(new[] { RedWine, Claret, dryDessert, whiteTable,
                new GateFood(287419, "Alcoholic beverage, wine, table, all", "Alcoholic beverage, wine, table, all", "sr_legacy_food", Pending, 14, NutrientRows: 33, FdcId: "173185") });
            var mussels = new AlcoholRecipeContext(Title: "White Wine Mussels", HasSourceSite: true);

            Codes(new GatePair("sherry", 0, dryDessert, ModelPick: false), catalog).Should().BeEmpty("sherry reads as dry fortified wine, whose sense allows the dry dessert wine");
            Codes(new GatePair("sherry", 0, dryDessert, Lines: new[] { new GateLine("2 glasses of sherry", "glass", 206m) }), catalog).Should().BeEmpty();
            Codes(new GatePair("sherry", 0, unknownFdc), catalog).Should().Contain(LinkGate.Containment, "with no FDC id the sense cannot name the food, so the words decide");
            Codes(new GatePair("sherry", 0, dryDessert, Lines: new[] { new GateLine("2 glasses of sherry", "glass", 206m), new GateLine("1 cup sugar and sherry", "cup", 200m) }), catalog)
                .Should().Contain(LinkGate.Containment, "a line that is a list does not share the drink sense");
            Codes(new GatePair("wine", 0, whiteTable, Lines: new[] { new GateLine("1 cup wine", "cup", 240m, mussels) }), catalog)
                .Should().Contain(LinkGate.Generic, "bare wine is white only by its recipe; a sense the recipe gives lets the line through the alcohol rule but is no identity evidence");
        }

        [Fact]
        public void A_drink_sense_is_identity_evidence_only_for_the_first_ready_food_of_its_table_not_for_a_sibling_varietal()
        {
            var redTable = RedWine with { FdcId = "173190" };
            var merlot = new GateFood(287415, "Alcoholic Beverage, wine, table, red, Merlot", "Alcoholic Beverage, wine, table, red, Merlot", "sr_legacy_food", Pending, 14, NutrientRows: 25, FdcId: "171925");
            var whiteTable = new GateFood(164, "Dry White Wine", "Alcoholic beverage, wine, table, white", "sr_legacy_food", Curated, 14, NutrientRows: 33, FdcId: "174837");
            var chardonnay = new GateFood(287437, "Alcoholic beverage, wine, table, white, Chardonnay", "Alcoholic beverage, wine, table, white, Chardonnay", "sr_legacy_food", Pending, 14, NutrientRows: 25, FdcId: "174110");
            var catalog = GateCatalog.Build(new[] { redTable, merlot, whiteTable, chardonnay });

            Codes(new GatePair("claret", 0, redTable), catalog).Should().BeEmpty("red table wine is the first ready food of the red sense");
            foreach (var wording in new[] { "chianti", "pinot noir", "rioja", "claret" })
                Codes(new GatePair(wording, 0, merlot), catalog).Should().Contain(LinkGate.Containment, $"\"{wording}\" names no Merlot; the sense's first ready food is red table wine");
            foreach (var wording in new[] { "champagne", "prosecco", "riesling" })
                Codes(new GatePair(wording, 0, chardonnay), catalog).Should().Contain(LinkGate.Containment, $"\"{wording}\" names no Chardonnay; the sense's first ready food is the white table wine");
            Codes(new GatePair("merlot", 0, merlot), catalog).Should().BeEmpty("a wording that names the varietal is the varietal");
            Codes(new GatePair("chianti", 0, merlot), GateCatalog.Build(new[] { redTable with { NutrientRows = 6 }, merlot }))
                .Should().BeEmpty("when the foods before it in the table are not ready, the varietal is the food the sense names, as the alcohol tier answers");
        }

        [Fact]
        public void The_signature_names_the_rules_version_and_is_stable()
        {
            LinkGate.Signature.Should().MatchRegex("^[0-9a-f]{16}$").And.Be(LinkGate.Signature);
            LinkGate.Version.Should().StartWith("link-gate/");
        }

        [Fact]
        public void The_signature_covers_the_staples_the_prepared_categories_and_the_gates_own_word_lists()
        {
            var source = LinkGate.SignatureSource();

            StapleFoods.Entries.Should().NotBeEmpty();
            StapleFoods.Entries.Where(kv => !source.Contains(kv.Key + "=" + kv.Value + "|")).Should().BeEmpty("a changed staple changes what the gate lets through");
            CatalogAdmissionRule.PreparedCategories.Where(kv => !source.Contains($"{kv.Key}={kv.Value}|")).Should().BeEmpty("a changed prepared category changes what the prepared rule holds");
            source.Should().Contain("dried-cues:").And.Contain("polarity:").And.Contain("unsalted=salt free|").And.Contain("patterns:").And.Contain(@"^\s*for\s+the\b|")
                .And.Contain("egg-white:").And.Contain("only=only|").And.Contain("guard-joiners:").And.Contain("staple-filler:");
        }
    }
}
