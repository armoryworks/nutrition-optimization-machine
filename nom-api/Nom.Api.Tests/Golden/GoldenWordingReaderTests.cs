using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The golden sets of the reader lanes, checked without a model: every lexicon row is a term of
    /// one word with a meaning in lexicon form, and every = row with a food is let past the gate's
    /// identity rules by its entry, though a state or marked rule may still hold it
    /// (wording-lexicon.tsv); every line of alcohol-readings.tsv is one the rules leave unclear in
    /// its mode, every judged reading is one of the mode's, and a reading a rule row refuses is never
    /// written whatever both readers say; and the case sets of the four scored lanes are what the
    /// reader scorer reads.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenWordingReaderTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenWordingReaderTests(ITestOutputHelper output) => _output = output;

        private static readonly Lazy<Dictionary<long, GateFood>> Foods = new(() =>
            ModelLanes.GateFoods(GoldenSet.Folder).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First()));

        private static readonly Lazy<GateCatalog> Catalog = new(() => ModelLanes.GoldenGateCatalog(GoldenSet.Folder));

        [Fact]
        public void Every_lexicon_row_is_a_term_of_one_word_with_a_meaning_in_lexicon_form()
        {
            foreach (var row in GoldenSet.Read("wording-lexicon.tsv").Rows)
            {
                WordingReaders.LexiconTerm(row["term"]).Should().Be(row["term"], row.Id);
                WordingReaders.Meaning(row["means"], row["term"]).Should().Be(row["means"], row.Id);
                row["expect"].Should().BeOneOf("=", "!");
            }
        }

        /// <summary>The gate rules a lexicon entry answers: a wording word, its head noun or the food's lead word missing from the other side.</summary>
        private static readonly HashSet<string> IdentityHolds = new(StringComparer.Ordinal) { LinkGate.HeadNoun, LinkGate.Containment, LinkGate.LeadNoun };

        [Fact]
        public void The_entry_of_every_lexicon_row_answers_the_gates_identity_rules_for_its_food()
        {
            var report = GoldenSet.Run("wording-lexicon.tsv", "status", row =>
            {
                if (row["expect"] != "=" || row.Value("foodId") is not { } id) return null;
                var food = Foods.Value[long.Parse(id, CultureInfo.InvariantCulture)];
                var pair = new GatePair(row["term"], 0, food, ReadersAgreed: true);
                var with = LinkGate.Check(pair, WordingReaders.WithLexicon(Catalog.Value, WordingReaders.GateLexicon(new[] { (row["term"], row["means"]) })));
                var without = LinkGate.Check(pair, Catalog.Value);
                _output.WriteLine($"{row.Id} {row["term"]} -> {food.Text}: without the entry {(without.Count == 0 ? "pass" : string.Join(", ", without.Select(h => h.Code)))}");
                var identity = with.Where(h => IdentityHolds.Contains(h.Code)).ToList();
                return new GoldenOutcome(identity.Count == 0, $"no identity hold ({row["term"]} means {row["means"]} -> {food.Text})",
                    identity.Count == 0 ? (with.Count == 0 ? "pass" : "other holds: " + string.Join(", ", with.Select(h => h.Code))) : string.Join("; ", identity.Select(h => h.Code + ": " + h)),
                    new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["judgedBy"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Every_alcohol_reading_row_is_a_line_the_rules_leave_unclear_in_its_mode()
        {
            foreach (var c in ModelLanes.AlcoholReadingCases(GoldenSet.Folder))
            {
                var reading = AlcoholLineContext.Read(c.Line.Raw, ModelLanes.Context(c.Line));
                AlcoholFixedReading.ModeOf(reading).Should().Be(c.Mode, $"{c.Id}: \"{c.Line.Raw}\" reads {reading}");
                var expected = c.Expect.TrimStart('!');
                AlcoholFixedReading.Options(c.Mode).Should().Contain(expected, c.Id);
            }
        }

        [Fact]
        public void A_reading_a_rule_row_refuses_is_never_written_whatever_both_readers_say()
        {
            var report = GoldenSet.Run("alcohol-readings.tsv", "status", row =>
            {
                if (!row["expect"].StartsWith('!')) return null;
                var c = ModelLanes.AlcoholReadingCases(GoldenSet.Folder).Single(x => x.Id == "alcohol-reading:" + row.Id);
                var refused = row["expect"][1..];
                var (reading, reason) = AlcoholFixedReading.Combine(c.Pack, new[] { refused }, new[] { refused });
                var outcome = c.Outcome(reading?.Code);
                return new GoldenOutcome(outcome.Pass && reading == null, $"{row["expect"]} ({row["raw"]})", reading?.Code ?? reason, new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
            report.Ok.Should().BeGreaterThanOrEqualTo(3);
        }

        [Fact]
        public void The_reader_lanes_score_the_case_sets_the_release_gate_checks()
        {
            var read = ModelLanes.ReadCases(GoldenSet.Folder).ToList();
            var pairs = GoldenSet.Read("wording-pairs.tsv").Rows.Count(r => r["expect"] is "=" or "!");
            read.Count(c => c.Id.StartsWith("read:wp-", StringComparison.Ordinal)).Should().Be(pairs);
            read.Should().Contain(c => c.Id.StartsWith("read:tiers:", StringComparison.Ordinal), "the judged pairs the tiers leave are read too");
            read.Select(c => c.Id).Should().OnlyHaveUniqueItems();
            _output.WriteLine($"wording-read: {read.Count} cases, {read.Select(c => c.Wording).Distinct().Count()} wordings");

            var audit = ModelLanes.AuditCases(GoldenSet.Folder).ToList();
            audit.Count(c => c.Bad).Should().Be(ModelLanes.AuditPairsPerSide);
            audit.Count(c => !c.Bad).Should().Be(ModelLanes.AuditPairsPerSide);
            ModelLanes.AuditCases(GoldenSet.Folder).Select(c => c.Id).Should().Equal(audit.Select(c => c.Id));

            ModelLanes.LexiconCases(GoldenSet.Folder).Should().HaveCount(GoldenSet.Read("wording-lexicon.tsv").Rows.Count);
            ModelLanes.AlcoholReadingCases(GoldenSet.Folder).Should().HaveCount(GoldenSet.Read("alcohol-readings.tsv").Rows.Count);
            ModelLanes.Readers.Should().OnlyContain(l => ModelLanes.Scored.Contains(l) && ModelLanes.Floors(GoldenSet.Folder).ContainsKey(l));
        }

        [Fact]
        public void A_reader_lane_record_names_its_reader_b()
        {
            ModelLanes.CheckModelOf(ModelLanes.WordingReadLane, null).Should().Be(new Nom.Orch.Settings.LaneModelRoster().ReaderB);
            ModelLanes.CheckModelOf(ModelLanes.LinkAuditLane, "granite3").Should().Be("granite3");
            ModelLanes.CheckModelOf(ModelLanes.LinkVerify, null).Should().BeNull();
        }

        [Fact]
        public void A_link_audit_case_counts_as_flagged_only_when_both_readers_find_it_wrong()
        {
            var bad = new ModelLanes.AuditCase("audit:x", "potatoes", "Potatoes, raw, skin", true, "agent-review-2026-11-13");
            bad.Outcome(Nom.Orch.Interfaces.LinkJudgement.DifferentFood, Nom.Orch.Interfaces.LinkJudgement.VarietyOrForm).Should().Match<LaneCaseResult>(r => r.Pass && r.Flagged == true);
            bad.Outcome(Nom.Orch.Interfaces.LinkJudgement.DifferentFood, Nom.Orch.Interfaces.LinkJudgement.Same).Should().Match<LaneCaseResult>(r => !r.Pass && r.FalseAccept && r.Flagged == false);
            var good = bad with { Bad = false };
            good.Outcome(Nom.Orch.Interfaces.LinkJudgement.Same, null).Should().Match<LaneCaseResult>(r => r.Pass && r.Flagged == false);

            var score = ModelLanes.Score(ModelLanes.LinkAuditLane, "phi4", "host", "sig", new[]
            {
                bad.Outcome(Nom.Orch.Interfaces.LinkJudgement.DifferentFood, Nom.Orch.Interfaces.LinkJudgement.DifferentFood),
                good.Outcome(Nom.Orch.Interfaces.LinkJudgement.Same, null),
            });
            (score.AuditKnownBad, score.AuditFlaggedBad, score.AuditKnownGood, score.AuditFlaggedGood).Should().Be((1, 1, 1, 0));
        }

        [Fact]
        public void A_link_audit_record_also_counts_the_auditor_alone_for_the_readers_whose_picks_reader_b_made()
        {
            var bad = new ModelLanes.AuditCase("audit:x", "potatoes", "Potatoes, raw, skin", true, "agent-review-2026-11-13");
            var good = bad with { Id = "audit:y", Bad = false };

            var score = ModelLanes.Score(ModelLanes.LinkAuditLane, "phi4", "host", "sig", new[]
            {
                bad.Outcome(Nom.Orch.Interfaces.LinkJudgement.DifferentFood, Nom.Orch.Interfaces.LinkJudgement.Same),
                good.Outcome(Nom.Orch.Interfaces.LinkJudgement.VarietyOrForm, Nom.Orch.Interfaces.LinkJudgement.Same),
            });

            (score.AuditKnownBad, score.AuditFlaggedBad, score.AuditKnownGood, score.AuditFlaggedGood).Should().Be((1, 0, 1, 0));
            (score.AuditorKnownBad, score.AuditorFlaggedBad, score.AuditorKnownGood, score.AuditorFlaggedGood).Should().Be((1, 1, 1, 1));
            ModelLanes.AuditModelOf(ModelLanes.LinkAuditLane, "phi4", null).Should().Be("phi4");
            ModelLanes.AuditModelOf(ModelLanes.AlcoholReadingLane, "qwen2.5:14b-instruct", null).Should().Be(new Nom.Orch.Settings.LaneModelRoster().Auditor);
            ModelLanes.AuditModelOf(ModelLanes.WordingReadLane, "qwen2.5:14b-instruct", null).Should().BeNull();
        }

        [Fact]
        public void The_alcohol_reading_auditor_flags_the_judged_reading_and_the_other_reading_of_a_line_it_reads_in_context()
        {
            var line = new FixedLine("1 pint cider", "Cider Cup", new List<string>(), null, false);
            var hard = new ModelLanes.AlcoholReadingCase("alcohol-reading:x", AlcoholFixedReading.Cider, line, "hard", "agent-review-wording-readers-2026-11-21");

            hard.AuditorFlags("hard").Should().Be(((bool?)false, (bool?)true));
            hard.AuditorFlags("sweet").Should().Be(((bool?)true, (bool?)false));
            hard.AuditorFlags(AlcoholFixedReading.Unsure).Should().Be(((bool?)null, (bool?)null));
            (hard with { Expect = "!sweet" }).AuditorFlags("hard").Should().Be(((bool?)null, (bool?)null), "a rule row names no judged reading");

            var score = ModelLanes.Score(ModelLanes.AlcoholReadingLane, "qwen2.5:14b-instruct", "host", "sig", new[] { hard.Outcome("hard", "sweet"), (hard with { Id = "alcohol-reading:y" }).Outcome(null, null) });
            (score.AuditorKnownBad, score.AuditorFlaggedBad, score.AuditorKnownGood, score.AuditorFlaggedGood).Should().Be((1, 0, 1, 1));
            score.AuditKnownBad.Should().BeNull();
        }
    }
}
