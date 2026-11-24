using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Import.Golden;
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
            using var none = LinkRecheckRules.AssumeProven();
            var verdict = LinkRecheckRules.Decide(LinkRecheckRules.Alcohol, new[] { Hold(LinkGate.Part) }, () => new[] { Candidate(LinkRecheckRules.NameRuleTier, 2) });
            verdict.Outcome.Should().Be(LinkRecheckRules.Keep);
            LinkRecheckRules.Decide(LinkRecheckRules.PartHerb, new[] { Hold(LinkGate.Part) }, () => new[] { Candidate(LinkRecheckRules.NameRuleTier, 2) })
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Unproven && v.Candidate == LinkRecheckRules.Move && v.Class == LinkGate.Part && v.Target!.Food.Id == 2,
                    "the part class is not proven, so its move is only a candidate for the model audit");
            LinkRecheckRules.Decide(LinkRecheckRules.Alcohol, new[] { Hold(LinkGate.AlcoholDrink) }, () => new[] { Candidate(LinkRecheckRules.AlcoholSenseTier, 2) })
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Unproven && v.Class == LinkRecheckRules.AlcoholClass && v.Candidate == LinkRecheckRules.Move && v.Target!.Food.Id == 2,
                    "a class that is not proven only names a candidate");
        }

        [Fact]
        public void A_proven_class_writes_by_rule_and_decides_ahead_of_an_earlier_hold_of_a_class_that_is_not()
        {
            using var proven = LinkRecheckRules.AssumeProven(LinkRecheckRules.AlcoholClass, LinkGate.Prepared);
            LinkRecheckRules.Decide(LinkRecheckRules.Alcohol, new[] { Hold(LinkGate.AlcoholDrink) }, () => new[] { Candidate(LinkRecheckRules.AlcoholSenseTier, 2) })
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Move && v.Class == LinkRecheckRules.AlcoholClass && v.Candidate == null && v.Target!.Food.Id == 2);
            LinkRecheckRules.Decide(LinkRecheckRules.Applied, new[] { Hold(LinkGate.Form), Hold(LinkGate.Prepared) }, () => Enumerable.Empty<RecheckCandidate>())
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Unlink && v.Class == LinkGate.Prepared && v.Candidate == null);
            LinkRecheckRules.Decide(LinkRecheckRules.PartHerb, new[] { Hold(LinkGate.Part) }, () => new[] { Candidate(LinkRecheckRules.NameRuleTier, 2) })
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Unproven && v.Candidate == LinkRecheckRules.Move && v.Class == LinkGate.Part);
        }

        [Fact]
        public void A_row_counts_for_every_class_that_refutes_its_food_whichever_classes_are_proven()
        {
            var row = new RecheckJudgedRow("t-1", LinkRecheckRules.Applied, "smoked paprika", 1, new[] { "1 tsp smoked paprika" }, null, LinkRecheckClassScorer.Wrong, null, "agent", "agent-test");
            IReadOnlyList<RecheckClassScore> ScoreUnder(params string[] proven)
            {
                using var assumed = LinkRecheckRules.AssumeProven(proven);
                var verdict = LinkRecheckRules.Decide(LinkRecheckRules.Applied, new[] { Hold(LinkGate.Form), Hold(LinkGate.Part) }, () => Enumerable.Empty<RecheckCandidate>());
                var acts = verdict.Outcome == LinkRecheckRules.Unlink ? verdict.Outcome : verdict.Candidate;
                return LinkRecheckClassScorer.Score(new[] { new RecheckRowOutcome(row, verdict, verdict.Class, acts, null, verdict.Outcome == LinkRecheckRules.Unlink, true) });
            }

            var none = ScoreUnder();
            none.Select(s => s.Class).Should().BeEquivalentTo(new[] { LinkGate.Form, LinkGate.Part },
                "the part class would take these lines back once proven, so it is scored on them before it is proven, not only the form class that decides today");
            ScoreUnder(LinkGate.Part).Should().BeEquivalentTo(none, o => o.Excluding(s => s.Proven), "proving a class does not change the rows any class is scored on");
            ScoreUnder(LinkGate.Form, LinkGate.Part).Should().BeEquivalentTo(none, o => o.Excluding(s => s.Proven));
        }

        [Fact]
        public void The_proven_classes_come_from_the_record_and_only_from_a_current_one()
        {
            var record = new LinkRecheckClassRecord.Record
            {
                Version = LinkRecheckRules.Version,
                DecisionSignature = LinkRecheckRules.DecisionSignature,
                Bar = LinkRecheckRules.ClassPrecisionBar,
                Minimum = LinkRecheckRules.ClassMinimumJudged,
                Classes =
                {
                    new LinkRecheckClassRecord.ClassFigures { Class = LinkRecheckRules.AlcoholClass, AgentJudged = 285, AgentRight = 285, MeetsBar = true },
                    new LinkRecheckClassRecord.ClassFigures { Class = LinkGate.Compound, AgentJudged = 65, AgentRight = 11, MeetsBar = false },
                },
                Proven = { LinkRecheckRules.AlcoholClass, LinkGate.Compound },
            };
            var (set, problem) = LinkRecheckClassRecord.Evaluate(record, LinkRecheckRules.Version, LinkRecheckRules.DecisionSignature);
            problem.Should().BeNull();
            set.Should().BeEquivalentTo(new[] { LinkRecheckRules.AlcoholClass }, "a class listed as proven whose figures miss the bar proves nothing");

            LinkRecheckClassRecord.Evaluate(record, LinkRecheckRules.Version, "0000000000000000").Should().Match<(IReadOnlySet<string> Proven, string? Problem)>(r => r.Proven.Count == 0 && r.Problem != null,
                "a record scored under other rules proves nothing until it is recorded again");
            LinkRecheckClassRecord.Evaluate(null, LinkRecheckRules.Version, LinkRecheckRules.DecisionSignature).Proven.Should().BeEmpty();
            record.Minimum = 20;
            LinkRecheckClassRecord.Evaluate(record, LinkRecheckRules.Version, LinkRecheckRules.DecisionSignature).Proven.Should().BeEmpty("a record scored on a lower minimum proves nothing");

            LinkRecheckRules.ProvenProblem.Should().BeNull("the committed record was scored under these rules");
            LinkRecheckRules.SignatureSource().Should().Contain(string.Join(",", LinkRecheckRules.ProvenClasses.OrderBy(c => c, StringComparer.Ordinal)),
                "the lanes' signature changes when the record proves another class");
        }

        [Fact]
        public void The_lines_move_to_the_first_food_the_gate_lets_through_and_go_back_only_on_a_refuting_hold()
        {
            using var none = LinkRecheckRules.AssumeProven();
            var holds = new[] { Hold(LinkGate.Prepared) };
            LinkRecheckRules.Decide(LinkRecheckRules.Prepared, holds, () => new[]
                {
                    Candidate(LinkRecheckRules.VerifiedWordingTier, 2, LinkGate.Containment),
                    Candidate(LinkRecheckRules.NameRuleTier, 3),
                })
                .Target!.Food.Id.Should().Be(3);
            LinkRecheckRules.Decide(LinkRecheckRules.Prepared, holds, () => new[] { Candidate(LinkRecheckRules.NameRuleTier, 2, LinkGate.Prepared) })
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Unproven && v.Candidate == LinkRecheckRules.Unlink && v.Class == LinkGate.Prepared);
            LinkRecheckRules.Decide(LinkRecheckRules.Compound, new[] { Hold(LinkGate.Compound) }, () => new[] { Candidate(LinkRecheckRules.NameRuleTier, 2, LinkGate.Compound) })
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Unproven && v.Candidate == LinkRecheckRules.Unlink && v.Class == LinkGate.Compound);
            LinkRecheckRules.Decide(LinkRecheckRules.Applied, new[] { Hold(LinkGate.Containment) }, () => Enumerable.Empty<RecheckCandidate>())
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Unproven && v.Candidate == null, "a word rule alone never takes a line off its food");
            LinkRecheckRules.Decide(LinkRecheckRules.Applied, new[] { Hold(LinkGate.Form), Hold(LinkGate.Prepared) }, () => Enumerable.Empty<RecheckCandidate>())
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Unproven && v.Candidate == LinkRecheckRules.Unlink && v.Class == LinkGate.Form, "with no class proven the first refuting hold names the candidate's class");
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
            LinkRecheckRules.Blocking(tier, new[] { Hold(LinkGate.Variety) }).Count.Should().Be(spared ? 0 : 1, "the distinguishing rule of link-gate/13 is a word rule too");
            LinkRecheckRules.Blocking(tier, new[] { Hold(LinkGate.Incomplete), Hold(LinkGate.AlcoholDrink) }).Should().HaveCount(2);
        }

        [Fact]
        public void The_container_rule_never_takes_a_line_off_its_food_and_moves_one_only_as_a_candidate_while_not_proven()
        {
            LinkRecheckRules.Decide(LinkRecheckRules.ContainerForm, Array.Empty<GateHold>(), () => new[] { Candidate(LinkRecheckRules.CannedSisterTier, 2) }, containerWrong: true)
                .Should().Match<RecheckVerdict>(v => v.Outcome == LinkRecheckRules.Unproven && v.Candidate == LinkRecheckRules.Move && v.Class == LinkRecheckRules.ContainerClass);
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
            foreach (var proven in LinkRecheckRules.ProvenClasses) source.Should().Contain(proven);
            source.Should().Contain("%corn%", "the restriction patterns an alias may not add are in the signature");
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
            var verdict = LinkRecheckRules.Decide(LinkRecheckRules.Applied, new[] { hold }, () => Enumerable.Empty<RecheckCandidate>());
            var writes = refutes && LinkRecheckRules.Proven(LinkRecheckRules.ClassOf(hold));
            verdict.Outcome.Should().Be(writes ? LinkRecheckRules.Unlink : LinkRecheckRules.Unproven);
            verdict.Candidate.Should().Be(refutes && !writes ? LinkRecheckRules.Unlink : null);
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
        public void A_hold_a_later_alternative_raises_a_home_preparation_or_a_compound_naming_no_other_food_proves_nothing()
        {
            var group = new GateHold(LinkGate.Group, LinkGate.Group, "margarine: category 1");
            LinkRecheckRules.Refutes(group, new[] { group }, firstNamed: true).Should().BeFalse("butter or margarine on Butter: the line counts its first");
            LinkRecheckRules.Refutes(group, new[] { group }).Should().BeTrue();
            var part = new GateHold(LinkGate.Part, "wording part not in the food", "juice");
            LinkRecheckRules.Refutes(part, new[] { part }, firstNamed: true).Should().BeFalse();
            var toasted = new GateHold(LinkGate.Form, "wording form not in the food", "toasted");
            LinkRecheckRules.Refutes(toasted, new[] { toasted }).Should().BeFalse("toasted pine nuts are the pine nuts, toasted at home");
            var frozen = new GateHold(LinkGate.Form, "wording form not in the food", "frozen");
            LinkRecheckRules.Refutes(frozen, new[] { frozen }).Should().BeFalse();
            var canned = new GateHold(LinkGate.Form, "wording form not in the food", "canned");
            LinkRecheckRules.Refutes(canned, new[] { canned }).Should().BeTrue();
            var compound = Hold(LinkGate.Compound);
            LinkRecheckRules.Refutes(compound, new[] { compound }, namesOtherFood: false).Should().BeFalse("heavy cream, whipped is one food");

            var catalog = GateCatalog.Build(new[] { Food(1, "Butter", "Butter, salted"), Food(2, "Vinegar", "Vinegar, distilled"), Food(3, "Lemon Juice", "Lemon juice, raw"), Food(4, "Black Pepper", "Spices, pepper, black"), Food(5, "Salt", "Salt, table") });
            LinkGate.NamesFirstAlternative("butter or margarine", Food(1, "Butter", "Butter, salted"), catalog).Should().BeTrue();
            LinkGate.NamesFirstAlternative("lemon juice or white vinegar", Food(2, "Vinegar", "Vinegar, distilled"), catalog).Should().BeFalse("the vinegar is the second alternative");
            LinkGate.NamesFirstAlternative("lemon juice or white vinegar", Food(3, "Lemon Juice", "Lemon juice, raw"), catalog).Should().BeTrue();
            LinkGate.NamesOtherFood("salt and black pepper", Food(5, "Salt", "Salt, table"), catalog).Should().BeTrue();
            LinkGate.NamesOtherFood("heavy cream, whipped", Food(6, "Heavy Cream", "Cream, fluid, heavy whipping"), catalog).Should().BeFalse();
            LinkGate.NamesOtherFood("dates stoned and sliced", Food(7, "Dates", "Dates, deglet noor"), catalog).Should().BeFalse();
        }

        [Theory]
        [InlineData("1 cinnamon stick, 1-inch piece of fresh turmeric", "Cinnamon", true)]
        [InlineData("cayenne pepper, 1/2 teaspoon, or chopped sweet pepper, 1 small", "Cayenne Pepper", true)]
        [InlineData("rosemary, chopped", "Dried Rosemary", false)]
        [InlineData("1 tablespoon rosemary, 2 sprigs fresh rosemary", "Dried Rosemary", false)]
        [InlineData("fresh turmeric, 1 cinnamon stick", "Cinnamon", false)]
        [InlineData("2 tsp cinnamon", "Cinnamon", false)]
        public void A_fresh_cue_proves_nothing_against_the_food_named_first_when_it_qualifies_a_later_item(string text, string food, bool later)
        {
            LinkRecheckRules.FreshOfLaterItem(text, Food(1, food)).Should().Be(later);
        }

        [Theory]
        [InlineData("sugar per quart of juice", "sugar")]
        [InlineData("sugar, 10 oz. for each pound of juice", "sugar, 10 oz.")]
        [InlineData("sugar to a gallon of vinegar", "sugar")]
        [InlineData("powdered sugar per egg", "powdered sugar")]
        [InlineData("orange juice", null)]
        [InlineData("per person", null)]
        [InlineData("1/2 cup each butter and sugar", null)]
        [InlineData("1 cup every flour and sugar", null)]
        public void A_ratio_phrase_cuts_the_wording_to_the_food_it_measures(string wording, string? cut)
        {
            LinkRecheckRules.BeforeRatio(wording).Should().Be(cut);
        }

        [Fact]
        public void The_fresh_herb_class_and_the_ratio_shape_in_group_and_compound_decide_the_checkpoint_5_rows_by_rule()
        {
            var folder = Nom.Api.Tests.Golden.GoldenSet.Folder;
            var catalog = ModelLanes.GoldenGateCatalog(folder);
            var foods = ModelLanes.GateFoods(folder).ToDictionary(f => f.Id);
            var byFdc = foods.Values.Where(f => f.FdcId != null).GroupBy(f => f.FdcId!).ToDictionary(g => g.Key, g => g.First());
            var rows = LinkRecheckClassScorer.Rows(folder).Where(r => r.Id.StartsWith("lr-ck10s-", StringComparison.Ordinal)).ToDictionary(r => r.Id);
            IReadOnlyList<string> Classes(string id) => LinkRecheckClassScorer.Decide(rows[id], catalog, foods, byFdc).Verdict.Classes ?? Array.Empty<string>();

            Classes("lr-ck10s-01").Should().NotContain(LinkGate.FreshHerb, "staging checkpoint 5, Q14 (38519): the fresh turmeric is a later item");
            Classes("lr-ck10s-02").Should().NotContain(LinkGate.FreshHerb, "staging checkpoint 5, Q14 (49500): the chopped sweet pepper is the second alternative");
            Classes("lr-ck10s-03").Should().Contain(LinkGate.FreshHerb, "fresh rosemary is named again, so the dried spice is wrong");
            Classes("lr-ck10s-04").Should().NotContain(LinkGate.Group, "staging checkpoint 5, Q15: the juice after per is a measure");
            Classes("lr-ck10s-05").Should().NotContain(LinkGate.Compound);
            Classes("lr-ck10s-06").Should().NotContain(LinkGate.Group);
        }

        [Fact]
        public void A_move_gives_no_alias_that_names_a_restricted_food_its_new_food_does_not()
        {
            var spirit = new GateFood(1, "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof", "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof",
                CatalogAdmissionRule.SrLegacyFood, (long)CurationStatusEnum.Curated, 14, NutrientRows: 30, FdcId: "174815");
            var catalog = GateCatalog.Build(new[] { spirit });
            LinkRecheckRules.RestrictedWords("corn brandy", spirit).Should().Contain("%corn%");
            LinkRecheckRules.MayAlias("corn brandy", spirit, catalog).Should().BeFalse("corn brandy on distilled spirits made Corn Allergy keep out every brandy recipe");
            LinkRecheckRules.RestrictedWords("gin", spirit).Should().BeEmpty();
        }

        [Fact]
        public void Every_class_a_set_can_refute_with_has_a_name_and_the_alcohol_rules_are_one_class()
        {
            foreach (var code in LinkRecheckRules.AlcoholCodes) LinkRecheckRules.ClassOf(Hold(code)).Should().Be(LinkRecheckRules.AlcoholClass);
            LinkRecheckRules.ClassOf(Hold(LinkGate.Part)).Should().Be(LinkGate.Part);
            LinkRecheckRules.ProvenClasses.Should().BeSubsetOf(LinkRecheckRules.Refuting.Select(c => LinkRecheckRules.ClassOf(Hold(c))).Append(LinkRecheckRules.ContainerClass));
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
