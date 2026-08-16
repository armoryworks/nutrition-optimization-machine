using FluentAssertions;
using Nom.Data.Nutrition;
using Xunit;

namespace Nom.Api.Tests.Services.Curation
{
    /// <summary>
    /// The safety rule that keeps model-authored numbers out of the catalog: an automated reviewer
    /// may propose names, food groups and flags, but a nutrient value may only change on the word
    /// of an authoritative source. A plausible-but-invented number is worse than no change at all.
    /// </summary>
    public class ProposalPolicyTests
    {
        [Theory]
        [InlineData("calories")]
        [InlineData("kcal_per_100g")]
        [InlineData("protein")]
        [InlineData("carb_per_100g")]
        [InlineData("fat")]
        [InlineData("reference_serving_grams")]
        public void ModelReviewer_CannotChangeNutrientValues(string field)
        {
            var allowed = ProposalPolicy.IsAllowed(field, "review:claude/2026-08-15", out var reason);
            allowed.Should().BeFalse();
            reason.Should().Be("nutrient_change_requires_authoritative_source");
        }

        [Theory]
        [InlineData("name")]
        [InlineData("food_group")]
        [InlineData("is_whole_food")]
        public void ModelReviewer_MayProposeCategoricalFields(string field)
        {
            ProposalPolicy.IsAllowed(field, "review:claude/2026-08-15", out _).Should().BeTrue();
        }

        [Theory]
        [InlineData("fdc:1105430")]
        [InlineData("label:example.com/nutrition")]
        [InlineData("admin:42")]
        [InlineData("deterministic:unit-normalization")]
        public void AuthoritativeSources_MayChangeNutrientValues(string source)
        {
            ProposalPolicy.IsAllowed("calories", source, out _).Should().BeTrue();
        }

        [Fact]
        public void MissingSource_IsAlwaysRejected()
        {
            ProposalPolicy.IsAllowed("name", null, out var reason).Should().BeFalse();
            reason.Should().Be("source_required");
            ProposalPolicy.IsAllowed("name", "  ", out _).Should().BeFalse();
        }

        [Fact]
        public void FlagWithoutAField_IsAllowedFromAnyNamedSource()
        {
            // Flags are the model's main job: raise a concern, change nothing.
            ProposalPolicy.IsAllowed(null, "review:claude/2026-08-15", out _).Should().BeTrue();
        }

        [Fact]
        public void RecognizesNutrientFieldsCaseInsensitively()
        {
            ProposalPolicy.IsNutrientField("Calories_Per_100g").Should().BeTrue();
            ProposalPolicy.IsNutrientField("FOOD_GROUP").Should().BeFalse();
            ProposalPolicy.IsNutrientField(null).Should().BeFalse();
        }
    }
}
