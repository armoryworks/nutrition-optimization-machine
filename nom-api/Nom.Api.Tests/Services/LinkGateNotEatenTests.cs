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
    /// The link gate of <c>link-gate/15</c> (staging checkpoint 9, U3: 2 of 45 fresh accepts wrong): a shell, husk, cob or
    /// pit is not eaten and is not the food ("large, smooth oyster shells" to bake in, on Fresh Oysters), leading comma
    /// parts that hold only qualifiers are not the wording's food, and a state the recipe brings the food to ("whites
    /// beaten dry") is no product form (dried egg white). Descriptions are SR Legacy rows or the staging copy's names.
    /// </summary>
    public class LinkGateNotEatenTests
    {
        private const long Curated = (long)CurationStatusEnum.Curated;

        private static long _next = 950000;

        private static GateFood Food(string name, string description, int category) =>
            new(++_next, name, description, CatalogAdmissionRule.SrLegacyFood, Curated, category, NutrientRows: 30, CoreNutrients: true);

        private static IReadOnlyList<GateHold> Check(string wording, GateFood target, params GateFood[] kin) =>
            LinkGate.Check(new GatePair(wording, 0, target, ModelPick: true), GateCatalog.Build(kin.Append(target).ToList()));

        [Theory]
        [InlineData("large, smooth oyster shells", "Fresh Oysters", "Fresh Oysters", 15)]
        [InlineData("clam shells", "Clams", "Mollusks, clam, mixed species, raw", 15)]
        [InlineData("corn husks", "Corn", "Corn, sweet, yellow, raw", 11)]
        [InlineData("cherry pits", "Cherries", "Cherries, sweet, raw", 9)]
        public void A_shell_husk_cob_or_pit_is_not_the_food(string wording, string name, string description, int category)
        {
            Check(wording, Food(name, description, category)).Should().Contain(h => h.Code == LinkGate.Part && h.Rule == LinkGate.NotEatenRule);
        }

        [Theory]
        [InlineData("12 oysters on the half shell", "Fresh Oysters", "Fresh Oysters", 15)]
        [InlineData("pastry shells", "Pastry shell", "Pie crust, pastry shell, frozen", 18)]
        [InlineData("jumbo pasta shells", "Pasta shells", "Pasta, dry, enriched, shells", 20)]
        [InlineData("oysters", "Fresh Oysters", "Fresh Oysters", 15)]
        [InlineData("4 ears corn on the cob", "Corn", "Corn, sweet, yellow, raw", 11)]
        [InlineData("sweet corn in the husk", "Corn", "Corn, sweet, yellow, raw", 11)]
        public void A_food_in_its_shell_or_a_shell_shaped_food_is_no_part_not_eaten(string wording, string name, string description, int category)
        {
            Check(wording, Food(name, description, category)).Should().NotContain(h => h.Rule == LinkGate.NotEatenRule);
        }

        [Theory]
        [InlineData("Whites of 3 eggs beaten dry")]
        [InlineData("whites of four eggs beaten stiff")]
        [InlineData("egg whites, whipped dry")]
        public void A_state_the_recipe_brings_the_food_to_is_no_product_form(string wording)
        {
            var dried = Food("Egg, white, dried", "Egg, white, dried", 1);
            var raw = Food("Egg Whites", "Egg, white, raw, fresh", 1);
            Check(wording, dried, raw).Should().Contain(h => h.Code == LinkGate.State, "staging checkpoint 9, U3: 'beaten dry' was read as the dried powder");
            Check("dried egg whites", dried, raw).Should().NotContain(h => h.Code == LinkGate.State);
        }

        [Fact]
        public void The_version_and_signature_name_the_new_rules()
        {
            LinkGate.Version.Should().Be("link-gate/15");
            LinkGate.SignatureSource().Should().Contain("not-eaten:").And.Contain("shape=pastry");
        }
    }
}
