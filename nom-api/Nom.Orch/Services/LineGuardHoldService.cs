using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Services
{
    public class LineGuardHoldService : ILineGuardHoldService
    {
        public const int MaxTake = 500;

        private readonly ApplicationDbContext _db;

        public LineGuardHoldService(ApplicationDbContext db) => _db = db;

        public async Task<LineGuardHoldReport> CountsAsync(CancellationToken cancellationToken = default)
        {
            var open = await _db.LineGuardHolds.AsNoTracking()
                .Where(h => h.ResolvedAt == null)
                .GroupBy(h => new { h.Origin, h.Reason })
                .Select(g => new { g.Key.Origin, g.Key.Reason, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var resolved = await _db.LineGuardHolds.AsNoTracking()
                .Where(h => h.ResolvedAt != null)
                .GroupBy(h => h.Resolution)
                .Select(g => new { Resolution = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var report = new LineGuardHoldReport { Open = open.Sum(o => o.Count), Resolved = resolved.Sum(r => r.Count) };
            foreach (var row in open.OrderBy(o => o.Origin, StringComparer.Ordinal).ThenBy(o => o.Reason, StringComparer.Ordinal))
            {
                report.OpenByReason[row.Reason] = report.OpenByReason.GetValueOrDefault(row.Reason) + row.Count;
                if (!report.OpenByOrigin.TryGetValue(row.Origin, out var byReason)) report.OpenByOrigin[row.Origin] = byReason = new Dictionary<string, int>(StringComparer.Ordinal);
                byReason[row.Reason] = row.Count;
            }
            foreach (var row in resolved) report.ResolvedByResolution[row.Resolution ?? string.Empty] = row.Count;
            return report;
        }

        public Task<List<LineGuardHoldRow>> ListAsync(string? reason, string? origin, bool resolved, long afterId, int take, CancellationToken cancellationToken = default)
        {
            var query = _db.LineGuardHolds.AsNoTracking().Where(h => h.Id > afterId && (resolved ? h.ResolvedAt != null : h.ResolvedAt == null));
            if (!string.IsNullOrWhiteSpace(reason)) query = query.Where(h => h.Reason == reason);
            if (!string.IsNullOrWhiteSpace(origin)) query = query.Where(h => h.Origin == origin);
            return query.OrderBy(h => h.Id).Take(Math.Clamp(take, 1, MaxTake)).Select(h => new LineGuardHoldRow
            {
                Id = h.Id, RecipeId = h.RecipeId, IngredientId = h.IngredientId, SortOrder = h.SortOrder, Origin = h.Origin, Reason = h.Reason,
                RuleVersion = h.RuleVersion, RawLine = h.RawLine, StoredFood = h.StoredFood, StoredQuantity = h.StoredQuantity, StoredUnit = h.StoredUnit,
                StoredKind = h.StoredKind, RebuiltIngredientId = h.RebuiltIngredientId, RebuiltFood = h.RebuiltFood, RebuiltQuantity = h.RebuiltQuantity,
                RebuiltUnit = h.RebuiltUnit, RebuiltKind = h.RebuiltKind, RebuiltFromMethod = h.RebuiltFromMethod, HeldAt = h.HeldAt,
                ResolvedAt = h.ResolvedAt, Resolution = h.Resolution,
            }).ToListAsync(cancellationToken);
        }
    }
}
