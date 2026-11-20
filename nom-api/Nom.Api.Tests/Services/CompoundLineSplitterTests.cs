using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Which recipe lines naming more than one food are split, moved to their first alternative or
    /// left alone, and what each part line says. Lines here are real staging rows.
    /// </summary>
    public class CompoundLineSplitterTests
    {
        internal static readonly Dictionary<string, long> Foods = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Salt"] = 1, ["Pepper"] = 2, ["Paprika"] = 3, ["Cayenne Pepper"] = 4, ["Flour"] = 5, ["Sugar"] = 6, ["Butter"] = 7,
            ["Lard"] = 8, ["Lemon Juice"] = 9, ["Lemon Peel"] = 10, ["Chicken Broth"] = 11, ["Vegetable Broth"] = 12, ["Milk"] = 13,
            ["Water"] = 14, ["Olive Oil"] = 15, ["Avocados"] = 16, ["Avocado Oil"] = 17, ["Grapes"] = 18, ["Cherry Tomatoes"] = 19,
            ["Eggs"] = 20, ["Egg Yolk"] = 21, ["Peas"] = 22, ["Green Beans"] = 23, ["Tomatoes"] = 24, ["Almonds"] = 25,
            ["Brown Sugar"] = 26, ["Cinnamon"] = 27, ["Nutmeg"] = 28, ["Half And Half"] = 29, ["Bread Crumbs"] = 31, ["Honey"] = 32,
            ["Garlic"] = 33, ["Onion Powder"] = 34, ["Fennel"] = 35, ["Coriander Seeds"] = 36, ["Oranges"] = 37, ["Red Bell Pepper"] = 38,
            ["Dark Chocolate Chips"] = 39, ["Orange Juice"] = 40, ["Lemons"] = 41, ["Ham"] = 42, ["Bacon Fat"] = 43, ["Orange Peel"] = 44,
        };

        private static readonly HashSet<string> Pieces = new(StringComparer.OrdinalIgnoreCase)
        {
            "Avocados", "Eggs", "Egg Yolk", "Oranges", "Red Bell Pepper", "Lemons", "Tomatoes", "Cherry Tomatoes",
        };

        internal static CompoundVocabulary Vocabulary() => CompoundVocabulary.Build(
            Foods.Select(f => new CompoundFood(f.Value, f.Key, true, Pieces.Contains(f.Key))).Append(new CompoundFood(30, "Peas and carrots, frozen", false)),
            new[]
            {
                new CompoundAlias(1, "Salt, table", IngredientLinkService.UsdaDescriptionContext),
                new CompoundAlias(2, "Spices, pepper, black", IngredientLinkService.UsdaDescriptionContext),
                new CompoundAlias(4, "Spices, pepper, red or cayenne", IngredientLinkService.UsdaDescriptionContext),
                new CompoundAlias(26, "Sugars, brown", IngredientLinkService.UsdaDescriptionContext),
                new CompoundAlias(7, "oleo", null),
                new CompoundAlias(7, "oleomargarine", "retired:99"),
            },
            Foods.Keys.Concat(new[] { "grape tomatoes", "honey syrup", "Kosher salt", "butter or margarine", "milk chocolate chips", "orange bell pepper",
                "garlic powder", "fennel seeds", "ham fat", "lemon rind" }));

        private static readonly CompoundVocabulary V = Vocabulary();

        private static CompoundPlan? Plan(string line) => CompoundLineSplitter.Plan(line, V);

        private static IEnumerable<(string Food, string Line, decimal Quantity, string? Unit)> Parts(CompoundPlan plan) =>
            plan.Parts.Select(p => (Foods.Single(f => f.Value == p.FoodId).Key, p.RawLine, p.Quantity?.Quantity ?? 0m, p.Quantity?.MeasurementName));

        [Fact]
        public void Salt_and_pepper_to_taste_becomes_two_unmeasured_rows()
        {
            var plan = Plan("salt and pepper to taste")!;

            plan.Rule.Should().Be(CompoundLineSplitter.AndSaltPepper);
            plan.Kind.Should().Be(CompoundKind.Split);
            Parts(plan).Should().Equal(("Salt", "salt to taste", 0m, null), ("Pepper", "pepper to taste", 0m, null));
        }

        [Theory]
        [InlineData("Pepper and Salt", "Pepper", "Pepper", "Salt", "Salt")]
        [InlineData("fine sea salt and freshly-cracked black pepper (to taste)", "Salt", "fine sea salt (to taste)", "Pepper", "freshly-cracked black pepper (to taste)")]
        [InlineData("Salt and pepper to taste ($0.05)", "Salt", "Salt to taste ($0.05)", "Pepper", "pepper to taste ($0.05)")]
        [InlineData("a little pepper and salt", "Pepper", "a little pepper", "Salt", "a little salt")]
        public void Salt_and_pepper_in_any_order_or_spelling_splits(string line, string firstFood, string firstLine, string secondFood, string secondLine)
        {
            var plan = Plan(line)!;

            plan.Rule.Should().Be(CompoundLineSplitter.AndSaltPepper);
            Parts(plan).Should().Equal((firstFood, firstLine, 0m, null), (secondFood, secondLine, 0m, null));
        }

        [Fact]
        public void An_unquantified_list_of_known_foods_gives_one_row_per_food()
        {
            var plan = Plan("salt, pepper, and paprika")!;

            plan.Rule.Should().Be(CompoundLineSplitter.AndUnquantified);
            Parts(plan).Select(p => (p.Food, p.Line)).Should().Equal(("Salt", "salt"), ("Pepper", "pepper"), ("Paprika", "paprika"));
        }

        [Theory]
        [InlineData("1 cup each flour and sugar", "1 cup flour", "1 cup sugar", 1, "Cup")]
        [InlineData("two tablespoonfuls each of butter and flour", "two tablespoonfuls of butter", "two tablespoonfuls of flour", 2, "Tablespoon")]
        [InlineData("1/2 tsp each sea salt and black pepper ((plus more to taste))", "1/2 tsp sea salt ((plus more to taste))", "1/2 tsp black pepper ((plus more to taste))", 0.5, "Teaspoon")]
        public void Each_gives_every_food_the_full_amount(string line, string firstLine, string secondLine, double quantity, string unit)
        {
            var plan = Plan(line)!;

            plan.Rule.Should().Be(CompoundLineSplitter.AndEach);
            plan.Parts.Select(p => p.RawLine).Should().Equal(firstLine, secondLine);
            plan.Parts.Should().OnlyContain(p => p.Quantity!.Quantity == (decimal)quantity && p.Quantity.MeasurementName == unit);
        }

        [Theory]
        [InlineData("2 tablespoons butter and 2 tablespoons flour", "Butter", "2 tablespoons butter", 2, "Tablespoon", "Flour", "2 tablespoons flour", 2, "Tablespoon")]
        [InlineData("2 large eggs, plus 1 egg yolk", "Eggs", "2 large eggs", 2, "Piece", "Egg Yolk", "1 egg yolk", 1, "Piece")]
        public void Parts_with_their_own_amounts_keep_them(string line, string food1, string line1, double q1, string u1, string food2, string line2, double q2, string u2)
        {
            var plan = Plan(line)!;

            plan.Rule.Should().Be(CompoundLineSplitter.OwnQuantity);
            Parts(plan).Should().Equal((food1, line1, (decimal)q1, u1), (food2, line2, (decimal)q2, u2));
        }

        [Theory]
        [InlineData("The rind and juice of 1 lemon", "Lemon Peel", "The rind of 1 lemon", "Lemon Juice", "The juice of 1 lemon")]
        [InlineData("grated rind and juice of 1/2 lemon", "Lemon Peel", "grated rind of 1/2 lemon", "Lemon Juice", "juice of 1/2 lemon")]
        public void Juice_and_rind_of_a_lemon_gives_both_parts_the_fruit_count(string line, string food1, string line1, string food2, string line2)
        {
            var plan = Plan(line)!;

            plan.Rule.Should().Be(CompoundLineSplitter.PartPair);
            Parts(plan).Select(p => (p.Food, p.Line)).Should().Equal((food1, line1), (food2, line2));
            plan.Parts.Select(p => p.Quantity).Distinct().Should().ContainSingle();
        }

        [Fact]
        public void A_measured_salt_and_pepper_keeps_the_amount_on_the_salt()
        {
            var plan = Plan("1/4 tsp kosher salt and black pepper")!;

            plan.Rule.Should().Be(CompoundLineSplitter.AndQuantifiedSaltPepper);
            Parts(plan).Should().Equal(("Salt", "1/4 tsp kosher salt", 0.25m, "Teaspoon"), ("Pepper", "black pepper", 0m, null));
        }

        [Theory]
        [InlineData("1/2 cup butter or lard", "Butter", CompoundLineSplitter.OrIndependent)]
        [InlineData("4 cups chicken or vegetable broth", "Chicken Broth", CompoundLineSplitter.OrSharedHead)]
        [InlineData("1½ T butter or margarine", "Butter", CompoundLineSplitter.OrFirstNamed)]
        [InlineData("1 tsp butter or olive oil", "Butter", CompoundLineSplitter.OrIndependent)]
        [InlineData("1 pint of boiling milk, or 1 pint of boiling cream", "Milk", CompoundLineSplitter.OrFirstNamed)]
        [InlineData("3 tablespoons butter or margarine, melted", "Butter", CompoundLineSplitter.OrFirstNamed)]
        public void A_line_with_alternatives_goes_to_its_first(string line, string food, string rule)
        {
            var plan = Plan(line)!;

            plan.Kind.Should().Be(CompoundKind.PickAlternative);
            plan.Rule.Should().Be(rule);
            plan.Parts.Should().ContainSingle().Which.FoodId.Should().Be(Foods[food]);
        }

        [Theory]
        [InlineData("1 Tbsp avocado or olive oil", CompoundLineSplitter.LeaveOrAmbiguous)]
        [InlineData("1 cup grape or cherry tomatoes", CompoundLineSplitter.LeaveOrAmbiguous)]
        [InlineData("2 tsp honey or maple syrup", CompoundLineSplitter.LeaveOrAmbiguous)]
        [InlineData("½ c cream or milk", CompoundLineSplitter.LeaveOrLastNamed)]
        [InlineData("2 ounces Sherry or Madeira", CompoundLineSplitter.LeaveOrUnresolved)]
        [InlineData("a teaspoonful of lemon or vanilla", CompoundLineSplitter.LeaveFlavour)]
        [InlineData("1 cup packed light or dark brown sugar", CompoundLineSplitter.LeaveOrLastNamed)]
        [InlineData("1 can pork and beans", CompoundLineSplitter.LeaveProtected)]
        [InlineData("1 box macaroni and cheese", CompoundLineSplitter.LeaveProtected)]
        [InlineData("sweet and sour sauce", CompoundLineSplitter.LeaveProtected)]
        [InlineData("1 bag salt and vinegar chips", CompoundLineSplitter.LeaveProtected)]
        [InlineData("1 cup peas and carrots", CompoundLineSplitter.LeaveProtected)]
        [InlineData("blanched and chopped almonds", CompoundLineSplitter.LeavePreparation)]
        [InlineData("3/4 cup butter and lard", CompoundLineSplitter.LeaveQuantifiedList)]
        [InlineData("milk or milk and water", CompoundLineSplitter.LeaveAndWithOr)]
        [InlineData("1 tsp each of cloves, cinnamon and nutmeg", CompoundLineSplitter.LeaveNotInCatalog)]
        [InlineData("salt, pepper, paprika, cinnamon and nutmeg", CompoundLineSplitter.LeaveTooManyNames)]
        [InlineData("salt and pepper + 1 tsp salt", CompoundLineSplitter.LeaveFolded)]
        [InlineData("salt and dragonfruit", CompoundLineSplitter.LeaveNotInCatalog)]
        [InlineData("3/4 cup milk or dark chocolate chips", CompoundLineSplitter.LeaveOrAmbiguous)]
        [InlineData("2 orange or red bell peppers, sliced", CompoundLineSplitter.LeaveOrAmbiguous)]
        [InlineData("1 medium orange or red bell pepper", CompoundLineSplitter.LeaveOrAmbiguous)]
        [InlineData("ham or bacon fat", CompoundLineSplitter.LeaveOrAmbiguous)]
        [InlineData("2 teaspoons grated lemon or orange rind", CompoundLineSplitter.LeaveOrAmbiguous)]
        [InlineData("one teaspoonful of lemon or vanilla flavor", CompoundLineSplitter.LeaveFlavour)]
        [InlineData("1/2 teaspoon each garlic and onion powder", CompoundLineSplitter.LeaveSharedHead)]
        [InlineData("1/4 tsp each garlic and onion powders", CompoundLineSplitter.LeaveSharedHead)]
        [InlineData("two ounces each of fennel and coriander seeds", CompoundLineSplitter.LeaveSharedHead)]
        [InlineData("2 tablespoonfuls each of lemon and orange juice", CompoundLineSplitter.LeaveSharedHead)]
        [InlineData("A very little black pepper and salt", CompoundLineSplitter.LeaveNotFixedPoint)]
        [InlineData("a very little salt and pepper", CompoundLineSplitter.LeaveNotFixedPoint)]
        [InlineData("1 quarter teaspoonful each of salt and pepper", CompoundLineSplitter.LeaveNotFixedPoint)]
        [InlineData("3 tablespoons lemon juice and grated rind", CompoundLineSplitter.LeaveNotFixedPoint)]
        [InlineData("1/2 butter and 1/2 lard", CompoundLineSplitter.LeaveBareNumber)]
        [InlineData("1-1/2 butter and 1-1/2 teaspoons baking powder", CompoundLineSplitter.LeaveBareNumber)]
        [InlineData("½ or ¾ milk and ½ or ¼ water", CompoundLineSplitter.LeaveBareNumber)]
        [InlineData("ounce each of butter and lard", CompoundLineSplitter.LeaveEachUnread)]
        [InlineData("1 pinch of pepper and one of salt", CompoundLineSplitter.LeaveQuantifiedList)]
        [InlineData("lemon or nutmeg", CompoundLineSplitter.LeaveFlavour)]
        public void Lines_the_rules_do_not_trust_are_left_with_a_reason(string line, string reason)
        {
            var plan = Plan(line);

            plan.Should().NotBeNull();
            plan!.Kind.Should().Be(CompoundKind.Leave);
            plan.LeaveReason.Should().Be(reason);
        }

        [Theory]
        [InlineData("2 cups half and half")]
        [InlineData("1 and 1/2 cups flour")]
        [InlineData("yolks of two or three eggs")]
        [InlineData("2 or 3 eggs")]
        [InlineData("1 cup flour")]
        [InlineData("garlic, minced")]
        [InlineData("3/4 cup (170g) unsalted butter, melted and slightly cooled")]
        [InlineData("yolk of one or more eggs")]
        [InlineData("2 cloves garlic, pressed or minced")]
        public void A_line_naming_one_food_is_not_compound(string line)
        {
            Plan(line).Should().BeNull();
        }

        [Theory]
        [InlineData("salt and pepper to taste")]
        [InlineData("Pepper and Salt")]
        [InlineData("fine sea salt and freshly-cracked black pepper (to taste)")]
        [InlineData("some salt and pepper")]
        [InlineData("salt, pepper, and paprika")]
        [InlineData("1 cup each flour and sugar")]
        [InlineData("two tablespoonfuls each of butter and flour")]
        [InlineData("2 tablespoons butter and 2 tablespoons flour")]
        [InlineData("2 large eggs, plus 1 egg yolk")]
        [InlineData("The rind and juice of 1 lemon")]
        [InlineData("1/4 tsp kosher salt and black pepper")]
        [InlineData("salt and cayenne to taste")]
        public void Every_part_line_reads_back_as_the_row_it_becomes(string line)
        {
            var plan = Plan(line)!;
            plan.Kind.Should().Be(CompoundKind.Split);
            var acceptable = RecipeVettingService.IsAcceptablyUnquantified(line);

            foreach (var part in plan.Parts)
            {
                IngredientLineParser.Parse(part.RawLine).Should().Be(part.Quantity, part.RawLine);
                var food = V.Resolve(IngredientNameNormalizer.Normalize(part.RawLine));
                if (plan.Rule == CompoundLineSplitter.PartPair) food.Should().Match(f => f == null || f == part.FoodId, part.RawLine);
                else food.Should().Be(part.FoodId, part.RawLine);
                if (acceptable && part.Quantity == null) RecipeVettingService.IsAcceptablyUnquantified(part.RawLine).Should().BeTrue(part.RawLine);
                line.Should().Contain(part.RawLine.Split(' ')[^1].Trim('(', ')'), "a part line only deletes words from its source");
            }
        }
    }

    /// <summary>The names a part of a compound line may be read as.</summary>
    public class CompoundVocabularyTests
    {
        private static readonly CompoundVocabulary V = CompoundLineSplitterTests.Vocabulary();

        [Fact]
        public void A_short_curated_name_with_and_protects_its_first_segment()
        {
            V.ProtectedNames.Should().Contain("peas and carrots");
            V.HoldsProtected("peas and carrots").Should().BeTrue();
            V.HoldsProtected("salt and pepper").Should().BeFalse();
        }

        [Fact]
        public void Only_a_retired_name_resolves_among_the_aliases()
        {
            V.Resolve("oleo").Should().BeNull("an alias with no context is import residue");
            V.Resolve("oleomargarine").Should().Be(7);
        }

        [Theory]
        [InlineData("kosher salt", 1)]
        [InlineData("fine sea salt", 1)]
        [InlineData("freshly ground black pepper", 2)]
        [InlineData("freshly-cracked black pepper", 2)]
        [InlineData("cayenne", 4)]
        [InlineData("bread-crumbs", 31)]
        [InlineData("large eggs", 20)]
        public void Staple_salt_and_pepper_forms_and_hyphens_resolve(string part, long food)
        {
            V.Resolve(part).Should().Be(food);
        }

        [Fact]
        public void Without_the_staple_foods_salt_and_pepper_resolve_to_nothing()
        {
            var bare = CompoundVocabulary.Build(new[] { new CompoundFood(7, "Butter", true) }, Array.Empty<CompoundAlias>(), new[] { "Butter" });

            bare.Resolve("kosher salt").Should().BeNull();
            CompoundLineSplitter.Plan("salt and pepper to taste", bare)!.Kind.Should().Be(CompoundKind.Leave);
        }
    }
}
