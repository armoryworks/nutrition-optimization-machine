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
    /// The re-check's own rules, apart from the link gate: which holds a set acts on, when a tier's
    /// food may take the lines, when the lines go back to their wording, the container form line
    /// rule and its canned sister, the catalog-name check and the signature. Holds are constructed,
    /// so these do not depend on what the gate lets through.
    /// </summary>
    public class LinkRecheckRulesTests
    {
        private static GateHold Hold(string code) => new(code, code);

        private static GateFood Food(long id, string name, string? description = null) =>
            new(id, name, description, CatalogAdmissionRule.SrLegacyFood, (long)CurationStatusEnum.Curated, NutrientRows: 30);

        private static RecheckCandidate Candidate(string tier, long id, params string[] holds) => new(tier, Food(id, "#" + id), holds.Select(Hold).ToList());

        [Fact]
        public void A_set_acts_only_on_its_own_rules_and_keeps_a_pair_the_gate_holds_for_another()
        {
            var verdict = LinkRecheckRules.Decide(LinkRecheckRules.Alcohol, new[] { Hold(LinkGate.Part) }, () => new[] { Candidate(LinkRecheckRules.NameRuleTier, 2) });
            verdict.Outcome.Should().Be(LinkRecheckRules.Keep);
            LinkRecheckRules.Decide(LinkRecheckRules.PartHerb, new[] { Hold(LinkGate.Part) }, () => new[] { Candidate(LinkRecheckRules.NameRuleTier, 2) })
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Move && v.Target!.Food.Id == 2);
        }

        [Fact]
        public void The_lines_move_to_the_first_food_the_gate_lets_through_and_go_back_only_on_a_refuting_hold()
        {
            var holds = new[] { Hold(LinkGate.Compound) };
            LinkRecheckRules.Decide(LinkRecheckRules.Compound, holds, () => new[]
                {
                    Candidate(LinkRecheckRules.VerifiedWordingTier, 2, LinkGate.Containment),
                    Candidate(LinkRecheckRules.NameRuleTier, 3),
                })
                .Target!.Food.Id.Should().Be(3);
            LinkRecheckRules.Decide(LinkRecheckRules.Compound, holds, () => new[] { Candidate(LinkRecheckRules.NameRuleTier, 2, LinkGate.Compound) })
                .Outcome.Should().Be(LinkRecheckRules.Unlink);
            LinkRecheckRules.Decide(LinkRecheckRules.Applied, new[] { Hold(LinkGate.Containment) }, () => Enumerable.Empty<RecheckCandidate>())
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Unproven, "a word rule alone never takes a line off its food");
            LinkRecheckRules.Decide(LinkRecheckRules.Applied, new[] { Hold(LinkGate.Form) }, () => Enumerable.Empty<RecheckCandidate>())
                .Outcome.Should().Be(LinkRecheckRules.Unlink);
            LinkRecheckRules.Decide(LinkRecheckRules.Applied, new[] { Hold(LinkGate.Units) }, () => new[] { Candidate(LinkRecheckRules.NameRuleTier, 2) })
                .Outcome.Should().Be(LinkRecheckRules.Keep, "the re-check computes no grams, so the units rule is not read");
        }

        [Theory]
        [InlineData(LinkRecheckRules.AlcoholSenseTier, true)]
        [InlineData(LinkRecheckRules.CannedSisterTier, true)]
        [InlineData(LinkRecheckRules.NameRuleTier, false)]
        public void A_tier_that_vouches_for_identity_is_spared_the_word_rules_but_not_the_rest(string tier, bool spared)
        {
            LinkRecheckRules.Blocking(tier, new[] { Hold(LinkGate.Containment), Hold(LinkGate.LeadNoun) }).Count.Should().Be(spared ? 0 : 2);
            LinkRecheckRules.Blocking(tier, new[] { Hold(LinkGate.Incomplete), Hold(LinkGate.AlcoholDrink) }).Should().HaveCount(2);
        }

        [Fact]
        public void The_container_rule_never_takes_a_line_off_its_food()
        {
            LinkRecheckRules.Decide(LinkRecheckRules.ContainerForm, new[] { Hold(LinkGate.Compound) }, () => Enumerable.Empty<RecheckCandidate>(), containerWrong: true)
                .Outcome.Should().Be(LinkRecheckRules.Held);
            LinkRecheckRules.Decide(LinkRecheckRules.ContainerForm, new[] { Hold(LinkGate.Compound) }, () => Enumerable.Empty<RecheckCandidate>(), containerWrong: false)
                .Outcome.Should().Be(LinkRecheckRules.Keep);
        }

        [Theory]
        [InlineData("1 can tomatoes", true)]
        [InlineData("2 (15 oz) cans black beans, drained", true)]
        [InlineData("1 jar roasted red peppers", true)]
        [InlineData("2 tomatoes, you can use any", false)]
        [InlineData("1 cup pecans", false)]
        [InlineData("tomatoes (canned are fine)", false)]
        public void A_container_line_counts_out_a_can_tin_or_jar(string raw, bool container) =>
            LinkRecheckRules.IsContainerLine(raw).Should().Be(container);

        [Fact]
        public void A_line_is_on_the_wrong_form_when_a_can_sits_on_a_food_that_is_not_canned_or_a_drained_line_on_the_whole_can()
        {
            LinkRecheckRules.WrongForm("1 can tomatoes", Food(1, "Tomatoes", "Tomatoes, red, ripe, raw")).Should().BeTrue();
            LinkRecheckRules.WrongForm("1 can tomatoes", Food(1, "Tomatoes", "Tomatoes, red, ripe, canned, packed in tomato juice")).Should().BeFalse();
            LinkRecheckRules.WrongForm("1 can tomatoes", Food(1, "Tomatoes")).Should().BeFalse("a food with no USDA entry is not judged");
            LinkRecheckRules.WrongForm("1 can chickpeas, drained", Food(1, "Chickpeas", "Chickpeas, mature seeds, canned, solids and liquids")).Should().BeTrue();
            LinkRecheckRules.WrongForm("1 can chickpeas, undrained", Food(1, "Chickpeas", "Chickpeas, mature seeds, canned, solids and liquids")).Should().BeFalse();
        }

        [Fact]
        public void The_canned_sister_is_the_one_curated_canned_food_of_the_lead_word_holding_the_wording()
        {
            var raw = Food(1, "Tomatoes", "Tomatoes, red, ripe, raw");
            var canned = Food(2, "Canned Tomatoes", "Tomatoes, red, ripe, canned, packed in tomato juice");
            var drained = Food(3, "Drained Tomatoes", "Tomatoes, red, ripe, canned, drained");
            var catalog = GateCatalog.Build(new[] { raw, canned, drained });
            LinkRecheckRules.CannedSister(raw, "tomatoes", drained: false, catalog)!.Id.Should().Be(2);
            LinkRecheckRules.CannedSister(raw, "tomatoes", drained: true, catalog)!.Id.Should().Be(3);
            LinkRecheckRules.CannedSister(raw, "green tomatoes", drained: false, catalog).Should().BeNull("no canned food holds every word of the wording");
            var two = GateCatalog.Build(new[] { raw, canned, Food(4, "Canned Tomatoes Salted", "Tomatoes, red, ripe, canned, with salt") });
            LinkRecheckRules.CannedSister(raw, "tomatoes", drained: false, two).Should().BeNull("two sisters: no one food");
        }

        [Theory]
        [InlineData("Black Mustard Seed", "Spices, mustard seed, ground", true)]
        [InlineData("Fresh Squid", "Mollusks, squid, mixed species, raw", false)]
        [InlineData("Garlic Powder", "Spices, garlic powder", false)]
        [InlineData("Cinnamon", "Spices, cinnamon, ground", false)]
        public void A_name_is_listed_when_it_claims_a_colour_or_seed_its_entry_lacks_while_the_entry_names_a_form_it_lacks(string name, string description, bool listed) =>
            LinkRecheckRules.NameContradicts(name, description).Should().Be(listed);

        [Fact]
        public void The_fixed_name_table_renames_only_while_the_wrong_word_is_there()
        {
            LinkRecheckRules.NameFix("170929", "Black Mustard Seed").Should().Be("Ground Mustard");
            LinkRecheckRules.NameFix("170929", "Ground Mustard").Should().BeNull();
            LinkRecheckRules.NameFix("170929", "Mustard Powder").Should().BeNull("a person renamed it since");
            LinkRecheckRules.NameFix("999999", "Black Mustard Seed").Should().BeNull();
        }

        [Fact]
        public void The_signature_covers_the_gate_the_alcohol_rules_the_sets_and_the_tables()
        {
            var source = LinkRecheckRules.SignatureSource();
            source.Should().Contain(LinkGate.Signature).And.Contain(AlcoholLineContext.Signature).And.Contain(LinkRecheckRules.Version);
            foreach (var code in LinkRecheckRules.Refuting.Concat(LinkRecheckRules.IdentityCodes)) source.Should().Contain(code);
            LinkRecheckRules.SetSignature(LinkRecheckRules.Alcohol).Should().NotBe(LinkRecheckRules.SetSignature(LinkRecheckRules.Applied));
        }

        [Fact]
        public void Every_refuting_code_and_every_code_a_set_acts_on_is_a_rule_of_the_gate()
        {
            var codes = typeof(LinkGate).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(LinkGate.Version))
                .Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
            LinkRecheckRules.Refuting.Should().BeSubsetOf(codes);
            LinkRecheckRules.IdentityCodes.Should().BeSubsetOf(codes);
            foreach (var set in LinkRecheckRules.Sets)
                LinkRecheckRules.ActingHolds(set, codes.Select(Hold)).Select(h => h.Code).Should().BeSubsetOf(codes);
        }

        [Theory]
        [InlineData(LinkGate.Marked, "restaurant", false)]
        [InlineData(LinkGate.Marked, "with salt added", false)]
        [InlineData(LinkGate.State, "canned", false)]
        [InlineData(LinkGate.Containment, "baby", false)]
        [InlineData(LinkGate.Variety, "meal", false)]
        [InlineData(LinkGate.Incomplete, "16 rows", false)]
        [InlineData(LinkGate.Part, "flesh", false, LinkGate.FoodPartRule)]
        [InlineData(LinkGate.Part, "meat only", false, LinkGate.FoodPartRule)]
        [InlineData(LinkGate.Part, "leaves", false, LinkGate.FoodPartRule)]
        [InlineData(LinkGate.Part, "leaves", true, "wording part not in the food")]
        [InlineData(LinkGate.Part, "skin", true)]
        [InlineData(LinkGate.Part, "bones", true)]
        [InlineData(LinkGate.Percent, "15%", false)]
        [InlineData(LinkGate.Percent, "wording states 93%", true)]
        [InlineData(LinkGate.HeadNoun, "glucose", false)]
        [InlineData(LinkGate.HeadNoun, "with crumb", true)]
        [InlineData(LinkGate.Form, "fresh shelled seeds against mature seeds", true)]
        [InlineData(LinkGate.FreshHerb, null, true)]
        [InlineData(LinkGate.Prepared, "Baby Foods", true)]
        [InlineData(LinkGate.Polarity, "unsalted against salted", true)]
        public void Only_a_hold_that_proves_the_food_wrong_refutes_an_applied_link(string code, string? detail, bool refutes, string? rule = null)
        {
            var hold = new GateHold(code, rule ?? code, detail);
            LinkRecheckRules.Refutes(hold, new[] { hold }).Should().Be(refutes);
            LinkRecheckRules.Decide(LinkRecheckRules.Applied, new[] { hold }, () => Enumerable.Empty<RecheckCandidate>())
                .Outcome.Should().Be(refutes ? LinkRecheckRules.Unlink : LinkRecheckRules.Unproven);
        }

        [Fact]
        public void Another_curated_foods_name_refutes_only_with_a_word_missing_and_a_compound_only_when_no_alternative_is_the_food()
        {
            var other = new GateHold(LinkGate.OtherFood, LinkGate.OtherFood, "Black Beans");
            LinkRecheckRules.Refutes(other, new[] { other }).Should().BeFalse("black beans on canned black beans: a less exact name, not another food");
            LinkRecheckRules.Refutes(other, new[] { other, Hold(LinkGate.Containment) }).Should().BeTrue("olive oil on canola oil: a word of the wording is missing");
            var compound = Hold(LinkGate.Compound);
            LinkRecheckRules.Refutes(compound, new[] { compound }, alternativeNamed: true).Should().BeFalse();
            LinkRecheckRules.Refutes(compound, new[] { compound }).Should().BeTrue();

            var catalog = GateCatalog.Build(Enumerable.Empty<GateFood>());
            LinkGate.NamesAlternative("butter or olive oil", Food(1, "Butter", "Butter, salted"), catalog).Should().BeTrue();
            LinkGate.NamesAlternative("lard or butter", Food(1, "Butter", "Butter, salted"), catalog).Should().BeTrue();
            LinkGate.NamesAlternative("light or dark brown sugar", Food(1, "Brown Sugar", "Sugars, brown"), catalog).Should().BeTrue();
            LinkGate.NamesAlternative("active dry or instant yeast", Food(1, "Yeast", "Leavening agents, yeast, baker's, active dry"), catalog).Should().BeTrue();
            LinkGate.NamesAlternative("rum or brandy", Food(1, "Coffee Cream Liqueur", "Alcoholic beverage, liqueur, coffee with cream, 34 proof"), catalog).Should().BeFalse();
            LinkGate.NamesAlternative("salt and pepper", Food(1, "Salt", "Salt, table"), catalog).Should().BeFalse("and joins two foods; it gives no alternatives");
        }

        [Fact]
        public void A_set_other_than_alcohol_reads_the_alcohol_rules_only_where_the_wording_itself_is_refused()
        {
            var holds = new[] { Hold(LinkGate.AlcoholNotDrink), Hold(LinkGate.Containment) };
            LinkRecheckRules.ForSet(LinkRecheckRules.Applied, holds, wordingRefused: false).Select(h => h.Code).Should().Equal(LinkGate.Containment);
            LinkRecheckRules.ForSet(LinkRecheckRules.Applied, holds, wordingRefused: true).Should().HaveCount(2);
            LinkRecheckRules.ForSet(LinkRecheckRules.Alcohol, holds, wordingRefused: false).Should().HaveCount(2);
        }
    }
}
