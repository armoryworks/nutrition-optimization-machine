using System;
using System.Collections.Generic;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Draw 21 of ck18: "6 T-salad dressing" was read clear for Egg Allergy because Fats and Oils is a category of single
    /// commodities and its curated dressings cleared the word "dressing". A composite word is not cleared by such a food; its
    /// other words are, so "olive oil" stays understood.
    /// </summary>
    public class DietCompositeWordsTests
    {
        private static readonly DietFoodWords.Food[] Catalog =
        {
            new(1, "French Dressing", 4, "Salad dressing, french dressing, commercial, regular"),
            new(2, "Olive Oil", 4, "Oil, olive, salad or cooking"),
            new(3, "Margarine", 4, "Margarine, regular, 80% fat, composite, stick, with salt"),
        };

        [Fact]
        public void A_composite_word_of_a_single_commodity_food_is_not_cleared_and_its_other_words_are()
        {
            var table = DietFoodWords.TableOf(Catalog);
            var egg = DietFoodWords.Build(table, _ => false, _ => Array.Empty<IReadOnlyList<string>>(), new HashSet<string>(), Array.Empty<DietFoodWords.Kind>());
            egg.ClassOf(table.IndexOf("dressing")).Should().NotBe(DietFoodWords.WordClass.Clear, "a dressing does not list what it holds");
            egg.ClassOf(table.IndexOf("margarine")).Should().NotBe(DietFoodWords.WordClass.Clear);
            egg.ClassOf(table.IndexOf("oil")).Should().Be(DietFoodWords.WordClass.Clear);
            egg.ClassOf(table.IndexOf("olive")).Should().Be(DietFoodWords.WordClass.Clear);
        }
    }
}
