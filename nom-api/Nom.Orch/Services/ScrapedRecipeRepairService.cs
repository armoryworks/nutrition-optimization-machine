using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    public class ScrapedRecipeRepairService : IScrapedRecipeRepairService
    {
        public const string StepSplitChangeType = "StepSplit";
        public const string FillChangeType = "StartupFill";
        public const string LineEntityType = "RecipeIngredient";

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
            var held = await RecipeLineHold.HeldAsync(_context, recipes.Select(r => r.Id).ToList(), cancellationToken);
            var rebuilt = await RecipeReprocessService.RebuiltRecipesAsync(_context, recipes.Select(r => r.Id).ToList(), cancellationToken);
            var now = DateTime.UtcNow;
            var foodIds = recipes.SelectMany(r => r.RecipeIngredients ?? Enumerable.Empty<Nom.Data.Recipe.RecipeIngredientEntity>())
                .Where(ri => ri.Quantity == 0 || (measurements.TryGetValue(IngredientLineParser.Piece, out var piece) && ri.MeasurementId == piece))
                .Select(ri => ri.IngredientId).Distinct().ToList();
            var foodNames = await _context.Ingredients.IgnoreQueryFilters().AsNoTracking()
                .Where(i => foodIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Name, i.GramsPerItem })
                .ToDictionaryAsync(i => i.Id, i => (i.Name, Counted: i.GramsPerItem > 0), cancellationToken);

            int repaired = 0, unparsed = 0, revetted = 0, cleared = 0, split = 0, decoded = 0;
            foreach (var recipe in recipes)
            {
                if (rebuilt.Contains(recipe.Id)) continue;
                var reviewable = recipe.DateCurationCompleted == null
                    && (recipe.CurationStatusId == (long)CurationStatusEnum.RequiresRevision
                        || recipe.CurationStatusId == (long)CurationStatusEnum.NonCurated);

                if (reviewable && DecodeEntities(recipe)) decoded++;
                if (reviewable && SplitSingleStep(recipe)) split++;

                var rows = (recipe.RecipeIngredients ?? Enumerable.Empty<Nom.Data.Recipe.RecipeIngredientEntity>()).Where(ri => !ri.IsDeleted).ToList();
                if (held.Contains(recipe.Id)) rows.Clear();
                foreach (var row in rows)
                {
                    var (was, wasUnit, wasModified) = (row.Quantity, row.MeasurementId, row.LastModifiedDate);
                    if (!Fill(recipe, row, rows, measurements, foodNames)) unparsed++;
                    if (row.Quantity == was && row.MeasurementId == wasUnit)
                    {
                        row.LastModifiedDate = wasModified;
                        continue;
                    }

                    Journal(recipe.Id, row, was, wasUnit, now);
                    repaired++;
                }

                if (!reviewable) continue;

                revetted++;
                if (await RevetAsync(_vetting, recipe)) cleared++;
            }

            await _context.SaveChangesAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            return new ScrapedRepairBatchResult(recipes.Count, repaired, unparsed, revetted, cleared, recipes[^1].Id, split, decoded, held.Except(rebuilt).Count(), rebuilt.Count);
        }

        /// <summary>
        /// Gives one line the amount its text, or the method, states: a piece count whose text states
        /// a unit takes that unit, and a line with no amount is filled. False when a line left without
        /// an amount could not be read. The caller journals only a net change, so a count that is read
        /// back as the same count ("one of rosemary": elliptical, then 1 piece) writes nothing.
        /// </summary>
        private static bool Fill(RecipeEntity recipe, Nom.Data.Recipe.RecipeIngredientEntity row, List<Nom.Data.Recipe.RecipeIngredientEntity> rows,
            Dictionary<string, long> measurements, Dictionary<long, (string Name, bool Counted)> foods)
        {
            var firstLine = (row.RawLine ?? string.Empty).Split(" + ", 2)[0];
            if (row.Quantity != 0 && (!RereadPieces(row, firstLine, measurements) || row.Quantity != 0)) return true;
            var known = foods.TryGetValue(row.IngredientId, out var food);
            if (known && !FoodNameMatcher.NamesFood(firstLine, food.Name)) return false;

            var others = rows.Where(other => !ReferenceEquals(other, row)).ToList();
            var parsed = IngredientLineParser.Parse(firstLine)
                ?? (others.Any(other => other.IngredientId == row.IngredientId) || (row.RawLine ?? string.Empty).Contains(FoldedLine.Separator, StringComparison.Ordinal)
                    ? null
                    : MethodAmountFinder.Find(
                        firstLine,
                        (recipe.RecipeSteps ?? Enumerable.Empty<RecipeStepEntity>()).Where(st => !st.IsDeleted).Select(st => st.Description ?? string.Empty),
                        others.Select(other => other.RawLine ?? string.Empty),
                        countedInPieces: known && food.Counted));
            if (parsed == null || !measurements.TryGetValue(parsed.MeasurementName, out var measurementId)) return false;

            row.Quantity = parsed.Quantity;
            row.MeasurementId = measurementId;
            return true;
        }

        private void Journal(long recipeId, Nom.Data.Recipe.RecipeIngredientEntity row, decimal quantity, long measurementId, DateTime now)
        {
            row.LastModifiedDate = now;
            row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            _context.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = LineEntityType,
                EntityId = recipeId,
                ChangeType = FillChangeType,
                PropertyName = RecipeLineClassificationService.LinePropertyName(row.IngredientId, row.SortOrder),
                OldValue = PackageLineService.Amount(row.IngredientId, row.SortOrder, quantity, measurementId),
                NewValue = PackageLineService.Amount(row.IngredientId, row.SortOrder, row.Quantity, row.MeasurementId),
                Timestamp = now,
                ChangedByPersonId = SystemConstants.SystemPersonId,
            });
        }

        public async Task<StartupFillUndoResult> UndoFillsAsync(DateTime sinceUtc, bool apply = false)
        {
            var entries = await _context.AuditLogEntries
                .Where(a => a.EntityType == LineEntityType && a.ChangeType == FillChangeType && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            var result = new StartupFillUndoResult { Applied = apply, Entries = entries.Count };

            foreach (var chunk in entries.Chunk(500))
            {
                await using var tx = apply && _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync() : null;
                var recipeIds = chunk.Select(e => e.EntityId).Distinct().ToList();
                var rebuilt = await RecipeReprocessService.RebuiltAtAsync(_context, recipeIds);
                var rows = (await _context.RecipeIngredients.Where(ri => recipeIds.Contains(ri.RecipeId)).ToListAsync())
                    .ToDictionary(ri => (ri.RecipeId, ri.IngredientId, ri.SortOrder));
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (rebuilt.TryGetValue(entry.EntityId, out var rebuiltAt) && rebuiltAt > entry.Timestamp)
                    {
                        result.Skipped++;
                        result.SkippedRebuilt++;
                        continue;
                    }

                    if (PackageLineService.ReadAmount(entry.OldValue) is not { } was || PackageLineService.ReadAmount(entry.NewValue) is not { } became
                        || !rows.TryGetValue((entry.EntityId, became.IngredientId, became.SortOrder), out var row)
                        || row.Quantity != became.Quantity || row.MeasurementId != became.MeasurementId)
                    {
                        result.Skipped++;
                        continue;
                    }

                    result.Restored++;
                    if (!apply) continue;
                    row.Quantity = was.Quantity;
                    row.MeasurementId = was.MeasurementId;
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                    _context.AuditLogEntries.Remove(entry);
                }

                if (apply)
                {
                    await _context.SaveChangesAsync();
                    if (tx != null) await tx.CommitAsync();
                }
                _context.ChangeTracker.Clear();
            }

            return result;
        }

        /// <summary>
        /// A row counted in pieces whose line states a real unit the parser has since learned
        /// ("2 qts. water", "1 teacup butter", "3 level teaspoons soda") takes that unit; a row whose
        /// number borrowed its unit from the line before ("two of butter") goes back to unquantified.
        /// A row whose line reads as a can or package ("1 (15 oz) can beans", "1 jar marinara") is
        /// left as it is: <see cref="IPackageLineService"/> re-reads those with a journal entry each.
        /// </summary>
        private static bool RereadPieces(Nom.Data.Recipe.RecipeIngredientEntity row, string firstLine, Dictionary<string, long> measurements)
        {
            if (!measurements.TryGetValue(IngredientLineParser.Piece, out var pieceId) || row.MeasurementId != pieceId) return false;
            if (IngredientLineParser.ReadsPackage(firstLine)) return false;

            if (IngredientLineParser.Parse(firstLine) is { MeasurementName: not IngredientLineParser.Piece } reread
                && measurements.TryGetValue(reread.MeasurementName, out var measurementId))
            {
                row.Quantity = reread.Quantity;
                row.MeasurementId = measurementId;
            }
            else if (IngredientLineParser.IsElliptical(firstLine))
            {
                row.Quantity = 0;
            }
            else
            {
                return false;
            }
            row.LastModifiedDate = DateTime.UtcNow;
            return true;
        }

        /// <summary>
        /// Turns HTML entities the extractor left in the text ("&amp;#8217;", "&amp;nbsp;") back into
        /// characters and drops bare tags ("&lt;span class="fn"&gt;"), so the vetting rule for markup
        /// residue only sees what a person has to look at: scripts, links and broken fragments.
        /// </summary>
        private static bool DecodeEntities(RecipeEntity recipe)
        {
            var changed = false;
            var now = DateTime.UtcNow;
            if (Decode(recipe.Name) is { } name && name != recipe.Name) { recipe.Name = name; changed = true; }
            if (Decode(recipe.Description) is { } description && description != recipe.Description) { recipe.Description = description; changed = true; }
            var steps = (recipe.RecipeSteps ?? Enumerable.Empty<RecipeStepEntity>()).Where(s => !s.IsDeleted).OrderBy(s => s.StepNumber).ToList();
            var dropped = false;
            foreach (var step in steps)
            {
                var text = Decode(step.Description);
                if (text == null || text == step.Description) continue;
                if (!RealWord.IsMatch(text))
                {
                    step.IsDeleted = true;
                    step.DeletedAt = now;
                    step.DeletedByPersonId = SystemConstants.SystemPersonId;
                    dropped = true;
                }
                else
                {
                    step.Description = text;
                    step.Summary = RecipeStepSplit.Clamp(text, 255);
                    step.LastModifiedDate = now;
                    step.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                }
                changed = true;
            }
            if (dropped)
            {
                var number = 1;
                foreach (var step in steps.Where(s => !s.IsDeleted)) step.StepNumber = number++;
            }
            if (changed)
            {
                recipe.LastModifiedDate = now;
                recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            }
            return changed;
        }

        private static readonly System.Text.RegularExpressions.Regex Tag = new(
            @"</?(?:span|div|p|b|i|em|strong|u|br|a|font|h[1-6]|ul|ol|li|table|tr|td|th|tbody|thead|section|article|header|footer)\b[^<>]*>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static readonly System.Text.RegularExpressions.Regex Residue = new(
            @"\s*(?:\d+\.\s*)?(?:note:\s*)?view\s+nutrition\s+facts\.?\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static readonly System.Text.RegularExpressions.Regex RealWord = new(
            @"\p{L}{3,}", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static string? Decode(string? text)
        {
            if (text == null) return text;
            var decoded = text.IndexOf('&') < 0 && text.IndexOf('<') < 0 ? text : System.Net.WebUtility.HtmlDecode(Tag.Replace(text, " ")).Replace('\u00a0', ' ');
            decoded = Residue.Replace(decoded, string.Empty);
            return decoded == text ? text : System.Text.RegularExpressions.Regex.Replace(decoded, @"[ \t]{2,}", " ").Trim();
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
