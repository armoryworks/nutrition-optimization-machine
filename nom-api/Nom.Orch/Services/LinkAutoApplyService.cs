using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Nom.Data;
using Nom.Data.Curation;
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

                var hold = picks.TryGetValue(id, out var pick) ? LinkAutoApplyGuard.Hold(pick, catalog!) : "target food not found";
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
                var hold = picks.TryGetValue(p.Id, out var pick) ? LinkAutoApplyGuard.Hold(pick, catalog) : "target food not found";
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
                        TargetName = pick?.TargetName ?? string.Empty,
                        Hold = hold ?? "marked earlier",
                    });
                }
            }
            return report;
        }

        private static bool Screened(FoodCatalogProposalEntity p) =>
            p.Field == FoodCatalogReviewService.FdcLinkField
            && (p.Source == IngredientLinkService.ExactSource || p.Source.StartsWith("ai:", StringComparison.Ordinal));

        private async Task<Dictionary<long, LinkAutoApplyGuard.Pick>> PicksAsync(IReadOnlyCollection<FoodCatalogProposalEntity> proposals, CancellationToken cancellationToken)
        {
            var targets = proposals.Select(p => long.TryParse(p.ProposedValue, out var id) ? id : 0L).Where(id => id > 0).Distinct().ToList();
            var foods = new Dictionary<long, (string Name, string Description)>();
            foreach (var chunk in targets.Chunk(2000))
            {
                var rows = await _db.Ingredients.AsNoTracking()
                    .Where(i => chunk.Contains(i.Id))
                    .Select(i => new
                    {
                        i.Id,
                        i.Name,
                        Description = i.Aliases.Where(a => !a.IsDeleted && a.SourceContext == IngredientLinkService.UsdaDescriptionContext).Select(a => a.AliasName).FirstOrDefault(),
                    })
                    .ToListAsync(cancellationToken);
                foreach (var r in rows) foods[r.Id] = (r.Name, r.Description ?? r.Name);
            }

            var picks = new Dictionary<long, LinkAutoApplyGuard.Pick>();
            foreach (var p in proposals)
            {
                if (!long.TryParse(p.ProposedValue, out var targetId) || !foods.TryGetValue(targetId, out var food) || p.IngredientId == null) continue;
                picks[p.Id] = new LinkAutoApplyGuard.Pick(p.CurrentValue ?? string.Empty, p.IngredientId.Value, targetId, food.Name, food.Description,
                    p.Source == IngredientLinkService.ExactSource);
            }
            return picks;
        }

        private async Task<LinkAutoApplyGuard.Catalog> CatalogAsync(CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue<LinkAutoApplyGuard.Catalog>(nameof(LinkAutoApplyService), out var cached) && cached != null) return cached;

            var curated = await _db.Ingredients.AsNoTracking()
                .Where(i => !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated && i.IngredientNutrients.Any(n => !n.IsDeleted))
                .OrderBy(i => i.Id)
                .Select(i => new { i.Id, i.Name })
                .ToListAsync(cancellationToken);
            var byName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var food in curated) byName.TryAdd(food.Name.Trim(), food.Id);

            var usda = await _db.Ingredients.AsNoTracking()
                .Where(i => i.FdcId != null && !i.IsDeleted && i.FdcDataType != "branded_food")
                .Select(i => new
                {
                    i.Name,
                    Description = i.Aliases.Where(a => !a.IsDeleted && a.SourceContext == IngredientLinkService.UsdaDescriptionContext).Select(a => a.AliasName).FirstOrDefault(),
                })
                .ToListAsync(cancellationToken);
            var leadWords = usda
                .Select(f => LinkAutoApplyGuard.Words(f.Description ?? f.Name).FirstOrDefault())
                .OfType<string>()
                .GroupBy(w => w, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            var catalog = new LinkAutoApplyGuard.Catalog
            {
                Curated = byName,
                CuratedNames = curated.ToDictionary(f => f.Id, f => f.Name),
                LeadWords = leadWords,
            };
            _cache.Set(nameof(LinkAutoApplyService), catalog, CatalogLifetime);
            return catalog;
        }
    }
}
