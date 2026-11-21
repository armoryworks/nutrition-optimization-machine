using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class LinkAutoApplyService : ILinkAutoApplyService
    {
        private const int ReasonLength = 2047;
        private static readonly TimeSpan CatalogLifetime = TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;

        public LinkAutoApplyService(ApplicationDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public async Task<IReadOnlyList<long>> PendingNameMatchesAsync(int count, IReadOnlyCollection<long> skip, IReadOnlyCollection<string> excludeTargets, CancellationToken cancellationToken = default,
            int perTargetCap = int.MaxValue, IReadOnlyDictionary<string, int>? appliedByTarget = null)
        {
            var pending = await _db.FoodCatalogProposals
                .Where(p => p.Status == FoodProposalStatus.Pending && p.IngredientId != null && !skip.Contains(p.Id)
                    && p.Field == FoodCatalogReviewService.FdcLinkField && p.Source == IngredientLinkService.ExactSource
                    && !excludeTargets.Contains(p.FdcId!)
                    && (p.Reason == null || !p.Reason.Contains(LinkAutoApplyGuard.HoldMark)))
                .OrderByDescending(p => _db.RecipeIngredients.Count(ri => ri.IngredientId == p.IngredientId))
                .ThenBy(p => p.Id)
                .Select(p => new { p.Id, p.FdcId })
                .Take(count)
                .ToListAsync(cancellationToken);
            var taken = new Dictionary<string, int>(appliedByTarget ?? new Dictionary<string, int>());
            var ids = new List<long>();
            foreach (var p in pending)
            {
                var key = p.FdcId ?? string.Empty;
                var already = taken.GetValueOrDefault(key);
                if (already >= perTargetCap) continue;
                taken[key] = already + 1;
                ids.Add(p.Id);
            }
            return ids;
        }

        public async Task<LinkApplyAllowance> AllowanceAsync(string sourcePrefix, int dailyCap, int perTargetCap, CancellationToken cancellationToken = default)
        {
            var since = DateTime.UtcNow.AddDays(-1);
            var applied = await _db.FoodCatalogProposals
                .Where(p => p.Status == FoodProposalStatus.Applied && p.Field == FoodCatalogReviewService.FdcLinkField && p.Source.StartsWith(sourcePrefix)
                    && p.ReviewedByPersonId == SystemConstants.SystemPersonId && p.ReviewedAt >= since && p.FdcId != null)
                .GroupBy(p => p.FdcId!)
                .Select(g => new { FdcId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var total = applied.Sum(a => a.Count);
            return new LinkApplyAllowance(
                Math.Max(0, dailyCap - total),
                applied.Where(a => a.Count >= perTargetCap).Select(a => a.FdcId).ToList(),
                applied.ToDictionary(a => a.FdcId, a => a.Count),
                total);
        }

        public async Task<LinkScreenResult> ScreenAsync(IReadOnlyList<long> proposalIds, CancellationToken cancellationToken = default)
        {
            var proposals = await _db.FoodCatalogProposals.Where(p => proposalIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
            var picks = await PicksAsync(proposals.Values.Where(Screened).ToList(), cancellationToken);
            var catalog = picks.Count == 0 ? null : await CatalogAsync(cancellationToken);
            var allowed = new List<long>();
            var held = new List<long>();
            foreach (var id in proposalIds)
            {
                if (!proposals.TryGetValue(id, out var p)) continue;
                if (!Screened(p))
                {
                    allowed.Add(id);
                    continue;
                }

                if (LinkAutoApplyGuard.IsMarked(p.Reason))
                {
                    held.Add(id);
                    continue;
                }

                var hold = picks.TryGetValue(id, out var pick) ? LinkGate.Hold(pick, catalog!) : "target food not found";
                if (hold == null)
                {
                    allowed.Add(id);
                    continue;
                }

                var reason = (p.Reason ?? string.Empty) + LinkAutoApplyGuard.HoldMark + hold;
                p.Reason = reason.Length > ReasonLength ? reason[..ReasonLength] : reason;
                p.LastModifiedDate = DateTime.UtcNow;
                held.Add(id);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return new LinkScreenResult(allowed, held);
        }

        public async Task<LinkHoldReport> HoldReportAsync(int sampleSize = 50, CancellationToken cancellationToken = default)
        {
            var pending = await _db.FoodCatalogProposals.AsNoTracking()
                .Where(p => p.Status == FoodProposalStatus.Pending && p.Field == FoodCatalogReviewService.FdcLinkField
                    && p.IngredientId != null && p.ProposedValue != null && p.FdcId != null
                    && (p.Source == IngredientLinkService.ExactSource || p.Source.StartsWith("ai:")))
                .OrderByDescending(p => _db.RecipeIngredients.Count(ri => ri.IngredientId == p.IngredientId))
                .ThenBy(p => p.Id)
                .ToListAsync(cancellationToken);
            var picks = await PicksAsync(pending, cancellationToken);
            var catalog = await CatalogAsync(cancellationToken);
            var report = new LinkHoldReport { Examined = pending.Count };
            var samples = Math.Clamp(sampleSize, 0, 5000);
            foreach (var p in pending)
            {
                var hold = picks.TryGetValue(p.Id, out var pick) ? LinkGate.Hold(pick, catalog) : "target food not found";
                var marked = LinkAutoApplyGuard.IsMarked(p.Reason);
                if (hold == null && !marked) continue;

                report.Held++;
                if (marked) report.AlreadyMarked++;
                var rule = hold == null ? "marked earlier" : hold.Split(" (", 2)[0];
                report.ByRule[rule] = report.ByRule.GetValueOrDefault(rule) + 1;
                report.BySource[p.Source] = report.BySource.GetValueOrDefault(p.Source) + 1;
                if (report.Samples.Count < samples)
                {
                    report.Samples.Add(new LinkHoldSample
                    {
                        ProposalId = p.Id,
                        Source = p.Source,
                        Wording = p.CurrentValue ?? string.Empty,
                        TargetName = pick?.Target.Name ?? string.Empty,
                        Hold = hold ?? "marked earlier",
                    });
                }
            }
            return report;
        }

        private static bool Screened(FoodCatalogProposalEntity p) =>
            p.Field == FoodCatalogReviewService.FdcLinkField
            && (p.Source == IngredientLinkService.ExactSource || p.Source.StartsWith("ai:", StringComparison.Ordinal));

        private async Task<Dictionary<long, GatePair>> PicksAsync(IReadOnlyCollection<FoodCatalogProposalEntity> proposals, CancellationToken cancellationToken)
        {
            var targets = proposals.Select(p => long.TryParse(p.ProposedValue, out var id) ? id : 0L).Where(id => id > 0).Distinct().ToList();
            var foods = new Dictionary<long, GateFood>();
            foreach (var chunk in targets.Chunk(2000))
            {
                foreach (var food in await GateFoodsAsync(_db.Ingredients.Where(i => chunk.Contains(i.Id)), cancellationToken)) foods[food.Id] = food;
            }

            var picks = new Dictionary<long, GatePair>();
            foreach (var p in proposals)
            {
                if (!long.TryParse(p.ProposedValue, out var targetId) || !foods.TryGetValue(targetId, out var food) || p.IngredientId == null) continue;
                picks[p.Id] = new GatePair(p.CurrentValue ?? string.Empty, p.IngredientId.Value, food,
                    NameMatch: p.Source == IngredientLinkService.ExactSource,
                    ModelPick: p.Source.StartsWith("ai:", StringComparison.Ordinal));
            }
            return picks;
        }

        /// <summary>
        /// The gate's facts about each food of the query: name, USDA description, data type, status,
        /// category, admission reason, nutrient rows and whether calories, protein, carbohydrate and
        /// fat are all stored, and the FDC id the alcohol sense rules read.
        /// </summary>
        private async Task<List<GateFood>> GateFoodsAsync(IQueryable<IngredientEntity> query, CancellationToken cancellationToken)
        {
            var core = await CoreNutrientIdsAsync(cancellationToken);
            var rows = await GateFoodRows(query, core).ToListAsync(cancellationToken);
            return rows.Select(r => new GateFood(r.Id, r.Name, r.Description, r.FdcDataType, r.CurationStatusId, r.FdcCategoryId, r.AdmissionReason, r.Rows,
                core.Count == 0 ? null : r.Core >= core.Count, r.IsDeleted, r.FdcId)).ToList();
        }

        /// <summary>One food's gate facts as the database gives them, before the core count becomes a yes or no.</summary>
        internal sealed record GateFoodRow(long Id, string Name, bool IsDeleted, string FdcDataType, long CurationStatusId, int? FdcCategoryId,
            string? AdmissionReason, string? Description, int Rows, int Core, string? FdcId = null);

        internal static IQueryable<GateFoodRow> GateFoodRows(IQueryable<IngredientEntity> query, IReadOnlyCollection<long> core) =>
            query.AsNoTracking().Select(i => new GateFoodRow(
                i.Id,
                i.Name,
                i.IsDeleted,
                i.FdcDataType,
                i.CurationStatusId,
                i.FdcCategoryId,
                i.AdmissionReason,
                i.Aliases.Where(a => !a.IsDeleted && a.SourceContext == IngredientLinkService.UsdaDescriptionContext).Select(a => a.AliasName).FirstOrDefault(),
                i.IngredientNutrients.Count(n => !n.IsDeleted),
                i.IngredientNutrients.Where(n => !n.IsDeleted && core.Contains(n.NutrientId)).Select(n => n.NutrientId).Distinct().Count(),
                i.FdcId));

        /// <summary>One nutrient id for each of calories, protein, carbohydrate and total fat; empty when the seed lacks one.</summary>
        private async Task<List<long>> CoreNutrientIdsAsync(CancellationToken cancellationToken)
        {
            var nutrients = await _db.Nutrients.AsNoTracking().Where(n => n.ParentNutrientId == null).Select(n => new { n.Id, n.Name }).ToListAsync(cancellationToken);
            long? Find(Func<string, bool> test) => nutrients.Where(n => test(n.Name.ToLowerInvariant())).OrderBy(n => n.Id).Select(n => (long?)n.Id).FirstOrDefault();
            var ids = new[]
            {
                Find(n => n.Contains("calorie") || n.Contains("energy")),
                Find(n => n.Contains("protein")),
                Find(n => n.Contains("carbohydrate")),
                Find(n => n.Contains("total lipid") || n == "fat" || n == "total fat"),
            };
            return ids.Any(id => id == null) ? new List<long>() : ids.Select(id => id!.Value).ToList();
        }

        private async Task<GateCatalog> CatalogAsync(CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue<GateCatalog>(nameof(LinkAutoApplyService), out var cached) && cached != null) return cached;

            var parts = await CatalogPartsAsync(cancellationToken);
            var trusted = parts.TrustedAliases
                .GroupBy(a => IngredientNameNormalizer.WordingKey(a.Name), StringComparer.Ordinal)
                .Where(g => g.Select(a => a.Owner).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Owner, StringComparer.Ordinal);

            var catalog = GateCatalog.Build(parts.Foods, parts.Matcher, trusted, staples: parts.Staples);
            _cache.Set(nameof(LinkAutoApplyService), catalog, CatalogLifetime);
            return catalog;
        }

        /// <summary>What the gate's catalog is built from: the foods a pass can link to with their gate facts, the name matcher and staples over the USDA foods, and the trusted aliases (admin, deterministic, retired, deterministic link) by name and owner.</summary>
        internal sealed record CatalogParts(List<GateFood> Foods, FoodNameMatcher Matcher, Dictionary<string, FoodCandidate> Staples, List<(string Name, long Owner)> TrustedAliases);

        internal async Task<CatalogParts> CatalogPartsAsync(CancellationToken cancellationToken)
        {
            var foods = await GateFoodsAsync(_db.Ingredients.Where(i => !i.IsDeleted
                && (i.CurationStatusId == (long)CurationStatusEnum.Curated
                    || (i.FdcId != null && i.FdcDataType != CatalogAdmissionRule.BrandedFood && i.CurationStatusId != (long)CurationStatusEnum.Rejected))), cancellationToken);
            var fdcIds = await _db.Ingredients.AsNoTracking()
                .Where(i => !i.IsDeleted && i.FdcId != null && i.FdcDataType != CatalogAdmissionRule.BrandedFood && i.CurationStatusId != (long)CurationStatusEnum.Rejected)
                .Select(i => new { i.Id, i.FdcId })
                .ToDictionaryAsync(i => i.Id, i => i.FdcId!, cancellationToken);
            var candidates = foods.Where(f => fdcIds.ContainsKey(f.Id)).Select(f => new FoodCandidate(f.Id, fdcIds[f.Id], f.Text, f.DataType ?? string.Empty)).ToList();
            var matcher = new FoodNameMatcher(candidates);
            var staples = IngredientLinkService.StapleTargets(candidates, foods.Where(f => f.Description != null && fdcIds.ContainsKey(f.Id)).Select(f => (f.Id, f.Description!)).ToList());

            var aliases = await _db.IngredientAliases.AsNoTracking()
                .Where(a => !a.IsDeleted && a.SourceContext != null
                    && (a.SourceContext.StartsWith(IngredientAliasTrust.AdminPrefix) || a.SourceContext.StartsWith(IngredientAliasTrust.DeterministicPrefix)
                        || a.SourceContext.StartsWith(IngredientAliasTrust.RetiredPrefix) || a.SourceContext.StartsWith(IngredientAliasTrust.DeterministicLinkPrefix)))
                .Select(a => new { a.AliasName, a.IngredientId })
                .ToListAsync(cancellationToken);
            return new CatalogParts(foods, matcher, staples, aliases.Select(a => (a.AliasName, a.IngredientId)).ToList());
        }
    }
}
