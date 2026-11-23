using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// A named exception of the word rule (<see cref="DietPatternRule.All"/>) never clears a compound a
    /// type means: the type holds a companion pattern that names it (<see cref="DefaultRestrictionCriteria.Companions"/>),
    /// whether its criteria are the defaults, db/seed.sql's rows or an admin's edit, and every compound
    /// each shipped type does let through is listed in diet-pattern-clears.tsv for review.
    /// </summary>
    public class DietPatternCompanionTests
    {
        private const long HouseholdId = 7, PersonId = 70, TypeId = 61000, Gram = 1;

        private readonly ITestOutputHelper _output;

        public DietPatternCompanionTests(ITestOutputHelper output) => _output = output;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static async Task<(RecipeEntity Recipe, IngredientEntity Food)> TypeWithLineAsync(ApplicationDbContext db, string type, string line, params RestrictionCriterionEntity[] criteria)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = TypeId, Name = type });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = type, RestrictionTypeId = TypeId });
            foreach (var c in criteria) c.RestrictionTypeId = TypeId;
            db.Set<RestrictionCriterionEntity>().AddRange(criteria);
            var wording = new IngredientEntity { Name = "wording", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            var food = new IngredientEntity { Name = line, CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.AddRange(wording, food);
            await db.SaveChangesAsync();
            var recipe = new RecipeEntity
            {
                Name = "Line", AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated,
                RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = wording.Id, MeasurementId = Gram, Quantity = 1m, RawLine = line, SortOrder = 0 } },
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            return (recipe, food);
        }

        private static RestrictionCriterionEntity Pattern(string pattern, int severity, string notes, bool deleted = false) =>
            new() { IngredientPattern = pattern, Severity = severity, Notes = notes, IsDeleted = deleted, CreatedDate = DateTime.UtcNow, LastModifiedDate = DateTime.UtcNow };

        private static RestrictionCriterionEntity[] Defaults(string type, Func<string, bool>? keep = null) =>
            DefaultRestrictionCriteria.ByName[type].Patterns.Where(p => keep?.Invoke(p) ?? true).Select(p => Pattern(p, DefaultRestrictionCriteria.ByName[type].Severity, DefaultRestrictionCriteria.DefaultNote)).ToArray();

        private static RestrictionCriterionEntity[] Seeded(string type) =>
            SeedRestrictionCriteria.All.Where(c => c.Type == type).Select(c => Pattern(c.Pattern, c.Severity, c.Notes)).ToArray();

        [Fact]
        public async Task An_admin_edited_vegetarian_type_gets_the_hamburger_companion_and_keeps_hamburger_out()
        {
            using var db = NewContext();
            var edited = Defaults("Vegetarian", p => p != "%hamburger%").Append(Pattern("%offal%", 3, "added by an admin")).ToArray();
            var (recipe, food) = await TypeWithLineAsync(db, "Vegetarian", "1 lb hamburger", edited);

            (await DefaultRestrictionCriteria.EnsureAsync(db)).Should().Be(1);
            (await DefaultRestrictionCriteria.EnsureAsync(db)).Should().Be(0, "the companion is added once");

            var row = await db.Set<RestrictionCriterionEntity>().SingleAsync(c => c.IngredientPattern == "%hamburger%");
            row.Severity.Should().Be(3, "a companion takes the severity of the pattern it accompanies");
            row.Notes.Should().Be(DefaultRestrictionCriteria.CompanionNote("%ham%"));
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            set.RecipeIds.Should().Contain(recipe.Id, "hamburger is beef, which a vegetarian type means, though the exception of %ham% clears it");
            set.IngredientIds.Should().Contain(food.Id);
        }

        [Fact]
        public async Task A_companion_an_admin_deleted_stays_deleted_and_a_type_without_the_accompanied_pattern_gets_none()
        {
            using var db = NewContext();
            var edited = Defaults("Vegetarian", p => p != "%hamburger%").Append(Pattern("%hamburger%", 3, DefaultRestrictionCriteria.DefaultNote, deleted: true)).ToArray();
            await TypeWithLineAsync(db, "Vegetarian", "1 lb hamburger", edited);

            (await DefaultRestrictionCriteria.EnsureAsync(db)).Should().Be(0);

            using var other = NewContext();
            await TypeWithLineAsync(other, "Paleo", "1 cup buckwheat groats", Defaults("Paleo", p => p != "%wheat%" && p != "%buckwheat%").Append(Pattern("%custom%", 3, "admin")).ToArray());
            (await DefaultRestrictionCriteria.EnsureAsync(other)).Should().Be(0, "a type that no longer holds %wheat% is not given its companion");
        }

        [Theory]
        [InlineData("Pancreatitis", "2 tbsp crunchy peanut butter")]
        [InlineData("Pancreatitis", "1/4 cup almond butter")]
        [InlineData("Pancreatitis", "2 oz cocoa butter, melted")]
        [InlineData("Pancreatitis", "4 slices turkey bacon")]
        [InlineData("Gallbladder Disease", "6 strips beef bacon")]
        public async Task A_seeded_fat_type_gets_its_companions_and_keeps_fatty_compounds_out(string type, string line)
        {
            using var db = NewContext();
            var (recipe, food) = await TypeWithLineAsync(db, type, line, Seeded(type));

            (await DefaultRestrictionCriteria.EnsureAsync(db)).Should().BeGreaterThan(0);

            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            set.RecipeIds.Should().Contain(recipe.Id, $"{type} restricts \"{line}\" for its fat");
            set.IngredientIds.Should().Contain(food.Id);
        }

        [Theory]
        [InlineData("Whole30", "12 oz root beer")]
        [InlineData("Whole30", "1 bottle ginger beer")]
        [InlineData("Paleo", "2 tbsp maltodextrin")]
        [InlineData("Paleo", "1 cup buckwheat groats")]
        [InlineData("FODMAP Sensitivity", "1 cup soy milk")]
        [InlineData("Low-FODMAP", "1 cup oat milk")]
        [InlineData("FODMAP Sensitivity", "1/2 cup soy yogurt")]
        [InlineData("Vegetarian", "1 lb hamburger")]
        [InlineData("Grain-Free", "1 tbsp maltodextrin")]
        [InlineData("Whole30", "4 oz black licorice")]
        [InlineData("Low-FODMAP", "2 Honeycrisp, sliced")]
        [InlineData("FODMAP Sensitivity", "1/2 cup pearl barley")]
        [InlineData("Jain", "1 medium eggplant")]
        [InlineData("Vegan", "1 cup Goldfish crackers")]
        [InlineData("Vegetarian", "4 scallopini")]
        public async Task A_default_type_holds_the_companions_its_patterns_need(string type, string line)
        {
            using var db = NewContext();
            var (recipe, _) = await TypeWithLineAsync(db, type, line);
            await DefaultRestrictionCriteria.EnsureAsync(db);

            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            set.RecipeIds.Should().Contain(recipe.Id, $"{type} means \"{line}\"");
        }

        [Fact]
        public void Every_companion_names_a_compound_an_exception_of_its_pattern_clears_and_every_default_type_holding_the_pattern_holds_it()
        {
            foreach (var c in DefaultRestrictionCriteria.Companions)
            {
                var compound = "1 cup " + DietPatternRule.For(c.Companion).Term;
                DietPatternRule.For(c.Companion).Substring.Should().BeFalse();
                DietPatternRule.For(c.Pattern).IsMatch(compound).Should().BeFalse($"{c.Companion} answers a compound an exception of {c.Pattern} clears");
                DietPatternRule.For(c.Companion).IsMatch(compound).Should().BeTrue();
                c.Reason.Should().NotBeNullOrWhiteSpace();
                if (DefaultRestrictionCriteria.ByName.TryGetValue(c.Type, out var def))
                {
                    def.Patterns.Should().Contain(c.Pattern, $"{c.Type} holds the pattern {c.Companion} accompanies");
                    def.Patterns.Should().Contain(c.Companion);
                }
                else
                {
                    SeedRestrictionCriteria.PatternsByType.Should().ContainKey(c.Type, "a companion names a default type or a type db/seed.sql ships");
                    SeedRestrictionCriteria.PatternsByType[c.Type].Should().Contain(c.Pattern);
                }
            }
        }

        internal static string ClearsTable()
        {
            var types = DefaultRestrictionCriteria.ByName.ToDictionary(kv => kv.Key, kv => kv.Value.Patterns, StringComparer.OrdinalIgnoreCase);
            foreach (var (type, patterns) in SeedRestrictionCriteria.PatternsByType)
                types[type] = types.TryGetValue(type, out var existing) ? existing.Concat(patterns).ToArray()
                    : patterns.Concat(DefaultRestrictionCriteria.Companions.Where(c => c.Type == type && patterns.Contains(c.Pattern)).Select(c => c.Companion)).ToArray();
            var clears = from t in types
                         let likes = t.Value.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Distinct().Select(p => DietPatternRule.For(p, t.Key)).ToList()
                         from rule in likes
                         from e in rule.Exceptions
                         where !likes.Any(q => q.IsMatch("1 cup " + e.Phrase))
                         select (e.Term, e.Phrase, Type: t.Key);
            var text = new StringBuilder("term\tphrase\ttypes that let it through\n");
            foreach (var g in clears.Distinct().GroupBy(c => (c.Term, c.Phrase)).OrderBy(g => g.Key.Term, StringComparer.Ordinal).ThenBy(g => g.Key.Phrase, StringComparer.Ordinal))
                text.Append(g.Key.Term).Append('\t').Append(g.Key.Phrase).Append('\t').Append(string.Join(", ", g.Select(c => c.Type).OrderBy(t => t, StringComparer.Ordinal))).Append('\n');
            return text.ToString();
        }

        private static string ClearsPath([CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "Fixtures", "diet-pattern-clears.tsv"));

        [Fact]
        public void Every_compound_a_shipped_type_lets_through_by_a_named_exception_is_in_the_reviewed_register()
        {
            var actual = ClearsTable();
            var expected = File.Exists(ClearsPath()) ? File.ReadAllText(ClearsPath()) : string.Empty;
            if (actual != expected) _output.WriteLine(actual);
            actual.Should().Be(expected, "a change to the exceptions, the defaults, the companions or db/seed.sql's criteria shows here as a diff a person reviews (the new register is in the test output)");
        }
    }
}
