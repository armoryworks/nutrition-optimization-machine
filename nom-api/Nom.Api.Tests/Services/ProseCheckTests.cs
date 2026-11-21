using System.Linq;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The prose check rule by rule on small constructed texts; the golden rows of prose.tsv hold
    /// the cases of the C9 review, and those of prose-check.tsv the recipes read at checkpoint 2.
    /// </summary>
    public class ProseCheckTests
    {
        private static RewriteFidelity.SourceText Text(params string[] steps) => new(string.Empty, steps);

        private static ProseCheck.Finding Check(string[] source, string[] rewrite, string[]? lines = null, ProseStockIndex? stock = null) =>
            ProseCheck.Check(Text(source), Text(rewrite), lines, stock);

        private static readonly string[] Copied =
        {
            "Scatter the crumbled walnuts across the cooled sheet cake before slicing it into neat squares for the picnic basket.",
        };

        [Fact]
        public void A_finding_names_the_check_version_and_the_stock_index_it_ran_with()
        {
            var finding = Check(new[] { "Bake the bread for 30 minutes." }, new[] { "Bake the loaf 30 minutes." }, stock: new ProseStockIndex(7, Enumerable.Empty<long>()));

            finding.CheckVersion.Should().Be(ProseCheck.Version).And.Be(3);
            finding.StockVersion.Should().Be(7);
            finding.Passed.Should().BeTrue(finding.ToString());
        }

        [Fact]
        public void A_whole_distinctive_sentence_is_copied_text_however_many_sites_use_its_phrases()
        {
            var stock = new ProseStockIndex(1, ProsePhrases.Hashes(ProseCheck.CopyTokens(ProseQuantities.Prepare(Copied[0]))));

            Check(Copied, Copied.Select(s => "Then " + s).ToArray()).Reasons.Should().Contain(ProseCheck.CopiedText);
            Check(Copied, Copied.Select(s => "Then " + s).ToArray(), stock: stock).Reasons.Should().Contain(ProseCheck.CopiedText);
        }

        [Fact]
        public void A_bracketed_conversion_may_be_dropped_but_not_changed()
        {
            var source = new[] { "Preheat the oven to 350°F (175°C)." };

            Check(source, new[] { "Heat the oven to 350°F." }).Passed.Should().BeTrue();
            Check(source, new[] { "Heat the oven to 350°F (190°C)." }).Reasons.Should().Contain(ProseCheck.AddedQuantity);
        }

        [Fact]
        public void An_amount_in_brackets_that_is_no_conversion_is_required()
        {
            Check(new[] { "Bake in a moderate oven (350° F.) for 20 minutes." }, new[] { "Bake in a moderate oven for 20 minutes." })
                .Reasons.Should().Contain(ProseCheck.LostQuantity);
        }

        [Fact]
        public void A_count_of_one_and_a_soft_count_are_neither_required_nor_added()
        {
            Check(new[] { "Add the eggs 1 at a time and fold all four sides over." }, new[] { "Add each egg separately, then fold the edges in." })
                .Reasons.Should().NotContain(ProseCheck.QuantityReasons);
        }

        [Fact]
        public void Stirring_an_ingredient_in_also_adds_it()
        {
            Check(new[] { "Stir in the flour and cook for 2 minutes." }, new[] { "Add the flour, then cook 2 minutes." }).Passed.Should().BeTrue();
            Check(new[] { "Pour the milk over the oats." }, new[] { "Stir the milk with the oats." }).Reasons.Should().Contain(ProseCheck.LostStep);
        }

        [Fact]
        public void Heat_as_a_noun_does_not_keep_the_verb_heat()
        {
            Check(new[] { "Heat the milk, then stir in the cocoa over medium heat." }, new[] { "Stir the cocoa into the milk over medium heat." })
                .Faults.Should().Contain(f => f.Reason == ProseCheck.LostStep && f.Detail.Contains("heat"));
        }

        [Fact]
        public void An_ingredient_a_step_names_must_be_kept()
        {
            Check(new[] { "Fold the blueberries into the batter." }, new[] { "Fold everything together." }, new[] { "1 cup blueberries", "2 cups batter" })
                .Faults.Should().Contain(f => f.Reason == ProseCheck.LostStep && f.Detail.Contains("blueberries"));
        }

        [Fact]
        public void A_rewrite_far_longer_than_its_source_is_too_long()
        {
            var source = new[] { "Whisk the eggs with the sugar until pale, then fold in the flour and the melted butter and bake the sponge until it springs back." };
            var padded = new[]
            {
                "Whisk the eggs together with the sugar for quite a while until the mixture turns pale and thick and falls in ribbons from the whisk.",
                "Next fold in the flour gently with a large spatula, followed by the melted butter, taking great care not to knock out the air you beat in.",
                "Bake the sponge in the hot oven until it springs back when you press the middle lightly with a fingertip.",
            };

            Check(source, padded).Reasons.Should().Contain(ProseCheck.TooLong);
        }

        [Fact]
        public void A_short_sentence_close_to_its_source_is_not_copied_text()
        {
            var finding = Check(new[] { "Toast the seeds in a dry pan." }, new[] { "Toast the seeds in a dry skillet." });

            finding.LongestRun.Should().BeLessThan(ProseCheck.SharedRun);
            finding.Reasons.Should().NotContain(ProseCheck.CopiedText);
        }

        [Fact]
        public void A_copied_run_is_found_across_step_boundaries_and_however_amounts_are_written()
        {
            var source = new[] { "Warm the rest of the butter in a wide pan over a low flame. Add the sliced leeks and sweat them for 8 minutes, turning them now and then. Add the thyme, lemon zest and a pinch of nutmeg and sweat for 1 more minute, scraping the pan." };
            var cutApart = new[]
            {
                "Warm the rest of the butter in a wide pan over a low flame.",
                "Add the sliced leeks and sweat them for 8 min, turning them now and then.",
                "Add the thyme, lemon zest and a pinch of nutmeg and sweat for one more minute, scraping the pan.",
            };

            var finding = Check(source, cutApart);

            finding.Faults.Should().Contain(f => f.Reason == ProseCheck.CopiedText && f.Detail.StartsWith("a run of"), finding.ToString());
            finding.LongestRun.Should().BeGreaterThan(30);
        }

        [Fact]
        public void A_run_of_stock_phrases_counts_like_any_other_run()
        {
            const string generic = "Heat the oven to 200°C and cover two big trays with greaseproof paper or a reusable mat before you start.";
            var stock = new ProseStockIndex(1, ProsePhrases.Hashes(ProseCheck.CopyTokens(ProseQuantities.Prepare(generic))));

            var empty = Check(new[] { generic }, new[] { generic.Replace("200°C", "200 degrees C") });
            var stocked = Check(new[] { generic }, new[] { generic.Replace("200°C", "200 degrees C") }, stock: stock);

            empty.Reasons.Should().Contain(ProseCheck.CopiedText, empty.ToString());
            stocked.Faults.Should().Equal(empty.Faults);
            stocked.StockVersion.Should().Be(1);
        }

        [Fact]
        public void An_ingredient_enumeration_shared_with_the_source_is_not_copied_text()
        {
            var lines = new[] { "3 tbsp walnut oil", "1 tbsp cider vinegar", "1 tsp maple syrup", "1 shallot", "2 tsp tarragon", "1 tsp grainy mustard", "1 garlic clove", "salt and pepper" };

            var finding = Check(
                new[] { "Shake the walnut oil, cider vinegar, maple syrup, shallot, tarragon, grainy mustard, garlic, salt, and pepper together in a lidded jar until blended." },
                new[] { "For the vinaigrette, put walnut oil, cider vinegar, maple syrup, shallot, tarragon, grainy mustard, garlic, salt, and pepper in a jar and shake well." },
                lines);

            finding.Reasons.Should().NotContain(ProseCheck.CopiedText, finding.ToString());
        }

        [Fact]
        public void A_close_paraphrase_with_no_long_run_is_copied_by_its_share_of_copied_words()
        {
            var source = new[]
            {
                "Heat the oven to 180°C. Butter a round tin and leave it ready.",
                "In a wide bowl, stir together the oats, spelt flour, cinnamon, and a pinch of salt. Keep the dry mix to one side.",
                "In a jug, combine brown sugar and lime zest. Rub them together with your fingertips until it smells strongly.",
                "Little by little add in the dry mix. Stir until just mixed. Carefully fold in the chopped figs.",
            };
            var rewrite = new[]
            {
                "Heat the oven to 180°C. Butter a round tin.",
                "In a wide bowl, mix the oats, spelt flour, cinnamon, and a pinch of salt. Keep to one side.",
                "Using a small jug, combine brown sugar and lime zest. Rub them together until it smells strongly.",
                "Bit by bit add the dry mix. Stir until just mixed. Carefully fold in the chopped figs.",
            };

            var finding = Check(source, rewrite);

            finding.LongestRun.Should().BeLessThan(15);
            finding.CopiedShare.Should().BeGreaterThanOrEqualTo(ProseCheck.MaximumCopiedShare);
            finding.Reasons.Should().Contain(ProseCheck.CopiedText, finding.ToString());
        }

        [Fact]
        public void A_description_that_keeps_the_source_description_is_copied_text()
        {
            var source = new RewriteFidelity.SourceText("These little oat and apricot bars are dairy free, egg free, wheat free, and there's no oven needed.", new[] { "Blend the dates and cocoa until smooth, then roll into balls." });

            ProseCheck.Check(source, new RewriteFidelity.SourceText("These little oat and apricot bars are dairy-free, egg-free, wheat-free, and need no oven.", new[] { "Blend the dates with the cocoa until smooth and shape the mixture into balls." }))
                .Reasons.Should().Contain(ProseCheck.CopiedText);
            ProseCheck.Check(source, new RewriteFidelity.SourceText("No-bake cocoa bites that suit vegan and nut-free diets.", new[] { "Blend the dates with the cocoa until smooth and shape the mixture into balls." }))
                .Passed.Should().BeTrue();
        }

        [Fact]
        public void A_long_sentence_kept_nearly_word_for_word_is_copied_text()
        {
            var finding = Check(
                new[] { "In a big glass jug, beat together 2 small eggs, 3 tablespoons melted butter, 1 cup buttermilk, and 1 teaspoon almond essence." },
                new[] { "In a big jug, beat together 2 small eggs, 3 tablespoons melted butter, 1 cup buttermilk, and 1 teaspoon almond essence." },
                new[] { "2 small eggs", "3 tablespoons melted butter", "1 cup buttermilk", "1 teaspoon almond essence" });

            finding.CopiedSentences.Should().Be(1);
            finding.Reasons.Should().Contain(ProseCheck.CopiedText);
        }

        [Theory]
        [InlineData("Lay the pears in the butter and fry them until they are covered with the dark syrup.", "Put the pears in the butter and fry until glossy.")]
        [InlineData("Stir in the honey and cook, stirring often, until the honey darkens, glazing all of the pecans.", "Add the honey and heat, stirring, until it darkens.")]
        [InlineData("Wash the potatoes well.", "Clean the potatoes thoroughly.")]
        [InlineData("Spread the carrot sticks out over a lined tray.", "Place the carrot sticks on a lined tray in one layer.")]
        [InlineData("Simmer until the stock soaks into the barley.", "Simmer until the barley absorbs the stock.")]
        [InlineData("Pour the warm syrup over the cake, letting it soak in.", "Pour the warm syrup over the cake.")]
        [InlineData("Line a tray with a silicone mat. Blend the mangoes to a very smooth puree.", "Prepare a tray with a silicone liner. Blend the mangoes until smooth.")]
        [InlineData("Pour the batter over the whole baking sheet and smooth it.", "Pour the batter onto the sheet and smooth it.")]
        [InlineData("Remove the pan from the heat and stir in the chocolate.", "Stir in the chocolate.")]
        [InlineData("Turn off the heat and stir in the chocolate.", "Take the pan off the heat and stir in the chocolate.")]
        [InlineData("Switch the burner off, then whisk in the butter.", "Whisk in the butter with the heat turned off.")]
        [InlineData("Cut the butter into 1-inch cubes and chill.", "Chop the butter into small pieces and chill.")]
        [InlineData("Add the mango, yogurt and ice cubes to a blender.", "Combine mango, yogurt and ice in a blender.")]
        [InlineData("Serve with home-cut fries.", "Serve with fries.")]
        [InlineData("Bake until the cheese has melted.", "Bake until the cheese is bubbly.")]
        [InlineData("Shape the dough into small discs.", "Roll the dough into small discs.")]
        [InlineData("Brush the sauce over the salmon.", "Coat the salmon with the sauce.")]
        public void A_faithful_rewrite_keeps_every_step(string source, string rewrite)
        {
            var finding = Check(new[] { source }, new[] { rewrite });

            finding.Reasons.Should().NotContain(ProseCheck.LostStep, finding.ToString());
        }

        [Fact]
        public void An_ingredient_kept_under_another_name_is_kept()
        {
            Check(new[] { "Sprinkle on the radishes and green onions." }, new[] { "Add the radishes and scallions." }, new[] { "4 green onions, thinly sliced", "6 radishes" })
                .Reasons.Should().NotContain(ProseCheck.LostStep);
        }

        [Theory]
        [InlineData("Put the prawns back in the sauce and let them bubble for another minute.", "Return the prawns to the sauce and let them bubble 1 minute.")]
        [InlineData("Preheat the oven to 350ºF (180ºC).", "Preheat the oven to 350°F (180°C).")]
        [InlineData("Preheat the oven to 425. Bake at 425 for 15 minutes.", "Heat the oven to 425 degrees and bake for 15 minutes.")]
        [InlineData("Butter a 9x9-inch square dish.", "Butter a square 9x9-inch dish.")]
        [InlineData("1. Line a tray.2. Toss the tofu with cumin.3. Warm the oil. Season with salt and sesame seeds 4. Pour the dressing over the tofu.", "Put parchment on a tray. Coat the tofu in cumin. Warm oil, then add salt and sesame seeds. Pour this over the tofu.")]
        public void An_amount_written_another_way_is_the_same_amount(string source, string rewrite)
        {
            var finding = Check(new[] { source }, new[] { rewrite });

            finding.Reasons.Should().NotContain(ProseCheck.QuantityReasons, finding.ToString());
        }

        [Fact]
        public void An_amount_in_brackets_after_an_ingredient_belongs_to_it()
        {
            var finding = Check(
                new[] { "Beat a little milk (about a tablespoon) with the egg and brush the pastry." },
                new[] { "Beat a tablespoon of milk with the egg and brush it over the pastry." },
                new[] { "1 cup cold milk", "1 egg" });

            finding.Reasons.Should().NotContain(ProseCheck.WrongAmount, finding.ToString());
        }

        [Fact]
        public void Amounts_swapped_between_two_ingredients_are_wrong_amounts()
        {
            var finding = Check(
                new[] { "Whisk 1 cup flour with 2 cups milk until smooth." },
                new[] { "Using a whisk, combine 2 cups flour and 1 cup milk until smooth." },
                new[] { "1 cup flour", "2 cups milk" });

            finding.Faults.Where(f => f.Reason == ProseCheck.WrongAmount).Should().HaveCount(2, finding.ToString());
            Check(
                new[] { "Whisk 1 cup flour with 2 cups milk until smooth." },
                new[] { "Using a whisk, combine 1 cup flour and 2 cups milk until smooth." },
                new[] { "1 cup flour", "2 cups milk" }).Passed.Should().BeTrue();
        }

        [Fact]
        public void An_amount_the_source_states_twice_must_be_stated_twice()
        {
            var finding = Check(new[] { "Bake 10 minutes, then turn and bake 10 minutes more." }, new[] { "Bake 10 minutes, turn it over and continue." });

            finding.Reasons.Should().Contain(ProseCheck.LostQuantity, finding.ToString());
            Check(new[] { "Bake 10 minutes, then turn and bake 10 minutes more." }, new[] { "Bake 10 minutes, turn it over and bake another 10 minutes." })
                .Passed.Should().BeTrue();
        }

        [Fact]
        public void A_short_method_copied_with_one_word_changed_is_copied_text()
        {
            var finding = Check(
                new[] { "Spread the softened cream cheese evenly over each warm tortilla, then sprinkle with the chopped green onions and roll up tightly." },
                new[] { "Spread the softened cream cheese evenly over each warm tortilla, and sprinkle with the chopped green onions and roll up tightly." });

            finding.Reasons.Should().Contain(ProseCheck.CopiedText, finding.ToString());
        }

        [Fact]
        public void A_soft_count_changed_in_the_same_context_is_a_changed_amount()
        {
            var finding = Check(
                new[] { "Divide the dough into two equal parts and shape each into a ball." },
                new[] { "Split the dough into three equal parts, then shape each one into a ball." });

            finding.Reasons.Should().Contain(ProseCheck.LostQuantity, finding.ToString());
            Check(new[] { "Divide the dough into two equal parts and shape each into a ball." }, new[] { "Halve the dough and shape each half into a ball." })
                .Reasons.Should().NotContain(ProseCheck.QuantityReasons);
        }

        [Fact]
        public void A_close_paraphrase_that_changes_a_word_every_few_words_is_copied_text()
        {
            var source = new[]
            {
                "Wash the celery, strip off the outer leaves, and cut it into lengths of about 4 inches.",
                "Put these into a saucepan, with the broth, and stew till tender, which will be in from 20 to 25 minutes.",
                "Then add the remaining ingredients, simmer altogether for 4 or 5 minutes.",
                "Pour into a dish, and serve.",
                "It may be garnished with sippets of toasted bread.",
            };
            var close = new[]
            {
                "Wash the celery, strip off those outer leaves, and cut it into pieces of about 4 inches.",
                "Put these into a pan, with the broth, and stew till soft, which will be in from 20 to 25 minutes.",
                "Next add the remaining ingredients, simmer all together for 4 or 5 minutes.",
                "Pour into a bowl, and serve.",
                "It can be garnished with sippets of toasted bread.",
            };
            var faithful = new[]
            {
                "Rinse the celery, remove its outer leaves and cut the stalks into 4-inch pieces.",
                "Stew them gently in the broth in a saucepan for 20 to 25 minutes, until soft.",
                "Add everything else and simmer 4 to 5 minutes more.",
                "Turn it out into a dish to serve, and garnish it with toasted bread sippets if you like.",
            };

            var finding = Check(source, close);

            finding.LongestRun.Should().BeLessThan(8);
            finding.CopiedShare.Should().BeLessThan(ProseCheck.MaximumCopiedShare);
            finding.CloseStretch.Should().BeGreaterThanOrEqualTo(ProseCheck.CloseStretchLimit);
            finding.Faults.Should().Contain(f => f.Reason == ProseCheck.CopiedText && f.Detail.StartsWith("a stretch of"), finding.ToString());
            Check(source, close.Reverse().ToArray()).CloseStretch.Should().BeLessThan(ProseCheck.CloseStretchLimit);
            Check(source, faithful).Passed.Should().BeTrue(Check(source, faithful).ToString());
        }

        [Theory]
        [InlineData("Warm the cream over low heat, whisking constantly so it does not catch.", "Warm the cream over low heat so it does not catch.", "mix")]
        [InlineData("Cube the cold butter and rub it into the flour.", "Rub the cold butter into the flour.", "cut")]
        [InlineData("Spread the icing evenly over the cooled cake.", "Pour the icing over the cooled cake.", "spread")]
        [InlineData("Heat the cream until it just boils. Turn off the heat and stir in the chocolate.", "Heat the cream until it just boils, then stir in the chocolate.", ProseVerbLexicon.OffHeatGroup)]
        public void A_dropped_instruction_is_a_lost_step(string source, string rewrite, string group)
        {
            var finding = Check(new[] { source }, new[] { rewrite });

            finding.Faults.Should().Contain(f => f.Reason == ProseCheck.LostStep && f.Detail.Contains(group), finding.ToString());
        }

        [Fact]
        public void The_source_lines_are_the_recipe_ingredients_of_its_json_ld()
        {
            const string json = """{"@type":"Recipe","recipeIngredient":["3/4 cup heavy cream","<b>1</b> tsp salt",""],"recipeInstructions":["Mix."]}""";

            ProseCheck.SourceLines(json).Should().Equal("3/4 cup heavy cream", "1 tsp salt");
            ProseCheck.SourceLines("{\"@type\":\"Recipe\"}").Should().BeEmpty();
            ProseCheck.SourceLines("not json").Should().BeEmpty();
        }
    }
}
