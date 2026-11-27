using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The rules around the prose judge, on constructed text: a different step count, a fact the prose
    /// check fails on and a heat conflict decide before any model is asked; the model's reading of a
    /// step pair is a fault when a fact is different, missing or quoted from words the rewrite does not
    /// hold, or anything is added; the check's weak signals reach the model as notes on their step.
    /// </summary>
    public class ProseJudgeRulesTests
    {
        private static readonly string[] Original =
        {
            "Toast the walnuts in a dry pan over medium heat until they smell nutty.",
            "Roast the squash in a moderately hot oven for 40 minutes.",
            "Serve the soup hot, with the walnuts on top.",
        };

        private static readonly string[] Faithful =
        {
            "Dry-toast the walnuts in a pan on medium until they smell nutty.",
            "Give the squash 40 minutes in an oven that is moderately hot.",
            "Scatter the walnuts over the soup and serve it hot.",
        };

        private static ProseCheck.Finding Check(IReadOnlyList<string> original, IReadOnlyList<string> rewrite) =>
            ProseCheck.Check(new RewriteFidelity.SourceText(string.Empty, original), new RewriteFidelity.SourceText(string.Empty, rewrite));

        [Theory]
        [InlineData("Fry the shallots over a medium-high heat.", "stove:medium-high")]
        [InlineData("Cook gently on low for an hour.", "stove:low")]
        [InlineData("Simmer over moderate heat.", "stove:medium")]
        [InlineData("Bake in a moderately hot oven.", "oven:moderately hot")]
        [InlineData("Put it in a cool oven to dry.", "oven:slow")]
        [InlineData("Serve chilled with cream.", "serve:cold")]
        [InlineData("Pour on hot water and leave it.", "")]
        public void The_heat_a_step_names_is_read_by_scale(string step, string heat) =>
            string.Join(",", ProseJudgeRules.Heats(step)).Should().Be(heat);

        [Fact]
        public void A_heat_conflict_needs_a_level_on_both_sides_of_one_scale()
        {
            ProseJudgeRules.HeatConflicts("Fry over low heat.", "Fry it on high.").Should().Equal("stove");
            ProseJudgeRules.HeatConflicts("Fry over medium heat.", "Fry it on medium.").Should().BeEmpty();
            ProseJudgeRules.HeatConflicts("Fry over medium heat.", "Fry it until brown.").Should().BeEmpty("a level left out is the model's to see");
            ProseJudgeRules.HeatConflicts("Bake in a slow oven.", "Bake in a very hot oven.").Should().Equal("oven");
            ProseJudgeRules.HeatConflicts("Serve hot.", "Serve it cold.").Should().Equal("serve");
        }

        [Fact]
        public void A_heat_level_or_an_oven_the_rewrite_leaves_out_is_lost_by_rule()
        {
            ProseJudgeRules.HeatsLost("Fry the shallots over medium heat.", "Fry the shallots until soft.").Should().Equal("stove");
            ProseJudgeRules.HeatsLost("Fry the shallots over medium heat.", "Fry the shallots on medium.").Should().BeEmpty();
            ProseJudgeRules.HeatsLost("Serve hot.", "Serve it.").Should().Equal("serve");
            ProseJudgeRules.OvenLost("Heat the oven to 200°C.", "Heat to 200°C.").Should().BeTrue();
            ProseJudgeRules.OvenLost("Heat the oven to 200°C.", "Preheat the oven to 200°C.").Should().BeFalse();
            ProseJudgeRules.OvenLost("Cook it in the oven for an hour.", "Bake it for an hour.").Should().BeFalse("baking is done in the oven");

            var lost = Faithful.Select(s => s.Replace(" on medium", string.Empty, StringComparison.Ordinal)).ToList();
            ProseJudgeRules.Before(Original, lost, Check(Original, lost))!.Steps.Where(s => !s.Same).Select(s => s.Number).Should().Equal(1);
        }

        [Fact]
        public void A_faithful_rewrite_is_left_to_the_model()
        {
            ProseJudgeRules.Before(Original, Faithful, Check(Original, Faithful)).Should().BeNull();
        }

        [Fact]
        public void Rules_decide_a_step_count_a_failed_fact_and_a_heat_conflict()
        {
            var shorter = Faithful.Take(2).ToList();
            ProseJudgeRules.Before(Original, shorter, Check(Original, shorter))!.Faithful.Should().BeFalse();

            var longer = Faithful.Select(s => s.Replace("40 minutes", "25 minutes", StringComparison.Ordinal)).ToList();
            var amount = ProseJudgeRules.Before(Original, longer, Check(Original, longer))!;
            amount.Faithful.Should().BeFalse();
            amount.FirstFlagged!.Amounts.Should().BeFalse();

            var hotter = Faithful.Select(s => s.Replace("on medium", "on high", StringComparison.Ordinal)).ToList();
            var heat = ProseJudgeRules.Before(Original, hotter, Check(Original, hotter))!;
            heat.Steps.Where(s => !s.Same).Select(s => s.Number).Should().Equal(1);
        }

        [Fact]
        public void A_weak_signal_of_the_check_is_a_note_on_its_own_step()
        {
            var rewrite = Faithful.ToArray();
            rewrite[1] = "Put the squash in an oven that is moderately hot for 40 minutes.";
            var finding = Check(Original, rewrite);

            finding.Unsure.Should().BeTrue();
            ProseJudgeRules.Notes(finding, 2).Should().ContainSingle().Which.Should().Contain("bake");
            ProseJudgeRules.Notes(finding, 1).Should().BeEmpty();
        }

        [Fact]
        public void A_step_is_the_same_only_when_every_fact_is_found_as_quoted_and_nothing_is_added()
        {
            var original = Original[0];
            var rewrite = Faithful[0];
            ProseStepFact Fact(string kind, string ours, string theirs, string match = ProseJudgeRules.Same) => new(kind, ours, theirs, match);
            var same = new[] { Fact("action", "Toast the walnuts", "toast the walnuts"), Fact("heat", "over medium heat", "on medium"), Fact("cue", "until they smell nutty", "until they smell nutty") };

            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(same, Array.Empty<ProseStepFact>())).Same.Should().BeTrue();
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(same.Append(Fact("cue", "until crisp", "until crisp")).ToList(), Array.Empty<ProseStepFact>()))
                .Missing.Should().BeTrue("words the rewrite does not hold are no match");
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(new[] { Fact("heat", "over medium heat", "on high") }, Array.Empty<ProseStepFact>()))
                .Same.Should().BeFalse();
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(new[] { Fact("cue", "until they smell nutty", "until browned", ProseJudgeRules.Different) }, Array.Empty<ProseStepFact>()))
                .Actions.Should().BeFalse();
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(new[] { Fact("action", "Toast the walnuts", string.Empty) }, Array.Empty<ProseStepFact>()))
                .Missing.Should().BeTrue();
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(same, new[] { Fact("ingredient", string.Empty, "a pinch of salt", ProseJudgeRules.Different) }))
                .Added.Should().BeTrue();
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(Array.Empty<ProseStepFact>(), Array.Empty<ProseStepFact>()))
                .Same.Should().BeFalse("a reading of no fact is not trusted");
        }

        [Fact]
        public void A_claim_asked_again_is_withdrawn_only_by_a_rule_or_a_same_result_whose_quote_holds()
        {
            var rewrite = "Dry-toast the walnuts in a pan on medium until they smell nutty.";
            var heat = new ProseStepFact("heat", "over medium heat", "on medium", ProseJudgeRules.Different);
            var words = new ProseStepFact("action", "Toast the walnuts", string.Empty, ProseJudgeRules.Missing);
            var cue = new ProseStepFact("cue", "until crisp", string.Empty, ProseJudgeRules.Different);

            ProseJudgeRules.FoundByRule(heat, rewrite).Should().BeTrue("the same level on the same scale");
            ProseJudgeRules.FoundByRule(heat, rewrite.Replace("on medium", "on high", StringComparison.Ordinal)).Should().BeFalse();
            ProseJudgeRules.FoundByRule(words, rewrite).Should().BeTrue("every word of it is in the step");
            ProseJudgeRules.FoundByRule(cue, rewrite).Should().BeFalse();
            ProseJudgeRules.FoundByRule(new ProseStepFact("heat", "in a deep skillet over medium heat", string.Empty, ProseJudgeRules.Different), rewrite).Should().BeTrue("a heat fact is its level");
            ProseJudgeRules.FoundByRule(new ProseStepFact("serve", "Serve hot with cream", string.Empty, ProseJudgeRules.Different), "Serve it hot with milk.").Should().BeFalse("the heat agrees, the food does not");
            ProseJudgeRules.FoundByRule(new ProseStepFact("serve", "Serve hot with cream", string.Empty, ProseJudgeRules.Different), "Pour cream over and serve it hot.").Should().BeTrue();
            ProseJudgeRules.Step(1, Original[0], rewrite, new ProseStepReading(new[] { ProseJudgeRules.Kept(heat, null, Original[0], rewrite) }, Array.Empty<ProseStepFact>())).Same.Should().BeTrue("a fact a rule found is not quoted again");

            static string Answer(string quote, string verdict) => JsonSerializer.Serialize(new { rewrite = quote, original_result = "x", rewrite_result = "y", verdict });
            ProseJudgeRules.Kept(cue, Answer("until they smell nutty", "same_result"), Original[0], rewrite).Match.Should().Be(ProseJudgeRules.Same);
            ProseJudgeRules.Kept(cue, Answer("until golden and crisp", "same_result"), Original[0], rewrite).Match.Should().Be(ProseJudgeRules.Different, "a quote the step does not hold withdraws nothing");
            ProseJudgeRules.Kept(cue, Answer("until they smell nutty", "other_result"), Original[0], rewrite).Match.Should().Be(ProseJudgeRules.Different);
            ProseJudgeRules.Kept(cue, Answer(string.Empty, "not_said"), Original[0], rewrite).Match.Should().Be(ProseJudgeRules.Missing);
            ProseJudgeRules.Kept(cue, "not json", Original[0], rewrite).Match.Should().Be(ProseJudgeRules.Different);
            ProseJudgeRules.AlreadySaid(JsonSerializer.Serialize(new { original = "in a dry pan", verdict = "kept" }), Original[0]).Should().BeTrue();
            ProseJudgeRules.AlreadySaid(JsonSerializer.Serialize(new { original = "with honey", verdict = "kept" }), Original[0]).Should().BeFalse();
        }

        [Fact]
        public void A_rule_finds_a_fact_said_in_words_of_the_same_meaning_and_never_another_action_or_cue()
        {
            ProseStepFact Fact(string kind, string ours) => new(kind, ours, string.Empty, ProseJudgeRules.Different);

            ProseJudgeRules.FoundByRule(Fact("action", "Tip in the stock"), "Add the stock and stir.").Should().BeTrue();
            ProseJudgeRules.FoundByRule(Fact("action", "Cream the lard in a basin"), "Cream the lard in a bowl.").Should().BeTrue();
            ProseJudgeRules.FoundByRule(Fact("action", "cook the shallot in a little oil"), "Fry the shallot in a little oil.").Should().BeTrue("a broad verb is kept by a narrower one");
            ProseJudgeRules.FoundByRule(Fact("action", "fry the shallot"), "Cook the shallot.").Should().BeFalse("a narrower verb is not kept by a broad one");
            ProseJudgeRules.FoundByRule(Fact("action", "cook the shallot in a little oil"), "Boil the shallot in a little oil.").Should().BeFalse();
            ProseJudgeRules.FoundByRule(Fact("cue", "until soft"), "Stew it until tender.").Should().BeTrue();
            ProseJudgeRules.FoundByRule(Fact("cue", "until soft"), "Stew it until still firm.").Should().BeFalse();
            ProseJudgeRules.FoundByRule(Fact("cue", "until golden"), "Fry until dark brown.").Should().BeFalse();
            ProseJudgeRules.FoundByRule(Fact("action", "Melt the lard in a small heavy pan"), "Melt the lard in a pan.").Should().BeTrue("size and manner are no fact");
        }

        [Fact]
        public void Amounts_are_left_to_the_check_only_when_it_finds_the_step_amounts_the_same()
        {
            string[] original = { "Rest the dough for a half hour.", "Bake it for 20 minutes." };
            string[] same = { "Let the dough sit 30 minutes.", "Bake it for 20 minutes." };
            string[] longer = { "Let the dough sit 30 minutes.", "Bake it for 35 minutes." };
            var amount = new ProseStepFact("amount", "for a half hour", string.Empty, ProseJudgeRules.Different);

            ProseJudgeRules.AmountsAgree(Check(original, same), 1).Should().BeTrue();
            ProseJudgeRules.AmountsAgree(Check(original, longer), 2).Should().BeFalse();
            ProseJudgeRules.FoundByRule(amount, same[0], amountsAgree: true).Should().BeTrue();
            ProseJudgeRules.FoundByRule(amount, same[0]).Should().BeFalse();
        }

        [Fact]
        public void A_thing_read_as_added_is_withdrawn_by_rule_only_when_the_original_step_says_it()
        {
            var original = "Cook the shallot in a little oil in a saucepan over low heat.";
            ProseStepFact Added(string words) => new("action", string.Empty, words, ProseJudgeRules.Different);

            ProseJudgeRules.AddedByRule(Added("in a small pan"), original).Should().BeFalse();
            ProseJudgeRules.AddedByRule(Added("fry the shallot"), original).Should().BeFalse("a narrower word for the original's action");
            ProseJudgeRules.AddedByRule(Added("Strain it through a fine sieve"), original).Should().BeTrue();
            ProseJudgeRules.AddedByRule(Added("on high"), original).Should().BeTrue("another heat");
            ProseJudgeRules.AddedByRule(Added("for 5 minutes"), original).Should().BeTrue("an amount is new unless the check finds the amounts the same");
            ProseJudgeRules.AddedByRule(Added("for 5 minutes"), original, amountsAgree: true).Should().BeFalse();
        }

        [Fact]
        public void A_clause_with_a_word_the_rule_does_not_find_is_asked_again_whatever_the_model_read()
        {
            var original = "Boil the noodles in salted water, then drain them.";

            ProseJudgeRules.Uncovered(original, "Boil the noodles until tender, then drain them.", Array.Empty<ProseStepFact>())
                .Should().ContainSingle().Which.Original.Should().Be("Boil the noodles in salted water");
            ProseJudgeRules.Uncovered(original, "Cook the noodles at a boil in salted water and strain them.", Array.Empty<ProseStepFact>()).Should().BeEmpty();
        }

        [Fact]
        public void An_action_of_the_original_no_fact_names_is_checked_as_missing()
        {
            var original = "Take the pan off the heat and whisk in the cream.";
            var read = new[] { new ProseStepFact("action", "Take the pan off the heat", "Lift the pan off the burner", ProseJudgeRules.Same) };

            ProseJudgeRules.Unread(original, read).Should().ContainSingle().Which.Should().Be(new ProseStepFact("action", "whisk in the cream", string.Empty, ProseJudgeRules.Missing));
            ProseJudgeRules.Unread(original, read.Append(new ProseStepFact("action", "whisk in the cream", "beat the cream in", ProseJudgeRules.Same)).ToList()).Should().BeEmpty();
        }

        [Fact]
        public void A_reading_is_parsed_only_with_a_known_match()
        {
            ProseJudgeRules.Parse("{\"facts\":[{\"kind\":\"action\",\"original\":\"a\",\"rewrite\":\"b\",\"match\":\"same\"}],\"added\":[]}")!.Facts.Should().ContainSingle();
            ProseJudgeRules.Parse("{\"facts\":[{\"kind\":\"action\",\"original\":\"a\",\"rewrite\":\"b\",\"match\":\"kept\"},{\"kind\":\"cue\",\"original\":\"c\",\"rewrite\":\"d\",\"match\":\"changed\"},{\"kind\":\"cue\",\"original\":\"e\",\"rewrite\":\"\",\"match\":\"lost\"}],\"added\":[]}")!
                .Facts.Select(f => f.Match).Should().Equal(ProseJudgeRules.Same, ProseJudgeRules.Different, ProseJudgeRules.Missing);
            ProseJudgeRules.Parse("{\"facts\":[{\"kind\":\"action\",\"original\":\"a\",\"rewrite\":\"b\",\"match\":\"maybe\"}],\"added\":[]}").Should().BeNull();
            ProseJudgeRules.Parse("not json").Should().BeNull();
        }

        [Fact]
        public void A_rule_pairs_voice_clause_order_articles_and_verbs_of_one_group()
        {
            var original = "Beat in the egg and the baking powder until smooth.";
            var passive = ProseJudgeRules.Pair(original, "The egg and baking powder should be beaten into the oat mixture until it becomes smooth.");
            passive.Unpaired.Should().BeEmpty("\"should be beaten\" is the passive of the original's verb");
            ProseJudgeRules.Pair(original, "Whisk the egg and baking powder in until smooth.").Unpaired.Should().BeEmpty("whisk beats");
            ProseJudgeRules.Pair("Drain the beans and rinse them under cold water.", "Under cold water the beans get rinsed, once they have been drained.").Unpaired.Should().BeEmpty();
            ProseJudgeRules.Pair("Cook the pancakes 2 minutes a side.", "Boil the pancakes 2 minutes a side.").Unpaired.Should().ContainSingle("the generic verb is not kept by another method");
            ProseJudgeRules.Pair("Peel the melon and cut it into cubes.", "Peel and cut the melon.").Unpaired.Select(u => u.Original).Should().Equal("cut it into cubes");
            ProseJudgeRules.Pair("Whip the whites until stiff.", "Fold the whites until stiff.").Unpaired.Should().ContainSingle();
        }

        [Fact]
        public void A_rewrite_clause_with_a_word_the_original_does_not_hold_may_add_something()
        {
            string[] method = { "Melt the chocolate over simmering water.", "Spread the chocolate on the tray and leave it to set." };
            ProseJudgeRules.Pair(method[1], "The melted chocolate should be spread on the tray and left to set.", method: method).Added.Should().BeEmpty("a state from an earlier step adds nothing");
            var added = ProseJudgeRules.Pair(method[1], "Spread the chocolate on the tray, squeeze lemon over it and leave it to set.", method: method).Added;
            added.Should().ContainSingle().Which.Rewrite.Should().Be("squeeze lemon over it");
            ProseJudgeRules.NewWords(added[0], method[1], method: method).Should().Equal("squeeze", "lemon");
            ProseJudgeRules.Pair("Fry the onion until soft.", "Fry the onion over high heat until soft.").Added.Should().ContainSingle("a heat the original does not name");
        }

        [Fact]
        public void A_terse_rewrite_is_read_whole_even_when_every_clause_pairs()
        {
            var terse = ProseJudgeRules.Pair("Peel the pears and cut them in half lengthwise.", "Peel pears, cut half lengthwise.");
            terse.Unpaired.Should().ContainSingle().Which.Original.Should().Be("Peel the pears and cut them in half lengthwise.");
            ProseJudgeRules.Pair("Peel the pears and cut them in half lengthwise.", "The pears should be peeled and then cut in half lengthwise.").Unpaired.Should().BeEmpty();
        }

        [Fact]
        public void An_unpaired_part_stands_unless_the_answer_keeps_it_with_a_quote_the_rewrite_holds()
        {
            var part = new ProseStepFact("action", "Pick over the lentils", string.Empty, ProseJudgeRules.Missing);
            var rewrite = "Sort through the lentils and rinse them.";
            static string Answer(string quote, string verdict) => JsonSerializer.Serialize(new { rewrite = quote, verdict });

            ProseJudgeRules.PartKept(part, Answer("Sort through the lentils", "kept"), rewrite).Match.Should().Be(ProseJudgeRules.Same);
            ProseJudgeRules.PartKept(part, Answer("Wash the beans", "kept"), rewrite).Match.Should().Be(ProseJudgeRules.Missing, "a quote the step does not hold withdraws nothing");
            ProseJudgeRules.PartKept(part, Answer("Sort through the lentils", "changed"), rewrite).Match.Should().Be(ProseJudgeRules.Different);
            ProseJudgeRules.PartKept(part, "not json", rewrite).Match.Should().Be(ProseJudgeRules.Missing);
            ProseJudgeRules.PartSaid(JsonSerializer.Serialize(new { original = "in the butter", verdict = "said" }), "Fry the onion in the butter.").Should().BeTrue();
            ProseJudgeRules.PartSaid(JsonSerializer.Serialize(new { original = "a pinch of salt", verdict = "said" }), "Fry the onion in the butter.").Should().BeFalse();
            ProseJudgeRules.PartSaid(JsonSerializer.Serialize(new { original = "in the butter", verdict = "new" }), "Fry the onion in the butter.").Should().BeFalse();
        }

        [Fact]
        public void The_writers_filler_adds_nothing_only_where_the_method_has_its_action_at_that_place()
        {
            string[] method = { "Grate the parsnips and squeeze them dry.", "Fry spoonfuls of the batter in hot oil.", "Drain the cakes on paper and serve." };
            ProseJudgeRules.Pair(method[0], "The parsnips should be grated and squeezed dry, by which time they are ready for frying.", method: method, step: 1).Added.Should().BeEmpty("they are fried in step 2");
            ProseJudgeRules.Pair(method[0], "Before frying, the parsnips get grated and squeezed dry.", method: method, step: 1).Added.Should().BeEmpty();
            ProseJudgeRules.Pair(method[2], "Once the cakes are fried, drain them on paper and serve.", method: method, step: 3).Added.Should().BeEmpty("they were fried in step 2");
            ProseJudgeRules.Pair(method[0], "The parsnips should be grated and squeezed dry, which is needed for the next step.", method: method, step: 1).Added.Should().BeEmpty();
            ProseJudgeRules.Pair(method[0], "The parsnips should be grated and squeezed dry, by which time they are ready for baking.", method: method, step: 1).Added.Should().ContainSingle("the method bakes nothing");
            ProseJudgeRules.Pair(method[1], "Fry spoonfuls of the batter in hot oil after draining.", method: method, step: 2).Added.Should().ContainSingle("draining comes later, not before");
            ProseJudgeRules.Pair(method[2], "Once the cakes are crisp, drain them on paper and serve.", method: method, step: 3).Added.Should().ContainSingle("a state the method never names is a cue");
            ProseJudgeRules.Pair(method[0], "The parsnips should be grated and squeezed dry, by which time they are ready for frying.").Added.Should().ContainSingle("with no method nothing is filler");
            ProseJudgeRules.Pair(method[2], "Drain the cakes on paper, which allows them to be served.", method: method, step: 3).Added.Should().BeEmpty();
            ProseJudgeRules.Pair(method[2], "With the cakes ready, drain them on paper and serve.", method: method, step: 3).Added.Should().BeEmpty();
            ProseJudgeRules.Pair(method[2], "Drain the cakes on paper and serve with chives to enhance their taste.", method: method, step: 3).Added.Should().ContainSingle("the chives are new, the purpose is not");
        }

        [Fact]
        public void The_dish_named_by_its_title_adds_nothing_and_a_word_of_manner_must_be_kept()
        {
            string[] method = { "Mash the beans with the oil.", "Stir in the lemon juice until smooth." };
            ProseJudgeRules.Pair(method[1], "The lemon juice gets stirred in until the hummus is smooth.", method: method, title: "White Bean Hummus", step: 2).Added.Should().BeEmpty();
            ProseJudgeRules.Pair(method[1], "The lemon juice gets stirred in until the hummus is smooth.", method: method, step: 2).Added.Should().ContainSingle();
            ProseJudgeRules.Dish("tomatoes", "Bread with Tomatoes", method).Should().BeFalse("only the head of the title names the dish");
            ProseJudgeRules.Pair("Brush the rolls lightly with milk.", "The rolls should be brushed with milk.").Unpaired.Should().ContainSingle();
            ProseJudgeRules.Pair("Brush the rolls lightly with milk.", "The rolls get a light brushing of milk.").Unpaired.Should().BeEmpty();
            ProseJudgeRules.Pair("Grill the steak for 3 minutes on each side.", "The steak should be grilled 3 minutes a side.").Unpaired.Should().BeEmpty();
            ProseJudgeRules.Pair("Grill the steak for 3 minutes on each side.", "The steak should be grilled for 3 minutes.").Unpaired.Should().ContainSingle();
            ProseJudgeRules.Pair("Lay the trout on the tray skin side down.", "The trout goes on the tray skin side up.").Unpaired.Should().ContainSingle("up is not down");
            ProseJudgeRules.Pair("Stir the jam constantly for 5 minutes.", "Keep stirring the jam for 5 minutes.").Unpaired.Should().BeEmpty();

            string[] pan = { "Heat the oil in a wok.", "Add the shrimp to the wok and fry for 2 minutes." };
            ProseJudgeRules.Pair(pan[1], "Next the shrimp get added and fry for 2 minutes.", method: pan, step: 2, previousRewrite: "The oil gets hot in a wok.").Unpaired.Should().BeEmpty("the wok of the step before");
            ProseJudgeRules.Pair(pan[1], "Next the shrimp get added and fry for 2 minutes.", method: pan, step: 2, previousRewrite: "The oil gets hot.").Unpaired.Should().ContainSingle();
        }

        [Fact]
        public void An_amount_bound_to_something_it_does_not_measure_is_a_fault_by_rule()
        {
            ProseJudgeRules.AmountMisbound("Fry the radishes for 3 minutes.", "Fry 3 minutes of radishes.").Should().BeTrue();
            ProseJudgeRules.AmountMisbound("Add ¼ teaspoon salt and cook for 1 minute.", "Add the salt and cook for ¼ teaspoon.").Should().BeTrue();
            ProseJudgeRules.AmountMisbound("Fry the radishes for 3 minutes, stirring.", "The radishes need 3 minutes of frying and stirring.").Should().BeFalse();
            ProseJudgeRules.AmountMisbound("Add ¼ teaspoon salt and cook for 1 minute.", "Cook for 1 minute with ¼ teaspoon salt.").Should().BeFalse();
        }

        [Fact]
        public void Two_answers_on_one_fact_that_disagree_refuse_the_step_as_a_disagreement()
        {
            var kept = new ProseStepFact("cue", "until smooth", "no lumps remain", ProseJudgeRules.Same);
            var added = new ProseStepFact("cue", string.Empty, "until no lumps remain in the sauce", ProseJudgeRules.Different);
            var step = ProseJudgeRules.Step(1, "Whisk until smooth.", "Whisk until no lumps remain in the sauce.", new ProseStepReading(new[] { kept }, new[] { added }));
            step.Same.Should().BeFalse();
            step.Note.Should().Be(ProseJudgeRules.Disagreement);
        }

        [Fact]
        public async Task The_judge_asks_no_model_when_a_rule_decides_or_pairs_every_part_and_one_call_per_unpaired_part_otherwise()
        {
            var handler = new Answering("{\"rewrite\":\"\",\"verdict\":\"lost\"}");
            var judge = new OllamaProseStepJudge(new HttpClient(handler), "http://judge.invalid", "mistral-nemo");

            var hotter = Faithful.Select(s => s.Replace("on medium", "on high", StringComparison.Ordinal)).ToList();
            (await judge.JudgeAsync(Original, hotter, CancellationToken.None))!.Faithful.Should().BeFalse();
            handler.Bodies.Should().BeEmpty();

            string[] passive = { Faithful[0], "The squash should be roasted for 40 minutes in a moderately hot oven.", "The soup is served hot, with the walnuts on top." };
            (await judge.JudgeAsync(Original, passive, CancellationToken.None))!.Faithful.Should().BeTrue();
            handler.Bodies.Should().BeEmpty("every part of every step is paired by rule");

            var other = passive.ToArray();
            other[0] = "Dry-toast the walnuts in a pan on medium until they look golden.";
            var verdict = (await judge.JudgeAsync(Original, other, CancellationToken.None))!;
            verdict.Steps.Where(s => !s.Same).Select(s => s.Number).Should().Equal(1);
            handler.Bodies.Should().HaveCount(2, "one question for the clause with the unmatched cue and one for the words the original does not hold");
            using var first = JsonDocument.Parse(handler.Bodies[0]);
            first.RootElement.GetProperty("options").GetProperty("seed").GetInt32().Should().Be(ProseLanePrompts.JudgeSeed);
            first.RootElement.GetProperty("options").GetProperty("temperature").GetDouble().Should().Be(0);
            first.RootElement.GetProperty("messages")[1].GetProperty("content").GetString().Should().Contain("PART: Toast the walnuts in a dry pan over medium heat until they smell nutty").And.Contain("WORDS NOT MATCHED: smell, nutty");

            handler.Bodies.Clear();
            other[0] = Faithful[0];
            other[1] = "The squash should be roasted in a moderately hot oven for 40 minutes, then sprinkled with sage.";
            (await judge.JudgeAsync(Original, other, CancellationToken.None, "Squash Soup"))!.Faithful.Should().BeFalse();
            using var asked = JsonDocument.Parse(handler.Bodies.Single());
            asked.RootElement.GetProperty("messages")[1].GetProperty("content").GetString().Should().Contain("RECIPE: Squash Soup").And.Contain("PREVIOUS REWRITE STEP: " + Faithful[0]);
        }

        private sealed class Answering : HttpMessageHandler
        {
            private readonly string _content;

            public Answering(string content) => _content = content;

            public List<string> Bodies { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                var reply = JsonSerializer.Serialize(new { message = new { content = _content } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
            }
        }
    }
}
