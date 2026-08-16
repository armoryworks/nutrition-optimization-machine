using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Nutrition;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Admin review of the imported food catalog plus the proposal pipeline. Nothing here trusts an
    /// automated reviewer: proposals are stored, shown with their provenance, and applied only when
    /// an admin approves them — and a proposal that tries to change a nutrient value without an
    /// authoritative source is rejected at ingest.
    /// </summary>
    public class FoodCatalogReviewService : IFoodCatalogReviewService
    {
        private const long CuratedStatus = 9003;

        private readonly ApplicationDbContext _context;
        private readonly IFoodCatalogAuditService _audit;

        public FoodCatalogReviewService(ApplicationDbContext context, IFoodCatalogAuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        private static readonly string[] CalorieNames = { "energy", "calories", "kcal" };
        private static readonly string[] ProteinNames = { "protein" };
        private static readonly string[] CarbNames = { "carbohydrate", "carbs" };
        private static readonly string[] FatNames = { "total lipid", "fat" };

        public async Task<FoodCatalogPageModel> GetPageAsync(
            string? source, long? status, long? foodGroupId, string? search, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = BuildQuery(source, status, foodGroupId, search);
            var total = await query.CountAsync();

            var rows = await query
                .OrderBy(i => i.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Include(i => i.FoodGroup)
                .Include(i => i.CurationStatus)
                .Include(i => i.IngredientNutrients).ThenInclude(n => n.Nutrient)
                .ToListAsync();

            return new FoodCatalogPageModel
            {
                Total = total,
                Items = rows.Select(ToItem).ToList(),
            };
        }

        private IQueryable<Nom.Data.Recipe.IngredientEntity> BuildQuery(
            string? source, long? status, long? foodGroupId, string? search)
        {
            var query = _context.Ingredients.AsQueryable();

            if (!string.IsNullOrWhiteSpace(source))
            {
                query = source.Equals("authored", StringComparison.OrdinalIgnoreCase)
                    ? query.Where(i => i.FdcId == null)
                    : query.Where(i => i.FdcDataType == source);
            }
            if (status.HasValue)
                query = query.Where(i => i.CurationStatusId == status.Value);
            if (foodGroupId.HasValue)
            {
                query = foodGroupId.Value == 0
                    ? query.Where(i => i.FoodGroupId == null)
                    : query.Where(i => i.FoodGroupId == foodGroupId.Value);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                query = query.Where(i => i.Name.ToLower().Contains(term));
            }
            return query;
        }

        private static FoodCatalogItemModel ToItem(Nom.Data.Recipe.IngredientEntity i) => new()
        {
            Id = i.Id,
            Name = i.Name,
            FdcId = i.FdcId,
            Source = string.IsNullOrEmpty(i.FdcDataType) ? "authored" : i.FdcDataType,
            CurationStatusId = i.CurationStatusId,
            CurationStatus = i.CurationStatus?.Name,
            FoodGroupId = i.FoodGroupId,
            FoodGroupName = i.FoodGroup?.Name,
            IsWholeFood = i.IsWholeFood,
            ReferenceServingGrams = i.ReferenceServingGrams,
            CaloriesPer100g = Amount(i, CalorieNames),
            ProteinPer100g = Amount(i, ProteinNames),
            CarbPer100g = Amount(i, CarbNames),
            FatPer100g = Amount(i, FatNames),
        };

        private static decimal? Amount(Nom.Data.Recipe.IngredientEntity ing, string[] patterns) =>
            ing.IngredientNutrients
                .FirstOrDefault(n => n.Nutrient != null
                    && patterns.Any(p => n.Nutrient.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                ?.Amount;

        public async Task<FoodCatalogItemModel?> UpdateAsync(long id, FoodCatalogUpdateModel model)
        {
            var ing = await _context.Ingredients
                .Include(i => i.FoodGroup).Include(i => i.CurationStatus)
                .Include(i => i.IngredientNutrients).ThenInclude(n => n.Nutrient)
                .FirstOrDefaultAsync(i => i.Id == id);
            if (ing == null) return null;

            if (!string.IsNullOrWhiteSpace(model.Name)) ing.Name = model.Name.Trim();
            if (model.FoodGroupId.HasValue)
                ing.FoodGroupId = model.FoodGroupId.Value == 0 ? null : model.FoodGroupId;
            if (model.IsWholeFood.HasValue) ing.IsWholeFood = model.IsWholeFood;
            if (model.ReferenceServingGrams.HasValue)
                ing.ReferenceServingGrams = model.ReferenceServingGrams.Value <= 0 ? null : model.ReferenceServingGrams;
            if (model.CurationStatusId.HasValue) ing.CurationStatusId = model.CurationStatusId.Value;
            ing.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            if (model.FoodGroupId.HasValue)
                await _context.Entry(ing).Reference(x => x.FoodGroup).LoadAsync();
            if (model.CurationStatusId.HasValue)
                await _context.Entry(ing).Reference(x => x.CurationStatus).LoadAsync();
            return ToItem(ing);
        }

        public async Task<int> SetCurationStatusAsync(IEnumerable<long> ingredientIds, long curationStatusId)
        {
            var ids = ingredientIds.Distinct().ToList();
            if (ids.Count == 0) return 0;

            var rows = await _context.Ingredients.Where(i => ids.Contains(i.Id)).ToListAsync();
            foreach (var r in rows)
            {
                r.CurationStatusId = curationStatusId;
                r.LastModifiedDate = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            return rows.Count;
        }

        public async Task<string> ExportCsvAsync(string? source, long? status, int limit)
        {
            var rows = await BuildQuery(source, status, null, null)
                .OrderBy(i => i.Id).Take(Math.Clamp(limit, 1, 20000))
                .Include(i => i.FoodGroup)
                .Include(i => i.IngredientNutrients).ThenInclude(n => n.Nutrient)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("ingredient_id,fdc_id,gtin_upc,source,name,food_group,is_whole_food,reference_serving_grams,kcal_per_100g,protein_per_100g,carb_per_100g,fat_per_100g");
            foreach (var i in rows)
            {
                sb.AppendLine(string.Join(',',
                    i.Id,
                    Csv(i.FdcId),
                    Csv(i.GtinUpc),
                    Csv(string.IsNullOrEmpty(i.FdcDataType) ? "authored" : i.FdcDataType),
                    Csv(i.Name),
                    Csv(i.FoodGroup?.Name),
                    i.IsWholeFood?.ToString() ?? string.Empty,
                    Num(i.ReferenceServingGrams),
                    Num(Amount(i, CalorieNames)),
                    Num(Amount(i, ProteinNames)),
                    Num(Amount(i, CarbNames)),
                    Num(Amount(i, FatNames))));
            }
            return sb.ToString();
        }

        public async Task<FoodProposalIngestResult> IngestProposalsCsvAsync(string csv, string batch)
        {
            var result = new FoodProposalIngestResult { Batch = batch };
            var lines = (csv ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1) return result;

            var header = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToArray();
            int Col(string n) => Array.IndexOf(header, n);
            int iAction = Col("action"), iIng = Col("ingredient_id"), iFdc = Col("fdc_id"),
                iField = Col("field"), iCur = Col("current_value"), iProp = Col("proposed_value"),
                iConf = Col("confidence"), iReason = Col("reason"), iSource = Col("source");

            foreach (var line in lines.Skip(1))
            {
                var f = SplitCsv(line.TrimEnd('\r'));
                string? At(int idx) => idx >= 0 && idx < f.Length && f[idx].Length > 0 ? f[idx] : null;

                var action = At(iAction)?.Trim().ToLowerInvariant();
                var source = At(iSource);
                var field = At(iField);

                if (!TryParseAction(action, out var parsedAction))
                {
                    Reject(result, "unknown_action");
                    continue;
                }
                if (!ProposalPolicy.IsAllowed(field, source, out var rejection))
                {
                    Reject(result, rejection ?? "not_allowed");
                    continue;
                }

                long? ingredientId = long.TryParse(At(iIng), out var ing) ? ing : null;
                if (parsedAction != FoodProposalAction.Add && ingredientId == null && At(iFdc) == null)
                {
                    Reject(result, "target_required");
                    continue;
                }

                decimal? confidence = decimal.TryParse(At(iConf), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var cf) ? Math.Clamp(cf, 0m, 1m) : null;

                _context.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
                {
                    Action = parsedAction,
                    IngredientId = ingredientId,
                    FdcId = At(iFdc),
                    Field = field,
                    CurrentValue = At(iCur),
                    ProposedValue = At(iProp),
                    Confidence = confidence,
                    Reason = At(iReason),
                    Source = source!,
                    Batch = batch,
                    Status = FoodProposalStatus.Pending,
                    CreatedDate = DateTime.UtcNow,
                });
                result.Accepted++;
            }

            await _context.SaveChangesAsync();
            return result;
        }

        private static void Reject(FoodProposalIngestResult r, string reason)
        {
            r.Rejected++;
            r.RejectedByReason[reason] = r.RejectedByReason.GetValueOrDefault(reason) + 1;
        }

        private static bool TryParseAction(string? action, out FoodProposalAction parsed)
        {
            parsed = action switch
            {
                "update" => FoodProposalAction.Update,
                "flag" => FoodProposalAction.Flag,
                "add" => FoodProposalAction.Add,
                "delete" => FoodProposalAction.Delete,
                _ => default,
            };
            return parsed != default;
        }

        public async Task<List<FoodProposalModel>> GetProposalsAsync(string? batch, string? status, int limit)
        {
            var query = _context.FoodCatalogProposals.Include(p => p.Ingredient).AsQueryable();
            if (!string.IsNullOrWhiteSpace(batch)) query = query.Where(p => p.Batch == batch);
            if (Enum.TryParse<FoodProposalStatus>(status, true, out var st))
                query = query.Where(p => p.Status == st);

            return await query
                .OrderByDescending(p => p.Confidence)
                .ThenBy(p => p.Id)
                .Take(Math.Clamp(limit, 1, 1000))
                .Select(p => new FoodProposalModel
                {
                    Id = p.Id,
                    Action = p.Action.ToString(),
                    IngredientId = p.IngredientId,
                    IngredientName = p.Ingredient != null ? p.Ingredient.Name : null,
                    FdcId = p.FdcId,
                    Field = p.Field,
                    CurrentValue = p.CurrentValue,
                    ProposedValue = p.ProposedValue,
                    Confidence = p.Confidence,
                    Reason = p.Reason,
                    Source = p.Source,
                    Batch = p.Batch,
                    Status = p.Status.ToString(),
                })
                .ToListAsync();
        }

        public async Task<bool> ApplyProposalAsync(long proposalId, long reviewerPersonId)
        {
            var p = await _context.FoodCatalogProposals.FirstOrDefaultAsync(x => x.Id == proposalId);
            if (p == null || p.Status != FoodProposalStatus.Pending) return false;

            // Re-check at apply time: policy may have tightened since ingest.
            if (!ProposalPolicy.IsAllowed(p.Field, p.Source, out _)) return false;

            if (p.Action == FoodProposalAction.Update && p.IngredientId.HasValue)
            {
                var ing = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == p.IngredientId.Value);
                if (ing == null) return false;

                switch (p.Field?.Trim().ToLowerInvariant())
                {
                    case "name":
                        if (!string.IsNullOrWhiteSpace(p.ProposedValue)) ing.Name = p.ProposedValue.Trim();
                        break;
                    case "food_group":
                        ing.FoodGroupId = Nom.Data.Reference.FoodGroupCatalog.TryResolve(p.ProposedValue);
                        break;
                    case "is_whole_food":
                        if (bool.TryParse(p.ProposedValue, out var wf)) ing.IsWholeFood = wf;
                        break;
                    default:
                        return false; // unknown field — never guess
                }
                ing.LastModifiedDate = DateTime.UtcNow;
            }
            else if (p.Action == FoodProposalAction.Delete && p.IngredientId.HasValue)
            {
                var ing = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == p.IngredientId.Value);
                if (ing == null) return false;
                var referenced = await _context.RecipeIngredients.AnyAsync(ri => ri.IngredientId == ing.Id);
                if (referenced) return false; // never remove something a recipe uses
                ing.IsDeleted = true;
                ing.DeletedAt = DateTime.UtcNow;
            }
            // Flag and Add are informational: approving records the decision without mutating the
            // catalog. Adds are fulfilled by an FDC import keyed on the proposal's FdcId.

            p.Status = FoodProposalStatus.Applied;
            p.ReviewedByPersonId = reviewerPersonId;
            p.ReviewedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RejectProposalAsync(long proposalId, long reviewerPersonId)
        {
            var p = await _context.FoodCatalogProposals.FirstOrDefaultAsync(x => x.Id == proposalId);
            if (p == null || p.Status != FoodProposalStatus.Pending) return false;
            p.Status = FoodProposalStatus.Rejected;
            p.ReviewedByPersonId = reviewerPersonId;
            p.ReviewedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        private static string Csv(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var needsQuotes = s.Contains(',') || s.Contains('"') || s.Contains('\n');
            var escaped = s.Replace("\"", "\"\"");
            return needsQuotes ? $"\"{escaped}\"" : escaped;
        }

        private static string Num(decimal? d) =>
            d?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

        private static string[] SplitCsv(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(ch);
                }
                else if (ch == '"') inQuotes = true;
                else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }
    }
}
