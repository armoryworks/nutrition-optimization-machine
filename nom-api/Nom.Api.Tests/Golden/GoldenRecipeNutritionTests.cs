using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The recipes of recipes.json, each seeded with its lines, its foods' per-100 g facts and
    /// weights, and the measurements of measurements.tsv: the derived label must give the stored
    /// calories, protein and fat to 4 dp and count the reviewed lines, and a hand-authored label
    /// is left as written. A known recipe holds data that is wrong (an item weight, a yield read
    /// as servings); its arithmetic is still asserted, so counting cannot drift under it.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenRecipeNutritionTests
    {
        private const long Calories = 5035, Protein = 5006, Fat = 5000, Other = 9999, Kcal = 16, Gram = 1;
        private const string File = "recipes.json";

        private readonly ITestOutputHelper _output;

        public GoldenRecipeNutritionTests(ITestOutputHelper output) => _output = output;

        private sealed record Result(long Id, string Status, bool Pass, string Expected, string Actual, bool HandLabel);

        private static decimal? Dec(JsonNode? node) => node == null ? null : node.GetValue<decimal>();

        private static async Task<(ApplicationDbContext Db, long RecipeId)> SeedAsync(JsonObject recipe)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var units = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var m in GoldenSet.Read("measurements.tsv").Rows)
            {
                db.Set<BaseMeasurementEntity>().Add(new BaseMeasurementEntity
                {
                    Id = long.Parse(m.Id), Name = m["name"], Symbol = m["name"].ToLowerInvariant(), MeasurementCategoryId = long.Parse(m["category"]),
                    BaseUnitConversionFactor = m.Value("factor") is { } f ? decimal.Parse(f, CultureInfo.InvariantCulture) : null,
                    IsBaseUnit = m["isBase"] == "true", IsContainer = m["isContainer"] == "true",
                });
                units[m["name"]] = long.Parse(m.Id);
            }
            if (!units.ContainsValue(Kcal)) db.Set<BaseMeasurementEntity>().Add(new BaseMeasurementEntity { Id = Kcal, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 5, BaseUnitConversionFactor = 1m });
            db.Nutrients.AddRange(
                new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = Kcal },
                new NutrientEntity { Id = Protein, Name = "Protein", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = Fat, Name = "Fat", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = Other, Name = "Other", DefaultMeasurementId = Gram });
            await db.SaveChangesAsync();

            var recipeId = recipe["id"]!.GetValue<long>();
            db.Recipes.Add(new RecipeEntity
            {
                Id = recipeId, Name = recipe["name"]?.GetValue<string>() ?? $"Recipe {recipeId}", AuthorId = 1,
                Servings = recipe["servings"]?.GetValue<long>(), RecipeServings = Dec(recipe["recipeServings"]),
            });
            var seeded = new HashSet<long>();
            var sort = 0;
            foreach (var line in recipe["lines"]!.AsArray().Select(l => l!.AsObject()))
            {
                var food = line["food"]!.AsObject();
                var foodId = food["id"]!.GetValue<long>();
                if (seeded.Add(foodId))
                {
                    db.Ingredients.Add(new IngredientEntity
                    {
                        Id = foodId, Name = food["name"]!.GetValue<string>(), CurationStatusId = 9003,
                        GramsPerItem = Dec(food["gramsPerItem"]), GramsPerMilliliter = Dec(food["gramsPerMl"]), ReferenceServingGrams = Dec(food["referenceServingGrams"]),
                    });
                    var per100 = food["per100"]!.AsObject();
                    var facts = new[] { (Calories, Kcal, Dec(per100["calories"])), (Protein, Gram, Dec(per100["protein"])), (Fat, Gram, Dec(per100["fat"])) }
                        .Where(f => f.Item3 != null).ToList();
                    if (facts.Count == 0 && food["hasNutrition"]!.GetValue<bool>()) facts.Add((Other, Gram, 0m));
                    db.IngredientNutrients.AddRange(facts.Select(f => new IngredientNutrientEntity { IngredientId = foodId, NutrientId = f.Item1, MeasurementId = f.Item2, Amount = f.Item3!.Value }));
                }
                db.RecipeIngredients.Add(new RecipeIngredientEntity
                {
                    RecipeId = recipeId, IngredientId = foodId, SortOrder = sort++, Quantity = line["qty"]!.GetValue<decimal>(),
                    MeasurementId = units[line["unit"]!.GetValue<string>()], LineKind = line["kind"]?.GetValue<string>(), RawLine = line["raw"]?.GetValue<string>() ?? string.Empty,
                });
            }
            await db.SaveChangesAsync();
            return (db, recipeId);
        }

        private static async Task<Result> ScoreAsync(JsonObject recipe)
        {
            var (db, recipeId) = await SeedAsync(recipe);
            using var _ = db;
            var expected = recipe["expected"]!.AsObject();
            var hand = expected["handLabel"]!.GetValue<bool>();
            var want = new Dictionary<long, decimal?> { [Calories] = Dec(expected["calories"]), [Protein] = Dec(expected["protein"]), [Fat] = Dec(expected["fat"]) };
            if (hand)
            {
                db.RecipeNutrition.AddRange(want.Where(w => w.Value != null).Select(w => new RecipeNutritionEntity { RecipeId = recipeId, NutrientId = w.Key, Amount = w.Value!.Value, DateCalculated = null }));
                await db.SaveChangesAsync();
            }

            await new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance).RecalculateAsync(recipeId);

            var rows = await db.RecipeNutrition.Where(n => n.RecipeId == recipeId).ToListAsync();
            var got = want.Keys.ToDictionary(k => k, k => rows.FirstOrDefault(r => r.NutrientId == k)?.Amount);
            var lines = await db.RecipeIngredients.Include(ri => ri.Measurement).Include(ri => ri.Ingredient).ThenInclude(i => i.IngredientNutrients)
                .Where(ri => ri.RecipeId == recipeId).ToListAsync();
            var counted = lines.Count(ri => RecipeNutritionService.StatusFor(ri, ri.Ingredient.IngredientNutrients.Count > 0) == NutritionLineStatus.Counted);
            var wantCounted = expected["countedLines"]?.GetValue<int>();

            var pass = want.All(w => w.Value == null ? got[w.Key] == null : got[w.Key] is { } g && Math.Round(g, 4) == Math.Round(w.Value.Value, 4))
                && (wantCounted == null || wantCounted == counted)
                && (!hand || rows.All(r => r.DateCalculated == null));
            string Show(Dictionary<long, decimal?> v) => $"kcal {v[Calories]?.ToString("0.####") ?? "-"}, protein {v[Protein]?.ToString("0.####") ?? "-"}, fat {v[Fat]?.ToString("0.####") ?? "-"}";
            return new Result(recipeId, recipe["status"]!.GetValue<string>(), pass,
                Show(want) + (wantCounted is { } c ? $", {c} counted lines" : string.Empty),
                Show(got) + $", {counted} counted lines", hand);
        }

        private static async Task<List<Result>> ScoreAllAsync(Func<Result, bool> include)
        {
            var recipes = JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(GoldenSet.Folder, File)))!.AsArray().Select(r => r!.AsObject()).ToList();
            var results = new List<Result>();
            foreach (var recipe in recipes) results.Add(await ScoreAsync(recipe));
            return results.Where(include).ToList();
        }

        private void Report(string key, List<Result> results)
        {
            var options = GoldenOptions.FromEnvironment();
            var baseline = GoldenSet.Baseline(options.ReadFolder);
            var report = new GoldenReport { Key = key, Baseline = baseline.TryGetValue(key, out var b) ? b : null };
            foreach (var r in results)
            {
                report.Judged++;
                var line = $"recipe {r.Id}: expected {r.Expected}, got {r.Actual}";
                switch (r.Status)
                {
                    case GoldenSet.Ok:
                        report.Ok++;
                        if (r.Pass) report.OkPassing++;
                        else report.Regressed.Add(line);
                        break;
                    case GoldenSet.Known:
                        report.Known++;
                        if (!r.Pass) report.Regressed.Add(line + " (known: its data is wrong, its arithmetic must still hold)");
                        break;
                    default:
                        report.Unreviewed.Add($"{r.Status}: {line}");
                        break;
                }
            }
            _output.WriteLine(report.Summary);
            if (options.Update && options.UnderCi) throw new InvalidOperationException($"{GoldenSet.UpdateVariable}=1 is refused under CI.");
            if (options.Update) UpdateNewRecipes(options, results);
            GoldenSet.Assert(report);
        }

        private static void UpdateNewRecipes(GoldenOptions options, List<Result> results)
        {
            var path = Path.Combine(options.ReadFolder, File);
            var recipes = JsonNode.Parse(System.IO.File.ReadAllText(path))!.AsArray();
            var changed = false;
            foreach (var recipe in recipes.Select(r => r!.AsObject()))
            {
                var result = results.FirstOrDefault(r => r.Id == recipe["id"]!.GetValue<long>());
                if (result == null || result.Status != GoldenSet.New || !result.Pass) continue;
                recipe["status"] = GoldenSet.Ok;
                changed = true;
            }
            if (!changed) return;
            System.IO.File.WriteAllText(Path.Combine(options.WriteFolder, File), recipes.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
            throw new GoldenSetFailure($"{GoldenSet.UpdateVariable}=1 marked new recipes that reproduce their stored label ok in {File}; review the diff and run again without it.");
        }

        [Fact]
        public async Task Each_recipe_counts_the_reviewed_lines_and_gives_the_reviewed_label()
        {
            Report($"{File}:status", await ScoreAllAsync(r => !r.HandLabel));
        }

        [Fact]
        public async Task A_hand_authored_label_is_left_as_written()
        {
            Report($"{File}:hand", await ScoreAllAsync(r => r.HandLabel));
        }
    }
}
