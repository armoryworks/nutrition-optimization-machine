using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Api.Services;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Staging checkpoint 6, R12 and R9: every writer of what the diet gate reads makes what it touched undecided in its
    /// own transaction (the triggers of <c>db/custom-objects.sql</c>, carried into <c>db/schema.sql</c>), whatever process
    /// or path wrote; requests hold an undecided recipe or food for every set; and the build job's cycle over the changes
    /// decides only what a writer left undecided, reading neither the whole catalog nor every line. The triggers
    /// themselves run on Postgres (<see cref="DietGateStorePostgresTests"/>).
    /// </summary>
    public class DietGateWritersTests
    {
        private const long HouseholdId = 7, PersonId = 70, CeliacRef = 60800, VegetarianRef = 60850, Gram = 1;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static string RepoFile(string relative, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", relative));

        private static RecipeEntity Recipe(string name, params (long Food, string Raw)[] lines)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var (food, raw) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = order++ });
            return recipe;
        }

        private sealed record Kitchen(long Flour, long Rice, long Water, long Bread, long Pilaf, long Toast);

        private static async Task<Kitchen> SeedAsync(ApplicationDbContext db, string type, long typeId)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = typeId, Name = type });
            var flour = new IngredientEntity { Name = "Wheat Flour", CurationStatusId = (long)CurationStatusEnum.Curated };
            var rice = new IngredientEntity { Name = "Rice", CurationStatusId = (long)CurationStatusEnum.Curated };
            var water = new IngredientEntity { Name = "Water", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.AddRange(flour, rice, water);
            await db.SaveChangesAsync();
            var bread = Recipe("Bread", (flour.Id, "3 cups wheat flour"), (water.Id, "1 cup water"));
            var pilaf = Recipe("Pilaf", (rice.Id, "1 cup rice"), (water.Id, "2 cups water"));
            var toast = Recipe("Toast", (rice.Id, "2 slices of bread, toasted"));
            db.Recipes.AddRange(bread, pilaf, toast);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = type, RestrictionTypeId = typeId });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            return new Kitchen(flour.Id, rice.Id, water.Id, bread.Id, pilaf.Id, toast.Id);
        }

        private static async Task ClearAsync(ApplicationDbContext db, IEnumerable<long> recipes, IEnumerable<long>? foods = null)
        {
            var r = recipes.ToList();
            var f = (foods ?? Array.Empty<long>()).ToList();
            db.DietRecipeStates.RemoveRange(await db.DietRecipeStates.Where(s => r.Contains(s.RecipeId)).ToListAsync());
            db.DietFoodStates.RemoveRange(await db.DietFoodStates.Where(s => f.Contains(s.IngredientId)).ToListAsync());
            await db.SaveChangesAsync();
        }

        [Fact]
        public void Every_table_the_gate_reads_has_a_trigger_for_every_write_that_makes_what_it_touched_undecided()
        {
            var objects = File.ReadAllText(RepoFile("db/custom-objects.sql"));
            var schema = File.ReadAllText(RepoFile("db/schema.sql"));
            var wanted = new Dictionary<string, string[]>
            {
                ["recipe.RecipeIngredient"] = new[] { "INSERT", "UPDATE", "DELETE" },
                ["recipe.Recipe"] = new[] { "UPDATE" },
                ["recipe.RecipeStep"] = new[] { "INSERT", "UPDATE", "DELETE" },
                ["recipe.Ingredient"] = new[] { "INSERT", "UPDATE", "DELETE" },
                ["recipe.IngredientAlias"] = new[] { "INSERT", "UPDATE", "DELETE" },
                ["nutrient.IngredientNutrient"] = new[] { "INSERT", "UPDATE", "DELETE" },
                ["recipe.IngredientContents"] = new[] { "INSERT", "UPDATE", "DELETE" },
                ["plan.RestrictionCriterion"] = new[] { "INSERT", "UPDATE", "DELETE" },
                ["measurement.Measurement"] = new[] { "UPDATE", "DELETE" },
            };
            foreach (var (text, file) in new[] { (objects, "db/custom-objects.sql"), (schema, "db/schema.sql") })
            {
                var found = Regex.Matches(text, @"CREATE TRIGGER ""?(\w+)""? AFTER ([A-Z ]+?) ON (\w+)\.""(\w+)""")
                    .SelectMany(m => m.Groups[2].Value.Split(" OR ", StringSplitOptions.TrimEntries).Select(e => (Table: m.Groups[3].Value + "." + m.Groups[4].Value, Event: e)))
                    .ToHashSet();
                foreach (var (table, events) in wanted)
                    foreach (var e in events)
                        found.Should().Contain((table, e), $"{file} must clear the stored diet state on every {e} of {table}, whatever writes it (staging checkpoint 6, R12)");
                text.Should().Contain("plan.\"DietGateClearRecipes\"").And.Contain("plan.\"DietGateClearFoods\"").And.Contain("INSERT INTO plan.\"DietGateWrite\"", file);
                text.Should().NotContain("nextval('plan.\"DietGate", "a sequence moves when the statement runs, not when it commits, so a build job polling it misses a write that commits later (review CK11-DM-1)");
            }
            Regex.IsMatch(objects, @"DELETE FROM plan\.""DietRecipeState""").Should().BeTrue();
            Regex.IsMatch(objects, @"DELETE FROM plan\.""DietFoodState""").Should().BeTrue();
        }

        [Fact]
        public async Task A_cycle_over_the_changes_decides_only_what_a_writer_left_undecided_and_the_recipes_on_a_changed_food()
        {
            using var db = NewContext();
            var k = await SeedAsync(db, "Celiac Disease", CeliacRef);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);

            await ClearAsync(db, new[] { k.Bread });
            var lines = await builder.RunChangesAsync(db);
            (lines.FoodsDecided, lines.RecipesDecided).Should().Be((0, 1), "only the recipe whose state a writer removed is read again");

            var rice = await db.Ingredients.SingleAsync(i => i.Id == k.Rice);
            rice.Name = "Wheat Berries";
            await db.SaveChangesAsync();
            await ClearAsync(db, new[] { k.Pilaf, k.Toast }, new[] { k.Rice });
            var food = await builder.RunChangesAsync(db);
            (food.FoodsDecided, food.RecipesDecided).Should().Be((1, 2), "the renamed food and the two recipes on it");
            food.Foods.Should().Be(2, "the catalog it read holds the changed food and the foods of the recipes it decided (rice and water), not the whole catalog");
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            (await db.Recipes.WithoutHeld(db, set).Select(r => r.Id).ToListAsync()).Should().NotContain(k.Pilaf);

            var idle = await builder.RunChangesAsync(db);
            (idle.FoodsDecided, idle.RecipesDecided).Should().Be((0, 0));
            await DietGateStoreTests.AssertStoreAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task A_write_that_commits_while_a_cycle_reads_stays_in_the_write_log_and_the_next_cycle_decides_its_recipe_again()
        {
            using var db = NewContext();
            var k = await SeedAsync(db, "Celiac Disease", CeliacRef);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            (await DietGateStoreBuilder.PendingWritesAsync(db)).Should().BeFalse();

            await ClearAsync(db, new[] { k.Pilaf });
            builder.AfterLinesRead = async ct =>
            {
                var line = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == k.Pilaf && ri.IngredientId == k.Rice, ct);
                db.RecipeIngredients.Remove(line);
                db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = k.Pilaf, IngredientId = k.Flour, MeasurementId = Gram, Quantity = 1m, RawLine = "1 cup wheat flour", SortOrder = line.SortOrder });
                db.DietGateWrites.Add(new DietGateWriteEntity { RecipeId = k.Pilaf, WrittenAt = DateTime.UtcNow });
                await db.SaveChangesAsync(ct);
            };
            var raced = await builder.RunChangesAsync(db);
            builder.AfterLinesRead = null;

            (raced.RecipesDecided, raced.RecipesLate).Should().Be((0, 1), "the cycle read the pilaf's lines before the write reached the log, so it does not store the verdict it made from them");
            (await db.DietRecipeStates.AnyAsync(s => s.RecipeId == k.Pilaf)).Should().BeFalse("a recipe written between the cycle's reading and its writing stays undecided, not served from the older lines");
            var held = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            (await db.Recipes.WithoutHeld(db, held).Select(r => r.Id).ToListAsync()).Should().NotContain(k.Pilaf, "an undecided recipe is held, so the flour written into it is never shown before a cycle reads it");
            (await DietGateStoreBuilder.PendingWritesAsync(db)).Should().BeTrue("a write that commits after the cycle read the log stays for the next cycle");
            var next = await builder.RunChangesAsync(db);
            (next.RecipesDecided, next.RecipesLate).Should().Be((1, 0), "the next cycle reads the logged recipe with the write and stores it");
            (await DietGateStoreBuilder.PendingWritesAsync(db)).Should().BeFalse("the rows a cycle read are removed once it decided them");
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            (await db.Recipes.WithoutHeld(db, set).Select(r => r.Id).ToListAsync()).Should().NotContain(k.Pilaf);
            await DietGateStoreTests.AssertStoreAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task A_whole_cycle_leaves_undecided_a_recipe_written_between_its_reading_and_its_writing_and_stores_the_rest()
        {
            using var db = NewContext();
            var k = await SeedAsync(db, "Celiac Disease", CeliacRef);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            await ClearAsync(db, new[] { k.Pilaf, k.Bread });

            builder.AfterLinesRead = async ct =>
            {
                db.DietGateWrites.Add(new DietGateWriteEntity { RecipeId = k.Pilaf, WrittenAt = DateTime.UtcNow });
                await db.SaveChangesAsync(ct);
            };
            var raced = await builder.RunAsync(db);
            builder.AfterLinesRead = null;

            (raced.RecipesDecided, raced.RecipesLate).Should().Be((1, 1));
            (await db.DietRecipeStates.Select(s => s.RecipeId).ToListAsync()).Should().Contain(k.Bread).And.NotContain(k.Pilaf);
            (await db.DietRecipeFlags.AnyAsync(f => f.RecipeId == k.Pilaf)).Should().BeFalse("a recipe left undecided keeps no flag of the verdict it was not given");
            (await builder.RunChangesAsync(db)).RecipesDecided.Should().Be(1);
            (await db.DietRecipeStates.AnyAsync(s => s.RecipeId == k.Pilaf)).Should().BeTrue();
        }

        [Fact]
        public async Task A_set_built_in_a_cycle_that_leaves_a_recipe_undecided_counts_only_the_flags_it_stored()
        {
            using var db = NewContext();
            var k = await SeedAsync(db, "Celiac Disease", CeliacRef);
            var builder = new DietGateStoreBuilder
            {
                AfterLinesRead = async ct =>
                {
                    db.DietGateWrites.Add(new DietGateWriteEntity { RecipeId = k.Bread, WrittenAt = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);
                },
            };

            var raced = await builder.RunAsync(db);

            raced.RecipesLate.Should().Be(1);
            raced.Built.Should().NotBeEmpty();
            (await db.DietRecipeFlags.AnyAsync(f => f.RecipeId == k.Bread)).Should().BeFalse();
            foreach (var set in await db.DietGateSets.ToListAsync())
            {
                var flags = await db.DietRecipeFlags.Where(f => f.SetId == set.Id).ToListAsync();
                (set.RecipesContaining, set.RecipesUnknown).Should().Be((flags.Count(f => f.Verdict == DietVerdict.Contains), flags.Count(f => f.Verdict == DietVerdict.Unknown)),
                    "a set's counts are of the flags stored for it, not of those withdrawn with a recipe written while the cycle read it (review ck12ls-r2)");
            }
        }

        [Fact]
        public async Task The_write_log_is_trimmed_of_rows_older_than_the_age_kept_when_no_build_reads_it()
        {
            using var db = NewContext();
            db.DietGateWrites.AddRange(
                new DietGateWriteEntity { RecipeId = 1, WrittenAt = DateTime.UtcNow.AddHours(-2) },
                new DietGateWriteEntity { IngredientId = 2, WrittenAt = DateTime.UtcNow.AddMinutes(-90) },
                new DietGateWriteEntity { RecipeId = 3, WrittenAt = DateTime.UtcNow.AddMinutes(-5) });
            await db.SaveChangesAsync();

            (await DietGateStoreBuilder.TrimWritesAsync(db, TimeSpan.FromHours(1))).Should().Be(2);

            (await db.DietGateWrites.Select(w => w.RecipeId).ToListAsync()).Should().Equal(3L);
        }

        [Fact]
        public async Task A_process_whose_build_is_off_trims_the_write_log()
        {
            var name = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddScoped(_ =>
                new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options, new HttpContextAccessor { HttpContext = new DefaultHttpContext() }));
            await using var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.DietGateWrites.AddRange(
                    new DietGateWriteEntity { RecipeId = 1, WrittenAt = DateTime.UtcNow.AddDays(-3) },
                    new DietGateWriteEntity { RecipeId = 2, WrittenAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DietGate:Build"] = "false" }).Build();
            var service = new DietGateHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                configuration, NullLogger<DietGateHostedService>.Instance);

            await service.StartAsync(default);
            var left = new List<long?>();
            for (var i = 0; i < 100; i++)
            {
                using var scope = provider.CreateScope();
                left = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .DietGateWrites.Select(w => w.RecipeId).ToListAsync();
                if (left.Count == 1) break;
                await Task.Delay(50);
            }
            await service.StopAsync(default);

            left.Should().Equal(2L);
        }

        [Fact]
        public async Task A_whole_cycle_decides_a_food_whose_state_a_writer_removed_although_its_fingerprint_did_not_move()
        {
            using var db = NewContext();
            var k = await SeedAsync(db, "Celiac Disease", CeliacRef);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);

            await ClearAsync(db, Array.Empty<long>(), new[] { k.Water });
            var report = await builder.RunAsync(db);

            report.FoodsDecided.Should().Be(1, "a food left undecided is held by every request until it is decided again");
            (await db.DietFoodStates.AnyAsync(s => s.IngredientId == k.Water)).Should().BeTrue();
        }

        [Fact]
        public async Task An_undecided_food_is_held_for_a_diet_as_well_as_for_a_severe_type()
        {
            using var db = NewContext();
            var k = await SeedAsync(db, "Vegetarian", VegetarianRef);
            await new DietGateStoreBuilder().RunAsync(db);
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            set.Stored!.UnknownSetIds.Should().BeEmpty("a vegetarian diet does not hold unknowns");
            (await db.Ingredients.WithoutHeld(db, set).Select(i => i.Id).ToListAsync()).Should().Contain(k.Water);

            await ClearAsync(db, Array.Empty<long>(), new[] { k.Water });

            (await db.Ingredients.WithoutHeld(db, set).Select(i => i.Id).ToListAsync()).Should().NotContain(k.Water,
                "a food a writer changed is held until the build decides it again, not served from its old facts");
            (await DietGateStore.UndecidedAsync(db, set, new[] { k.Water, k.Rice })).Should().BeEquivalentTo(new[] { k.Water });
        }
        private static async Task FoodWrittenAsync(ApplicationDbContext db, long food)
        {
            db.DietFoodStates.RemoveRange(await db.DietFoodStates.Where(s => s.IngredientId == food).ToListAsync());
            db.DietGateWrites.Add(new DietGateWriteEntity { IngredientId = food, WrittenAt = DateTime.UtcNow });
            foreach (var recipe in await db.RecipeIngredients.Where(ri => ri.IngredientId == food).Select(ri => ri.RecipeId).Distinct().ToListAsync())
                db.DietGateWrites.Add(new DietGateWriteEntity { RecipeId = recipe, IngredientId = food, WrittenAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        [Fact]
        public void A_write_to_a_food_logs_its_recipes_for_the_cycle_instead_of_removing_their_states()
        {
            foreach (var file in new[] { "db/custom-objects.sql", "db/schema.sql" })
            {
                var text = File.ReadAllText(RepoFile(file));
                var start = text.IndexOf("FUNCTION plan.\"DietGateClearFoods\"", StringComparison.Ordinal);
                var body = text[start..text.IndexOf("END;", start, StringComparison.Ordinal)];
                body.Should().Contain("INSERT INTO plan.\"DietGateWrite\" (\"RecipeId\", \"IngredientId\")", file);
                body.Should().NotContain("DietRecipeState", $"{file}: a food write holds its recipes through the write log (DietGateStore.Standing) and the cycle reads them again only when the food's decision moved (staging checkpoint 7, S2)");
            }
        }

        [Fact]
        public void On_Postgres_a_state_stored_without_its_reading_transaction_does_not_stand_while_a_write_is_logged_for_its_recipe()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options);

            var sql = db.DietRecipeStates.Standing(db).ToQueryString();

            sql.Should().Contain("\"ReadXmin\" IS NULL",
                "a food write no longer removes its recipes' states, so a state stored before ReadXmin existed would otherwise keep serving its old verdict until a cycle (review ck16-r5)");
        }

        [Fact]
        public async Task A_write_to_a_food_that_moves_no_decision_releases_its_recipes_unread_and_one_that_moves_a_fact_decides_them_again()
        {
            using var db = NewContext();
            var k = await SeedAsync(db, "Celiac Disease", CeliacRef);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            var states = await db.DietRecipeStates.CountAsync();

            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = k.Water, AliasName = "spring water", SourceContext = "admin:1" });
            await db.SaveChangesAsync();
            await FoodWrittenAsync(db, k.Water);
            var quiet = await builder.RunChangesAsync(db);

            (quiet.FoodsDecided, quiet.FoodsMoved, quiet.RecipesDecided, quiet.RecipesConfirmed).Should().Be((1, 0, 0, 2),
                "an alias no pattern reads moves no decision of the water, so the bread and the pilaf keep their verdicts and are not read again (staging checkpoint 7, S2)");
            (await db.DietRecipeStates.CountAsync()).Should().Be(states);
            (await DietGateStoreBuilder.PendingWritesAsync(db)).Should().BeFalse();

            var water = await db.Ingredients.SingleAsync(i => i.Id == k.Water);
            water.Name = "Wheat Water";
            await db.SaveChangesAsync();
            await FoodWrittenAsync(db, k.Water);
            var moved = await builder.RunChangesAsync(db);

            (moved.FoodsMoved, moved.RecipesDecided, moved.RecipesConfirmed).Should().Be((1, 2, 0), "the renamed water now counts for Celiac Disease, so its recipes are decided again");
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            (await db.Recipes.WithoutHeld(db, set).Select(r => r.Id).ToListAsync()).Should().NotContain(k.Pilaf);
            await DietGateStoreTests.AssertStoreAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task A_cycle_over_more_recipes_than_the_limit_reads_every_line_in_windows_and_decides_the_same()
        {
            using var db = NewContext();
            var k = await SeedAsync(db, "Celiac Disease", CeliacRef);
            var builder = new DietGateStoreBuilder { LargeScopeLimit = 1 };
            await builder.RunAsync(db);

            var rice = await db.Ingredients.SingleAsync(i => i.Id == k.Rice);
            rice.Name = "Wheat Berries";
            await db.SaveChangesAsync();
            await FoodWrittenAsync(db, k.Rice);
            var report = await builder.RunChangesAsync(db);

            report.Windowed.Should().BeTrue("two recipes are more than the limit of one");
            (report.FoodsDecided, report.RecipesDecided).Should().Be((1, 2));
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            (await db.Recipes.WithoutHeld(db, set).Select(r => r.Id).ToListAsync()).Should().NotContain(new[] { k.Pilaf, k.Toast });
            (await builder.RunChangesAsync(db)).RecipesDecided.Should().Be(0);
            await DietGateStoreTests.AssertStoreAgreesAsync(db, HouseholdId, PersonId);
        }
    }
}
