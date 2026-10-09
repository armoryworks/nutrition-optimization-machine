using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    public class ScrapedRecipeRepairService : IScrapedRecipeRepairService
    {
        private readonly ApplicationDbContext _context;
        private readonly IRecipeVettingService _vetting;
        private Dictionary<string, long>? _measurementIds;

        public ScrapedRecipeRepairService(ApplicationDbContext context, IRecipeVettingService vetting)
        {
            _context = context;
            _vetting = vetting;
        }

        public async Task<ScrapedRepairBatchResult> RepairBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default)
        {
            var measurements = _measurementIds ??= await _context.Measurements
                .AsNoTracking()
                .Where(m => !m.IsDeleted)
                .GroupBy(m => m.Name)
                .Select(g => new { Name = g.Key, Id = g.Min(m => m.Id) })
                .ToDictionaryAsync(m => m.Name, m => m.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var query = _context.Recipes
                .Where(r => r.Id > afterRecipeId && r.ScrapedAtUtc != null && !r.IsDeleted)
                .OrderBy(r => r.Id)
                .Take(batchSize)
                .Include(r => r.RecipeIngredients)
                .Include(r => r.RecipeSteps);
            var recipes = _context.Database.IsRelational()
                ? await query.AsSplitQuery().ToListAsync(cancellationToken)
                : await query.ToListAsync(cancellationToken);
            if (recipes.Count == 0) return new ScrapedRepairBatchResult(0, 0, 0, 0, 0, afterRecipeId);

            int repaired = 0, unparsed = 0, revetted = 0, cleared = 0;
            foreach (var recipe in recipes)
            {
                foreach (var row in recipe.RecipeIngredients ?? Enumerable.Empty<Nom.Data.Recipe.RecipeIngredientEntity>())
                {
                    if (row.Quantity != 0) continue;

                    var firstLine = (row.RawLine ?? string.Empty).Split(" + ", 2)[0];
                    var parsed = IngredientLineParser.Parse(firstLine);
                    if (parsed == null || !measurements.TryGetValue(parsed.MeasurementName, out var measurementId))
                    {
                        unparsed++;
                        continue;
                    }

                    row.Quantity = parsed.Quantity;
                    row.MeasurementId = measurementId;
                    repaired++;
                }

                var reviewable = recipe.DateCurationCompleted == null
                    && (recipe.CurationStatusId == (long)CurationStatusEnum.RequiresRevision
                        || recipe.CurationStatusId == (long)CurationStatusEnum.NonCurated);
                if (!reviewable) continue;

                var issues = await _vetting.VetAsync(ToVettable(recipe));
                revetted++;
                var wasFlagged = recipe.CurationStatusId == (long)CurationStatusEnum.RequiresRevision;
                recipe.VettingIssues = issues.Count > 0 ? string.Join("\n", issues) : null;
                recipe.CurationStatusId = issues.Count > 0
                    ? (long)CurationStatusEnum.RequiresRevision
                    : (long)CurationStatusEnum.NonCurated;
                if (wasFlagged && issues.Count == 0) cleared++;
            }

            await _context.SaveChangesAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            return new ScrapedRepairBatchResult(recipes.Count, repaired, unparsed, revetted, cleared, recipes[^1].Id);
        }

        private static ScraperRecipe ToVettable(Nom.Data.Recipe.RecipeEntity recipe) => new()
        {
            Name = recipe.Name,
            PrepTimeMinutes = recipe.PrepTimeMinutes is long prep ? (int)Math.Min(prep, int.MaxValue) : null,
            CookTimeMinutes = recipe.CookTimeMinutes is long cook ? (int)Math.Min(cook, int.MaxValue) : null,
            RecipeServings = recipe.RecipeServings,
            Ingredients = (recipe.RecipeIngredients ?? Enumerable.Empty<Nom.Data.Recipe.RecipeIngredientEntity>())
                .Select(i => new ScraperIngredient { RawLine = i.RawLine ?? string.Empty, Quantity = i.Quantity > 0 ? i.Quantity : null })
                .ToList(),
            Steps = (recipe.RecipeSteps ?? Enumerable.Empty<Nom.Data.Recipe.RecipeStepEntity>())
                .OrderBy(s => s.StepNumber)
                .Select(s => new ScraperStep { Order = s.StepNumber, Instruction = s.Description ?? string.Empty })
                .ToList(),
        };
    }
}
