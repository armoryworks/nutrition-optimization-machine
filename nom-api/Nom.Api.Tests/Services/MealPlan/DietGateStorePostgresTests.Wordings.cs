using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Npgsql;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    public partial class DietGateStorePostgresTests
    {
        private const long WordFoodBase = 50_000_000, WordRecipeBase = 50_000_000;
        private const int Staples = 8, WordingCount = 232, WordRecipes = 20_000, SourceLines = 10, StapleNamedLines = 2, Marked = 10;
        private const string WordBatch = "ck23w";

        private sealed class SystemUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static ApplicationDbContext NewApiContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).AddInterceptors(DietRecipeStateClearer.Shared).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static async Task RemoveWordingKitchenAsync(NpgsqlConnection connection, DateTime since)
        {
            var foods = $"\"IngredientId\" BETWEEN {WordFoodBase} AND {WordFoodBase + 1000}";
            var recipes = $"\"RecipeId\" BETWEEN {WordRecipeBase} AND {WordRecipeBase + WordRecipes}";
            await ExecAsync(connection, $"DELETE FROM recipe.\"IngredientWording\" WHERE \"Batch\" = '{WordBatch}' OR {foods}");
            await ExecAsync(connection, $"DELETE FROM recipe.\"IngredientWordingRejection\" WHERE {foods}");
            await ExecAsync(connection, $"DELETE FROM recipe.\"RecipeIngredient\" WHERE {recipes}");
            await ExecAsync(connection, $"DELETE FROM recipe.\"Recipe\" WHERE \"Id\" BETWEEN {WordRecipeBase} AND {WordRecipeBase + WordRecipes}");
            await ExecAsync(connection, $"DELETE FROM curation.\"FoodCatalogProposal\" WHERE \"Batch\" = 'ck23' OR {foods}");
            await ExecAsync(connection, $"DELETE FROM nutrient.\"IngredientNutrient\" WHERE {foods}");
            await ExecAsync(connection, $"DELETE FROM recipe.\"IngredientAlias\" WHERE {foods}");
            await ExecAsync(connection, $"DELETE FROM recipe.\"Ingredient\" WHERE \"Id\" BETWEEN {WordFoodBase} AND {WordFoodBase + 1000}");
            await ExecAsync(connection, $"DELETE FROM audit.\"AuditLogEntry\" WHERE \"Timestamp\" >= '{since:O}'");
        }

        private static async Task SeedWordingKitchenAsync(NpgsqlConnection connection)
        {
            await ExecAsync(connection, $"INSERT INTO recipe.\"Ingredient\" (\"Id\", \"Name\", \"FdcDataType\", \"CurationStatusId\", \"CreatedDate\", \"IsDeleted\") " +
                $"SELECT {WordFoodBase} + k, 'ck23 staple ' || k, 'foundation_food', 9003, now(), false FROM generate_series(0, {Staples - 1}) k");
            await ExecAsync(connection, $"INSERT INTO nutrient.\"IngredientNutrient\" (\"IngredientId\", \"NutrientId\", \"Amount\", \"MeasurementId\", \"CreatedDate\") " +
                $"SELECT {WordFoodBase} + k, (SELECT min(\"Id\") FROM nutrient.\"Nutrient\"), 1, {Gram}, now() FROM generate_series(0, {Staples - 1}) k");
            await ExecAsync(connection, $"INSERT INTO recipe.\"Ingredient\" (\"Id\", \"Name\", \"FdcDataType\", \"CurationStatusId\", \"CreatedDate\", \"IsDeleted\") " +
                $"SELECT {WordFoodBase + 100} + i, 'ck23 wording ' || i, '', 9000, now(), false FROM generate_series(0, {WordingCount - 1}) i");
            await ExecAsync(connection, $"INSERT INTO recipe.\"Recipe\" (\"Id\", \"Name\", \"CurationStatusId\", \"AuthorId\", \"Version\", \"CreatedDate\", \"Visibility\", \"Servings\", \"ScrapedAtUtc\", \"SourceUrl\") " +
                $"SELECT {WordRecipeBase} + r, 'ck23 recipe ' || r, 9003, {PersonId}, 1, now(), 4, 4, now() - interval '30 days', 'https://ck23.example/' || r FROM generate_series(0, {WordRecipes - 1}) r");
            await ExecAsync(connection, "INSERT INTO recipe.\"RecipeIngredient\" (\"RecipeId\", \"IngredientId\", \"Quantity\", \"MeasurementId\", \"RawLine\", \"CreatedDate\", \"SortOrder\") " +
                $"SELECT {WordRecipeBase} + r, {WordFoodBase} + (r + j) % {Staples}, 1, {Gram}, '1 cup ck23 staple ' || ((r + j) % {Staples}), now(), j FROM generate_series(0, {WordRecipes - 1}) r, generate_series(0, 3) j");
            await ExecAsync(connection, "INSERT INTO recipe.\"RecipeIngredient\" (\"RecipeId\", \"IngredientId\", \"Quantity\", \"MeasurementId\", \"RawLine\", \"CreatedDate\", \"SortOrder\") " +
                $"SELECT {WordRecipeBase} + ((i * {SourceLines} + m) * 7919) % {WordRecipes}, {WordFoodBase + 100} + i, 2, {Gram}, '2 tbsp ck23 wording ' || i, now(), 10 + m FROM generate_series(0, {WordingCount - 1}) i, generate_series(0, {SourceLines - 1}) m");
            await ExecAsync(connection, "INSERT INTO recipe.\"RecipeIngredient\" (\"RecipeId\", \"IngredientId\", \"Quantity\", \"MeasurementId\", \"RawLine\", \"CreatedDate\", \"SortOrder\") " +
                $"SELECT {WordRecipeBase} + ((i * {StapleNamedLines} + m) * 104729) % {WordRecipes}, {WordFoodBase} + i % {Staples}, 1, {Gram}, '1 ck23 wording ' || i, now(), 30 + i * {StapleNamedLines} + m FROM generate_series(0, {WordingCount - 1}) i, generate_series(0, {StapleNamedLines - 1}) m");
            await ExecAsync(connection, "INSERT INTO curation.\"FoodCatalogProposal\" (\"Action\", \"IngredientId\", \"Field\", \"CurrentValue\", \"ProposedValue\", \"Source\", \"Batch\", \"Status\", \"CreatedDate\") " +
                $"SELECT 1, {WordFoodBase + 100} + i, 'fdc_link', 'ck23 wording ' || i, ({WordFoodBase} + i % {Staples})::text, 'ai:ck23', 'ck23', 4, now() FROM generate_series(0, {WordingCount - 1}) i");
            await ExecAsync(connection, "ANALYZE");
        }

        private static async Task<string> WordingFingerprintAsync(NpgsqlConnection connection)
        {
            var parts = new List<string>();
            foreach (var sql in new[]
            {
                $"SELECT md5(string_agg(concat_ws('|', \"RecipeId\", \"IngredientId\", \"SortOrder\", \"RawLine\", \"Quantity\", \"MeasurementId\", \"IsDeleted\"), ',' ORDER BY \"RecipeId\", \"SortOrder\", \"IngredientId\")) FROM recipe.\"RecipeIngredient\" WHERE \"RecipeId\" BETWEEN {WordRecipeBase} AND {WordRecipeBase + WordRecipes}",
                $"SELECT md5(string_agg(concat_ws('|', \"Id\", \"Name\", \"CurationStatusId\", \"IsDeleted\"), ',' ORDER BY \"Id\")) FROM recipe.\"Ingredient\" WHERE \"Id\" BETWEEN {WordFoodBase} AND {WordFoodBase + 1000}",
                $"SELECT md5(coalesce(string_agg(concat_ws('|', \"IngredientId\", \"AliasName\", \"SourceContext\", \"IsDeleted\"), ',' ORDER BY \"IngredientId\", \"AliasName\"), '')) FROM recipe.\"IngredientAlias\" WHERE \"IngredientId\" BETWEEN {WordFoodBase} AND {WordFoodBase + 1000}",
                $"SELECT md5(string_agg(concat_ws('|', \"Id\", \"Status\", \"IsDeleted\", \"ProposedValue\"), ',' ORDER BY \"Id\")) FROM curation.\"FoodCatalogProposal\" WHERE \"Batch\" = 'ck23'",
                $"SELECT md5(string_agg(concat_ws('|', \"Wording\", \"Verdict\", \"IngredientId\", \"IsDeleted\", \"Batch\"), ',' ORDER BY \"Wording\")) FROM recipe.\"IngredientWording\" WHERE \"Batch\" = '{WordBatch}'",
            })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                parts.Add(Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty);
            }
            return string.Join(" ", parts);
        }

        private static async Task<Dictionary<long, string>> WordingFlagsAsync(NpgsqlConnection connection, long build)
        {
            var flags = new Dictionary<long, string>();
            await using var command = new NpgsqlCommand(
                "SELECT f.\"RecipeId\", string_agg(concat_ws(':', f.\"SetId\", f.\"Verdict\", f.\"Reason\", f.\"IngredientId\", f.\"CriterionId\"), ',' ORDER BY f.\"SetId\", f.\"Verdict\", f.\"Reason\", f.\"IngredientId\", f.\"CriterionId\") " +
                $"FROM plan.\"DietRecipeFlag\" f JOIN plan.\"DietGateSet\" s ON s.\"Id\" = f.\"SetId\" WHERE s.\"BuildId\" = {build} AND f.\"RecipeId\" BETWEEN {WordRecipeBase} AND {WordRecipeBase + WordRecipes} GROUP BY f.\"RecipeId\"",
                connection) { CommandTimeout = 1200 };
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) flags[reader.GetInt64(0)] = reader.GetString(1);
            return flags;
        }

        private static async Task<HashSet<long>> HeldRecipesAsync(NpgsqlConnection connection)
        {
            var held = new HashSet<long>();
            await using var command = new NpgsqlCommand("SELECT DISTINCT \"RecipeId\" FROM plan.\"DietGateWrite\" WHERE \"RecipeId\" IS NOT NULL", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) held.Add(reader.GetInt64(0));
            return held;
        }

        private async Task DecideAllAsync(DietGateStoreBuilder builder, NpgsqlConnection connection)
        {
            for (var i = 0; i < 6 && await ScalarAsync(connection, "SELECT count(*) FROM plan.\"DietGateWrite\"") > 0; i++)
            {
                await using var db = NewContext();
                var report = await builder.RunChangesAsync(db);
                _output.WriteLine($"cycle: {report.FoodsDecided} foods and {report.RecipesDecided} recipes decided in {report.Seconds:F1} s");
            }
        }

        private static List<VerifiedWordingRow> WordingRows() =>
            Enumerable.Range(0, WordingCount).Select(i => new VerifiedWordingRow
            {
                Row = "ck23-" + i, Wording = "ck23 wording " + i, Verdict = IngredientWordingVerdict.Food,
                FoodId = WordFoodBase + i % Staples, FoodName = "ck23 staple " + (i % Staples),
            }).ToList();

        [DietStorePostgresFact]
        [Trait("Category", "Performance")]
        public async Task A_wording_batch_of_staging_size_unloads_with_its_lines_and_reloads_within_the_command_timeout_holds_only_what_it_changes_and_the_round_trip_is_byte_identical()
        {
            await SeedAsync();
            var build = await ActiveBuildAsync();
            var builder = new DietGateStoreBuilder();
            var since = DateTime.UtcNow;
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await RemoveWordingKitchenAsync(connection, since.AddYears(1));
            await SeedWordingKitchenAsync(connection);
            var user = new SystemUser();
            try
            {
                async Task<T> TimedAsync<T>(string step, Func<ApplicationDbContext, Task<T>> run)
                {
                    await using var db = NewApiContext();
                    var clock = Stopwatch.StartNew();
                    var result = await run(db);
                    _output.WriteLine($"{step}: {clock.Elapsed.TotalSeconds:F1} s");
                    return result;
                }

                var loaded = await TimedAsync("load", db => new VerifiedWordingService(db, user).LoadAsync(WordingRows(), WordBatch, apply: true));
                (loaded.Added, loaded.Rejected).Should().Be((WordingCount, 0));
                var linked = await TimedAsync("relink", db => new LineRelinkService(db, user).LinkVerifiedWordingsAsync(apply: true, maxLines: 50000, batch: WordBatch));
                linked.Moved.Should().Be(WordingCount * SourceLines);
                await ExecAsync(connection, "INSERT INTO recipe.\"IngredientAlias\" (\"IngredientId\", \"AliasName\", \"SourceContext\", \"CreatedDate\") " +
                    $"SELECT {WordFoodBase + 100} + i, 'ck23 wording ' || i, '{RecheckDietGuard.UnknownContentsContext}', now() FROM generate_series(0, {Marked - 1}) i");
                await DecideAllAsync(builder, connection);
                var before = await WordingFingerprintAsync(connection);
                var flagsBefore = await WordingFlagsAsync(connection, build);
                await ExecAsync(connection, "DELETE FROM plan.\"DietGateWrite\"");

                var unloaded = await TimedAsync("unload with lines", db => new VerifiedWordingService(db, user).UnloadAsync(WordBatch, apply: true, withLines: true));
                unloaded.Removed.Should().Be(WordingCount, "staging checkpoint 9, U11: the w01 unload's apply failed on the 30 s command timeout where its dry run succeeded");
                unloaded.Lines!.Undone!.Restored.Should().Be(WordingCount * SourceLines);
                var held = await HeldRecipesAsync(connection);
                var named = (await ScalarAsync(connection, $"SELECT count(DISTINCT \"RecipeId\") FROM recipe.\"RecipeIngredient\" WHERE \"IngredientId\" BETWEEN {WordFoodBase} AND {WordFoodBase + Staples - 1} AND \"RawLine\" LIKE '1 ck23 wording %'"));
                _output.WriteLine($"held by the unload: {held.Count:N0} recipes of {WordRecipes:N0} on the batch's foods ({named:N0} with a line on a food named like a written wording)");
                held.Count.Should().BeLessThan(WordRecipes / 4, "staging checkpoint 9, U1: a wording write holds the recipes whose reading it can change, not every recipe on the batch's foods");

                await DecideAllAsync(builder, connection);
                var flagsAfter = await WordingFlagsAsync(connection, build);
                var changed = flagsBefore.Keys.Union(flagsAfter.Keys).Where(id => flagsBefore.GetValueOrDefault(id) != flagsAfter.GetValueOrDefault(id)).ToList();
                _output.WriteLine($"recipes whose stored flags the unload changed: {changed.Count:N0}, of them held from the commit: {changed.Count(held.Contains):N0}");
                changed.Where(id => !held.Contains(id)).Should().BeEmpty("every recipe whose reading the unload changed was held from its commit");

                var reloaded = await TimedAsync("reload", db => new VerifiedWordingService(db, user).LoadAsync(WordingRows(), WordBatch, apply: true));
                (reloaded.Added, reloaded.Rejected).Should().Be((WordingCount, 0));
                var dry = await TimedAsync("relink dry run", db => new LineRelinkService(db, user).LinkVerifiedWordingsAsync(apply: false, maxLines: 50000, batch: WordBatch));
                var relinked = await TimedAsync("relink", db => new LineRelinkService(db, user).LinkVerifiedWordingsAsync(apply: true, maxLines: 50000, batch: WordBatch));
                (dry.HeldByDiet, relinked.HeldByDiet, relinked.Moved).Should().Be((0, 0, WordingCount * SourceLines),
                    "staging checkpoint 8 and 9, T14: a verified wording wins over a re-check mark on its own row");
                (await WordingFingerprintAsync(connection)).Should().Be(before, "the w01 round trip ends byte-identical in lines, foods, aliases, proposals and wording rows");
                await DecideAllAsync(builder, connection);
                (await WordingFlagsAsync(connection, build)).Should().BeEquivalentTo(flagsBefore, "and in the stored flags once decided");
            }
            finally
            {
                await RemoveWordingKitchenAsync(connection, since);
                await DecideAllAsync(builder, connection);
            }
        }

        [DietStorePostgresFact]
        [Trait("Category", "Performance")]
        public async Task A_batched_load_and_unload_count_each_held_recipe_once_and_a_stopped_unload_counts_only_its_committed_batches()
        {
            const long food = WordFoodBase + 2000, recipe = WordRecipeBase + 30_000;
            const int count = VerifiedWordingService.Chunk * 2 + 20;
            const string batch = "ck23d";
            static string Word(int i) => "ck23d wording " + (char)('a' + i / 26) + (char)('a' + i % 26);
            var rows = Enumerable.Range(0, count).Select(i => new VerifiedWordingRow { Row = "ck23d-" + i, Wording = Word(i), Verdict = IngredientWordingVerdict.Food, FoodId = food, FoodName = "ck23d staple" }).ToList();
            await SeedAsync();
            var builder = new DietGateStoreBuilder();
            var since = DateTime.UtcNow;
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            async Task RemoveAsync(DateTime from)
            {
                var foods = $"\"IngredientId\" BETWEEN {food} AND {food + 1000}";
                await ExecAsync(connection, $"DELETE FROM recipe.\"IngredientWording\" WHERE \"Batch\" = '{batch}' OR {foods}");
                await ExecAsync(connection, $"DELETE FROM recipe.\"RecipeIngredient\" WHERE \"RecipeId\" BETWEEN {recipe} AND {recipe + 1000}");
                await ExecAsync(connection, $"DELETE FROM recipe.\"Recipe\" WHERE \"Id\" BETWEEN {recipe} AND {recipe + 1000}");
                await ExecAsync(connection, $"DELETE FROM curation.\"FoodCatalogProposal\" WHERE \"Batch\" = '{batch}' OR {foods}");
                await ExecAsync(connection, $"DELETE FROM nutrient.\"IngredientNutrient\" WHERE {foods}");
                await ExecAsync(connection, $"DELETE FROM recipe.\"IngredientAlias\" WHERE {foods}");
                await ExecAsync(connection, $"DELETE FROM recipe.\"Ingredient\" WHERE \"Id\" BETWEEN {food} AND {food + 1000}");
                await ExecAsync(connection, $"DELETE FROM audit.\"AuditLogEntry\" WHERE \"Timestamp\" >= '{from:O}'");
            }

            await RemoveAsync(since.AddYears(1));
            var words = string.Join(",", Enumerable.Range(0, count).Select(i => $"({i}, '{Word(i)}')"));
            await ExecAsync(connection, $"INSERT INTO recipe.\"Ingredient\" (\"Id\", \"Name\", \"FdcDataType\", \"CurationStatusId\", \"CreatedDate\", \"IsDeleted\") VALUES ({food}, 'ck23d staple', 'foundation_food', 9003, now(), false)");
            await ExecAsync(connection, $"INSERT INTO nutrient.\"IngredientNutrient\" (\"IngredientId\", \"NutrientId\", \"Amount\", \"MeasurementId\", \"CreatedDate\") SELECT {food}, (SELECT min(\"Id\") FROM nutrient.\"Nutrient\"), 1, {Gram}, now()");
            await ExecAsync(connection, $"INSERT INTO recipe.\"Ingredient\" (\"Id\", \"Name\", \"FdcDataType\", \"CurationStatusId\", \"CreatedDate\", \"IsDeleted\") SELECT {food + 1} + i, w, '', 9000, now(), false FROM (VALUES {words}) v(i, w)");
            await ExecAsync(connection, $"INSERT INTO recipe.\"Recipe\" (\"Id\", \"Name\", \"CurationStatusId\", \"AuthorId\", \"Version\", \"CreatedDate\", \"Visibility\", \"Servings\", \"ScrapedAtUtc\", \"SourceUrl\") " +
                $"SELECT {recipe} + r, 'ck23d recipe ' || r, 9003, {PersonId}, 1, now(), 4, 4, now() - interval '30 days', 'https://ck23d.example/' || r FROM generate_series(0, {count + 9}) r");
            var named = new[] { (0, 0), (0, 60), (1, 61), (1, 110), (2, 111) };
            await ExecAsync(connection, "INSERT INTO recipe.\"RecipeIngredient\" (\"RecipeId\", \"IngredientId\", \"Quantity\", \"MeasurementId\", \"RawLine\", \"CreatedDate\", \"SortOrder\") VALUES " +
                string.Join(",", named.Select((n, k) => $"({recipe + n.Item1}, {food}, 1, {Gram}, '1 {Word(n.Item2)}', now(), {k})")));
            await ExecAsync(connection, "INSERT INTO recipe.\"RecipeIngredient\" (\"RecipeId\", \"IngredientId\", \"Quantity\", \"MeasurementId\", \"RawLine\", \"CreatedDate\", \"SortOrder\") " +
                $"SELECT {recipe + 10} + i, {food + 1} + i, 2, {Gram}, '2 tbsp ' || w, now(), 0 FROM (VALUES {words}) v(i, w)");
            await ExecAsync(connection, "INSERT INTO curation.\"FoodCatalogProposal\" (\"Action\", \"IngredientId\", \"Field\", \"CurrentValue\", \"ProposedValue\", \"Source\", \"Batch\", \"Status\", \"CreatedDate\") " +
                $"SELECT 1, {food + 1} + i, 'fdc_link', w, '{food}', 'ai:ck23', '{batch}', 4, now() FROM (VALUES {words}) v(i, w)");
            var user = new SystemUser();
            try
            {
                await using (var db = NewApiContext())
                {
                    var loaded = await new VerifiedWordingService(db, user).LoadAsync(rows, batch, apply: true);
                    (loaded.Added, loaded.BatchesWritten).Should().Be((count, 3));
                    loaded.RecipesHeld.Should().Be(3, "the recipe naming wordings of the second and third batches is held by both and counted once");
                }
                await using (var db = NewApiContext())
                    (await new LineRelinkService(db, user).LinkVerifiedWordingsAsync(apply: true, batch: batch)).Moved.Should().Be(count);

                VerifiedWordingUnloadResult dry, stopped, resumed;
                await using (var db = NewApiContext()) dry = await new VerifiedWordingService(db, user).UnloadAsync(batch, withLines: true);
                await using (var db = NewApiContext())
                {
                    var stopping = new VerifiedWordingService(db, user) { BeforeBatchSave = index => index == 1 ? throw new TimeoutException("Timeout during reading attempt") : Task.CompletedTask };
                    stopped = await stopping.UnloadAsync(batch, apply: true, withLines: true);
                }
                (stopped.Completed, stopped.BatchesWritten, stopped.Removed).Should().Be((false, 1, VerifiedWordingService.Chunk));
                (stopped.Lines!.LineMoves, stopped.Lines.Undone!.Restored).Should().Be((VerifiedWordingService.Chunk, VerifiedWordingService.Chunk), "the failed batch's lines were rolled back with it and are not counted");
                stopped.RecipesHeld.Should().Be(1, "only the committed batch's holds stand");

                await using (var db = NewApiContext()) resumed = await new VerifiedWordingService(db, user).UnloadAsync(batch, apply: true, withLines: true);
                (resumed.Completed, resumed.BatchesWritten).Should().Be((true, 2));
                resumed.RecipesHeld.Should().Be(3, "a recipe held by two batches is counted once");
                (stopped.Lines.LineMoves + resumed.Lines!.LineMoves, stopped.Lines.Undone.Restored + resumed.Lines.Undone!.Restored, stopped.Removed + resumed.Removed)
                    .Should().Be((dry.Lines!.LineMoves, count, dry.Removed), "the stopped unload and the same call again add up to the dry run");
            }
            finally
            {
                await RemoveAsync(since);
                await DecideAllAsync(builder, connection);
            }
        }

        [DietStorePostgresFact]
        [Trait("Category", "Performance")]
        public async Task A_food_row_any_process_writes_holds_its_picked_foods_recipes_unless_the_writer_held_them_itself_and_a_deleted_row_holds_nothing()
        {
            await SeedAsync();
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            var food = await ScalarAsync(connection, "SELECT min(i.\"Id\") FROM recipe.\"Ingredient\" i WHERE (SELECT count(DISTINCT ri.\"RecipeId\") FROM recipe.\"RecipeIngredient\" ri WHERE ri.\"IngredientId\" = i.\"Id\") BETWEEN 3 AND 300");
            var onFood = await ScalarAsync(connection, $"SELECT count(DISTINCT \"RecipeId\") FROM recipe.\"RecipeIngredient\" WHERE \"IngredientId\" = {food}");
            Task<long> HeldAsync() => ScalarAsync(connection, "SELECT count(DISTINCT \"RecipeId\") FROM plan.\"DietGateWrite\" WHERE \"RecipeId\" IS NOT NULL");
            await ExecAsync(connection, $"INSERT INTO curation.\"FoodCatalogProposal\" (\"Action\", \"Field\", \"CurrentValue\", \"ProposedValue\", \"Source\", \"Batch\", \"Status\", \"CreatedDate\") VALUES (1, 'fdc_link', 'ck23 trade name', '{food}', 'ai:ck23', 'ck23t', 4, now())");
            try
            {
                await ExecAsync(connection, "DELETE FROM plan.\"DietGateWrite\"");
                await ExecAsync(connection, $"INSERT INTO recipe.\"IngredientWording\" (\"Wording\", \"Verdict\", \"IngredientId\", \"Source\", \"Batch\", \"CreatedDate\") VALUES ('ck23 trade name', 'food', {food}, 'verified:ck23t', 'ck23t', now())");
                (await HeldAsync()).Should().Be(onFood, "a food row written by a process that held nothing itself holds every recipe on its picked food, as before");

                await ExecAsync(connection, "DELETE FROM plan.\"DietGateWrite\"");
                await using (var transaction = await connection.BeginTransactionAsync())
                {
                    await using (var command = new NpgsqlCommand($"SELECT set_config('{DietGateWordingHolds.WriterSetting}', 'writer', true)", connection, transaction)) await command.ExecuteNonQueryAsync();
                    await using (var command = new NpgsqlCommand("UPDATE recipe.\"IngredientWording\" SET \"IsDeleted\" = true WHERE \"Batch\" = 'ck23t'", connection, transaction)) await command.ExecuteNonQueryAsync();
                    await transaction.CommitAsync();
                }
                (await HeldAsync()).Should().Be(0, "a writer that held the recipes its rows change itself (DietGateWordingHolds) leaves the trigger's food part out");
                await ExecAsync(connection, "UPDATE recipe.\"IngredientWording\" SET \"Note\" = 'ck23' WHERE \"Batch\" = 'ck23t'");
                await ExecAsync(connection, "DELETE FROM recipe.\"IngredientWording\" WHERE \"Batch\" = 'ck23t'");
                (await HeldAsync()).Should().Be(0, "a row deleted before and after is read by nothing (the purge deletes deleted wording rows)");
            }
            finally
            {
                await ExecAsync(connection, "DELETE FROM recipe.\"IngredientWording\" WHERE \"Batch\" = 'ck23t'");
                await ExecAsync(connection, "DELETE FROM curation.\"FoodCatalogProposal\" WHERE \"Batch\" = 'ck23t'");
                await ExecAsync(connection, "DELETE FROM plan.\"DietGateWrite\"");
            }
        }
    }
}
