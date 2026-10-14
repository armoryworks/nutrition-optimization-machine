using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Services;

namespace Nom.Import.Services
{
    /// <summary>
    /// Gives USDA-linked ingredients the gram conversions recipe lines need, from USDA's own household
    /// portions: grams per millilitre for cups and spoons (<see cref="UsdaVolumeDensity"/>) and the
    /// weight of one item for pieces, cloves and stalks (<see cref="UsdaCountWeight"/>). Nutrients are
    /// not touched. Only empty fields are filled, so a value an admin entered always stands; a rerun
    /// finds nothing left to do. A Foundation food without portions borrows them from the USDA food
    /// with the same description in another supplied release (SR Legacy usually has them).
    /// Every filled value is logged as an applied proposal (batch <see cref="Batch"/>, source
    /// fdc:&lt;id&gt; of the food the portion came from), which <see cref="UndoAsync"/> reverses.
    /// Run: `dotnet run -- --backfill-portions &lt;csv-dir&gt; [&lt;csv-dir&gt; …] [--dry-run]`.
    /// </summary>
    public class FdcPortionBackfillService
    {
        public const string Batch = "fdc-portions";
        public const string DensityField = "grams_per_milliliter";
        public const string CountField = "grams_per_item";
        private const string SrLegacy = FdcFoundationImportService.SrLegacyFood;
        private const long Curated = (long)CurationStatusEnum.Curated;

        private readonly ApplicationDbContext _db;
        private readonly ILogger<FdcPortionBackfillService> _logger;

        public FdcPortionBackfillService(ApplicationDbContext db, ILogger<FdcPortionBackfillService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<PortionBackfillReport> BackfillAsync(IReadOnlyList<string> csvDirs, bool dryRun, CancellationToken ct = default)
        {
            var foods = new Dictionary<string, (string DataType, string Description)>();
            var portions = new Dictionary<string, List<UsdaPortion>>();
            foreach (var dir in csvDirs)
            {
                foreach (var (fdc, food) in ReadFoods(FindFile(dir, "food.csv"))) foods.TryAdd(fdc, food);
                foreach (var (fdc, rows) in UsdaPortionFile.Read(FindFile(dir, "food_portion.csv"))) portions.TryAdd(fdc, rows);
            }
            var sameDescription = portions.Keys
                .Where(foods.ContainsKey)
                .GroupBy(fdc => foods[fdc].Description.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g
                    .OrderBy(fdc => foods[fdc].DataType == SrLegacy ? 0 : 1)
                    .ThenBy(fdc => long.TryParse(fdc, out var n) ? n : long.MaxValue)
                    .ToList());

            var linked = (await _db.FoodCatalogProposals
                .Where(p => p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == FoodProposalStatus.Applied
                    && p.IngredientId != null && p.FdcId != null)
                .OrderBy(p => p.Id)
                .Select(p => new { IngredientId = p.IngredientId!.Value, FdcId = p.FdcId! })
                .ToListAsync(ct))
                .GroupBy(p => p.IngredientId)
                .ToDictionary(g => g.Key, g => g.Last().FdcId);
            var linkedIds = linked.Keys.ToList();

            var ingredients = await _db.Ingredients
                .Where(i => !i.IsDeleted && (i.FdcId != null || linkedIds.Contains(i.Id)))
                .OrderBy(i => i.Id)
                .ToListAsync(ct);

            var report = new PortionBackfillReport { DryRun = dryRun };
            foreach (var ing in ingredients)
            {
                var fdcId = ing.FdcId ?? linked[ing.Id];
                if (!foods.TryGetValue(fdcId, out var food))
                {
                    report.NotInFiles++;
                    continue;
                }
                report.Linked++;

                var sources = new List<string> { fdcId };
                if (sameDescription.TryGetValue(food.Description.Trim().ToLowerInvariant(), out var twins))
                    sources.AddRange(twins.Where(t => t != fdcId));

                var density = sources
                    .Select(s => (Fdc: s, Value: portions.TryGetValue(s, out var rows) ? UsdaVolumeDensity.GramsPerMilliliter(rows) : null))
                    .FirstOrDefault(x => x.Value != null);
                if (density.Value is { } gml)
                {
                    var change = new PortionChange(ing.Id, ing.Name, fdcId, DensityField, ing.GramsPerMilliliter, gml, density.Fdc,
                        $"{gml} g/ml from {Describe(density.Fdc, foods)} household volumes");
                    if (ing.GramsPerMilliliter == null) report.Changes.Add(change);
                    else if (ing.GramsPerMilliliter != gml) report.KeptExisting.Add(change);
                }

                var item = sources
                    .Select(s => (Fdc: s, Value: portions.TryGetValue(s, out var rows) ? UsdaCountWeight.GramsPerCount(rows, foods[s].Description) : null))
                    .FirstOrDefault(x => x.Value != null);
                if (item.Value is { } one)
                {
                    var change = new PortionChange(ing.Id, ing.Name, fdcId, CountField, ing.GramsPerItem, one.Grams, item.Fdc,
                        $"{one.Portion} = {one.Grams} g each, from {Describe(item.Fdc, foods)}", ing.ReferenceServingGrams);
                    if (ing.GramsPerItem == null) report.Changes.Add(change);
                    else if (ing.GramsPerItem != one.Grams) report.KeptExisting.Add(change);
                }
            }

            await MeasureImpactAsync(report, ct);

            if (!dryRun && report.Changes.Count > 0)
            {
                var now = DateTime.UtcNow;
                var byId = ingredients.ToDictionary(i => i.Id);
                foreach (var c in report.Changes)
                {
                    var ing = byId[c.IngredientId];
                    if (c.Field == DensityField) ing.GramsPerMilliliter = c.Value;
                    else ing.GramsPerItem = c.Value;
                    ing.LastModifiedDate = now;
                    _db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
                    {
                        Action = FoodProposalAction.Update,
                        IngredientId = c.IngredientId,
                        FdcId = c.SourceFdcId,
                        Field = c.Field,
                        CurrentValue = null,
                        ProposedValue = c.Value.ToString(CultureInfo.InvariantCulture),
                        Confidence = 1m,
                        Reason = c.Evidence,
                        Source = $"fdc:{c.SourceFdcId}",
                        Batch = Batch,
                        Status = FoodProposalStatus.Applied,
                        ReviewedAt = now,
                        CreatedDate = now,
                    });
                }
                await _db.SaveChangesAsync(ct);
                await TouchRecipeLinesAsync(report.Changes.Select(c => c.IngredientId).Distinct().ToArray(), ct);
                _logger.LogInformation("FDC portion backfill: {Count} values filled on {Ingredients} ingredients",
                    report.Changes.Count, report.Changes.Select(c => c.IngredientId).Distinct().Count());
            }
            return report;
        }

        /// <summary>
        /// Reverses every value this backfill wrote that is still unchanged, and marks its log row
        /// rejected. A value someone has since edited is left alone.
        /// </summary>
        public async Task<int> UndoAsync(CancellationToken ct = default)
        {
            var applied = await _db.FoodCatalogProposals
                .Where(p => p.Batch == Batch && p.Status == FoodProposalStatus.Applied && p.IngredientId != null)
                .ToListAsync(ct);
            var ids = applied.Select(p => p.IngredientId!.Value).Distinct().ToList();
            var byId = await _db.Ingredients.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

            var now = DateTime.UtcNow;
            var undone = 0;
            var touched = new HashSet<long>();
            foreach (var p in applied)
            {
                if (!byId.TryGetValue(p.IngredientId!.Value, out var ing)
                    || !decimal.TryParse(p.ProposedValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) continue;
                if (p.Field == DensityField && ing.GramsPerMilliliter == value) ing.GramsPerMilliliter = null;
                else if (p.Field == CountField && ing.GramsPerItem == value) ing.GramsPerItem = null;
                else continue;

                ing.LastModifiedDate = now;
                p.Status = FoodProposalStatus.Rejected;
                p.LastModifiedDate = now;
                touched.Add(ing.Id);
                undone++;
            }
            await _db.SaveChangesAsync(ct);
            await TouchRecipeLinesAsync(touched.ToArray(), ct);
            return undone;
        }

        private async Task MeasureImpactAsync(PortionBackfillReport report, CancellationToken ct)
        {
            var lines = await _db.RecipeIngredients
                .AsNoTracking()
                .Where(ri => !ri.Recipe.IsDeleted)
                .Select(ri => new
                {
                    ri.RecipeId,
                    RecipeCurated = ri.Recipe.CurationStatusId == Curated,
                    ri.IngredientId,
                    ri.Quantity,
                    ri.LineKind,
                    ri.Measurement,
                    ri.Ingredient.GramsPerMilliliter,
                    ri.Ingredient.ReferenceServingGrams,
                    ri.Ingredient.GramsPerItem,
                    HasData = ri.Ingredient.IngredientNutrients.Any(),
                })
                .ToListAsync(ct);

            var fills = report.Changes.ToLookup(c => c.IngredientId);
            var before = new List<(long RecipeId, bool Curated)>();
            var after = new List<(long RecipeId, bool Curated)>();
            foreach (var l in lines)
            {
                var line = new RecipeIngredientEntity
                {
                    Quantity = l.Quantity,
                    LineKind = l.LineKind,
                    Measurement = l.Measurement,
                    Ingredient = new IngredientEntity { GramsPerMilliliter = l.GramsPerMilliliter, ReferenceServingGrams = l.ReferenceServingGrams, GramsPerItem = l.GramsPerItem },
                };
                var status = RecipeNutritionService.StatusFor(line, l.HasData);
                var grams = RecipeNutritionService.GramsFor(line);

                foreach (var c in fills[l.IngredientId])
                {
                    if (c.Field == DensityField) line.Ingredient.GramsPerMilliliter = c.Value;
                    else line.Ingredient.GramsPerItem = c.Value;
                }
                if (status == NutritionLineStatus.Counted)
                {
                    if (RecipeNutritionService.GramsFor(line) != grams) report.CountedLinesReweighed++;
                    continue;
                }
                if (status != NutritionLineStatus.NoConversion) continue;
                before.Add((l.RecipeId, l.RecipeCurated));
                if (RecipeNutritionService.StatusFor(line, l.HasData) == NutritionLineStatus.NoConversion)
                    after.Add((l.RecipeId, l.RecipeCurated));
            }

            report.NoConversionLinesBefore = before.Count;
            report.NoConversionLinesAfter = after.Count;
            report.NoConversionRecipesBefore = before.Select(x => x.RecipeId).Distinct().Count();
            report.NoConversionRecipesAfter = after.Select(x => x.RecipeId).Distinct().Count();
            report.CuratedNoConversionRecipesBefore = before.Where(x => x.Curated).Select(x => x.RecipeId).Distinct().Count();
            report.CuratedNoConversionRecipesAfter = after.Where(x => x.Curated).Select(x => x.RecipeId).Distinct().Count();
        }

        private async Task TouchRecipeLinesAsync(long[] ingredientIds, CancellationToken ct)
        {
            if (ingredientIds.Length == 0) return;
            if (_db.Database.IsRelational())
            {
                await _db.Database.ExecuteSqlAsync(
                    $"""UPDATE recipe."RecipeIngredient" SET "LastModifiedDate" = now() WHERE "IngredientId" = ANY({ingredientIds})""", ct);
                return;
            }
            foreach (var row in await _db.RecipeIngredients.Where(ri => ingredientIds.Contains(ri.IngredientId)).ToListAsync(ct))
                row.LastModifiedDate = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        private static string Describe(string fdcId, Dictionary<string, (string DataType, string Description)> foods) =>
            $"USDA {foods[fdcId].DataType} {fdcId} '{foods[fdcId].Description}'";

        private static Dictionary<string, (string DataType, string Description)> ReadFoods(string path)
        {
            var result = new Dictionary<string, (string, string)>();
            using var reader = new StreamReader(path);
            reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length >= 3) result[f[0]] = (f[1], f[2]);
            }
            return result;
        }

        private static string FindFile(string dir, string name)
        {
            if (File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);
            return Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException($"{name} not found under {dir}");
        }
    }

    /// <summary>One conversion the backfill fills, or would have filled over an existing value.</summary>
    public sealed record PortionChange(
        long IngredientId, string Ingredient, string FdcId, string Field,
        decimal? Current, decimal Value, string SourceFdcId, string Evidence, decimal? ReferenceServing = null);

    public sealed class PortionBackfillReport
    {
        public bool DryRun { get; set; }
        public int Linked { get; set; }
        public int NotInFiles { get; set; }
        public List<PortionChange> Changes { get; } = new();
        public List<PortionChange> KeptExisting { get; } = new();
        public int NoConversionLinesBefore { get; set; }
        public int NoConversionLinesAfter { get; set; }
        public int NoConversionRecipesBefore { get; set; }
        public int NoConversionRecipesAfter { get; set; }
        public int CuratedNoConversionRecipesBefore { get; set; }
        public int CuratedNoConversionRecipesAfter { get; set; }
        public int CountedLinesReweighed { get; set; }
    }
}
