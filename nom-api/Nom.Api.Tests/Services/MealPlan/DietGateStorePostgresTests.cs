using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Orch.Extensions;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>A test that runs only against a throwaway Postgres named by <c>NOM_DIET_STORE_PG</c> (a connection string), reported skipped otherwise.</summary>
    public sealed class DietStorePostgresFactAttribute : FactAttribute
    {
        public DietStorePostgresFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DietGateStorePostgresTests.Variable)))
                Skip = "set " + DietGateStorePostgresTests.Variable + " to a throwaway Postgres made from db/schema.sql and db/seed.sql to measure the diet gate store";
        }
    }

    /// <summary>
    /// The diet gate store's budgets (staging checkpoint 5, Q2, Q3, Q4) on generated data of the production copy's size in
    /// a throwaway Postgres 16: 88,000 foods, 122,000 recipes, 850,000 lines and 79 restriction types. The first build
    /// finishes within 20 minutes, a change to one food is decided within a minute, a search with any one restriction
    /// answers under a second and no slower than with none, and the gate's part of a plan shuffle (the household set and
    /// the filtered random draw) answers under a second. The figures are written to the test output.
    /// </summary>
    public class DietGateStorePostgresTests
    {
        public const string Variable = "NOM_DIET_STORE_PG";
        private const int FoodCount = 88_000, RecipeCount = 122_000, LineCount = 850_000, TypeCount = 79, Lineless = 26, AliasCount = 44_000, StatementCount = 2_000;
        private const long PersonId = 1, HouseholdId = 900_001, AlcoholNutrient = 5038, Gram = 1;

        private readonly ITestOutputHelper _output;

        public DietGateStorePostgresTests(ITestOutputHelper output) => _output = output;

        private static string ConnectionString => Environment.GetEnvironmentVariable(Variable)!;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString, o => o.CommandTimeout(1200)).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static readonly string[] Vocabulary =
        {
            "all-purpose flour", "whole wheat flour", "bread flour", "unsalted butter", "salted butter", "large eggs", "egg yolks", "whole milk", "buttermilk",
            "heavy cream", "sour cream", "cream cheese", "cheddar cheese", "parmesan cheese", "granulated sugar", "brown sugar", "powdered sugar", "honey",
            "maple syrup", "kosher salt", "black pepper", "olive oil", "vegetable oil", "canola oil", "garlic cloves", "yellow onion", "shallots", "carrots",
            "celery stalks", "tomatoes", "tomato paste", "red bell pepper", "jalapeño", "chicken breasts", "chicken broth", "beef stock", "ground beef",
            "bacon", "ham", "pork shoulder", "turkey bacon", "salmon fillets", "shrimp", "anchovies", "canned tuna", "crab meat", "soy sauce", "tamari",
            "Worcestershire sauce", "fish sauce", "rice vinegar", "red wine vinegar", "dry white wine", "sherry", "brandy", "dark rum", "beer", "vanilla extract",
            "baking soda", "baking powder", "cornstarch", "cornmeal", "rolled oats", "quinoa", "brown rice", "pasta", "egg noodles", "bread crumbs", "panko",
            "graham crackers", "peanut butter", "almond flour", "coconut milk", "walnuts", "pecans", "almonds", "cashews", "pine nuts", "sesame seeds", "tahini",
            "chickpeas", "black beans", "lentils", "green peas", "frozen corn", "eggplant", "zucchini", "potatoes", "sweet potatoes", "spinach", "kale",
            "lemon juice", "lime zest", "orange juice", "apples", "pineapple", "bananas", "raisins", "dried apricots", "dates", "fresh basil", "dried oregano",
            "ground cumin", "chili powder", "paprika", "cinnamon", "nutmeg", "ground ginger", "fresh parsley", "thyme", "mayonnaise", "dijon mustard", "ketchup",
            "barbecue sauce", "hot sauce", "salsa", "tortillas", "pita bread", "hamburger buns", "dark chocolate", "cocoa powder", "chocolate chips", "coffee",
            "green tea", "gelatin", "marshmallows", "yeast", "water", "crème fraîche", "barley", "rye flour", "couscous", "sauerkraut", "dill pickles", "capers",
            "olives", "feta cheese", "goat cheese", "tofu", "tempeh", "seitan", "lard", "suet", "sausage meat", "pepperoni", "salami", "prosciutto", "spaghetti",
            "fettuccine", "macaroni", "angel food cake mix", "puff pastry", "vermicelli", "lady fingers", "sponge cake", "baguette", "Bisquick", "rice",
        };

        private static readonly string[] Titles = { "Chicken", "Bread", "Soup", "Salad", "Cake", "Pie", "Stew", "Curry", "Pasta", "Tacos", "Cookies", "Casserole", "Muffins", "Chili", "Roast" };
        private static readonly string[] Amounts = { "1", "2", "3", "1/2", "1/4", "3/4", "1 1/2", "4", "½", "a", "one", "two" };
        private static readonly string[] Units = { "cup", "cups", "tbsp", "tsp", "oz", "lb", "g", "pinch of", "can", "" };
        private static readonly (string Text, string? Context)[] AliasKinds = { ("", null), ("", "fdc:description"), ("", "admin:x"), ("", "deterministic:recheck:x"), ("", "w01") };

        private static ulong Mix(ulong x)
        {
            x ^= x >> 33;
            x *= 0xff51afd7ed558ccdUL;
            x ^= x >> 33;
            x *= 0xc4ceb3fe1a85ec53UL;
            return x ^ (x >> 33);
        }

        private static async Task ExecAsync(NpgsqlConnection connection, string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 1200 };
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        private async Task SeedAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            if (await ScalarAsync(connection, "SELECT count(*) FROM recipe.\"Recipe\"") >= RecipeCount) return;
            var clock = Stopwatch.StartNew();
            await ExecAsync(connection, "SET session_replication_role = replica");

            var names = DefaultRestrictionCriteria.ByName.Keys.ToList();
            var typeSql = string.Join(",", names.Select((n, i) => $"({70_000 + i}, '{n.Replace("'", "''")}', now())"));
            await ExecAsync(connection, $"INSERT INTO reference.\"Reference\" (\"Id\", \"Name\", \"CreatedDate\") SELECT v.* FROM (VALUES {typeSql}) AS v(id, name, at) WHERE NOT EXISTS (SELECT 1 FROM reference.\"Reference\" r WHERE r.\"Name\" = v.name)");
            await using (var db = NewContext()) await DefaultRestrictionCriteria.EnsureAsync(db);
            var types = await ScalarAsync(connection, "SELECT count(DISTINCT \"RestrictionTypeId\") FROM plan.\"RestrictionCriterion\" WHERE NOT \"IsDeleted\"");
            var copies = new List<string>();
            for (var i = 0; types + copies.Count < TypeCount; i++) copies.Add($"({71_000 + i}, '{names[i % names.Count].Replace("'", "''")}', now())");
            if (copies.Count > 0)
            {
                await ExecAsync(connection, $"INSERT INTO reference.\"Reference\" (\"Id\", \"Name\", \"CreatedDate\") VALUES {string.Join(",", copies)}");
                await using var db = NewContext();
                await DefaultRestrictionCriteria.EnsureAsync(db);
            }

            var firstFood = await ScalarAsync(connection, "SELECT coalesce(max(\"Id\"), 0) + 1 FROM recipe.\"Ingredient\"");
            var existingFoods = await ScalarAsync(connection, "SELECT count(*) FROM recipe.\"Ingredient\"");
            var byWord = Vocabulary.Select(_ => new List<long>()).ToArray();
            var used = new HashSet<string>(StringComparer.Ordinal);
            await using (var nameQuery = new NpgsqlCommand("SELECT \"Name\" FROM recipe.\"Ingredient\"", connection))
            await using (var reader = await nameQuery.ExecuteReaderAsync())
                while (await reader.ReadAsync()) used.Add(reader.GetString(0));
            await using (var w = await connection.BeginBinaryImportAsync("COPY recipe.\"Ingredient\" (\"Id\", \"Name\", \"FdcDataType\", \"CurationStatusId\", \"CreatedDate\", \"FdcCategoryId\", \"IsDeleted\") FROM STDIN (FORMAT BINARY)"))
            {
                for (var i = 0; i < FoodCount - existingFoods; i++)
                {
                    var id = firstFood + i;
                    var h = Mix((ulong)id);
                    var word = (int)(h % (ulong)Vocabulary.Length);
                    byWord[word].Add(id);
                    var name = ((h >> 16) % 5) switch
                    {
                        0 => Vocabulary[word],
                        1 => char.ToUpperInvariant(Vocabulary[word][0]) + Vocabulary[word][1..] + ", raw",
                        2 => "ACME " + Vocabulary[word] + " " + (h >> 40) % 1000,
                        3 => Vocabulary[word] + ", organic",
                        _ => Vocabulary[word] + " (" + (h >> 30) % 50 + ")",
                    };
                    if (!used.Add(name)) used.Add(name = name + " #" + id);
                    await w.StartRowAsync();
                    await w.WriteAsync(id, NpgsqlDbType.Bigint);
                    await w.WriteAsync(name, NpgsqlDbType.Varchar);
                    await w.WriteAsync(((h >> 20) % 3) switch { 0 => "foundation_food", 1 => "sr_legacy_food", _ => "branded_food" }, NpgsqlDbType.Varchar);
                    await w.WriteAsync((h >> 24) % 4 == 0 ? 9001L : 9003L, NpgsqlDbType.Bigint);
                    await w.WriteAsync(DateTime.UtcNow, NpgsqlDbType.TimestampTz);
                    if ((h >> 28) % 5 < 2) await w.WriteAsync((int)((h >> 32) % 28 + 1), NpgsqlDbType.Integer);
                    else await w.WriteNullAsync();
                    await w.WriteAsync((h >> 36) % 33 == 0, NpgsqlDbType.Boolean);
                }
                await w.CompleteAsync();
            }
            await using (var w = await connection.BeginBinaryImportAsync("COPY recipe.\"IngredientAlias\" (\"IngredientId\", \"AliasName\", \"SourceContext\", \"CreatedDate\") FROM STDIN (FORMAT BINARY)"))
            {
                var aliases = new HashSet<(long, string)>();
                for (var i = 0; i < AliasCount; i++)
                {
                    var h = Mix((ulong)i + 7_000_000UL);
                    var kind = AliasKinds[(int)(h % (ulong)AliasKinds.Length)];
                    var owner = firstFood + (long)((h >> 8) % (ulong)(FoodCount - existingFoods));
                    var text = Vocabulary[(int)((h >> 24) % (ulong)Vocabulary.Length)] + ((h >> 40) % 4 == 0 ? " or " + Vocabulary[(int)((h >> 44) % (ulong)Vocabulary.Length)] : string.Empty);
                    if (!aliases.Add((owner, text))) continue;
                    await w.StartRowAsync();
                    await w.WriteAsync(owner, NpgsqlDbType.Bigint);
                    await w.WriteAsync(text, NpgsqlDbType.Varchar);
                    if (kind.Context == null) await w.WriteNullAsync();
                    else await w.WriteAsync(kind.Context, NpgsqlDbType.Varchar);
                    await w.WriteAsync(DateTime.UtcNow, NpgsqlDbType.TimestampTz);
                }
                await w.CompleteAsync();
            }
            var drinks = Vocabulary.Select((v, i) => (v, i)).Where(x => x.v is "dry white wine" or "sherry" or "brandy" or "dark rum" or "beer" or "vanilla extract").SelectMany(x => byWord[x.i]).ToList();
            await using (var w = await connection.BeginBinaryImportAsync("COPY nutrient.\"IngredientNutrient\" (\"IngredientId\", \"NutrientId\", \"Amount\", \"MeasurementId\", \"CreatedDate\") FROM STDIN (FORMAT BINARY)"))
            {
                foreach (var id in drinks)
                {
                    await w.StartRowAsync();
                    await w.WriteAsync(id, NpgsqlDbType.Bigint);
                    await w.WriteAsync(AlcoholNutrient, NpgsqlDbType.Bigint);
                    await w.WriteAsync(id % 3 == 0 ? 0.2m : 12m, NpgsqlDbType.Numeric);
                    await w.WriteAsync(Gram, NpgsqlDbType.Bigint);
                    await w.WriteAsync(DateTime.UtcNow, NpgsqlDbType.TimestampTz);
                }
                await w.CompleteAsync();
            }

            var firstRecipe = await ScalarAsync(connection, "SELECT coalesce(max(\"Id\"), 0) + 1 FROM recipe.\"Recipe\"");
            var existingRecipes = await ScalarAsync(connection, "SELECT count(*) FROM recipe.\"Recipe\"");
            var existingLines = await ScalarAsync(connection, "SELECT count(*) FROM recipe.\"RecipeIngredient\"");
            var newRecipes = (int)(RecipeCount - existingRecipes);
            var counts = new int[newRecipes];
            var total = 0;
            for (var r = 0; r < newRecipes; r++)
            {
                counts[r] = r < Lineless ? 0 : 2 + (int)(Mix((ulong)r) % 11);
                total += counts[r];
            }
            for (var r = Lineless; total != LineCount - existingLines; r = r + 1 >= newRecipes ? Lineless : r + 1)
            {
                if (total < LineCount - existingLines && counts[r] < 30) { counts[r]++; total++; }
                else if (total > LineCount - existingLines && counts[r] > 1) { counts[r]--; total--; }
            }
            await using (var w = await connection.BeginBinaryImportAsync("COPY recipe.\"Recipe\" (\"Id\", \"Name\", \"CurationStatusId\", \"AuthorId\", \"Version\", \"CreatedDate\", \"Visibility\", \"Servings\") FROM STDIN (FORMAT BINARY)"))
            {
                for (var r = 0; r < newRecipes; r++)
                {
                    var h = Mix((ulong)r + 99UL);
                    await w.StartRowAsync();
                    await w.WriteAsync(firstRecipe + r, NpgsqlDbType.Bigint);
                    await w.WriteAsync(Titles[(int)(h % (ulong)Titles.Length)] + " with " + Vocabulary[(int)((h >> 16) % (ulong)Vocabulary.Length)] + " " + r, NpgsqlDbType.Varchar);
                    await w.WriteAsync(9003L, NpgsqlDbType.Bigint);
                    await w.WriteAsync(PersonId, NpgsqlDbType.Bigint);
                    await w.WriteAsync(1L, NpgsqlDbType.Bigint);
                    await w.WriteAsync(DateTime.UtcNow.AddMinutes(-r), NpgsqlDbType.TimestampTz);
                    await w.WriteAsync(4, NpgsqlDbType.Integer);
                    await w.WriteAsync(2L + (long)((h >> 30) % 6), NpgsqlDbType.Bigint);
                }
                await w.CompleteAsync();
            }
            await using (var w = await connection.BeginBinaryImportAsync("COPY recipe.\"RecipeIngredient\" (\"RecipeId\", \"IngredientId\", \"Quantity\", \"MeasurementId\", \"RawLine\", \"CreatedDate\", \"SortOrder\") FROM STDIN (FORMAT BINARY)"))
            {
                for (var r = 0; r < newRecipes; r++)
                {
                    for (var l = 0; l < counts[r]; l++)
                    {
                        var h = Mix((ulong)r * 1_000_003UL + (ulong)l * 7919UL);
                        var word = (int)(h % (ulong)Vocabulary.Length);
                        var foods = byWord[word].Count > 0 ? byWord[word] : byWord.First(b => b.Count > 0);
                        var raw = (Amounts[(int)((h >> 20) % (ulong)Amounts.Length)] + " " + Units[(int)((h >> 30) % (ulong)Units.Length)] + " " + Vocabulary[word] + ((h >> 40) % 7 == 0 ? ", divided" : string.Empty)).Replace("  ", " ");
                        await w.StartRowAsync();
                        await w.WriteAsync(firstRecipe + r, NpgsqlDbType.Bigint);
                        await w.WriteAsync(foods[(int)((h >> 44) % (ulong)foods.Count)], NpgsqlDbType.Bigint);
                        await w.WriteAsync(1m + (h >> 50) % 4, NpgsqlDbType.Numeric);
                        await w.WriteAsync(Gram, NpgsqlDbType.Bigint);
                        await w.WriteAsync(raw, NpgsqlDbType.Text);
                        await w.WriteAsync(DateTime.UtcNow, NpgsqlDbType.TimestampTz);
                        await w.WriteAsync(l, NpgsqlDbType.Integer);
                    }
                }
                await w.CompleteAsync();
            }
            await ExecAsync(connection, $"INSERT INTO plan.\"HouseholdMember\" (\"HouseholdId\", \"PersonId\", \"Role\", \"IsActive\", \"IsAdmin\", \"CanManage\", \"CanInvite\", \"CreatedDate\") VALUES ({HouseholdId}, {PersonId}, 'member', true, false, false, false, now())");
            await ExecAsync(connection, "SET session_replication_role = origin");

            var statements = new List<ContentsStatement>();
            for (var i = 0; i < StatementCount; i++)
            {
                var h = Mix((ulong)i + 55_000UL);
                var picks = Enumerable.Range(0, 5).Select(k => Vocabulary[(int)(Mix(h + (ulong)k) % (ulong)Vocabulary.Length)]);
                statements.Add(new ContentsStatement(firstFood + (long)(h % (ulong)(FoodCount - existingFoods)), "fdc:" + i, "Ingredients: " + string.Join(", ", picks) + ". Contains: " + ((h >> 8) % 3 == 0 ? "wheat, milk" : "soy") + "."));
            }
            await using (var db = NewContext())
                await new PreparedContentsService(db).RecordAsync(statements.DistinctBy(s => s.IngredientId).ToList(), "diet-store-perf", apply: true);
            await ExecAsync(connection, "SELECT setval(pg_get_serial_sequence('recipe.\"Ingredient\"', 'Id'), (SELECT max(\"Id\") FROM recipe.\"Ingredient\"))");
            await ExecAsync(connection, "SELECT setval(pg_get_serial_sequence('recipe.\"Recipe\"', 'Id'), (SELECT max(\"Id\") FROM recipe.\"Recipe\"))");
            await ExecAsync(connection, "ANALYZE");
            _output.WriteLine($"generated in {clock.Elapsed.TotalSeconds:F0} s");
        }

        private async Task<(TimeSpan Median, int Count)> TimeAsync(Func<ApplicationDbContext, Task<int>> run)
        {
            var times = new List<TimeSpan>();
            var count = 0;
            for (var i = 0; i < 4; i++)
            {
                await using var db = NewContext();
                var clock = Stopwatch.StartNew();
                count = await run(db);
                clock.Stop();
                if (i > 0) times.Add(clock.Elapsed);
            }
            return (times.OrderBy(t => t).ElementAt(1), count);
        }

        private static async Task SetRestrictionAsync(long? typeId)
        {
            await using var db = NewContext();
            await db.Restrictions.IgnoreQueryFilters().Where(r => r.PersonId == PersonId).ExecuteDeleteAsync();
            if (typeId is { } id)
            {
                db.Restrictions.Add(new Nom.Data.Plan.RestrictionEntity { PersonId = PersonId, Name = "perf", RestrictionTypeId = id, CreatedDate = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }

        [DietStorePostgresFact]
        [Trait("Category", "Performance")]
        public async Task A_search_with_a_severe_restriction_answers_under_a_second_on_a_built_store()
        {
            await SeedAsync();
            await using (var db = NewContext())
                if (await DietGateStore.ActiveBuildAsync(db) == null) await new DietGateStoreBuilder().RunAsync(db);
            foreach (var (typeId, query) in new (long?, string?)[] { (null, null), (null, "chicken"), (60800, null), (60800, "chicken"), (60700, null), (60717, null) })
            {
                await SetRestrictionAsync(typeId);
                var model = new RecipeSearchModel { Query = query, PageSize = 20, RequesterPersonId = PersonId, SortBy = "relevance" };
                var timed = await TimeAsync(async db => (await new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance).SearchRecipesAsync(model)).TotalCount);
                _output.WriteLine($"type {typeId?.ToString() ?? "none"}, query '{query}': {timed.Median.TotalMilliseconds:F0} ms, {timed.Count:N0} results");
                timed.Median.Should().BeLessThan(TimeSpan.FromSeconds(1));
            }
            await SetRestrictionAsync(null);
        }

        [DietStorePostgresFact]
        [Trait("Category", "Performance")]
        public async Task The_store_builds_within_twenty_minutes_and_answers_every_type_under_a_second()
        {
            await SeedAsync();
            await using (var db = NewContext())
            {
                await db.DietRecipeFlags.ExecuteDeleteAsync();
                await db.DietRecipeStates.ExecuteDeleteAsync();
                await db.DietFoodFacts.ExecuteDeleteAsync();
                await db.DietFoodStates.ExecuteDeleteAsync();
                await db.DietGateSets.ExecuteDeleteAsync();
                await db.DietGateBuilds.ExecuteDeleteAsync();
                _output.WriteLine($"catalog: {await db.Ingredients.IgnoreQueryFilters().CountAsync():N0} foods, {await db.Recipes.CountAsync():N0} recipes, {await db.RecipeIngredients.CountAsync():N0} lines, "
                    + $"{await db.Set<Nom.Data.Plan.RestrictionCriterionEntity>().Select(c => c.RestrictionTypeId).Distinct().CountAsync()} types, {await db.IngredientContents.CountAsync():N0} statements");
            }
            await SetRestrictionAsync(null);

            var builder = new DietGateStoreBuilder();
            DietGateStoreBuilder.CycleReport first;
            await using (var db = NewContext()) first = await builder.RunAsync(db);
            _output.WriteLine($"first build: {first.Seconds:F1} s (foods {first.FoodSeconds:F1} s, recipes {first.RecipeSeconds:F1} s), {first.Sets} sets, {first.Foods:N0} foods, {first.Recipes:N0} recipes");
            first.Switched.Should().BeTrue();
            TimeSpan.FromSeconds(first.Seconds).Should().BeLessThan(TimeSpan.FromMinutes(20), "the first build must finish within 20 minutes");
            await using (var db = NewContext())
            {
                var flags = await db.DietRecipeFlags.CountAsync();
                var facts = await db.DietFoodFacts.CountAsync();
                _output.WriteLine($"stored: {facts:N0} food facts, {flags:N0} recipe flags");
                foreach (var set in (await DietGateStoreReports.BuildsAsync(db)).Single().SetReports.OrderByDescending(s => s.FoodSeconds + s.RecipeSeconds).Take(8))
                    _output.WriteLine($"  slowest set: {set.Name} ({set.Reading}) foods {set.FoodSeconds:F2} s, recipes {set.RecipeSeconds:F2} s, {set.Foods:N0} foods, {set.RecipesContaining:N0} containing, {set.RecipesUnknown} unknown");
            }

            DietGateStoreBuilder.CycleReport restart;
            await using (var db = NewContext()) restart = await new DietGateStoreBuilder().RunAsync(db);
            _output.WriteLine($"cycle after a restart, nothing changed: {restart.Seconds:F1} s, {restart.FoodsDecided} foods and {restart.RecipesDecided} recipes decided");
            restart.RecipesDecided.Should().Be(0);

            long food;
            await using (var db = NewContext())
            {
                food = await db.RecipeIngredients.Where(ri => ri.RawLine.Contains("rice") && !ri.RawLine.Contains("vinegar")).Select(ri => ri.IngredientId).FirstAsync();
                var entity = await db.Ingredients.SingleAsync(i => i.Id == food);
                entity.Name = "Wheat berries " + DateTime.UtcNow.Ticks;
                await db.SaveChangesAsync();
            }
            DietGateStoreBuilder.CycleReport change;
            await using (var db = NewContext()) change = await builder.RunAsync(db);
            _output.WriteLine($"one food changed: cycle {change.Seconds:F1} s, {change.FoodsDecided} food and {change.RecipesDecided} recipes decided");
            change.FoodsDecided.Should().Be(1);
            TimeSpan.FromSeconds(change.Seconds).Should().BeLessThan(TimeSpan.FromSeconds(45), "with the 15 s poll a one-food change shows within a minute");
            await using (var db = NewContext())
            {
                var celiac = await db.DietGateSets.Where(s => s.Criteria.Contains("%wheat%")).Select(s => s.Id).ToListAsync();
                (await db.DietRecipeFlags.AnyAsync(f => celiac.Contains(f.SetId) && f.IngredientId == food)).Should().BeTrue("recipes on the renamed food are flagged in the same cycle");
            }

            await using (var db = NewContext())
            {
                var clock = Stopwatch.StartNew();
                var counts = await DietGateStoreReports.CountsAsync(db);
                _output.WriteLine($"gate-counts: {clock.Elapsed.TotalSeconds:F2} s for {counts.Types.Count} sets");
                clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
            }

            var search = new RecipeSearchModel { Query = "chicken", PageSize = 5, RequesterPersonId = PersonId, SortBy = "relevance" };
            var empty = new RecipeSearchModel { PageSize = 20, RequesterPersonId = PersonId, SortBy = "relevance" };
            var none = await TimeAsync(async db => (await new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance).SearchRecipesAsync(search)).TotalCount);
            var noneEmpty = await TimeAsync(async db => (await new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance).SearchRecipesAsync(empty)).TotalCount);
            _output.WriteLine($"search without a restriction: 'chicken' {none.Median.TotalMilliseconds:F0} ms ({none.Count:N0}), empty {noneEmpty.Median.TotalMilliseconds:F0} ms ({noneEmpty.Count:N0})");

            List<long> typeIds;
            await using (var db = NewContext()) typeIds = await db.Set<Nom.Data.Plan.RestrictionCriterionEntity>().Select(c => c.RestrictionTypeId).Distinct().OrderBy(id => id).ToListAsync();
            var slowestSearch = TimeSpan.Zero;
            var slowestShuffle = TimeSpan.Zero;
            var rows = new List<string>();
            var searches = new List<TimeSpan>();
            var emptySearches = new List<TimeSpan>();
            foreach (var typeId in typeIds)
            {
                await SetRestrictionAsync(typeId);
                var s = await TimeAsync(async db => (await new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance).SearchRecipesAsync(search)).TotalCount);
                var e = await TimeAsync(async db => (await new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance).SearchRecipesAsync(empty)).TotalCount);
                var shuffle = await TimeAsync(async db =>
                {
                    var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
                    var picked = await db.Recipes.VisibleTo(db, PersonId).WithoutHeld(db, set).OrderBy(r => EF.Functions.Random()).Take(30).Select(r => r.Id).ToListAsync();
                    var foods = await db.Ingredients.Where(i => i.FoodGroupId != null).WithoutHeld(db, set).OrderBy(i => i.Id).Take(10).Select(i => i.Id).ToListAsync();
                    return picked.Count + foods.Count;
                });
                slowestSearch = new[] { slowestSearch, s.Median, e.Median }.Max();
                searches.Add(s.Median);
                emptySearches.Add(e.Median);
                slowestShuffle = shuffle.Median > slowestShuffle ? shuffle.Median : slowestShuffle;
                rows.Add($"{typeId}\t{s.Median.TotalMilliseconds:F0}\t{s.Count}\t{e.Median.TotalMilliseconds:F0}\t{e.Count}\t{shuffle.Median.TotalMilliseconds:F0}");
            }
            await SetRestrictionAsync(null);
            _output.WriteLine("type\tsearch ms\tresults\tempty search ms\tresults\tshuffle gate ms");
            foreach (var row in rows) _output.WriteLine(row);
            _output.WriteLine($"slowest search with a restriction: {slowestSearch.TotalMilliseconds:F0} ms; slowest shuffle gate: {slowestShuffle.TotalMilliseconds:F0} ms");
            slowestSearch.Should().BeLessThan(TimeSpan.FromSeconds(1), "search answers under a second with any restriction");
            var typical = (Search: searches.OrderBy(t => t).ElementAt(searches.Count / 2), Empty: emptySearches.OrderBy(t => t).ElementAt(emptySearches.Count / 2));
            _output.WriteLine($"median over types: 'chicken' {typical.Search.TotalMilliseconds:F0} ms (without {none.Median.TotalMilliseconds:F0}), empty {typical.Empty.TotalMilliseconds:F0} ms (without {noneEmpty.Median.TotalMilliseconds:F0}); worst gap over the unrestricted empty search {(emptySearches.Max() - noneEmpty.Median).TotalMilliseconds:F0} ms");
            typical.Search.Should().BeLessThanOrEqualTo(none.Median + TimeSpan.FromMilliseconds(100), "a restricted search is typically no slower than without one");
            typical.Empty.Should().BeLessThanOrEqualTo(noneEmpty.Median + TimeSpan.FromMilliseconds(100), "a restricted search is typically no slower than without one");
            slowestShuffle.Should().BeLessThan(TimeSpan.FromSeconds(1), "the gate's part of a shuffle answers under a second");
        }
    }
}
