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
        private readonly ICatalogCleanupService _cleanup;

        public const string FdcLinkField = "fdc_link";
        public const string FdcAttachField = "fdc_attach";

        public FoodCatalogReviewService(ApplicationDbContext context, IFoodCatalogAuditService audit, ICatalogCleanupService cleanup)
        {
            _context = context;
            _cleanup = cleanup;
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

            if (p.Action == FoodProposalAction.Update && p.IngredientId.HasValue
                && string.Equals(p.Field?.Trim(), FdcLinkField, StringComparison.OrdinalIgnoreCase))
            {
                if (!await ApplyFdcLinkAsync(p, reviewerPersonId)) return false;
            }
            else if (p.Action == FoodProposalAction.Update && p.IngredientId.HasValue
                && string.Equals(p.Field?.Trim(), FdcAttachField, StringComparison.OrdinalIgnoreCase))
            {
                if (!await ApplyFdcAttachAsync(p, reviewerPersonId)) return false;
            }
            else if (p.Action == FoodProposalAction.Update && p.IngredientId.HasValue)
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

        public async Task<FoodProposalBatchResult> ApplyProposalsAsync(IEnumerable<long> proposalIds, long reviewerPersonId)
        {
            var result = new FoodProposalBatchResult();
            foreach (var id in proposalIds.Distinct())
            {
                if (await ApplyProposalAsync(id, reviewerPersonId)) result.Applied++;
                else result.Skipped++;
                _context.ChangeTracker.Clear();
            }
            return result;
        }

        /// <summary>
        /// Links a catalog ingredient to the USDA food the proposal names. When recipes are its only
        /// users, the ingredient is merged into the USDA food (recipes re-pointed, its name kept as an
        /// alias so future imports land on the USDA food). Otherwise it stays and copies the USDA
        /// food's nutrient values (source fdc:&lt;id&gt;) for the nutrients it lacks. Approving a link
        /// also curates the USDA food, since a reviewer just looked at it.
        /// </summary>
        private async Task<bool> ApplyFdcLinkAsync(FoodCatalogProposalEntity p, long reviewerPersonId)
        {
            if (!long.TryParse(p.ProposedValue, out var targetId)) return false;
            var source = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == p.IngredientId!.Value && !i.IsDeleted);
            var target = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == targetId && !i.IsDeleted);
            if (source == null || target == null || target.FdcId == null || target.FdcId != p.FdcId) return false;

            target.CurationStatusId = (long)Nom.Data.Recipe.CurationStatusEnum.Curated;
            target.LastModifiedDate = DateTime.UtcNow;
            target.LastModifiedByPersonId = reviewerPersonId;
            var sourceName = source.Name;
            await _context.SaveChangesAsync();

            if (await _cleanup.MergeIntoAsync(source.Id, target.Id, reviewerPersonId, ignoreProposals: true))
            {
                var friendly = FriendlyName(sourceName);
                if (target.Name.Contains(',') && friendly != null
                    && !await _context.Ingredients.AnyAsync(i => i.Id != source.Id && i.Id != target.Id && i.Name.ToLower() == friendly.ToLower()))
                {
                    var usdaName = target.Name;
                    source.Name = Truncate($"{sourceName} (merged into #{target.Id})", 2000);
                    await _context.SaveChangesAsync();
                    target.Name = friendly;
                    await AddAliasAsync(target.Id, usdaName, reviewerPersonId);
                }
                if (!string.Equals(target.Name, sourceName, StringComparison.OrdinalIgnoreCase))
                {
                    await AddAliasAsync(target.Id, sourceName, reviewerPersonId);
                }
                return true;
            }

            var have = await _context.IngredientNutrients
                .Where(n => n.IngredientId == source.Id)
                .Select(n => n.NutrientId)
                .ToListAsync();
            var copies = await _context.IngredientNutrients
                .AsNoTracking()
                .Where(n => n.IngredientId == target.Id && !have.Contains(n.NutrientId))
                .ToListAsync();
            foreach (var n in copies)
            {
                _context.IngredientNutrients.Add(new Nom.Data.Nutrient.IngredientNutrientEntity
                {
                    IngredientId = source.Id,
                    NutrientId = n.NutrientId,
                    Amount = n.Amount,
                    MeasurementId = n.MeasurementId,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = reviewerPersonId,
                });
            }
            if (source.ReferenceServingGrams == null && target.ReferenceServingGrams != null)
            {
                source.ReferenceServingGrams = target.ReferenceServingGrams;
            }
            source.GramsPerMilliliter ??= target.GramsPerMilliliter;
            source.LastModifiedDate = DateTime.UtcNow;
            if (_context.Database.IsRelational())
            {
                await _context.Database.ExecuteSqlAsync(
                    $"""UPDATE recipe."RecipeIngredient" SET "LastModifiedDate" = now() WHERE "IngredientId" = {source.Id}""");
            }
            else
            {
                foreach (var row in await _context.RecipeIngredients.Where(ri => ri.IngredientId == source.Id).ToListAsync())
                {
                    row.LastModifiedDate = DateTime.UtcNow;
                }
            }
            return true;
        }

        /// <summary>
        /// Gives a catalog ingredient the USDA identity and values of the same-named food the
        /// importer found (carried in the proposal, source fdc:&lt;id&gt;). Only fills what is missing.
        /// </summary>
        private async Task<bool> ApplyFdcAttachAsync(FoodCatalogProposalEntity p, long reviewerPersonId)
        {
            var ingredient = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == p.IngredientId!.Value && !i.IsDeleted);
            if (ingredient == null || string.IsNullOrWhiteSpace(p.ProposedValue) || string.IsNullOrWhiteSpace(p.FdcId)) return false;
            if (ingredient.FdcId != null && ingredient.FdcId != p.FdcId) return false;

            using var doc = System.Text.Json.JsonDocument.Parse(p.ProposedValue);
            var root = doc.RootElement;
            var have = await _context.IngredientNutrients
                .Where(n => n.IngredientId == ingredient.Id)
                .Select(n => n.NutrientId)
                .ToListAsync();
            if (root.TryGetProperty("nutrients", out var facts))
            {
                foreach (var f in facts.EnumerateArray())
                {
                    var nutrientId = f.GetProperty("nutrientId").GetInt64();
                    if (have.Contains(nutrientId)) continue;
                    _context.IngredientNutrients.Add(new Nom.Data.Nutrient.IngredientNutrientEntity
                    {
                        IngredientId = ingredient.Id,
                        NutrientId = nutrientId,
                        Amount = f.GetProperty("amount").GetDecimal(),
                        MeasurementId = f.GetProperty("measurementId").GetInt64(),
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = reviewerPersonId,
                    });
                }
            }

            ingredient.FdcId ??= p.FdcId;
            if (ingredient.FdcDataType == null && root.TryGetProperty("fdcDataType", out var dt) && dt.ValueKind == System.Text.Json.JsonValueKind.String)
                ingredient.FdcDataType = dt.GetString();
            if (ingredient.ReferenceServingGrams == null && root.TryGetProperty("referenceServingGrams", out var rs) && rs.ValueKind == System.Text.Json.JsonValueKind.Number)
                ingredient.ReferenceServingGrams = rs.GetDecimal();
            if (ingredient.GramsPerMilliliter == null && root.TryGetProperty("gramsPerMilliliter", out var gml) && gml.ValueKind == System.Text.Json.JsonValueKind.Number)
                ingredient.GramsPerMilliliter = gml.GetDecimal();
            ingredient.LastModifiedDate = DateTime.UtcNow;
            ingredient.LastModifiedByPersonId = reviewerPersonId;

            if (_context.Database.IsRelational())
            {
                await _context.Database.ExecuteSqlAsync(
                    $"""UPDATE recipe."RecipeIngredient" SET "LastModifiedDate" = now() WHERE "IngredientId" = {ingredient.Id}""");
            }
            return true;
        }

        private async Task AddAliasAsync(long ingredientId, string alias, long reviewerPersonId)
        {
            var lowered = alias.ToLowerInvariant();
            var exists = await _context.IngredientAliases.AnyAsync(a => a.IngredientId == ingredientId && a.AliasName.ToLower() == lowered && !a.IsDeleted)
                || _context.IngredientAliases.Local.Any(a => a.IngredientId == ingredientId && a.AliasName.ToLower() == lowered);
            if (exists) return;
            _context.IngredientAliases.Add(new Nom.Data.Recipe.IngredientAliasEntity
            {
                IngredientId = ingredientId,
                AliasName = alias,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = reviewerPersonId,
            });
        }

        private static readonly System.Text.RegularExpressions.Regex PrepWords = new(
            @"\b(chopped|diced|minced|sliced|grated|shredded|crushed|melted|softened|divided|optional|taste|peeled|cubed|beaten|packed|drained|rinsed|halved|quartered|thinly|finely|roughly)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// The everyday name a USDA food should go by after a link ("Olive Oil" rather than
        /// "Oil, olive, salad or cooking"), or null when the linked name is too messy to adopt.
        /// </summary>
        public static string? FriendlyName(string name)
        {
            var trimmed = name.Trim();
            if (trimmed.Length < 2 || trimmed.Any(char.IsDigit) || trimmed.IndexOfAny(new[] { ',', '(', ')', '/', ';', ':' }) >= 0) return null;
            var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 4 || PrepWords.IsMatch(trimmed)) return null;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(string.Join(' ', words).ToLowerInvariant());
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

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
