using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    public class ScrapedRecipeRepairService : IScrapedRecipeRepairService
    {
        public const string StepSplitChangeType = "StepSplit";

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

            int repaired = 0, unparsed = 0, revetted = 0, cleared = 0, split = 0, decoded = 0;
            foreach (var recipe in recipes)
            {
                var reviewable = recipe.DateCurationCompleted == null
                    && (recipe.CurationStatusId == (long)CurationStatusEnum.RequiresRevision
                        || recipe.CurationStatusId == (long)CurationStatusEnum.NonCurated);

                if (reviewable && DecodeEntities(recipe)) decoded++;
                if (reviewable && SplitSingleStep(recipe)) split++;

                foreach (var row in recipe.RecipeIngredients ?? Enumerable.Empty<Nom.Data.Recipe.RecipeIngredientEntity>())
                {
                    if (row.Quantity != 0) continue;

                    var firstLine = (row.RawLine ?? string.Empty).Split(" + ", 2)[0];
                    var parsed = IngredientLineParser.Parse(firstLine)
                        ?? MethodAmountFinder.Find(firstLine, (recipe.RecipeSteps ?? Enumerable.Empty<RecipeStepEntity>()).Where(st => !st.IsDeleted).Select(st => st.Description ?? string.Empty));
                    if (parsed == null || !measurements.TryGetValue(parsed.MeasurementName, out var measurementId))
                    {
                        unparsed++;
                        continue;
                    }

                    row.Quantity = parsed.Quantity;
                    row.MeasurementId = measurementId;
                    repaired++;
                }

                if (!reviewable) continue;

                revetted++;
                if (await RevetAsync(_vetting, recipe)) cleared++;
            }

            await _context.SaveChangesAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            return new ScrapedRepairBatchResult(recipes.Count, repaired, unparsed, revetted, cleared, recipes[^1].Id, split, decoded);
        }

        /// <summary>
        /// Turns HTML entities the extractor left in the text ("&amp;#8217;", "&amp;nbsp;") back into
        /// characters, so the vetting rule for markup residue only sees real tags and links.
        /// </summary>
        private static bool DecodeEntities(RecipeEntity recipe)
        {
            var changed = false;
            var now = DateTime.UtcNow;
            if (Decode(recipe.Name) is { } name && name != recipe.Name) { recipe.Name = name; changed = true; }
            if (Decode(recipe.Description) is { } description && description != recipe.Description) { recipe.Description = description; changed = true; }
            foreach (var step in (recipe.RecipeSteps ?? Enumerable.Empty<RecipeStepEntity>()).Where(s => !s.IsDeleted))
            {
                if (Decode(step.Description) is { } text && text != step.Description)
                {
                    step.Description = text;
                    step.Summary = RecipeStepSplit.Clamp(text, 255);
                    step.LastModifiedDate = now;
                    step.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                    changed = true;
                }
            }
            if (changed)
            {
                recipe.LastModifiedDate = now;
                recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            }
            return changed;
        }

        private static string? Decode(string? text)
        {
            if (text == null || text.IndexOf('&') < 0) return text;
            var decoded = System.Net.WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');
            return decoded == text ? text : System.Text.RegularExpressions.Regex.Replace(decoded, @"[ \t]{2,}", " ");
        }

        private bool SplitSingleStep(RecipeEntity recipe)
        {
            var steps = (recipe.RecipeSteps ?? Enumerable.Empty<RecipeStepEntity>()).Where(s => !s.IsDeleted).ToList();
            if (steps.Count != 1) return false;

            var original = steps[0].Description ?? string.Empty;
            var accepted = RecipeRepairGrounding.SplitSentences(original);
            if (accepted == null) return false;

            RecipeStepSplit.Apply(recipe, steps[0], accepted);
            recipe.LastModifiedDate = DateTime.UtcNow;
            recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            _context.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = "Recipe",
                EntityId = recipe.Id,
                ChangeType = StepSplitChangeType,
                PropertyName = "RecipeSteps",
                OldValue = RecipeStepSplit.Clamp(original, 4000),
                NewValue = RecipeStepSplit.Clamp(System.Text.Json.JsonSerializer.Serialize(accepted), 4000),
                Timestamp = DateTime.UtcNow,
                ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            return true;
        }

        internal static async Task<bool> RevetAsync(IRecipeVettingService vetting, Nom.Data.Recipe.RecipeEntity recipe)
        {
            var issues = await vetting.VetAsync(ToVettable(recipe));
            var wasFlagged = recipe.CurationStatusId == (long)CurationStatusEnum.RequiresRevision;
            recipe.VettingIssues = issues.Count > 0 ? string.Join("\n", issues) : null;
            recipe.CurationStatusId = issues.Count > 0
                ? (long)CurationStatusEnum.RequiresRevision
                : (long)CurationStatusEnum.NonCurated;
            return wasFlagged && issues.Count == 0;
        }

        internal static ScraperRecipe ToVettable(Nom.Data.Recipe.RecipeEntity recipe) => new()
        {
            Name = recipe.Name,
            PublicDomain = recipe.LicenseStatus == RecipeLicenseStatus.PublicDomain,
            PrepTimeMinutes = recipe.PrepTimeMinutes is long prep ? (int)Math.Min(prep, int.MaxValue) : null,
            CookTimeMinutes = recipe.CookTimeMinutes is long cook ? (int)Math.Min(cook, int.MaxValue) : null,
            RecipeServings = recipe.RecipeServings,
            Ingredients = (recipe.RecipeIngredients ?? Enumerable.Empty<Nom.Data.Recipe.RecipeIngredientEntity>())
                .Select(i => new ScraperIngredient { RawLine = i.RawLine ?? string.Empty, Quantity = i.Quantity > 0 ? i.Quantity : null, LineKind = i.LineKind })
                .ToList(),
            Steps = (recipe.RecipeSteps ?? Enumerable.Empty<Nom.Data.Recipe.RecipeStepEntity>())
                .OrderBy(s => s.StepNumber)
                .Select(s => new ScraperStep { Order = s.StepNumber, Instruction = s.Description ?? string.Empty })
                .ToList(),
        };
    }
}
