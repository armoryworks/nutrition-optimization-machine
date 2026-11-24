using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Api.Tests.Services.MealPlan;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The diet gate's reading of name patterns by words (<see cref="DietPatternRule"/>) against
    /// diet-patterns.tsv: each row is one recipe line under one default restriction type, a line
    /// that names the restricted food (<c>hit</c>) or a look-alike that must not (<c>clear</c>). The
    /// line sits on a food no pattern names, so the household set keeps its recipe out only by the
    /// line's own text; a food named by the same text must be counted the same way. A type db/seed.sql
    /// ships (Pancreatitis) is read with its seeded criteria and their companions. Every default
    /// type holds rows in both directions, and no line the rows say names a restricted food loses its
    /// flag.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenDietPatternTests
    {
        private const long HouseholdId = 7, PersonId = 70, TypeId = 61000, Gram = 1;

        private readonly ITestOutputHelper _output;

        public GoldenDietPatternTests(ITestOutputHelper output) => _output = output;

        private sealed record Verdict(bool Line, bool Name);

        private static async Task<Dictionary<string, Verdict>> VerdictsAsync()
        {
            var verdicts = new Dictionary<string, Verdict>(StringComparer.Ordinal);
            foreach (var type in GoldenSet.Read("diet-patterns.tsv").Rows.GroupBy(r => r["restriction"]))
            {
                using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                    new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
                db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
                db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = TypeId, Name = type.Key });
                db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = type.Key, RestrictionTypeId = TypeId });
                db.Set<RestrictionCriterionEntity>().AddRange(SeedRestrictionCriteria.All.Where(c => c.Type == type.Key).Select(c => new RestrictionCriterionEntity
                {
                    RestrictionTypeId = TypeId, IngredientPattern = c.Pattern, Severity = c.Severity, Notes = c.Notes, CreatedDate = DateTime.UtcNow, LastModifiedDate = DateTime.UtcNow,
                }));
                var wording = new IngredientEntity { Name = "wording", CurationStatusId = (long)CurationStatusEnum.NonCurated };
                db.Ingredients.Add(wording);
                await db.SaveChangesAsync();
                var recipes = new List<(string Id, RecipeEntity Line, IngredientEntity Named, RecipeEntity ByName)>();
                foreach (var row in type)
                {
                    var named = new IngredientEntity { Name = row["line"], CurationStatusId = (long)CurationStatusEnum.Curated };
                    db.Ingredients.Add(named);
                    await db.SaveChangesAsync();
                    var line = Recipe("Line " + row.Id, wording.Id, row["line"]);
                    var byName = Recipe("Name " + row.Id, named.Id, "1 portion");
                    db.Recipes.AddRange(line, byName);
                    recipes.Add((row.Id, line, named, byName));
                }
                await db.SaveChangesAsync();
                await DefaultRestrictionCriteria.EnsureAsync(db);

                var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
                await Nom.Api.Tests.Services.MealPlan.DietGateStoreTests.AssertStoreAgreesAsync(db, HouseholdId, PersonId);
                set.IngredientIds.Should().NotContain(wording.Id);
                foreach (var (id, line, named, byName) in recipes)
                    verdicts[id] = new Verdict(set.RecipeIds.Contains(line.Id), set.IngredientIds.Contains(named.Id) || set.RecipeIds.Contains(byName.Id));
            }
            return verdicts;
        }

        private static RecipeEntity Recipe(string name, long food, string raw) => new()
        {
            Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated,
            RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = 0 } },
        };

        private static IEnumerable<string> LikePatterns(string restriction) =>
            (DefaultRestrictionCriteria.ByName.TryGetValue(restriction, out var defaults) ? defaults.Patterns : Seeded(restriction)).Where(p => !DietCriteriaMatcher.IsWordPattern(p));

        private static string[] Seeded(string restriction)
        {
            var patterns = SeedRestrictionCriteria.PatternsByType[restriction];
            return patterns.Concat(DefaultRestrictionCriteria.Companions.Where(c => c.Type == restriction && patterns.Contains(c.Pattern)).Select(c => c.Companion)).ToArray();
        }

        private static bool Crude(string restriction, string text) =>
            LikePatterns(restriction).Any(p => DietCriteriaMatcher.LikeRegex(p).IsMatch(text));

        private static bool Rule(string restriction, string text) =>
            LikePatterns(restriction).Any(p => DietPatternRule.For(p).IsMatch(text));

        [Fact]
        public async Task Each_line_names_its_restricted_food_or_not_by_its_own_text_and_as_a_foods_name()
        {
            var verdicts = await VerdictsAsync();
            var report = GoldenSet.Run("diet-patterns.tsv", "status", row =>
            {
                var v = verdicts[row.Id];
                var expect = row["expect"] == "hit";
                return new GoldenOutcome(v.Line == expect && v.Name == expect, $"{row["expect"]} ({row["restriction"]}: {row["line"]})",
                    $"line {(v.Line ? "hit" : "clear")}, name {(v.Name ? "hit" : "clear")}", new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Every_default_type_holds_rows_in_both_directions()
        {
            var rows = GoldenSet.Read("diet-patterns.tsv").Rows;
            var missing = DefaultRestrictionCriteria.ByName.Keys
                .Where(t => !rows.Any(r => r["restriction"] == t && r["expect"] == "hit") || !rows.Any(r => r["restriction"] == t && r["expect"] == "clear"));

            missing.Should().BeEmpty("every restriction type that ships by default holds a line that names its food and a look-alike that must not");
        }

        [Fact]
        public async Task No_line_that_names_a_restricted_food_loses_its_flag_and_every_line_the_rule_clears_is_a_reviewed_look_alike()
        {
            var rows = GoldenSet.Read("diet-patterns.tsv").Rows.Concat(GoldenSet.Read("diet-gate.tsv").Rows.Where(r => r["alcohol"] is "" or GoldenTsv.NoAnswer)).ToList();
            var verdicts = await VerdictsAsync();

            var lost = rows.Where(r => r["expect"] is "hit" or "out" && Crude(r["restriction"], r["line"]) && !Rule(r["restriction"], r["line"])).Select(r => $"{r.Id}: {r["line"]}");
            lost.Should().BeEmpty("a line that names a restricted food by a pattern keeps naming it");
            verdicts.Where(v => rows.Single(r => r.Id == v.Key)["expect"] == "hit" && !v.Value.Line).Select(v => v.Key).Should().BeEmpty();

            var cleared = rows.Where(r => Crude(r["restriction"], r["line"]) && !Rule(r["restriction"], r["line"])).ToList();
            cleared.Where(r => r["expect"] is not ("clear" or "in")).Select(r => r.Id).Should().BeEmpty("a line the crude reading flagged and the rule clears is a look-alike a row reviews");
            _output.WriteLine($"look-alikes the crude reading flagged and the rule clears: {cleared.Count} ({string.Join(", ", cleared.Select(r => r.Id))})");
            cleared.Count.Should().BeGreaterThan(80);

            var severe = DefaultRestrictionCriteria.ByName.Where(kv => kv.Value.Severity >= 4).Select(kv => kv.Key).ToList();
            var withLookAlike = cleared.Select(r => r["restriction"]).ToHashSet();
            _output.WriteLine("severe types with no look-alike the crude reading flagged (their patterns have none in the rows): "
                + string.Join(", ", severe.Where(t => !withLookAlike.Contains(t))));
        }

        [Fact]
        public void The_rule_reads_no_more_than_ilike_did_beside_the_separators_of_a_term_of_two_words_and_its_accents()
        {
            var texts = GoldenSet.Read("diet-patterns.tsv").Rows.Select(r => r["line"]).Concat(DietPatternRule.All.Select(e => "1 cup " + e.Phrase)).ToList();
            var wider = from p in DefaultRestrictionCriteria.ByName.Values.SelectMany(v => v.Patterns).Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Distinct()
                        let rule = DietPatternRule.For(p)
                        from t in texts
                        where rule.IsMatch(t) && !DietCriteriaMatcher.LikeRegex(DietPatternRule.Fold(p)).IsMatch(DietPatternRule.Fold(Regex.Replace(t, @"[\s\-]+", " ")))
                        select $"{p} on {t}";

            wider.Should().BeEmpty();
        }
    }
}
