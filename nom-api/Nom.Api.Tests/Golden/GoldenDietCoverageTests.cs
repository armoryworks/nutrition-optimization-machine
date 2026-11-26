using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// What the severe types know, measured (staging checkpoint 5, Q5, Q7, Q19) against diet-coverage.tsv: per type at least
    /// 150 foods of the frozen USDA catalog across its categories, each read by its name and its USDA description as the
    /// gate reads them (<see cref="DietPatternRule.IsMatch"/>, <see cref="DietPatternRule.IsDescriptionMatch"/>), and
    /// constructed recipe lines read by their own words, under the type's default patterns. The bar: no row that names
    /// the restricted thing is missed, and under 5 % of each type's clear rows are flagged.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenDietCoverageTests
    {
        public const string File = "diet-coverage.tsv";
        public const decimal OverFlagBar = 0.05m;
        public const int FoodsPerType = 150;

        /// <summary>The sources of the rows judged at staging checkpoint 7 and in the ck17 proof draws, which a type holds by its patterns or by the reading of words, so the patterns' own bar does not read them.</summary>
        public static readonly IReadOnlySet<string> WordRowsSources = new HashSet<string>(StringComparer.Ordinal) { "agent-ck16-diet-known", "agent-ck17-diet-proof" };

        private readonly ITestOutputHelper _output;

        public GoldenDietCoverageTests(ITestOutputHelper output) => _output = output;

        /// <summary>Whether the type's default patterns flag the row: a food by its name or its USDA description, a line by its own words, a recipe by any of its lines, by the food its hold rests on, by an ingredient list with no line or, in a book, one that builds on another recipe (<see cref="DietRecipeText.BuildsOnAnother"/>), and by its book title as <see cref="DietRecipeText.ReadsTitle"/> reads it beside a method.</summary>
        public static bool Flags(GoldenRow row)
        {
            var type = row["restriction"];
            var rules = DefaultRestrictionCriteria.ByName[type].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Select(p => DietPatternRule.For(p, type)).ToList();
            var description = row["description"] is "" or GoldenTsv.NoAnswer ? null : row["description"];
            if (row["kind"] == "recipe")
            {
                var lines = row["text"].Split(" ;; ");
                var title = row["category"] is "" or GoldenTsv.NoAnswer ? null : row["category"];
                return lines.All(DietRecipeText.IsPlaceholder)
                    || (title != null && DietRecipeText.BuildsOnAnother(null, lines))
                    || rules.Any(r => lines.Any(r.IsMatch) || (description != null && r.IsMatch(description))
                        || (title != null && r.IsMatch(DietRecipeText.Title(title)) && DietRecipeText.ReadsTitle("-", r, DietRecipeText.RefersElsewhere(lines), title, lines)));
            }
            return row["kind"] == "food"
                ? rules.Any(r => r.IsMatch(row["text"]) || r.IsDescriptionMatch(description))
                : rules.Any(r => r.IsMatch(row["text"]));
        }

        private static readonly Lazy<DietFoodWords.Table> CatalogWords = new(() => DietFoodWords.TableOf(GoldenSet.Read("gate-catalog.tsv").Rows
            .Where(r => r["status"] == "curated")
            .Select(r => new DietFoodWords.Food(long.Parse(r["id"], System.Globalization.CultureInfo.InvariantCulture), r["name"],
                int.TryParse(r["categoryId"], out var c) ? c : null, r["description"] is "" or GoldenTsv.NoAnswer ? null : r["description"]))));

        /// <summary>The word table of the frozen catalog's curated foods.</summary>
        public static DietFoodWords.Table CatalogTable => CatalogWords.Value;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DietFoodWords.Vocabulary> Vocabularies = new(StringComparer.Ordinal);

        /// <summary>A severe type's reading of words (<see cref="DietFoodWords"/>) built from the curated foods of the frozen catalog, each flagged when the type's default patterns read its name or its USDA description, as the build job builds it from the stored facts.</summary>
        public static DietFoodWords.Vocabulary VocabularyOf(string type) => Vocabularies.GetOrAdd(type, t =>
        {
            var table = CatalogWords.Value;
            var criteria = DefaultRestrictionCriteria.ByName[t].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p))
                .Select((p, i) => new DietDataCriterion(i, 0, null, null, null, null, 4, null, p, t)).ToList();
            var rules = criteria.Select(c => (Rule: c.Rule!, Words: DietFoodWords.TermWords(c))).ToList();
            bool Reads(DietPatternRule rule, DietFoodWords.Food food) => rule.IsMatch(food.Name) || (food.Description != null && rule.IsDescriptionMatch(food.Description));
            return DietFoodWords.Build(table,
                i => rules.Any(r => Reads(r.Rule, table.Foods[i])),
                i => rules.Where(r => r.Rule.IsMatch(table.Foods[i].Name)).Select(r => (IReadOnlyList<string>)r.Words).ToList(),
                rules.Where(r => r.Words.Count == 1).Select(r => r.Words[0]).ToHashSet(StringComparer.Ordinal),
                DietFoodWords.KindsOf(criteria.Select(c => c.LikePattern!)));
        });

        /// <summary>The three values a severe type reads in a recipe row: flagged by its patterns (<see cref="Flags"/>) or by a line's word, unknown when a line holds a word the catalog does not understand for the type or leaves its food to the cook (<see cref="DietRecipeText.LeavesChoice"/>), clear otherwise; a food or line row is read by its patterns only (staging checkpoint 7, S3).</summary>
        public static DietFoodWords.WordClass Reads(GoldenRow row)
        {
            if (Flags(row)) return DietFoodWords.WordClass.Flagged;
            if (row["kind"] != "recipe") return DietFoodWords.WordClass.Clear;
            var vocabulary = VocabularyOf(row["restriction"]);
            var unknown = false;
            foreach (var line in row["text"].Split(" ;; "))
            {
                var value = vocabulary.Read(CatalogWords.Value.Read(line)).Value;
                if (value == DietFoodWords.WordClass.Flagged) return value;
                unknown |= value != DietFoodWords.WordClass.Clear || DietRecipeText.LeavesChoice(line);
            }
            return unknown ? DietFoodWords.WordClass.NotKnown : DietFoodWords.WordClass.Clear;
        }

        [Fact]
        public void No_severe_type_shows_a_row_that_holds_what_it_restricts_and_each_flags_under_the_bar_with_the_unknown_share_reported()
        {
            var rows = GoldenSet.Read(File).Rows.Where(r => r["status"] is GoldenSet.Ok or GoldenSet.Agent).ToList();
            var table = new StringBuilder("type\thit rows\tshown\tclear rows\tflagged\tunknown\tunknown share\n");
            var shown = new List<string>();
            var failing = new List<string>();
            foreach (var type in rows.GroupBy(r => r["restriction"]).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var hits = type.Where(r => r["expect"] == "hit").ToList();
                var clears = type.Where(r => r["expect"] == "clear").Select(r => (Row: r, Read: Reads(r))).ToList();
                var lost = hits.Where(r => Reads(r) == DietFoodWords.WordClass.Clear).ToList();
                var flagged = clears.Count(c => c.Read == DietFoodWords.WordClass.Flagged);
                var unknown = clears.Count(c => c.Read == DietFoodWords.WordClass.NotKnown);
                table.Append(type.Key).Append('\t').Append(hits.Count).Append('\t').Append(lost.Count).Append('\t').Append(clears.Count).Append('\t').Append(flagged).Append('\t').Append(unknown).Append('\t')
                    .Append(clears.Count == 0 ? "-" : ((decimal)unknown / clears.Count).ToString("P1", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                shown.AddRange(lost.Select(r => $"{r.Id} {type.Key}: {r["text"]} | {r["description"]}"));
                if (clears.Count > 0 && (decimal)flagged / clears.Count >= OverFlagBar) failing.Add($"{type.Key}: {flagged} of {clears.Count} clear rows flagged");
            }
            _output.WriteLine(table.ToString());
            _output.WriteLine("shown:\n" + string.Join('\n', shown));
            shown.Should().BeEmpty("a severe type holds a row that holds what it restricts, as flagged or as unknown");
            failing.Should().BeEmpty();
        }

        [Fact]
        public void No_severe_type_misses_a_row_that_names_what_it_restricts_and_each_over_flags_under_the_bar()
        {
            var rows = GoldenSet.Read(File).Rows.Where(r => r["status"] is GoldenSet.Ok or GoldenSet.Agent && !WordRowsSources.Contains(r["source"])).ToList();
            var table = new StringBuilder("type\tfoods\tlines\trecipes\thit rows\tmissed\tclear rows\tover-flagged\tshare\n");
            var missed = new List<string>();
            var over = new List<string>();
            var failing = new List<string>();
            foreach (var type in rows.GroupBy(r => r["restriction"]).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var hits = type.Where(r => r["expect"] == "hit").ToList();
                var clears = type.Where(r => r["expect"] == "clear").ToList();
                var lost = hits.Where(r => !Flags(r)).ToList();
                var wrong = clears.Where(Flags).ToList();
                var share = clears.Count == 0 ? 0m : (decimal)wrong.Count / clears.Count;
                table.Append(type.Key).Append('\t').Append(type.Count(r => r["kind"] == "food")).Append('\t').Append(type.Count(r => r["kind"] == "line")).Append('\t').Append(type.Count(r => r["kind"] == "recipe")).Append('\t')
                    .Append(hits.Count).Append('\t').Append(lost.Count).Append('\t').Append(clears.Count).Append('\t').Append(wrong.Count).Append('\t')
                    .Append((share * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(" %\n");
                missed.AddRange(lost.Select(r => $"{r.Id} {type.Key}: {r["text"]} | {r["description"]}"));
                over.AddRange(wrong.Select(r => $"{r.Id} {type.Key}: {r["text"]} | {r["description"]}"));
                if (type.Count(r => r["kind"] == "food") < FoodsPerType) failing.Add($"{type.Key}: {type.Count(r => r["kind"] == "food")} foods, fewer than {FoodsPerType}");
                if (share >= OverFlagBar) failing.Add($"{type.Key}: {wrong.Count} of {clears.Count} clear rows flagged ({share:P1}), at or over the bar");
            }
            _output.WriteLine(table.ToString());
            _output.WriteLine("missed:\n" + string.Join('\n', missed));
            _output.WriteLine("over-flagged:\n" + string.Join('\n', over));
            missed.Should().BeEmpty("no severe type may miss a food or line that names what it restricts");
            failing.Should().BeEmpty();
        }

        [Fact]
        public void Every_severe_type_with_a_vocabulary_is_measured()
        {
            var measured = GoldenSet.Read(File).Rows.Select(r => r["restriction"]).ToHashSet(StringComparer.Ordinal);
            DietVocabulary.Classes.Select(c => c.ReferenceType).Where(t => !measured.Contains(t)).Should().BeEmpty();
            measured.Should().Contain(new[] { "Celiac Disease" });
        }
    }
}
