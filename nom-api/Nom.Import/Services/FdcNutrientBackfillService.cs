using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;

namespace Nom.Import.Services
{
    /// <summary>
    /// Fills the <see cref="FdcNutrients"/> values onto ingredients that are already linked to a
    /// USDA food (by FdcId), reading one FDC bulk CSV directory (Foundation, SR Legacy or Branded;
    /// FDC ids are unique across them). Only nutrients an ingredient has no row for are written,
    /// so a stored value is never overwritten and a rerun writes nothing. Values come only from
    /// the FDC file and pass <see cref="FoodDataQualityValidator.CheckComponent"/>.
    /// Run: <c>dotnet run -- --backfill-nutrients &lt;csv-dir&gt; [--dry-run]</c>.
    /// </summary>
    public class FdcNutrientBackfillService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<FdcNutrientBackfillService> _logger;
        private readonly FoodDataQualityValidator _validator = new();

        public FdcNutrientBackfillService(ApplicationDbContext db, ILogger<FdcNutrientBackfillService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <param name="dryRun">Report what would be written without saving anything.</param>
        public async Task<BackfillReport> BackfillAsync(string csvDir, bool dryRun, CancellationToken ct = default)
        {
            var nutrientCsv = FindFile(csvDir, "food_nutrient.csv");
            var report = new BackfillReport { DryRun = dryRun };

            var targets = await FdcNutrients.ResolveAsync(_db, _logger, ct);
            report.UnresolvedNutrients.AddRange(FdcNutrients.All.Select(s => s.Key).Where(k => !targets.ContainsKey(k)));

            var linked = await _db.Ingredients
                .Where(i => i.FdcId != null && !i.IsDeleted)
                .Select(i => new { i.Id, i.FdcId })
                .ToListAsync(ct);
            report.LinkedIngredients = linked.Count;
            var byFdc = linked.GroupBy(i => i.FdcId!).ToDictionary(g => g.Key, g => g.Select(i => i.Id).ToList());

            _logger.LogInformation("Reading {File} for {Count} FDC-linked ingredients…", nutrientCsv, linked.Count);
            var foods = FdcNutrients.Read(nutrientCsv, byFdc.Keys.ToHashSet());

            var have = (await _db.Set<IngredientNutrientEntity>()
                .Where(n => n.Ingredient.FdcId != null)
                .Select(n => new { n.IngredientId, n.NutrientId })
                .ToListAsync(ct))
                .Select(n => (n.IngredientId, n.NutrientId)).ToHashSet();

            var now = DateTime.UtcNow;
            var filled = new HashSet<long>();
            foreach (var (fdcId, food) in foods)
            {
                ct.ThrowIfCancellationRequested();
                var accepted = FdcNutrients.Accepted(food, _validator, r =>
                    report.DroppedValuesByReason[r] = report.DroppedValuesByReason.GetValueOrDefault(r) + 1).ToList();
                foreach (var ingredientId in byFdc[fdcId])
                {
                    report.MatchedInFile++;
                    foreach (var (key, amount) in accepted)
                    {
                        if (!targets.TryGetValue(key, out var t) || !have.Add((ingredientId, t.NutrientId))) continue;
                        if (!dryRun)
                        {
                            _db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity
                            {
                                IngredientId = ingredientId,
                                NutrientId = t.NutrientId,
                                Amount = amount,
                                MeasurementId = t.MeasurementId,
                                CreatedDate = now,
                            });
                        }
                        report.RowsByNutrient[key] = report.RowsByNutrient.GetValueOrDefault(key) + 1;
                        report.Rows++;
                        filled.Add(ingredientId);
                    }
                }
            }
            report.IngredientsFilled = filled.Count;

            if (!dryRun) await _db.SaveChangesAsync(ct);
            _logger.LogInformation("FDC nutrient backfill{DryRun}: {Rows} rows on {Ingredients} ingredients.",
                dryRun ? " (dry run)" : string.Empty, report.Rows, report.IngredientsFilled);
            return report;
        }

        private static string FindFile(string dir, string name)
        {
            var direct = Path.Combine(dir, name);
            if (File.Exists(direct)) return direct;
            return Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException($"{name} not found under {dir}");
        }

        public sealed class BackfillReport
        {
            public bool DryRun { get; set; }
            public int LinkedIngredients { get; set; }
            public int MatchedInFile { get; set; }
            public int IngredientsFilled { get; set; }
            public int Rows { get; set; }
            public Dictionary<string, int> RowsByNutrient { get; } = new();
            public Dictionary<string, int> DroppedValuesByReason { get; } = new();
            public List<string> UnresolvedNutrients { get; } = new();
        }
    }
}
